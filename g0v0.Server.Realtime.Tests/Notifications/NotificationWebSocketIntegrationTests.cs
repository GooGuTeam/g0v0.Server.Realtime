// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

// This file intentionally hosts the WebSocket test fixture together with its socket helpers.
#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable SA1204 // Static members should appear before non-static members

using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using g0v0.Server.Common.Caching;
using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Extensions;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Notifications;
using g0v0.Server.Realtime.Objects.Chat;
using g0v0.Server.Realtime.Objects.Notifications;
using g0v0.Server.Realtime.Objects.Players;
using g0v0.Server.Realtime.Services;
using g0v0.Server.Realtime.Tests.Hubs;
using g0v0.Server.Realtime.Tests.Multiplayer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using osu.Game.Online.Chat;

namespace g0v0.Server.Realtime.Tests.Notifications;

/// <summary>
/// Real multi-client tests for the <c>/notification-server</c> WebSocket endpoint:
/// several authenticated clients connect to a real host and receive the chat and
/// notification events emitted by <see cref="NotificationServer"/>.
/// </summary>
[TestFixture]
public class NotificationWebSocketIntegrationTests
{
    private const string JwtSecret = "notification_test_key_12345678901234567890";
    private const string Issuer = "notification-tests";
    private const string Audience = "notification-clients";
    private readonly FakeOAuthTokenRepository _tokenRepository = new();
    private TestServer _server = null!;
    private FakeChatRepository _chatRepository = null!;
    private PlayerManager _playerManager = null!;

