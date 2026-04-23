using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using NETmessenger.Application.Abstractions.Security;
using NETmessenger.Application.Abstractions.Auth;
using NETmessenger.Infrastructure;
using NETmessenger.Infrastructure.Persistence;
using NETmessenger.Web.Hubs;
using Microsoft.EntityFrameworkCore;
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
var defaultDevelopmentSecret = "YourSuperSecretKeyThatIsLongEnoughForHmacSha256Algorithm";
if (!builder.Environment.IsDevelopment() &&
    (string.IsNullOrWhiteSpace(secretKey) ||
     secretKey == defaultDevelopmentSecret ||
     secretKey.Length < 32))
{
    throw new InvalidOperationException(
        "JwtSettings:SecretKey must be configured with a unique strong production secret.");
}

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

            if (string.IsNullOrEmpty(token) &&
                context.HttpContext.Request.Path.StartsWithSegments("/hubs/chat"))
            {
                token = context.Request.Query["access_token"];
            }
            
            if (!string.IsNullOrEmpty(token))
            {
                context.Token = token;
            }
            
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var auditService = context.HttpContext.RequestServices.GetService<ISecurityAuditService>();
        if (auditService is null)
        {
            return;
        }

        try
        {
            await auditService.RecordAsync(new SecurityAuditEventInput(
                "rate_limit_rejected",
                "rejected",
                "warning",
                TryReadUserId(context.HttpContext.User),
                context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                context.HttpContext.Request.Headers.UserAgent.ToString(),
                "route",
                context.HttpContext.Request.Path,
                "rate limit exceeded"),
                cancellationToken);
        }
        catch
        {
            // Never fail the rejection path because audit persistence is unavailable.
        }
    };

    options.AddPolicy("auth-login", httpContext => FixedWindowByClient(httpContext, "login", 8, TimeSpan.FromMinutes(5)));
    options.AddPolicy("auth-register", httpContext => FixedWindowByClient(httpContext, "register", 4, TimeSpan.FromMinutes(30)));
    options.AddPolicy("user-search", httpContext => FixedWindowByUser(httpContext, "search", 30, TimeSpan.FromMinutes(1)));
    options.AddPolicy("send-message", httpContext => FixedWindowByUser(httpContext, "send", 60, TimeSpan.FromMinutes(1)));
    options.AddPolicy("files", httpContext => FixedWindowByUser(httpContext, "files", 120, TimeSpan.FromMinutes(1)));
    options.AddPolicy("hub", httpContext => FixedWindowByClient(httpContext, "hub", 60, TimeSpan.FromMinutes(1)));
});

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

app.UseRouting();
app.UseCors(DevClientCorsPolicy);
app.UseRateLimiter();

app.MapGet("/", () => Results.Ok(new { status = "ok", service = "kindly-messenger-api" }));
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat").RequireRateLimiting("hub");



app.Run();

static RateLimitPartition<string> FixedWindowByClient(
    HttpContext httpContext,
    string name,
    int permitLimit,
    TimeSpan window)
{
    var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return RateLimitPartition.GetFixedWindowLimiter(
        $"{name}:ip:{ip}",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true
        });
}

static RateLimitPartition<string> FixedWindowByUser(
    HttpContext httpContext,
    string name,
    int permitLimit,
    TimeSpan window)
{
    var userId = TryReadUserId(httpContext.User)?.ToString("D")
        ?? httpContext.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";

    return RateLimitPartition.GetFixedWindowLimiter(
        $"{name}:{userId}",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true
        });
}

static Guid? TryReadUserId(System.Security.Claims.ClaimsPrincipal? principal)
{
    var raw = principal?.FindFirst("user_id")?.Value;
    return Guid.TryParse(raw, out var userId) ? userId : null;
}

public partial class Program { }
