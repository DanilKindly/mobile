using System.Net;
using System.Net.Http.Json;
using NETmessenger.Contracts.Messages;

namespace NETmessenger.Tests;

public sealed class SecurityIntegrationTests : IClassFixture<KindlyApiFactory>
{
    private readonly KindlyApiFactory _factory;

    public SecurityIntegrationTests(KindlyApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Chat_read_requires_authentication()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/chats/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_participant_cannot_read_chat_or_messages()
    {
        var scenario = await _factory.SeedScenarioAsync();
        using var malloryClient = _factory.CreateAuthenticatedClient(scenario.Mallory.Token);

        var chatResponse = await malloryClient.GetAsync($"/api/chats/{scenario.Chat.ChatId:D}");
        var messagesResponse = await malloryClient.GetAsync($"/api/chats/{scenario.Chat.ChatId:D}/messages");

        Assert.Equal(HttpStatusCode.Forbidden, chatResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, messagesResponse.StatusCode);
    }

    [Fact]
    public async Task Client_sender_user_id_is_ignored_when_sending_message()
    {
        var scenario = await _factory.SeedScenarioAsync();

        var message = await _factory.SendTextAsync(
            scenario.Alice,
            scenario.Chat.ChatId,
            spoofedSenderUserId: scenario.Bob.UserId,
            text: "spoof attempt");

        Assert.Equal(scenario.Alice.UserId, message.SenderUserId);
        Assert.NotEqual(scenario.Bob.UserId, message.SenderUserId);
    }

    [Fact]
    public async Task Non_participant_cannot_access_protected_media_or_voice()
    {
        var scenario = await _factory.SeedScenarioAsync();
        var media = await _factory.SendMediaAsync(scenario.Alice, scenario.Chat.ChatId);
        var voice = await _factory.SendVoiceAsync(scenario.Alice, scenario.Chat.ChatId);

        using var aliceClient = _factory.CreateAuthenticatedClient(scenario.Alice.Token);
        using var malloryClient = _factory.CreateAuthenticatedClient(scenario.Mallory.Token);
        using var anonymousClient = _factory.CreateClient();

        var aliceMediaResponse = await aliceClient.GetAsync(media.MediaUrl);
        var aliceVoiceResponse = await aliceClient.GetAsync(voice.AudioUrl);
        var malloryMediaResponse = await malloryClient.GetAsync(media.MediaUrl);
        var malloryVoiceResponse = await malloryClient.GetAsync(voice.AudioUrl);
        var anonymousMediaResponse = await anonymousClient.GetAsync(media.MediaUrl);
        var anonymousVoiceResponse = await anonymousClient.GetAsync(voice.AudioUrl);

        Assert.Equal(HttpStatusCode.OK, aliceMediaResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, aliceVoiceResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, malloryMediaResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, malloryVoiceResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousMediaResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousVoiceResponse.StatusCode);
    }

    [Fact]
    public async Task Message_read_requires_authentication()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/chats/{Guid.NewGuid():D}/messages");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