    [SetUp]
    public void SetUp()
    {
        _chatRepository = new FakeChatRepository();

        // The shared test channel: join events carry the channel object, so the
        // channel has to exist in the repository.
        _chatRepository.AddChannel(new ChatChannel
        {
            ChannelId = 100,
            Name = "#osu",
            Description = "test channel",
            Type = ChannelType.Public,
        });

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddSingleton<IChatRepository>(_chatRepository);
        builder.Services.AddSingleton<IStringCache, FakeStringCache>();
        builder.Services.AddSingleton<IRankedCache, FakeRankedCache>();
        string chatConfigBasePath = Path.Combine(Path.GetTempPath(), "g0v0-notification-ws-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(chatConfigBasePath, "config"));
        File.WriteAllText(
            Path.Combine(chatConfigBasePath, "config", "chat.json"),
            "{\"PublicMessageLimit\":100,\"PrivateMessageLimit\":100}");
        File.WriteAllText(
            Path.Combine(chatConfigBasePath, "config", "general.json"),
            JsonSerializer.Serialize(new { JwtSecretKey = JwtSecret, JwtIssuer = Issuer, JwtAudience = Audience }));
        builder.Services.AddSingleton(new ConfigManager(chatConfigBasePath));
        builder.Services.AddSingleton<IOAuthTokenRepository>(_tokenRepository);
        builder.Services.AddRealtimeAuthentication();
        builder.Services.AddSingleton<IUserRepository>(new FakeUserRepository());
        builder.Services.AddSingleton<IRelationshipRepository>(new FakeRelationshipRepository());
        builder.Services.AddSingleton<INotificationRepository>(new FakeNotificationRepository());
        builder.Services.AddScoped<ChatPmStartChecker>();
        builder.Services.AddScoped<ChatChannelDtoFactory>();
        builder.Services.AddSingleton(new InterProcessCommunicationClient(new NoOpTransport(), "realtime-tests"));
        builder.Services.AddSingleton<PlayerManager>();
        builder.Services.AddSingleton<INotificationServer, NotificationServer>();
        builder.Services.AddSingleton<LazerWebSocketListener>();

        WebApplication app = builder.Build();
        app.UseWebSockets();
        app.UseAuthentication();
        app.UseAuthorization();
        app.Map("/notification-server", async context =>
        {
            LazerWebSocketListener listener = context.RequestServices.GetRequiredService<LazerWebSocketListener>();
            await listener.HandleAsync(context);
        });
        app.Start();

        _server = app.GetTestServer();
        _playerManager = _server.Services.GetRequiredService<PlayerManager>();
    }

    [TearDown]
    public void TearDown()
    {
        _server.Dispose();
    }

    [Test]
    public async Task TwoClients_ShouldReceiveScopedChatEvents()
    {
        await using NotificationSocket first = await ConnectAsync(1);
        await using NotificationSocket second = await ConnectAsync(2);
        INotificationServer notificationServer = GetNotificationServer();
        await WaitUntilRegisteredAsync(first, 1);
        await WaitUntilRegisteredAsync(second, 2);
        await ActivateChatAsync(first, 1);
        await ActivateChatAsync(second, 2);

        IPlayer firstPlayer = LazerPlayer.GetOrCreate(_playerManager, 1);
        IPlayer secondPlayer = LazerPlayer.GetOrCreate(_playerManager, 2);

        // Joining notifies only the joining user's connections.
        await notificationServer.JoinChannel(firstPlayer, 100);
        SocketEvent firstJoin = await first.WaitForEventAsync("chat.channel.join", data => GetChannelId(data) == 100);
        Assert.That(GetChannelId(firstJoin.Data), Is.EqualTo(100));

        await notificationServer.JoinChannel(secondPlayer, 100);
        await second.WaitForEventAsync("chat.channel.join", data => GetChannelId(data) == 100);
        await Task.Delay(300);
        Assert.That(first.CountEvents("chat.channel.join", data => GetChannelId(data) == 100), Is.EqualTo(1));

        // A normal message reaches every member of the channel.
        await notificationServer.SendMessage(100, 1, "hello", isAction: false);
        SocketEvent firstMessage = await first.WaitForEventAsync("chat.message.new");
        SocketEvent secondMessage = await second.WaitForEventAsync("chat.message.new");
        Assert.Multiple(() =>
        {
            Assert.That(MessageContent(firstMessage), Is.EqualTo("hello"));
            Assert.That(MessageContent(secondMessage), Is.EqualTo("hello"));
            Assert.That(MessageSenderId(firstMessage), Is.EqualTo(1));
        });

        // A user command is delivered only to the sender.
        await notificationServer.SendMessage(100, 1, "!roll", isAction: false);
        await Task.Delay(300);
        int senderMessageCount = first.CountEvents("chat.message.new");
        int receiverMessageCount = second.CountEvents("chat.message.new");
        Assert.Multiple(() =>
        {
            Assert.That(senderMessageCount, Is.EqualTo(2));
            Assert.That(receiverMessageCount, Is.EqualTo(1));
        });

        // Parting notifies only the leaving user's connections.
        await notificationServer.LeaveChannel(secondPlayer, 100);
        SocketEvent part = await second.WaitForEventAsync("chat.channel.part", data => GetChannelId(data) == 100);
        Assert.That(GetChannelId(part.Data), Is.EqualTo(100));
    }

    [Test]
    public async Task PrivateMessageChannel_ShouldReachBothMembers()
    {
        await using NotificationSocket first = await ConnectAsync(1);
        await using NotificationSocket second = await ConnectAsync(2);
        INotificationServer notificationServer = GetNotificationServer();
        await WaitUntilRegisteredAsync(first, 1);
        await WaitUntilRegisteredAsync(second, 2);
        await ActivateChatAsync(first, 1);
        await ActivateChatAsync(second, 2);

        ChatChannel pmChannel = await _chatRepository.GetOrCreatePmChannelAsync(1, 2);

        await notificationServer.SendMessage(pmChannel.ChannelId, 1, "pm hello", isAction: false);

        SocketEvent firstMessage = await first.WaitForEventAsync("chat.message.new");
        SocketEvent secondMessage = await second.WaitForEventAsync("chat.message.new");
        Assert.Multiple(() =>
        {
            Assert.That(MessageChannelId(firstMessage), Is.EqualTo(pmChannel.ChannelId));
            Assert.That(MessageContent(firstMessage), Is.EqualTo("pm hello"));
            Assert.That(MessageContent(secondMessage), Is.EqualTo("pm hello"));
        });
    }

    [Test]
    public async Task PrivateNotification_ShouldReachReceiver()
    {
        await using NotificationSocket receiver = await ConnectAsync(3);
        INotificationServer notificationServer = GetNotificationServer();
        await WaitUntilRegisteredAsync(receiver, 3);

        ChannelMessageNotification detail = new(sourceUserId: 1, receiverIds: [3])
        {
            Title = "New message",
            CoverUrl = "https://example.com/cover.png",
            Type = "pm",
            ChannelId = 500,
        };

        await notificationServer.SendNotification(detail);

        SocketEvent notification = await receiver.WaitForEventAsync(
            "new",
            data => string.Equals(GetTitle(data), "New message", StringComparison.Ordinal));
        Assert.Multiple(() =>
        {
            Assert.That(notification.Data.GetProperty("title").GetString(), Is.EqualTo("New message"));
            Assert.That(notification.Data.GetProperty("cover_url").GetString(), Is.EqualTo("https://example.com/cover.png"));
            Assert.That(notification.Data.GetProperty("name").GetString(), Is.EqualTo("channel_message"));
            Assert.That(notification.Data.GetProperty("channel_id").GetInt32(), Is.EqualTo(500));
            Assert.That(notification.Data.GetProperty("details").GetProperty("title").GetString(), Is.EqualTo("New message"));
            Assert.That(notification.Data.GetProperty("details").GetProperty("type").GetString(), Is.EqualTo("pm"));
            Assert.That(
                notification.Data.GetProperty("details").GetProperty("cover_url").GetString(),
                Is.EqualTo("https://example.com/cover.png"));
        });
    }

    [Test]
    public async Task UserAchievementUnlock_ShouldCarryMedalDetails()
    {
        await using NotificationSocket receiver = await ConnectAsync(5);
        INotificationServer notificationServer = GetNotificationServer();
        await WaitUntilRegisteredAsync(receiver, 5);

        // The medal overlay on the client needs name == "user_achievement_unlock"
        // and the achievement in details.
        await notificationServer.SendNotification(new UserAchievementUnlockNotification
        {
            AchievementId = 42,
            AchievementMode = "osu",
            CoverUrl = "https://example.com/medal.png",
            Slug = "all-intro-silver",
            Title = "Stamina Training",
            Description = "Play a map with a length of at least 5 minutes.",
            UserId = 5,
        });

        SocketEvent notification = await receiver.WaitForEventAsync(
            "new",
            data => string.Equals(GetTitle(data), "Stamina Training", StringComparison.Ordinal));

        JsonElement details = notification.Data.GetProperty("details");
        Assert.Multiple(() =>
        {
            Assert.That(notification.Data.GetProperty("name").GetString(), Is.EqualTo("user_achievement_unlock"));
            Assert.That(details.GetProperty("achievement_id").GetInt32(), Is.EqualTo(42));
            Assert.That(details.GetProperty("achievement_mode").GetString(), Is.EqualTo("osu"));
            Assert.That(details.GetProperty("cover_url").GetString(), Is.EqualTo("https://example.com/medal.png"));
            Assert.That(details.GetProperty("slug").GetString(), Is.EqualTo("all-intro-silver"));
            Assert.That(details.GetProperty("title").GetString(), Is.EqualTo("Stamina Training"));
            Assert.That(
                details.GetProperty("description").GetString(),
                Is.EqualTo("Play a map with a length of at least 5 minutes."));
            Assert.That(details.GetProperty("user_id").GetInt32(), Is.EqualTo(5));
        });
    }

    [Test]
    public async Task ChatEnd_ShouldStopChatEvents()
    {
        await using NotificationSocket socket = await ConnectAsync(4);
        INotificationServer notificationServer = GetNotificationServer();
        await WaitUntilRegisteredAsync(socket, 4);

        IPlayer player = LazerPlayer.GetOrCreate(_playerManager, 4);
        await ActivateChatAsync(socket, 4);
        await notificationServer.JoinChannel(player, 100);
        await socket.WaitForEventAsync("chat.channel.join", data => GetChannelId(data) == 100);

        await socket.SendEventAsync("chat.end");
        await Task.Delay(300);
        await notificationServer.SendMessage(100, 4, "after chat.end", isAction: false);
        await Task.Delay(500);

        Assert.That(socket.CountEvents("chat.message.new"), Is.EqualTo(0));
    }

    [Test]
    public void Connect_WithoutToken_ShouldBeRejected()
    {
        WebSocketClient client = _server.CreateWebSocketClient();

        Assert.That(
            async () => await client.ConnectAsync(new Uri("ws://localhost/notification-server"), CancellationToken.None),
            Throws.Exception);
    }

    [Test]
    public void Connect_WithUnregisteredToken_ShouldBeRejected()
    {
        string token = CreateToken(99);
        WebSocketClient client = _server.CreateWebSocketClient();
        Assert.That(
            async () => await client.ConnectAsync(
                new Uri($"ws://localhost/notification-server?access_token={Uri.EscapeDataString(token)}"), CancellationToken.None),
            Throws.Exception);
    }

    [Test]
    public void Connect_WithClientCredentialsSubject_ShouldBeRejected()
    {
        string token = CreateToken(98);
        _tokenRepository.Register(userId: null, token, "*");
        WebSocketClient client = _server.CreateWebSocketClient();
        Assert.That(
            async () => await client.ConnectAsync(
                new Uri($"ws://localhost/notification-server?access_token={Uri.EscapeDataString(token)}"), CancellationToken.None),
            Throws.Exception);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Connect_UsesDatabaseUserInsteadOfSubject(bool useHeader)
    {
        string token = CreateToken(97);
        _tokenRepository.Register(96, token, "*");
        WebSocketClient client = _server.CreateWebSocketClient();
        string url = "ws://localhost/notification-server";
        if (useHeader)
        {
            client.ConfigureRequest = request => request.Headers.Authorization =
                $"Bearer {token}";
        }
        else
        {
            url += $"?access_token={Uri.EscapeDataString("Bearer " + token)}";
        }

        await using NotificationSocket socket = new(await client.ConnectAsync(new Uri(url), CancellationToken.None));
        await WaitUntilRegisteredAsync(socket, 96);
        Assert.That(_playerManager.GetPlayerAllInstances(97), Is.Empty);
    }

    private async Task<NotificationSocket> ConnectAsync(int userId)
    {
        WebSocketClient client = _server.CreateWebSocketClient();
        string token = CreateToken(userId);
        _tokenRepository.Register(userId, token, "*");
        WebSocket webSocket = await client.ConnectAsync(
            new Uri($"ws://localhost/notification-server?access_token={Uri.EscapeDataString(token)}"),
            CancellationToken.None);
        return new NotificationSocket(webSocket);
    }

    private async Task ActivateChatAsync(NotificationSocket socket, int userId)
    {
        INotificationServer notificationServer = GetNotificationServer();
        IPlayer player = LazerPlayer.GetOrCreate(_playerManager, userId);
        int probeChannelId = await GetOrCreateProbeChannelAsync(userId);

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await socket.SendEventAsync("chat.start");
            await notificationServer.JoinChannel(player, probeChannelId);
            if (socket.CountEvents("chat.channel.join", data => GetChannelId(data) == probeChannelId) > 0)
            {
                await notificationServer.LeaveChannel(player, probeChannelId);
                return;
            }

            await Task.Delay(100);
            await notificationServer.LeaveChannel(player, probeChannelId);
        }

        throw new TimeoutException($"User {userId} did not activate chat on the notification WebSocket.");
    }

    /// <summary>
    /// Gets (or creates) the probe channel used to detect that the client has
    /// activated chat: join events carry the whole channel object, so the
    /// channel has to exist.
    /// </summary>
    /// <param name="userId">
    /// The user the probe channel is created for.
    /// </param>
    /// <returns>
    /// The probe channel ID.
    /// </returns>
    private async Task<int> GetOrCreateProbeChannelAsync(int userId)
    {
        int probeChannelId = 1000 + userId;
        if (await _chatRepository.GetChannelAsync(probeChannelId) == null)
        {
            _chatRepository.AddChannel(new ChatChannel
            {
                ChannelId = probeChannelId,
                Name = $"probe-{userId}",
                Description = "probe",
                Type = ChannelType.Public,
            });
        }

        return probeChannelId;
    }

    private static int GetChannelId(JsonElement data)
    {
        return data.TryGetProperty("channel_id", out JsonElement channelId) ? channelId.GetInt32() : 0;
    }

    private INotificationServer GetNotificationServer() => _server.Services.GetRequiredService<INotificationServer>();

    private async Task WaitUntilRegisteredAsync(NotificationSocket socket, int userId)
    {
        INotificationServer notificationServer = GetNotificationServer();
        ChannelMessageNotification probe = new(sourceUserId: 0, receiverIds: [userId])
        {
            Title = "probe",
            Type = "pm",
        };

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await notificationServer.SendNotification(probe);
            if (socket.CountEvents("new", data => string.Equals(GetTitle(data), "probe", StringComparison.Ordinal)) > 0)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"User {userId} was not registered by the notification server.");
    }

