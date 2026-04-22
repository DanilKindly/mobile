using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using NETmessenger.Application.Abstractions.Auth;
using NETmessenger.Infrastructure;
using NETmessenger.Infrastructure.Persistence;
using NETmessenger.Web.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.Collections.Generic;

const string DevClientCorsPolicy = "DevClient";

var builder = WebApplication.CreateBuilder(args);

var defaultAllowedOrigins = new[]
{
    "http://localhost:5066",
    "https://localhost:5066",
    "http://localhost:5067",
    "https://localhost:5067",
    "http://localhost:5173",
    "https://localhost:5173",
    "http://127.0.0.1:5066",
    "https://127.0.0.1:5066",
    "http://127.0.0.1:5067",
    "https://127.0.0.1:5067",
    "http://127.0.0.1:5173",
    "https://127.0.0.1:5173",
    "https://front2-main.vercel.app",
    "https://kindlydevelopment.vercel.app",
};

var configuredOrigins = builder.Configuration["Cors:AllowedOrigins"]
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? Array.Empty<string>();

var allowedOrigins = new HashSet<string>(defaultAllowedOrigins, StringComparer.OrdinalIgnoreCase);
foreach (var origin in configuredOrigins)
{
    allowedOrigins.Add(origin);
}

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddPolicy(DevClientCorsPolicy, policy =>
    {
        policy.WithOrigins(allowedOrigins.ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure(builder.Configuration);


var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"];

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!)),
        ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var token = context.Request.Cookies["auth_token"];
            
            if (!string.IsNullOrEmpty(token))
            {
                context.Token = token;
            }
            
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(DevClientCorsPolicy);
app.UseStaticFiles();

var configuredFileStorageRoot = builder.Configuration["FileStorage:RootPath"];
if (!string.IsNullOrWhiteSpace(configuredFileStorageRoot))
{
    var normalizedStorageRoot = Path.IsPathRooted(configuredFileStorageRoot)
        ? configuredFileStorageRoot
        : Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, configuredFileStorageRoot));

    var mediaPath = Path.Combine(normalizedStorageRoot, "media");
    Directory.CreateDirectory(mediaPath);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(mediaPath),
        RequestPath = "/media"
    });

    var voicePath = Path.Combine(normalizedStorageRoot, "voice");
    Directory.CreateDirectory(voicePath);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(voicePath),
        RequestPath = "/voice"
    });
}

app.MapGet("/", () => Results.Ok(new { status = "ok", service = "kindly-messenger-api" }));
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");



app.Run();

public partial class Program { }
