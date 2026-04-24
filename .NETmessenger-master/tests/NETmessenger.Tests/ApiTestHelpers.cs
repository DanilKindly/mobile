using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NETmessenger.Contracts.Chats;
using NETmessenger.Contracts.Messages;
using NETmessenger.Domain.Entities;
using NETmessenger.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace NETmessenger.Tests;

public static class ApiTestHelpers
{
    public static async Task<GetChatDto> CreateDirectChatAsync(
        this KindlyApiFactory factory,
        SeededUser currentUser,
        SeededUser peer)
    {
        using var client = factory.CreateAuthenticatedClient(currentUser.Token);
        var response = await client.PostAsJsonAsync("/api/chats", new
        {
            isGroup = false,
            name = (string?)null,
            participantUserIds = new[] { currentUser.UserId, peer.UserId },
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GetChatDto>())!;
    }

    public static async Task<GetMessageDto> SendTextAsync(
        this KindlyApiFactory factory,
        SeededUser currentUser,
        Guid chatId,
        Guid spoofedSenderUserId,
        string text = "hello from test")
    {
        using var client = factory.CreateAuthenticatedClient(currentUser.Token);
        var response = await client.PostAsJsonAsync($"/api/chats/{chatId:D}/messages", new
        {
            senderUserId = spoofedSenderUserId,
            text,
            clientMessageId = Guid.NewGuid().ToString("D"),
            sentAtClient = DateTime.UtcNow,
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GetMessageDto>())!;
    }

    public static async Task<GetMessageDto> SendMediaAsync(
        this KindlyApiFactory factory,
        SeededUser currentUser,
        Guid chatId)
    {
        using var client = factory.CreateAuthenticatedClient(currentUser.Token);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(Guid.NewGuid().ToString("D")), "senderUserId");

        var file = new ByteArrayContent(Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII="));
        file.Headers.ContentType = new("image/png");
        content.Add(file, "file", "test.png");

        var response = await client.PostAsync($"/api/chats/{chatId:D}/messages/media", content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GetMessageDto>())!;
    }

    public static async Task<GetMessageDto> SendVoiceAsync(
        this KindlyApiFactory factory,
        SeededUser currentUser,
        Guid chatId)
    {
        using var client = factory.CreateAuthenticatedClient(currentUser.Token);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(Guid.NewGuid().ToString("D")), "senderUserId");
        content.Add(new StringContent("1"), "durationSeconds");

        var file = new ByteArrayContent(new byte[] { 0, 1, 2, 3, 4, 5 });
        file.Headers.ContentType = new("audio/mp4");
        content.Add(file, "audio", "test.m4a");

        var response = await client.PostAsync($"/api/chats/{chatId:D}/messages/voice", content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GetMessageDto>())!;
    }

    public static async Task<SeededScenario> SeedScenarioAsync(this KindlyApiFactory factory)
    {
        var alice = await factory.SeedUserAsync("alice");
        var bob = await factory.SeedUserAsync("bob");
        var mallory = await factory.SeedUserAsync("mallory");
        var chat = await factory.CreateDirectChatAsync(alice, bob);
        return new SeededScenario(alice, bob, mallory, chat);
    }

    public static async Task<int> CountMessagesBySenderAsync(this KindlyApiFactory factory, Guid senderUserId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.Messages.CountAsync(m => m.SenderId == senderUserId);
    }
}

public sealed record SeededScenario(
    SeededUser Alice,
    SeededUser Bob,
    SeededUser Mallory,
    GetChatDto Chat);
