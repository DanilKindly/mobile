using System.Runtime.CompilerServices;

namespace NETmessenger.Tests;

internal static class TestEnvironmentBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        ConfigureLocalTestcontainers();
    }

    internal static void ConfigureLocalTestcontainers()
    {
        // Local Windows/Docker setups can fail while pulling Testcontainers Ryuk
        // through stale proxies. We dispose the PostgreSQL container explicitly.
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Testing");

        ClearLoopbackProxy("HTTP_PROXY");
        ClearLoopbackProxy("HTTPS_PROXY");
        ClearLoopbackProxy("ALL_PROXY");
        ClearLoopbackProxy("http_proxy");
        ClearLoopbackProxy("https_proxy");
        ClearLoopbackProxy("all_proxy");
    }

    private static void ClearLoopbackProxy(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsLoopback)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
