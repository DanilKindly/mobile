using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NETmessenger.Application.Abstractions.Auth;
using NETmessenger.Domain.Entities;
using NETmessenger.Infrastructure.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;

namespace NETmessenger.Tests;

public sealed class KindlyApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly string[] TestEnvironmentVariables =
    [
        "ConnectionStrings__DbConnection",
        "JwtSettings__SecretKey",
        "JwtSettings__TokenExpirationHours",
        "FileStorage__RootPath",
        "Push__VapidPublicKey",
        "Push__VapidPrivateKey",
        "Push__VapidSubject",
    ];

    private readonly PostgreSqlContainer _postgres;
    private readonly string _storageRoot = Path.Combine(
        Path.GetTempPath(),
        "kindly-messenger-tests",
        Guid.NewGuid().ToString("N"));

    public KindlyApiFactory()
    {
        TestEnvironmentBootstrap.ConfigureLocalTestcontainers();

        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:15-alpine")
            .WithDatabase("kindly_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCleanUp(true)
            .Build();
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_storageRoot);
        await _postgres.StartAsync();
        ApplyTestEnvironment();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        ClearTestEnvironment();

        try
        {
            if (Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }
        catch
        {
            // Temp cleanup must not hide the real test result.
        }
    }

    public HttpClient CreateAuthenticatedClient(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<SeededUser> SeedUserAsync(string role)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtService>();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = $"test_{role}_{suffix}",
            Username = $"Test {role} {suffix}",
            PasswordHash = passwordHasher.GenerateHash("Password123!"),
            LastSeenAt = DateTime.UtcNow,
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return new SeededUser(user.Id, user.Login, user.Username, jwtService.GenerateToken(user));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.Sources.Clear();
            config.AddInMemoryCollection(BuildTestSettings());
        });
    }

    private void ApplyTestEnvironment()
    {
        foreach (var setting in BuildTestSettings())
        {
            Environment.SetEnvironmentVariable(setting.Key.Replace(":", "__"), setting.Value);
        }
    }

    private static void ClearTestEnvironment()
    {
        foreach (var variable in TestEnvironmentVariables)
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    private Dictionary<string, string?> BuildTestSettings() =>
        new()
        {
            ["ConnectionStrings:DbConnection"] = GetPostgresConnectionString(),
            ["JwtSettings:SecretKey"] = "kindly-tests-secret-key-kindly-tests-secret-key-64-chars",
            ["JwtSettings:TokenExpirationHours"] = "12",
            ["FileStorage:RootPath"] = _storageRoot,
            ["Push:VapidPublicKey"] = "",
            ["Push:VapidPrivateKey"] = "",
            ["Push:VapidSubject"] = "mailto:test@kindly.local",
        };

    private string GetPostgresConnectionString()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Host = "127.0.0.1",
        };

        return connectionString.ConnectionString;
    }
}

public sealed record SeededUser(Guid UserId, string Login, string Username, string Token);
