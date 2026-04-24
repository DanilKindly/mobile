using System.Net;
using System.Net.Http.Json;

namespace NETmessenger.Tests;

public sealed class RateLimitIntegrationTests : IClassFixture<KindlyApiFactory>
{
    private readonly KindlyApiFactory _factory;

    public RateLimitIntegrationTests(KindlyApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Repeated_bad_login_attempts_eventually_return_429()
    {
        using var client = _factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 12; i += 1)
        {
            var response = await client.PostAsJsonAsync("/api/users/login", new
            {
                login = $"missing_{i}",
                password = "wrong-password",
            });
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}
