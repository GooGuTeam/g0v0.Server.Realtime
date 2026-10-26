// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

// This file intentionally hosts the multiplayer test base and a test hub helper.
#pragma warning disable MA0048 // File name must match type name
#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable NUnit1032 // IDisposable fields should be Disposed in TearDown (hub is not disposable)
#pragma warning disable SA1201 // Elements should appear in the correct order
#pragma warning disable SA1202 // Elements should be ordered by access

using System.Collections.Concurrent;
using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Realtime.Hubs;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Rooms;
using g0v0.Server.Realtime.Services;
using g0v0.Server.Realtime.Tests.Hubs;
using g0v0.Server.Realtime.Tests.Notifications;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Base class for multiplayer unit tests, providing the same style of Moq-based
/// client verification as osu-server-spectator's <c>MultiplayerTest</c>, adapted
/// to this server's <see cref="RoomManager"/>-based architecture.
/// </summary>
public abstract class MultiplayerTestBase
{
    protected const int UserId = 1234;
    protected const int UserId2 = 2345;

    protected const long RoomId = 8888;

    protected PlayerManager PlayerManager { get; private set; } = null!;

    protected RoomManager RoomManager { get; private set; } = null!;

    protected RoomFacade RoomFacade { get; private set; } = null!;

    internal FakeRoomRepository RoomRepository { get; private set; } = null!;

    internal FakeBeatmapRepository? BeatmapRepository { get; private set; }

    internal FakeRelationshipRepository? RelationshipRepository { get; private set; }

    internal FakeUserRepository? UserRepository { get; private set; }

    internal FakeChatRepository ChatRepository { get; private set; } = null!;

    /// <summary>
    /// Gets the service scope factory backing the test room facade.
    /// </summary>
    internal IServiceScopeFactory ScopeFactory { get; private set; } = null!;

    /// <summary>
    /// Gets the dispatcher broadcasting room events to the mocked clients.
    /// </summary>
    internal MultiplayerEventDispatcher EventDispatcher { get; private set; } = null!;

    /// <summary>
    /// Gets a value indicating whether gets whether beatmap validation should be enabled for this test fixture
    /// (registers a fake beatmap repository). Defaults to <see langword="false"/>
    /// so tests which add arbitrary beatmap IDs are unaffected.
    /// </summary>
    protected virtual bool EnableBeatmapValidation => false;

    /// <summary>
    /// Gets a value indicating whether gets whether user-relationship validation should be enabled for this test
    /// fixture (registers fake relationship and user repositories). Defaults to
    /// <see langword="false"/> so tests which invite arbitrary users are unaffected.
    /// </summary>
    protected virtual bool EnableRelationshipValidation => false;

    protected TestMultiplayerHub Hub { get; private set; } = null!;

    protected Mock<IHubClients<IMultiplayerClient>> Clients { get; private set; } = null!;

    protected Mock<IGroupManager> Groups { get; private set; } = null!;

    /// <summary>
    /// Gets a general non-gameplay receiver for the room with ID <see cref="RoomId"/>.
    /// </summary>
    protected Mock<DelegatingMultiplayerClient> Receiver { get; private set; } = null!;

    /// <summary>
    /// Gets a receiver specific to the user with ID <see cref="UserId"/>.
    /// </summary>
    protected Mock<DelegatingMultiplayerClient> UserReceiver { get; private set; } = null!;

    /// <summary>
    /// Gets a receiver specific to the user with ID <see cref="UserId2"/>.
    /// </summary>
    protected Mock<DelegatingMultiplayerClient> User2Receiver { get; private set; } = null!;

    protected Mock<HubCallerContext> ContextUser { get; private set; } = null!;

    protected Mock<HubCallerContext> ContextUser2 { get; private set; } = null!;

    private readonly Dictionary<string, List<string>> _groupMapping = new(StringComparer.Ordinal);
    private readonly Dictionary<int, DelegatingMultiplayerClient> _clientMapping = new();

