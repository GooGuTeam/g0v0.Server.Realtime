// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

// The NoOpTransport test helper lives in this file.
#pragma warning disable SA1402
#pragma warning disable SA1204

using System.Text.Json;
using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Realtime.Hubs;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Rooms;
using g0v0.Server.Realtime.Services;
using g0v0.Server.Realtime.Tests.Notifications;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using osu.Game.Online;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using osu.Game.Online.Rooms;
using OsuMatchType = osu.Game.Online.Rooms.MatchType;

namespace g0v0.Server.Realtime.Tests.Hubs;

/// <summary>
/// Real multi-client SignalR collaboration tests: two or more clients connect to
/// a real <see cref="MultiplayerHub"/> through the MessagePack protocol and
/// exercise the full room lifecycle together.
/// </summary>
[TestFixture]
public class MultiplayerIntegrationTests
{
    private TestServer _server = null!;
    private FakeRoomRepository _roomRepository = null!;

    [SetUp]
    public void SetUp()
    {
        _roomRepository = new FakeRoomRepository();

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddLogging();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<FakeRoomRepository>(_roomRepository);
        builder.Services.AddSingleton<IRoomRepository>(_roomRepository);
        builder.Services.AddSingleton<IChatRepository>(new FakeChatRepository());
        RecordingTransport ipcTransport = new();
        InterProcessCommunicationClient realtimeIpcClient = new(ipcTransport, "realtime-tests");
        InterProcessCommunicationClient lazerIpcClient = new(ipcTransport, "lazer");
        RegisterLazerMultiplayerResponders(lazerIpcClient);
        builder.Services.AddSingleton(realtimeIpcClient);
        builder.Services.AddSingleton<PlayerManager>();
        builder.Services.AddSingleton<MultiplayerEventDispatcher>();
        builder.Services.AddSingleton(new RulesetManager(Path.GetTempPath()));
        builder.Services.AddSingleton<IRulesetHashValidator, RulesetHashValidator>();
        builder.Services.AddSingleton<IRoomFacade>(serviceProvider => new RoomFacade(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<MultiplayerEventDispatcher>(),
            serviceProvider.GetService<ILogger<RoomFacade>>())
        {
            RulesetManager = serviceProvider.GetRequiredService<RulesetManager>(),
            RulesetHashValidator = serviceProvider.GetRequiredService<IRulesetHashValidator>(),
        });
        builder.Services.AddSingleton<RoomManager>();

        builder.Services.AddSignalR()
            .AddMessagePackProtocol(options =>
            {
                options.SerializerOptions = SignalRUnionWorkaroundResolver.OPTIONS;
            })
            .AddHubOptions<MultiplayerHub>(options =>
            {
                options.SupportedProtocols?.Remove("json");
            });

        builder.Services.AddSingleton<IUserIdProvider, TestUserIdProvider>();

        WebApplication app = builder.Build();
        app.MapHub<MultiplayerHub>("/signalr/multiplayer");
        app.Start();

        _server = app.GetTestServer();
    }

    [TearDown]
    public void TearDown()
    {
        _server.Dispose();
    }

