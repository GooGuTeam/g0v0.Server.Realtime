// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using g0v0.Server.Common.Caching;
using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Extensions;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Notifications;
using g0v0.Server.Realtime.Notifications.Bot;
using g0v0.Server.Realtime.Objects.Chat;
using g0v0.Server.Realtime.Services;
using g0v0.Server.Realtime.Tests.Multiplayer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using osu.Game.Online.Chat;
using ConfigurationManager = g0v0.Server.Common.Configuration.ConfigurationManager;

namespace g0v0.Server.Realtime.Tests.Notifications;

/// <summary>
/// Multi-client integration tests over the real ASP.NET pipeline: a desktop
/// lazer client (Bearer header authentication, SignalR-style hub connections
/// implied) and a web/browser client (query parameter authentication, since
/// browsers cannot send WebSocket headers) talking to the same channels.
/// </summary>
[TestFixture]
public class MultiClientChatFlowTests
{
    private const string JwtSecret = "multi-client-test-secret-0123456789abcdef";
    private const string Issuer = "https://localdev.g0v0.top:9090/";
    private const string Audience = "5";

    private WebApplication _app = null!;
    private TestServer _server = null!;
    private FakeChatRepository _chatRepository = null!;
    private FakeRankedCache _rankedCache = null!;
    private PlayerManager _playerManager = null!;
    private FakeOAuthTokenRepository _tokenRepository = null!;
    private string _basePath = null!;