    [SetUp]
    public void SetUp()
    {
        _groupMapping.Clear();
        _clientMapping.Clear();

        RoomRepository = new FakeRoomRepository();
        BeatmapRepository = EnableBeatmapValidation ? new FakeBeatmapRepository() : null;
        RelationshipRepository = EnableRelationshipValidation ? new FakeRelationshipRepository() : null;
        UserRepository = EnableRelationshipValidation ? new FakeUserRepository() : null;
        ChatRepository = new FakeChatRepository();
        FakeMultiplayerScopeFactory scopeFactory = new(RoomRepository, BeatmapRepository, RelationshipRepository, UserRepository, ChatRepository);
        ScopeFactory = scopeFactory;

        RulesetManager rulesetManager = new(Path.GetTempPath());
        NoOpMultiplayerTransport ipcTransport = new();
        InterProcessCommunicationClient ipcClient = new(ipcTransport, "realtime-tests");
        InterProcessCommunicationClient lazerIpcClient = new(ipcTransport, "lazer");
        RegisterLazerMultiplayerResponders(lazerIpcClient);
        PlayerManager = new PlayerManager(NullLogger<PlayerManager>.Instance, ipcClient);
        IRulesetHashValidator rulesetHashValidator = new RulesetHashValidator(
            rulesetManager,
            ipcClient,
            PlayerManager,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<RulesetHashValidator>.Instance);
        RoomFacade roomFacade = new(scopeFactory, null!, NullLogger<RoomFacade>.Instance)
        {
            RulesetManager = rulesetManager,
            RulesetHashValidator = rulesetHashValidator,
        };
        RoomFacade = roomFacade;
        RoomManager = new RoomManager(NullLogger<RoomManager>.Instance, scopeFactory, roomFacade);

        Clients = new Mock<IHubClients<IMultiplayerClient>>();
        Groups = new Mock<IGroupManager>();

        Receiver = new Mock<DelegatingMultiplayerClient> { CallBase = true };
        Receiver.Setup(c => c.Clients).Returns(GetClientsForGroup(MultiplayerEventDispatcher.GetGroupId(RoomId)));

        // Generic group receiver.
        Clients.Setup(clients => clients.Group(It.IsAny<string>())).Returns<string>(groupName =>
        {
            Mock<DelegatingMultiplayerClient> groupReceiver = new() { CallBase = true };
            groupReceiver.Setup(c => c.Clients).Returns(GetClientsForGroup(groupName));
            return groupReceiver.Object;
        });

        // Room-specific group receiver.
        Clients.Setup(clients => clients.Group(MultiplayerEventDispatcher.GetGroupId(RoomId))).Returns(Receiver.Object);

        // Per-user receivers.
        Clients.Setup(clients => clients.User(It.IsAny<string>())).Returns<string>(userId => !int.TryParse(userId, out int id)
            ? Mock.Of<IMultiplayerClient>()
            : _clientMapping.TryGetValue(id, out DelegatingMultiplayerClient? client)
                ? client
                : Mock.Of<IMultiplayerClient>());

        Clients.Setup(clients => clients.Client(It.IsAny<string>())).Returns<string>(connectionId => !int.TryParse(connectionId, out int id)
            ? Mock.Of<IMultiplayerClient>()
            : _clientMapping.TryGetValue(id, out DelegatingMultiplayerClient? client)
                ? client
                : Mock.Of<IMultiplayerClient>());

        Groups.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((connectionId, groupId, _) =>
            {
                if (!_groupMapping.TryGetValue(groupId, out List<string>? connectionIds))
                {
                    _groupMapping[groupId] = connectionIds = new List<string>();
                }

                connectionIds.Add(connectionId);
            });
        Groups.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((connectionId, groupId, _) =>
            {
                if (!_groupMapping.TryGetValue(groupId, out List<string>? connectionIds))
                {
                    _groupMapping[groupId] = connectionIds = new List<string>();
                }

                connectionIds.Remove(connectionId);
            });

        Mock<IHubContext<MultiplayerHub, IMultiplayerClient>> hubContext = new();
        hubContext.Setup(ctx => ctx.Groups).Returns(Groups.Object);
        hubContext.Setup(ctx => ctx.Clients).Returns(Clients.Object);

        MultiplayerEventDispatcher eventDispatcher = new(hubContext.Object, NullLogger<MultiplayerEventDispatcher>.Instance);
        roomFacade.EventDispatcher = eventDispatcher;
        EventDispatcher = eventDispatcher;

        Hub = new TestMultiplayerHub(
            PlayerManager,
            RoomManager,
            eventDispatcher,
            scopeFactory);

        CreateUser(UserId, out Mock<HubCallerContext> contextUser, out Mock<DelegatingMultiplayerClient> userReceiver);
        ContextUser = contextUser;
        UserReceiver = userReceiver;

        CreateUser(UserId2, out Mock<HubCallerContext> contextUser2, out Mock<DelegatingMultiplayerClient> user2Receiver);
        ContextUser2 = contextUser2;
        User2Receiver = user2Receiver;

        SetUserContext(ContextUser);

        IEnumerable<IMultiplayerClient> GetClientsForGroup(string groupName)
        {
            if (!_groupMapping.TryGetValue(groupName, out List<string>? connectionIds))
            {
                yield break;
            }

            foreach (string id in connectionIds)
            {
                if (_clientMapping.TryGetValue(int.Parse(id), out DelegatingMultiplayerClient? client))
                {
                    yield return client;
                }
            }
        }
    }