    [Test]
    public async Task HeadToHead_TwoClients_FullLifecycle()
    {
        await using HubConnection hostConnection = await ConnectAsync(1);
        await using HubConnection guestConnection = await ConnectAsync(2);

        TestMultiplayerClient hostClient = new(hostConnection);
        TestMultiplayerClient guestClient = new(guestConnection);
        await hostClient.SubscribeAsync();
        await guestClient.SubscribeAsync();

        // Host creates a head-to-head room.
        MultiplayerRoom room = await hostConnection.InvokeAsync<MultiplayerRoom>(
            nameof(IMultiplayerLoungeServer.CreateRoom),
            CreateRoomRequest(OsuMatchType.HeadToHead));

        Assert.That(room.RoomID, Is.GreaterThan(0));

        // Guest joins.
        MultiplayerRoom joined = await guestConnection.InvokeAsync<MultiplayerRoom>(
            nameof(IMultiplayerLoungeServer.JoinRoom),
            room.RoomID);

        Assert.That(joined.Users, Has.Count.EqualTo(2));
        await guestClient.WaitForUserJoinedAsync(1);

        // Both ready, host starts.
        await hostConnection.InvokeAsync(nameof(IMultiplayerRoomServer.ChangeBeatmapAvailability), BeatmapAvailability.LocallyAvailable());
        await guestConnection.InvokeAsync(nameof(IMultiplayerRoomServer.ChangeBeatmapAvailability), BeatmapAvailability.LocallyAvailable());
        await hostConnection.InvokeAsync(nameof(IMultiplayerRoomServer.ChangeState), MultiplayerUserState.Ready);
        await guestConnection.InvokeAsync(nameof(IMultiplayerRoomServer.ChangeState), MultiplayerUserState.Ready);

        await hostConnection.InvokeAsync(nameof(IMultiplayerRoomServer.StartMatch));

        Assert.That(await hostClient.WaitForRoomStateAsync(MultiplayerRoomState.WaitingForLoad), Is.True);
        Assert.That(await guestClient.WaitForLoadRequestedAsync(), Is.True);

        // Both load and become ready for gameplay.
        await hostConnection.InvokeAsync(nameof(IMultiplayerRoomServer.ChangeState), MultiplayerUserState.Loaded);
        await hostConnection.InvokeAsync(nameof(IMultiplayerRoomServer.ChangeState), MultiplayerUserState.ReadyForGameplay);
        await guestConnection.InvokeAsync(nameof(IMultiplayerRoomServer.ChangeState), MultiplayerUserState.Loaded);
        await guestConnection.InvokeAsync(nameof(IMultiplayerRoomServer.ChangeState), MultiplayerUserState.ReadyForGameplay);

        Assert.That(await hostClient.WaitForRoomStateAsync(MultiplayerRoomState.Playing), Is.True);
        Assert.That(await guestClient.WaitForGameplayStartedAsync(), Is.True);

        // Both finish.
        await hostConnection.InvokeAsync(nameof(IMultiplayerRoomServer.ChangeState), MultiplayerUserState.FinishedPlay);
        await guestConnection.InvokeAsync(nameof(IMultiplayerRoomServer.ChangeState), MultiplayerUserState.FinishedPlay);

        Assert.That(await hostClient.WaitForRoomStateAsync(MultiplayerRoomState.Open), Is.True);
        Assert.That(await hostClient.WaitForResultsReadyAsync(), Is.True);
        Assert.That(await guestClient.WaitForResultsReadyAsync(), Is.True);
    }

    [Test]
    public async Task CreateAndJoin_NoSelfUserJoinedEvents()
    {
        await using HubConnection hostConnection = await ConnectAsync(1);
        await using HubConnection guestConnection = await ConnectAsync(2);

        TestMultiplayerClient hostClient = new(hostConnection);
        TestMultiplayerClient guestClient = new(guestConnection);
        await hostClient.SubscribeAsync();
        await guestClient.SubscribeAsync();

        // The host creates a room: they must not receive their own UserJoined
        // (which would play the player-joined sound on the client).
        MultiplayerRoom room = await hostConnection.InvokeAsync<MultiplayerRoom>(
            nameof(IMultiplayerLoungeServer.CreateRoom),
            CreateRoomRequest(OsuMatchType.HeadToHead));

        Assert.That(hostClient.UserJoinedCount, Is.EqualTo(0));

        // The guest joins: they must not receive their own UserJoined either,
        // while the host (already subscribed) receives the guest's.
        await guestConnection.InvokeAsync<MultiplayerRoom>(nameof(IMultiplayerLoungeServer.JoinRoom), room.RoomID);

        Assert.That(await hostClient.WaitForUserJoinedAsync(2), Is.True);
        Assert.That(guestClient.UserJoinedCount, Is.EqualTo(0));
        Assert.That(hostClient.UserLeftCount, Is.EqualTo(0));
        Assert.That(guestClient.UserLeftCount, Is.EqualTo(0));
    }