    private static string GetTitle(JsonElement data)
    {
        return data.TryGetProperty("title", out JsonElement title) ? title.GetString() ?? string.Empty : string.Empty;
    }

    private static string CreateToken(int userId)
    {
        JwtSecurityToken token = new(
            issuer: Issuer,
            audience: Audience,
            claims: [new Claim("sub", userId.ToString(CultureInfo.InvariantCulture))],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string MessageContent(SocketEvent socketEvent)
    {
        return socketEvent.Data.GetProperty("messages")[0].GetProperty("content").GetString() ?? string.Empty;
    }

    private static int MessageSenderId(SocketEvent socketEvent)
    {
        return socketEvent.Data.GetProperty("messages")[0].GetProperty("sender_id").GetInt32();
    }

    private static int MessageChannelId(SocketEvent socketEvent)
    {
        return socketEvent.Data.GetProperty("messages")[0].GetProperty("channel_id").GetInt32();
    }
}

/// <summary>
/// A received <c>{ "event": ..., "data": ... }</c> WebSocket frame.
/// </summary>
internal sealed record SocketEvent(string Event, JsonElement Data);

/// <summary>
/// A connected notification WebSocket client that records received frames.
/// </summary>
internal sealed class NotificationSocket : IAsyncDisposable
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    private readonly WebSocket _webSocket;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _readTask;
    private readonly List<SocketEvent> _events = new();

    public NotificationSocket(WebSocket webSocket)
    {
        _webSocket = webSocket;
        _readTask = Task.Run(ReadLoopAsync);
    }

    public async Task SendEventAsync(string eventName)
    {
        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { @event = eventName }));
        await _webSocket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
    }

    public async Task<SocketEvent> WaitForEventAsync(string eventName, Func<JsonElement, bool>? predicate = null)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(EventTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            SocketEvent? match = FindEvent(eventName, predicate);
            if (match != null)
            {
                return match;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for the '{eventName}' WebSocket event.");
    }

    public int CountEvents(string eventName, Func<JsonElement, bool>? predicate = null)
    {
        lock (_events)
        {
            return _events.FindAll(socketEvent =>
                string.Equals(socketEvent.Event, eventName, StringComparison.Ordinal) &&
                (predicate == null || predicate(socketEvent.Data))).Count;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync();
        _webSocket.Dispose();

        // The read loop terminates once the socket is disposed; its result (or the
        // exception raised by the disposal) is intentionally not observed here.
        await Task.WhenAny(_readTask, Task.Delay(TimeSpan.FromSeconds(5)));
        _cancellation.Dispose();
    }

    private SocketEvent? FindEvent(string eventName, Func<JsonElement, bool>? predicate)
    {
        lock (_events)
        {
            return _events.Find(socketEvent =>
                string.Equals(socketEvent.Event, eventName, StringComparison.Ordinal) &&
                (predicate == null || predicate(socketEvent.Data)));
        }
    }

    private async Task ReadLoopAsync()
    {
        byte[] buffer = new byte[8192];
        while (!_cancellation.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
        {
            using MemoryStream stream = new();
            WebSocketReceiveResult result;
            do
            {
                result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellation.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                stream.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            using JsonDocument document = JsonDocument.Parse(stream.ToArray());
            string eventName = document.RootElement.GetProperty("event").GetString() ?? string.Empty;
            JsonElement data = document.RootElement.TryGetProperty("data", out JsonElement value)
                ? value.Clone()
                : default;

            lock (_events)
            {
                _events.Add(new SocketEvent(eventName, data));
            }
        }
    }
}