    private static void RegisterLazerMultiplayerResponders(InterProcessCommunicationClient lazerIpcClient)
    {
        lazerIpcClient.RegisterResponder<object, object>("create_mp_channel", _ => new { channel_id = 1 });
        lazerIpcClient.RegisterResponder<object, object>("make_user_join_mp_channel", _ => new { });
        lazerIpcClient.RegisterResponder<object, object>("make_user_leave_mp_channel", _ => new { });
    }

    protected void CreateUser(int userId, out Mock<HubCallerContext> context, out Mock<DelegatingMultiplayerClient> client)
    {
        context = new Mock<HubCallerContext>();
        context.Setup(c => c.UserIdentifier).Returns(userId.ToString());
        context.Setup(c => c.ConnectionId).Returns(userId.ToString());

        client = new Mock<DelegatingMultiplayerClient>();
        _clientMapping[userId] = client.Object;

        Clients.Setup(clients => clients.Client(userId.ToString())).Returns(client.Object);
        Clients.Setup(clients => clients.User(userId.ToString())).Returns(client.Object);

        // Create the new state for the user by invoking OnConnectedAsync().
        HubCallerContext lastContext = Hub.Context;
        SetUserContext(context);
        Hub.OnConnectedAsync().Wait();
        Hub.Context = lastContext;
    }

    /// <summary>
    /// Sets the multiplayer hub's current user context.
    /// </summary>
    /// <param name="context">The user context.</param>
    protected void SetUserContext(Mock<HubCallerContext> context) => Hub.Context = context.Object;

    protected async Task MarkCurrentUserReadyAndAvailable()
    {
        await Hub.ChangeState(MultiplayerUserState.Ready);
        await Hub.ChangeBeatmapAvailability(BeatmapAvailability.LocallyAvailable());
    }

    protected async Task LoadAndFinishGameplay(params Mock<HubCallerContext>[] users)
    {
        await LoadGameplay(users);
        await FinishGameplay(users);
    }

    protected async Task LoadGameplay(params Mock<HubCallerContext>[] users)
    {
        foreach (Mock<HubCallerContext> u in users)
        {
            SetUserContext(u);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
        }
    }

    protected async Task FinishGameplay(params Mock<HubCallerContext>[] users)
    {
        foreach (Mock<HubCallerContext> u in users)
        {
            SetUserContext(u);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);
        }
    }
}

/// <summary>
/// Test hub which joins or creates a room with a given ID (mirrors
/// osu-server-spectator's <see cref="TestMultiplayerHub"/> semantics).
/// </summary>
public class TestMultiplayerHub : MultiplayerHub
{
    private readonly RoomManager _roomManager;

    public TestMultiplayerHub(
        PlayerManager playerManager,
        RoomManager roomManager,
        MultiplayerEventDispatcher eventDispatcher,
        IServiceScopeFactory scopeFactory)
        : base(
            playerManager,
            NullLogger<MultiplayerHub>.Instance,
            roomManager,
            eventDispatcher,
            scopeFactory)
    {
        this._roomManager = roomManager;
    }

    /// <summary>
    /// Joins the room with the given ID, creating it first if it does not exist.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>The joined room.</returns>
    public new Task<MultiplayerRoom> JoinRoom(long roomId)
        => JoinRoomWithPassword(roomId, string.Empty);