    [Test]
    public async Task TeamVersus_ThreeClients_TeamsAndChangeTeam()
    {
        await using HubConnection hostConnection = await ConnectAsync(1);
        await using HubConnection guestAConnection = await ConnectAsync(2);
        await using HubConnection guestBConnection = await ConnectAsync(3);

        TestMultiplayerClient hostClient = new(hostConnection);
        TestMultiplayerClient guestAClient = new(guestAConnection);
        TestMultiplayerClient guestBClient = new(guestBConnection);
        await hostClient.SubscribeAsync();
        await guestAClient.SubscribeAsync();
        await guestBClient.SubscribeAsync();

        MultiplayerRoom room = await hostConnection.InvokeAsync<MultiplayerRoom>(
            nameof(IMultiplayerLoungeServer.CreateRoom),
            CreateRoomRequest(OsuMatchType.TeamVersus));

        await guestAConnection.InvokeAsync<MultiplayerRoom>(nameof(IMultiplayerLoungeServer.JoinRoom), room.RoomID);
        MultiplayerRoom joinedB = await guestBConnection.InvokeAsync<MultiplayerRoom>(nameof(IMultiplayerLoungeServer.JoinRoom), room.RoomID);

        Assert.That(joinedB.MatchState, Is.TypeOf<TeamVersusRoomState>());
        TeamVersusRoomState teamState = (TeamVersusRoomState)joinedB.MatchState!;
        Assert.That(teamState.Teams, Has.Count.EqualTo(2));

        int?[] teams = joinedB.Users.Select(u => (u.MatchState as TeamVersusUserState)?.TeamID).ToArray();
        Assert.That(teams, Is.All.InRange(0, 1));

        // Change team of guest A to the opposite team of guest B.
        int guestBTeam = teams[2]!.Value;
        await guestAConnection.InvokeAsync(
            nameof(IMultiplayerRoomServer.SendMatchRequest),
            new ChangeTeamRequest { TeamID = guestBTeam == 0 ? 1 : 0 });

        // The room state is not re-queryable through the hub; assert via the shared manager instead.
        RoomManager manager = _server.Services.GetRequiredService<RoomManager>();
        IRoom realRoom = manager.GetRoom(room.RoomID)!;
        TeamVersusUserState? guestAState = realRoom.Users.First(u => u.UserID == 2).MatchState as TeamVersusUserState;
        Assert.That(guestAState!.TeamID, Is.EqualTo(guestBTeam == 0 ? 1 : 0));
    }

    [Test]
    public async Task KickUser_AsHost_RemovesUserAndBroadcasts()
    {
        await using HubConnection hostConnection = await ConnectAsync(1);
        await using HubConnection guestConnection = await ConnectAsync(2);

        TestMultiplayerClient hostClient = new(hostConnection);
        TestMultiplayerClient guestClient = new(guestConnection);
        await hostClient.SubscribeAsync();
        await guestClient.SubscribeAsync();

        MultiplayerRoom room = await hostConnection.InvokeAsync<MultiplayerRoom>(
            nameof(IMultiplayerLoungeServer.CreateRoom),
            CreateRoomRequest(OsuMatchType.HeadToHead));

        await guestConnection.InvokeAsync<MultiplayerRoom>(nameof(IMultiplayerLoungeServer.JoinRoom), room.RoomID);

        await hostConnection.InvokeAsync(nameof(IMultiplayerRoomServer.KickUser), 2);

        Assert.That(await hostClient.WaitForUserKickedAsync(2), Is.True);
        Assert.That(await guestClient.WaitForUserKickedAsync(2), Is.True);
    }