    [SetUp]
    public void SetUp()
    {
        _basePath = Path.Combine(Path.GetTempPath(), "g0v0-multi-client-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_basePath, "config"));
        File.WriteAllText(
            Path.Combine(_basePath, "config", "general.json"),
            "{\"JwtSecretKey\":\"" + JwtSecret + "\",\"JwtAlgorithm\":\"HS256\",\"JwtAudience\":\"" + Audience + "\",\"JwtIssuer\":\"" + Issuer + "\"}");
        File.WriteAllText(
            Path.Combine(_basePath, "config", "chat.json"),
            "{\"PublicMessageLimit\":100,\"PrivateMessageLimit\":100}");
        ConfigurationManager configurationManager = new(_basePath);

        _chatRepository = new FakeChatRepository();
        _rankedCache = new FakeRankedCache();
        _playerManager = new PlayerManager(
            NullLogger<PlayerManager>.Instance,
            new InterProcessCommunicationClient(new NoOpTransport(), "realtime-tests"));
        _tokenRepository = new FakeOAuthTokenRepository();

        // Seed the default public channel, as in the osu-web database.
        _chatRepository.AddChannel(new ChatChannel
        {
            ChannelId = 1,
            Name = "osu!",
            Description = "General discussion for osu!",
            Type = ChannelType.Public,
        });

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddSingleton(configurationManager);
        builder.Services.AddSingleton(_chatRepository);
        builder.Services.AddSingleton<IChatRepository>(_chatRepository);
        builder.Services.AddSingleton<IStringCache, FakeStringCache>();
        builder.Services.AddSingleton<IRankedCache>(_rankedCache);
        builder.Services.AddSingleton<IUserRepository>(new FakeUserRepository());
        builder.Services.AddSingleton<IRelationshipRepository>(new FakeRelationshipRepository());
        builder.Services.AddSingleton<IOAuthTokenRepository>(_tokenRepository);
        builder.Services.AddSingleton(new InterProcessCommunicationClient(new NoOpTransport(), "realtime-tests"));
        builder.Services.AddSingleton(_playerManager);
        builder.Services.AddScoped<ChatPmStartChecker>();
        builder.Services.AddScoped<ChatChannelDtoFactory>();
        builder.Services.AddSingleton<INotificationServer, NotificationServer>();
        builder.Services.AddSingleton<BanchoBot>();
        builder.Services.AddSingleton<IBanchoBot>(serviceProvider =>
        {
            BanchoBot bot = serviceProvider.GetRequiredService<BanchoBot>();
            bot.Initialize();
            return bot;
        });
        builder.Services.AddSingleton<LazerWebSocketListener>();
        builder.Services.AddOAuthAuthentication();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(g0v0.Server.Realtime.Controllers.ChatController).Assembly);

        WebApplication app = builder.Build();
        app.UseWebSockets();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.Map("/notification-server", async context =>
        {
            LazerWebSocketListener listener = context.RequestServices.GetRequiredService<LazerWebSocketListener>();
            await listener.HandleAsync(context);
        });
        app.Start();

        _server = app.GetTestServer();
        _app = app;
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        _server.Dispose();
        await _app.DisposeAsync();
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, recursive: true);
        }
    }

    [Test]
    public async Task DesktopClient_FullChatFlow_ShouldJoinDefaultChannelAndReceiveOwnMessage()
    {
        HttpClient client = CreateRestClient(1, "chat.read,chat.write,chat.write_manage");
        NotificationSocket socket = await ConnectAsync(1);

        // lazer startup: the channel catalogue drives the default joins.
        HttpRequestMessage listRequest = new(HttpMethod.Get, "/chat/channels");
        HttpResponseMessage listResponse = await client.SendAsync(listRequest);
        Assert.That(listResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        JsonElement[] channels = (await listResponse.Content.ReadFromJsonAsync<JsonElement[]>()) ?? [];
        Assert.That(channels.Select(c => c.GetProperty("name").GetString()), Does.Contain("osu!"));

        await socket.SendEventAsync("chat.start");

        // joining a public channel answers with the full channel object and the
        // join is announced on the WebSocket.
        HttpRequestMessage joinRequest = new(HttpMethod.Put, "/chat/channels/1/users/1");
        HttpResponseMessage joinResponse = await client.SendAsync(joinRequest);
        Assert.That(joinResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await socket.WaitForEventAsync("chat.channel.join", data => GetChannelId(data) == 1);

        // sending a message echoes it back on the WebSocket (uuid confirmation).
        HttpRequestMessage messageRequest = new(HttpMethod.Post, "/chat/channels/1/messages")
        {
            Content = new StringContent(
                "{\"message\":\"hello from the desktop client\",\"uuid\":\"desktop-1\"}",
                Encoding.UTF8,
                "application/json"),
        };
        HttpResponseMessage messageResponse = await client.SendAsync(messageRequest);
        Assert.That(messageResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        SocketEvent message = await socket.WaitForEventAsync(
            "chat.message.new",
            data => string.Equals(GetMessageContent(data), "hello from the desktop client", StringComparison.Ordinal));
        Assert.That(
            message.Data.GetProperty("messages")[0].GetProperty("uuid").GetString(),
            Is.EqualTo("desktop-1"));

        // the read marker is persisted through the ack endpoint.
        HttpRequestMessage ackRequest = new(HttpMethod.Post, "/chat/ack");
        HttpResponseMessage ackResponse = await client.SendAsync(ackRequest);
        Assert.That(ackResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await socket.DisposeAsync();
    }

    [Test]
    public async Task BrowserClient_ShouldAuthenticateThroughTheQueryParameter()
    {
        // browsers cannot send WebSocket headers: the access token travels in
        // the URL, while regular HTTP requests still use the Bearer header.
        NotificationSocket socket = await ConnectAsync(2);
        await socket.SendEventAsync("chat.start");

        HttpClient client = CreateRestClient(2, "chat.read,chat.write,chat.write_manage");
        using (HttpRequestMessage request = new(HttpMethod.Put, "/chat/channels/1/users/2"))
        {
            HttpResponseMessage response = await client.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        await socket.WaitForEventAsync("chat.channel.join", data => GetChannelId(data) == 1);

        using (HttpRequestMessage request = new(HttpMethod.Get, "/chat/channels"))
        {
            HttpResponseMessage response = await client.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        await socket.DisposeAsync();
    }

    [Test]
    public async Task MultiClient_MessageShouldReachDesktopAndBrowserConnections()
    {
        // user 1 talks from the desktop client, user 2 has the desktop client
        // and a browser tab open at the same time.
        NotificationSocket user1Desktop = await ConnectAsync(1);
        NotificationSocket user2Desktop = await ConnectAsync(2);
        NotificationSocket user2Browser = await ConnectAsync(2);

        foreach (NotificationSocket socket in new[] { user1Desktop, user2Desktop, user2Browser })
        {
            await socket.SendEventAsync("chat.start");
        }

        HttpClient user1Client = CreateRestClient(1, "chat.read,chat.write,chat.write_manage");
        HttpClient user2Client = CreateRestClient(2, "chat.read,chat.write,chat.write_manage");
        await user1Client.PutAsync("/chat/channels/1/users/1", content: null);
        await user2Client.PutAsync("/chat/channels/1/users/2", content: null);

        await user1Client.PostAsync("/chat/ack", content: null);
        await user2Client.PostAsync("/chat/ack", content: null);

        HttpRequestMessage request = new(HttpMethod.Post, "/chat/channels/1/messages")
        {
            Content = new StringContent(
                "{\"message\":\"hi everyone\",\"uuid\":\"u1-1\"}",
                Encoding.UTF8,
                "application/json"),
        };
        HttpResponseMessage response = await user1Client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // the sender's own client confirms the local echo ...
        SocketEvent echo = await user1Desktop.WaitForEventAsync(
            "chat.message.new",
            data => string.Equals(GetMessageContent(data), "hi everyone", StringComparison.Ordinal));
        Assert.That(echo.Data.GetProperty("messages")[0].GetProperty("uuid").GetString(), Is.EqualTo("u1-1"));

        // ... and both of user 2's connections receive the broadcast.
        await user2Desktop.WaitForEventAsync(
            "chat.message.new",
            data => string.Equals(GetMessageContent(data), "hi everyone", StringComparison.Ordinal));
        await user2Browser.WaitForEventAsync(
            "chat.message.new",
            data => string.Equals(GetMessageContent(data), "hi everyone", StringComparison.Ordinal));

        await user1Desktop.DisposeAsync();
        await user2Desktop.DisposeAsync();
        await user2Browser.DisposeAsync();
    }

    [Test]
    public async Task Presence_ShouldStayOnlineUntilTheLastConnectionCloses()
    {
        NotificationSocket browserTab1 = await ConnectAsync(3);
        await WaitForOnlineAsync(3);
        Assert.That(IsOnline(3), Is.True);

        NotificationSocket browserTab2 = await ConnectAsync(3);
        await browserTab1.DisposeAsync();
        await WaitForConditionAsync(() => IsOnline(3), stillOnline: true);
        Assert.That(IsOnline(3), Is.True, "the second browser tab keeps the player online");

        await browserTab2.DisposeAsync();
        await WaitForConditionAsync(() => IsOnline(3), stillOnline: false);
        Assert.That(IsOnline(3), Is.False, "the player goes offline once the last connection is gone");
    }

    private bool IsOnline(int userId) =>
        _playerManager.GetPlayerAllInstances(userId).Any();

    private string CreateToken(int userId, string scope)
    {
        JwtSecurityTokenHandler handler = new();
        JwtSecurityToken token = new(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim("sub", userId.ToString(CultureInfo.InvariantCulture)),
                new Claim("scope", scope),
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
                SecurityAlgorithms.HmacSha256));
        return handler.WriteToken(token);
    }

    private int GetChannelId(JsonElement data) =>
        data.GetProperty("channel_id").GetInt32();
    private string GetMessageContent(JsonElement data) =>
        data.GetProperty("messages")[0].GetProperty("content").GetString() ?? string.Empty;

    private async Task WaitForConditionAsync(Func<bool> condition, bool stillOnline)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition() != stillOnline)
            {
                await Task.Delay(50);
            }
            else
            {
                return;
            }
        }

        Assert.Fail($"Timed out waiting for the player to be {(stillOnline ? "online" : "offline")}.");
    }

    private async Task WaitForOnlineAsync(int userId)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (IsOnline(userId))
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("Timed out waiting for the player to come online.");
    }

    private HttpClient CreateRestClient(int userId, string scopes)
    {
        string token = CreateToken(userId, scopes);
        _tokenRepository.Register(userId, token, scopes);

        HttpClient client = _server.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<NotificationSocket> ConnectAsync(int userId)
    {
        WebSocketClient client = _server.CreateWebSocketClient();
        string token = CreateToken(userId, "*");
        string url = $"ws://localhost/notification-server?access_token={Uri.EscapeDataString(token)}";

        WebSocket webSocket = await client.ConnectAsync(new Uri(url), CancellationToken.None);
        return new NotificationSocket(webSocket);
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public Task<Common.Database.Models.User?> GetByIdAsync(int userId)
            => Task.FromResult<Common.Database.Models.User?>(new Common.Database.Models.User { Id = userId, Username = $"user{userId}" });

        public Task<IReadOnlyList<Common.Database.Models.User>> GetByIdsAsync(IReadOnlyList<int> userIds)
            => Task.FromResult<IReadOnlyList<Common.Database.Models.User>>(
                userIds.Select(userId => new Common.Database.Models.User { Id = userId, Username = $"user{userId}" }).ToList());

        public Task<string?> GetUsernameByIdAsync(int userId)
            => Task.FromResult<string?>($"user{userId}");

        public Task<Common.Database.Models.User?> GetByUsernameAsync(string username)
            => Task.FromResult<Common.Database.Models.User?>(new Common.Database.Models.User { Id = 1, Username = username });

        public Task<bool> UsernameExistsAsync(string username) => Task.FromResult(true);

        public Task CreateAsync(Common.Database.Models.User user) => Task.CompletedTask;

        public Task UpdateAsync(Common.Database.Models.User user) => Task.CompletedTask;
    }

    private sealed class NoOpTransport : IInterProcessCommunicationTransport
    {
        public Task PublishAsync(string channel, string payload) => Task.CompletedTask;

        public void Subscribe(string channel, Func<string, Task> handler)
        {
        }
    }

    private sealed class FakeOAuthTokenRepository : IOAuthTokenRepository
    {
        private readonly Dictionary<string, OAuthToken> _tokens = new(StringComparer.Ordinal);

        public void Register(int userId, string accessToken, string scopes)
        {
            OAuthToken token = new()
            {
                UserId = userId,
                ClientId = 1,
                AccessToken = accessToken,
                RefreshToken = $"refresh-{Guid.NewGuid():N}",
                Scope = scopes,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            };
            _tokens[token.AccessToken] = token;
        }

        public Task<OAuthToken?> GetByAccessTokenAsync(string token)
        {
            _tokens.TryGetValue(token, out OAuthToken? record);
            return Task.FromResult(record is { } found && found.ExpiresAt > DateTimeOffset.UtcNow ? found : null);
        }

        public Task<bool> CheckAccessTokenIsValidAsync(string token)
        {
            return Task.FromResult(_tokens.ContainsKey(token));
        }
    }
}