    /// <summary>
    /// Joins the room with the given ID and password, creating it first if it
    /// does not exist.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="password">The room password.</param>
    /// <returns>The joined room.</returns>
    public new Task<MultiplayerRoom> JoinRoomWithPassword(long roomId, string password)
    {
        if (CheckRoomExists(roomId))
        {
            return base.JoinRoomWithPassword(roomId, password);
        }

        // Mirror osu-server-spectator's test setup: newly created rooms are
        // seeded with a single playlist item so matches can start.
        MultiplayerRoom newRoom = new(roomId)
        {
            Settings = { Password = password },
            Playlist = new List<MultiplayerPlaylistItem>
            {
                new()
                {
                    OwnerID = 1234,
                    BeatmapID = 1234,
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

        return CreateRoom(newRoom);
    }

    /// <summary>
    /// Joins the room with the given ID without creating it.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>The joined room.</returns>
    public Task<MultiplayerRoom> JoinRoomExplicit(long roomId)
        => base.JoinRoom(roomId);

    /// <summary>
    /// Joins the room with the given ID and password without creating it.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="password">The room password.</param>
    /// <returns>The joined room.</returns>
    public Task<MultiplayerRoom> JoinRoomWithPasswordExplicit(long roomId, string password)
        => base.JoinRoomWithPassword(roomId, password);

    /// <summary>
    /// Gets the room with the given ID from the room manager.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>The room, or <see langword="null"/> if not tracked.</returns>
    public IRoom? GetRoom(long roomId) => _roomManager.GetRoom(roomId);

    /// <summary>
    /// Determines whether a room with the given ID is currently tracked.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns><see langword="true"/> when the room exists.</returns>
    public bool CheckRoomExists(long roomId) => _roomManager.GetRoom(roomId) != null;
}

/// <summary>
/// Scope factory resolving the fake room repository.
/// </summary>
internal sealed class FakeMultiplayerScopeFactory(
    FakeRoomRepository roomRepository,
    FakeBeatmapRepository? beatmapRepository = null,
    FakeRelationshipRepository? relationshipRepository = null,
    FakeUserRepository? userRepository = null,
    IChatRepository? chatRepository = null) : IServiceScopeFactory
{
    public IServiceScope CreateScope() => new FakeScope(roomRepository, beatmapRepository, relationshipRepository, userRepository, chatRepository);

    private sealed class FakeScope(
        FakeRoomRepository roomRepository,
        FakeBeatmapRepository? beatmapRepository,
        FakeRelationshipRepository? relationshipRepository,
        FakeUserRepository? userRepository,
        IChatRepository? chatRepository) : IServiceScope
    {
        public IServiceProvider ServiceProvider => new FakeProvider(roomRepository, beatmapRepository, relationshipRepository, userRepository, chatRepository);

        public void Dispose()
        {
        }
    }

    private sealed class FakeProvider(
        FakeRoomRepository roomRepository,
        FakeBeatmapRepository? beatmapRepository,
        FakeRelationshipRepository? relationshipRepository,
        FakeUserRepository? userRepository,
        IChatRepository? chatRepository) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(IRoomRepository)
                ? roomRepository
                : serviceType == typeof(IBeatmapRepository)
                    ? beatmapRepository
                    : serviceType == typeof(IRelationshipRepository)
                        ? relationshipRepository
                        : serviceType == typeof(IUserRepository)
                            ? userRepository
                            : serviceType == typeof(IChatRepository)
                                ? chatRepository
                                : (object?)null;
        }
    }
}

/// <summary>
/// In-memory <see cref="IBeatmapRepository"/> used by beatmap-validation tests.
/// Returns a default beatmap (checksum <c>"checksum"</c>) for any ID unless the
/// ID is listed in <see cref="MissingBeatmapIds"/> (mirrors osu-server-spectator's
/// <c>Database.Setup(db =&gt; db.GetBeatmapAsync(It.IsAny&lt;int&gt;()))</c> default).
/// </summary>.
internal sealed class FakeBeatmapRepository : IBeatmapRepository
{
    public List<Beatmap> Beatmaps { get; } =
    [
        new() { Id = 1234, BeatmapSetId = 1, Mode = 0, Checksum = "checksum", DifficultyRating = 4.2 },
        new() { Id = 12345, BeatmapSetId = 1, Mode = 0, Checksum = "checksum2", DifficultyRating = 5.5 },
        new() { Id = 123456, BeatmapSetId = 1, Mode = 0, Checksum = "checksum3", DifficultyRating = 6.0 },
        new() { Id = 3333, BeatmapSetId = 4, Mode = 0, Checksum = "3333", DifficultyRating = 6.5 },
        new() { Id = 4444, BeatmapSetId = 5, Mode = 0, Checksum = "4444", DifficultyRating = 7.1 },
        new() { Id = 100, BeatmapSetId = 6, Mode = 1, Checksum = "checksum", DifficultyRating = 5.0 }
    ];