    [Test]
    public async Task ServiceProvider_CanConstructAllServices()
    {
        // Build a real web host (same pipeline as Program.ConfigureServices) and
        // verify the singleton services resolve without scoped-service violations.
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<FakeRoomRepository>(_roomRepository);
        builder.Services.AddSingleton<IRoomRepository>(_roomRepository);
        builder.Services.AddSingleton<IChatRepository>(new FakeChatRepository());
        builder.Services.AddSingleton(new InterProcessCommunicationClient(new NoOpTransport(), "realtime-tests"));
        builder.Services.AddSingleton<MultiplayerEventDispatcher>();
        builder.Services.AddSingleton(new RulesetManager(Path.GetTempPath()));
        builder.Services.AddSingleton<IRulesetHashValidator, RulesetHashValidator>();
        builder.Services.AddSingleton<IRoomFacade>(serviceProvider => new RoomFacade(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<MultiplayerEventDispatcher>(),
            serviceProvider.GetService<ILogger<RoomFacade>>())
        {
            RulesetManager = serviceProvider.GetRequiredService<RulesetManager>(),
            RulesetHashValidator = serviceProvider.GetRequiredService<IRulesetHashValidator>(),
        });
        builder.Services.AddSingleton<RoomManager>();
        builder.Services.AddSingleton<PlayerManager>();
        builder.Services.AddSignalR()
            .AddMessagePackProtocol(options => options.SerializerOptions = SignalRUnionWorkaroundResolver.OPTIONS);

        WebApplication app = builder.Build();
        await app.StartAsync();

        try
        {
            Assert.That(app.Services.GetRequiredService<IRoomFacade>(), Is.Not.Null);
            Assert.That(app.Services.GetRequiredService<RoomManager>(), Is.Not.Null);
            Assert.That(app.Services.GetRequiredService<PlayerManager>(), Is.Not.Null);
            Assert.That(app.Services.GetRequiredService<MultiplayerEventDispatcher>(), Is.Not.Null);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Test]
    public async Task WrongPassword_JoinFails()
    {
        await using HubConnection hostConnection = await ConnectAsync(1);
        await using HubConnection guestConnection = await ConnectAsync(2);

        MultiplayerRoom room = await hostConnection.InvokeAsync<MultiplayerRoom>(
            nameof(IMultiplayerLoungeServer.CreateRoom),
            CreateRoomRequest(OsuMatchType.HeadToHead, password: "secret"));

        Exception? joinException = null;
        try
        {
            await guestConnection.InvokeAsync<MultiplayerRoom>(
                nameof(IMultiplayerLoungeServer.JoinRoomWithPassword),
                room.RoomID,
                "wrong");
        }
        catch (Exception exception)
        {
            joinException = exception;
        }

        Assert.That(joinException, Is.TypeOf<HubException>());
    }

    private static void RegisterLazerMultiplayerResponders(InterProcessCommunicationClient lazerIpcClient)
    {
        lazerIpcClient.RegisterResponder<object, object>("create_mp_channel", _ => new { channel_id = 1 });
        lazerIpcClient.RegisterResponder<object, object>("make_user_join_mp_channel", _ => new { });
        lazerIpcClient.RegisterResponder<object, object>("make_user_leave_mp_channel", _ => new { });
    }

    private async Task<HubConnection> ConnectAsync(
        int userId,
        IReadOnlyDictionary<string, string>? rulesetHashes = null)
    {
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(_server.BaseAddress, $"/signalr/multiplayer?userId={userId}"),
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => _server.CreateHandler();
                    if (rulesetHashes != null)
                    {
                        options.Headers["X-Osu-Ruleset-Hashes"] = JsonSerializer.Serialize(rulesetHashes);
                    }
                })
            .AddMessagePackProtocol(options =>
            {
                options.SerializerOptions = SignalRUnionWorkaroundResolver.OPTIONS;
            })
            .Build();

        // Provide the user id through the query string so the JwtUserIdProvider
        // fallback can pick it up.
        connection.On<object>("__noop", _ => { });

