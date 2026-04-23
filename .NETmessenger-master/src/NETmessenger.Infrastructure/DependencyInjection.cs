using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NETmessenger.Application.Abstractions.Chats;
using NETmessenger.Application.Abstractions.Files;
using NETmessenger.Application.Abstractions.Messages;
using NETmessenger.Application.Abstractions.Users;
using NETmessenger.Infrastructure.Persistence;
using NETmessenger.Infrastructure.Services.Chats;
using NETmessenger.Infrastructure.Services.Files;
using NETmessenger.Infrastructure.Services.Messages;
using NETmessenger.Infrastructure.Services.Push;
using NETmessenger.Infrastructure.Services.Users;
using NETmessenger.Infrastructure.Services;



using NETmessenger.Application.Abstractions.Auth;
using NETmessenger.Application.Abstractions.Push;
using NETmessenger.Domain.Entities;
using NETmessenger.Infrastructure.Services.Auth;

namespace NETmessenger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DbConnection")
            ?? $"Host={configuration["POSTGRES_HOST"] ?? "localhost"};" +
               $"Port={configuration["POSTGRES_PORT"] ?? "5431"};" +
               $"Database={configuration["POSTGRES_DB"] ?? "messagerdb"};" +
               $"Username={configuration["POSTGRES_USER"] ?? "user"};" +
               $"Password={configuration["POSTGRES_PASSWORD"] ?? "password"}";

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IVoiceStorage, LocalVoiceStorage>();
        services.AddScoped<IMediaStorage, LocalMediaStorage>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IPushNotificationService, PushNotificationService>();

        return services;
    }
}