    /// <summary>
    /// Gets the set of beatmap IDs which resolve to <see langword="null"/>.
    /// </summary>
    public HashSet<int> MissingBeatmapIds { get; } = new();

    public Task<IReadOnlyList<Beatmap>> GetByFilenameAsync(string filename)
    {
        string? version = BeatmapFilename.ExtractVersion(filename);
        IReadOnlyList<Beatmap> matches = version is null
            ? []
            : Beatmaps.Where(beatmap => string.Equals(beatmap.Version, version, StringComparison.Ordinal)).ToArray();
        return Task.FromResult(matches);
    }

    public Task<Beatmap?> GetByIdAsync(int beatmapId)
    {
        return MissingBeatmapIds.Contains(beatmapId)
            ? Task.FromResult<Beatmap?>(null)
            : Task.FromResult<Beatmap?>(
                Beatmaps.Find(b => b.Id == beatmapId)
                ?? new Beatmap { Id = beatmapId, BeatmapSetId = beatmapId, Mode = 0, Checksum = "checksum", DifficultyRating = 4.2 });
    }

    public Task<Beatmap?> GetByChecksumAsync(string checksum)
        => Task.FromResult(Beatmaps.Find(b => string.Equals(b.Checksum, checksum, StringComparison.Ordinal)));

    public async Task<Beatmap> GetOrFetchByIdAsync(int beatmapId, g0v0.Server.Common.Fetching.IFetcher fetcher, CancellationToken cancellationToken = default)
        => await GetByIdAsync(beatmapId) ?? throw new InvalidOperationException($"Beatmap {beatmapId} is missing.");

    public async Task<Beatmap> GetOrFetchByChecksumAsync(string checksum, g0v0.Server.Common.Fetching.IFetcher fetcher, CancellationToken cancellationToken = default)
        => await GetByChecksumAsync(checksum) ?? throw new InvalidOperationException($"Beatmap {checksum} is missing.");

    public Task<IReadOnlyList<Beatmap>> GetByBeatmapSetIdAsync(int beatmapSetId)
        => Task.FromResult<IReadOnlyList<Beatmap>>(Beatmaps.Where(b => b.BeatmapSetId == beatmapSetId).ToList());

    public Task<IReadOnlyList<Beatmap>> GetByMapperIdAsync(int mapperId)
        => Task.FromResult<IReadOnlyList<Beatmap>>([]);

    public Task CreateAsync(Beatmap beatmap) => Task.CompletedTask;

    public Task UpdateAsync(Beatmap beatmap) => Task.CompletedTask;

    public Task DeleteAsync(Beatmap beatmap) => Task.CompletedTask;
}

/// <summary>
/// In-memory <see cref="IRelationshipRepository"/> with a configurable set of
/// block/follow relationships.
/// </summary>
internal sealed class FakeRelationshipRepository : IRelationshipRepository
{
    private readonly List<Relationship> _relationships = new();

    public void AddBlock(int userId, int targetId)
        => _relationships.Add(new Relationship { UserId = userId, TargetId = targetId, Type = RelationshipType.Block });

    public void AddFollow(int userId, int targetId)
        => _relationships.Add(new Relationship { UserId = userId, TargetId = targetId, Type = RelationshipType.Follow });

    public Task<int[]> GetAllFriendIds(int userId)
        => Task.FromResult(_relationships.Where(r => r.UserId == userId && r.Type == RelationshipType.Follow).Select(r => r.TargetId).ToArray());

    public Task<IReadOnlyList<Relationship>> GetByUserIdAsync(int userId)
        => Task.FromResult<IReadOnlyList<Relationship>>(_relationships.Where(r => r.UserId == userId).ToList());