        await connection.StartAsync();
        return connection;
    }

    private static MultiplayerRoom CreateRoomRequest(OsuMatchType type, string? password = null)
    {
        return new MultiplayerRoom(0)
        {
            Settings = new MultiplayerRoomSettings
            {
                Name = "integration room",
                MatchType = type,
                Password = password ?? string.Empty,
                QueueMode = QueueMode.HostOnly,
                AutoStartDuration = TimeSpan.Zero,
            },
            Playlist = new List<MultiplayerPlaylistItem>
            {
                new()
                {
                    ID = 0,
                    OwnerID = 1,
                    BeatmapID = 100,
                    RulesetID = 0,
                    RequiredMods = [],
                    AllowedMods = [],
                    Freestyle = false,
                    Expired = false,
                    PlaylistOrder = 0,
                    WinCondition = osu.Game.Online.Multiplayer.WinCondition.Score,
                },
            },
        };
    }

    /// <summary>
    /// User ID provider which returns a fixed test user id.
    /// </summary>
    private sealed class TestUserIdProvider : IUserIdProvider
    {
        public string? GetUserId(HubConnectionContext connection)
        {
            // In a real deployment this reads the JWT; the integration test host
            // has no auth so fall back to the connection's query string.
            Microsoft.AspNetCore.Http.IQueryCollection? query = connection.GetHttpContext()?.Request.Query;
            return query?["userId"].ToString();
        }
    }

    private sealed class TestMultiplayerClient(HubConnection connection) : IDisposable
    {
        private readonly List<MultiplayerRoomState> _roomStates = new();
        private readonly List<int> _userJoined = new();
        private readonly List<int> _userLeft = new();
        private readonly List<int> _userKicked = new();
        private readonly TaskCompletionSource<bool> _loadRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _gameplayStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _resultsReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _gate = new(1, 1);

        public int UserJoinedCount
        {
            get
            {
                lock (_userJoined)
                {
                    return _userJoined.Count;
                }
            }
        }

        public int UserLeftCount
        {
            get
            {
                lock (_userLeft)
                {
                    return _userLeft.Count;
                }
            }
        }

        public async Task SubscribeAsync()
        {
            connection.On<MultiplayerRoomState>(nameof(IMultiplayerClient.RoomStateChanged), state =>
            {
                lock (_roomStates)
                {
                    _roomStates.Add(state);
                }
            });

            connection.On<MultiplayerRoomUser>(nameof(IMultiplayerClient.UserJoined), user =>
            {
                lock (_userJoined)
                {
                    _userJoined.Add(user.UserID);
                }
            });

            connection.On<MultiplayerRoomUser>(nameof(IMultiplayerClient.UserLeft), user =>
            {
                lock (_userLeft)
                {
                    _userLeft.Add(user.UserID);
                }
            });

            connection.On<MultiplayerRoomUser>(nameof(IMultiplayerClient.UserKicked), user =>
            {
                lock (_userKicked)
                {
                    _userKicked.Add(user.UserID);
                }
            });

            connection.On(nameof(IMultiplayerClient.LoadRequested), () => _loadRequested.TrySetResult(true));
            connection.On(nameof(IMultiplayerClient.GameplayStarted), () => _gameplayStarted.TrySetResult(true));
            connection.On(nameof(IMultiplayerClient.ResultsReady), () => _resultsReady.TrySetResult(true));

            await Task.CompletedTask;
        }

        public void Dispose()
        {
            _gate.Dispose();
        }

        public async Task<bool> WaitForRoomStateAsync(MultiplayerRoomState expected, int timeoutMs = 5000)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                lock (_roomStates)
                {
                    if (_roomStates.Contains(expected))
                    {
                        return true;
                    }
                }

                await Task.Delay(20);
            }

            return false;
        }

        // ReSharper disable once InconsistentlySynchronizedField
        public Task<bool> WaitForUserJoinedAsync(int userId, int timeoutMs = 5000) => WaitForListAsync(_userJoined, userId, timeoutMs);

        // ReSharper disable once InconsistentlySynchronizedField
        public Task<bool> WaitForUserKickedAsync(int userId, int timeoutMs = 5000) => WaitForListAsync(_userKicked, userId, timeoutMs);

        public Task<bool> WaitForLoadRequestedAsync(int timeoutMs = 5000) => _loadRequested.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));

        public Task<bool> WaitForGameplayStartedAsync(int timeoutMs = 5000) => _gameplayStarted.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));

        public Task<bool> WaitForResultsReadyAsync(int timeoutMs = 5000) => _resultsReady.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));

        private async Task<bool> WaitForListAsync(List<int> list, int value, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                lock (list)
                {
                    if (list.Contains(value))
                    {
                        return true;
                    }
                }

                await Task.Delay(20);
            }

            return false;
        }
    }
}

/// <summary>
/// No-op IPC transport for integration tests.
/// </summary>
internal sealed class NoOpTransport : IInterProcessCommunicationTransport
{
    public void Subscribe(string channel, Func<string, Task> handler)
    {
    }

    public void Unsubscribe(string channel)
    {
    }

    public Task PublishAsync(string channel, string payload) => Task.CompletedTask;
}