    public Task<IReadOnlyList<Relationship>> GetByTargetIdAsync(int targetId)
        => Task.FromResult<IReadOnlyList<Relationship>>(_relationships.Where(r => r.TargetId == targetId).ToList());

    public Task<Relationship?> GetRelationshipAsync(int userId, int targetId)
        => Task.FromResult(_relationships.Find(r => r.UserId == userId && r.TargetId == targetId));

    public Task<bool> IsFollowingAsync(int userId, int targetId)
        => Task.FromResult(_relationships.Exists(r => r.UserId == userId && r.TargetId == targetId && r.Type == RelationshipType.Follow));

    public Task<bool> IsBlockedAsync(int userId, int targetId)
        => Task.FromResult(_relationships.Exists(r => r.UserId == userId && r.TargetId == targetId && r.Type == RelationshipType.Block));

    public Task CreateAsync(Relationship relationship)
    {
        _relationships.Add(relationship);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<int>> GetBlockedUserIdsAsync(int userId)
        => Task.FromResult<IReadOnlyList<int>>(_relationships
            .Where(r => r.UserId == userId && r.Type == RelationshipType.Block)
            .Select(r => r.TargetId)
            .ToList());

    public Task UpdateAsync(Relationship relationship) => Task.CompletedTask;

    public Task DeleteAsync(Relationship relationship)
    {
        _relationships.Remove(relationship);
        return Task.CompletedTask;
    }
}

/// <summary>
/// In-memory <see cref="IUserRepository"/> with a configurable set of users.
/// </summary>
internal sealed class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<int, User> _users = new();

    public void AddUser(User user) => _users[user.Id] = user;

    public Task<User?> GetByIdAsync(int userId)
        => Task.FromResult(_users.GetValueOrDefault(userId));

    public Task<IReadOnlyList<User>> GetByIdsAsync(IReadOnlyList<int> userIds)
        => Task.FromResult<IReadOnlyList<User>>(userIds.Where(_users.ContainsKey).Select(userId => _users[userId]).ToList());

    public Task<string?> GetUsernameByIdAsync(int userId)
        => Task.FromResult(_users.TryGetValue(userId, out User? user) ? user.Username : null);

    public Task<User?> GetByUsernameAsync(string username)
        => Task.FromResult(_users.Values.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.Ordinal)));

    public Task<bool> UsernameExistsAsync(string username)
        => Task.FromResult(_users.Values.Any(u => string.Equals(u.Username, username, StringComparison.Ordinal)));

    public Task<User?> GetByEmailAsync(string email)
        => Task.FromResult(_users.Values.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.Ordinal)));

    public async Task CreateAsync(User user, IReadOnlyList<int> statisticsModes, CancellationToken cancellationToken = default)
    {
        await CreateAsync(user).ConfigureAwait(false);
    }

    public Task CreateAsync(User user)
    {
        _users[user.Id] = user;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(User user)
    {
        _users[user.Id] = user;
        return Task.CompletedTask;
    }
}

/// <summary>
/// No-op IPC transport for multiplayer tests.
/// </summary>
internal sealed class NoOpMultiplayerTransport : IInterProcessCommunicationTransport
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<Func<string, Task>>> _subscriptions = new(StringComparer.Ordinal);

    public Task PublishAsync(string channel, string payload)
        => !_subscriptions.TryGetValue(channel, out ConcurrentBag<Func<string, Task>>? handlers)
            ? Task.CompletedTask
            : Task.WhenAll(handlers.Select(handler => handler(payload)));

    public void Subscribe(string channel, Func<string, Task> handler)
    {
        _subscriptions.GetOrAdd(channel, _ => []).Add(handler);
    }
}

/// <summary>
/// Test hub caller context with configurable identity.
/// </summary>
internal sealed class TestMultiplayerCallerContext(string connectionId, string userIdentifier) : HubCallerContext
{
    private readonly FeatureCollection _features = new();
    private readonly Dictionary<object, object?> _items = [];

    public override string ConnectionId => connectionId;

    public override string UserIdentifier => userIdentifier;

    public override System.Security.Claims.ClaimsPrincipal? User => null;

    public override IDictionary<object, object?> Items => _items;

    public override IFeatureCollection Features => _features;

    public override CancellationToken ConnectionAborted => CancellationToken.None;

    public override void Abort()
    {
    }
}