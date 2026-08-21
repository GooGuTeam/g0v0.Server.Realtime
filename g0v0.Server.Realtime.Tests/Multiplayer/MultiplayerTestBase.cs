// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

// This file intentionally hosts the multiplayer test base and a test hub helper.
#pragma warning disable MA0048 // File name must match type name
#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable NUnit1032 // IDisposable fields should be Disposed in TearDown (hub is not disposable)
#pragma warning disable SA1201 // Elements should appear in the correct order
#pragma warning disable SA1202 // Elements should be ordered by access

using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Realtime.Hubs;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Rooms;
using g0v0.Server.Realtime.Tests.Hubs;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using osu.Game.Online.API;
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
    protected const int USERID = 1234;
    protected const int USERID2 = 2345;

    protected const long ROOMID = 8888;

    protected PlayerManager PlayerManager { get; private set; } = null!;

    protected RoomManager RoomManager { get; private set; } = null!;

    protected RoomFacade RoomFacade { get; private set; } = null!;

    internal FakeRoomRepository RoomRepository { get; private set; } = null!;

    internal FakeBeatmapRepository? BeatmapRepository { get; private set; }

    internal FakeRelationshipRepository? RelationshipRepository { get; private set; }

    internal FakeUserRepository? UserRepository { get; private set; }

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
    /// Gets a general non-gameplay receiver for the room with ID <see cref="ROOMID"/>.
    /// </summary>
    protected Mock<DelegatingMultiplayerClient> Receiver { get; private set; } = null!;

    /// <summary>
    /// Gets a receiver specific to the user with ID <see cref="USERID"/>.
    /// </summary>
    protected Mock<DelegatingMultiplayerClient> UserReceiver { get; private set; } = null!;

    /// <summary>
    /// Gets a receiver specific to the user with ID <see cref="USERID2"/>.
    /// </summary>
    protected Mock<DelegatingMultiplayerClient> User2Receiver { get; private set; } = null!;

    protected Mock<HubCallerContext> ContextUser { get; private set; } = null!;

    protected Mock<HubCallerContext> ContextUser2 { get; private set; } = null!;

    private readonly Dictionary<string, List<string>> groupMapping = new(StringComparer.Ordinal);
    private readonly Dictionary<int, DelegatingMultiplayerClient> clientMapping = new();

    [SetUp]
    public void SetUp()
    {
        groupMapping.Clear();
        clientMapping.Clear();

        RoomRepository = new FakeRoomRepository();
        BeatmapRepository = EnableBeatmapValidation ? new FakeBeatmapRepository() : null;
        RelationshipRepository = EnableRelationshipValidation ? new FakeRelationshipRepository() : null;
        UserRepository = EnableRelationshipValidation ? new FakeUserRepository() : null;
        var scopeFactory = new FakeMultiplayerScopeFactory(RoomRepository, BeatmapRepository, RelationshipRepository, UserRepository);

        var roomFacade = new RoomFacade(scopeFactory, null!, NullLogger<RoomFacade>.Instance)
        {
            RulesetManager = new RulesetManager(Path.GetTempPath()),
        };
        RoomFacade = roomFacade;

        PlayerManager = new PlayerManager(NullLogger<PlayerManager>.Instance, new InterProcessCommunicationClient(new NoOpMultiplayerTransport(), "realtime-tests"));
        RoomManager = new RoomManager(NullLogger<RoomManager>.Instance, scopeFactory, roomFacade);

        Clients = new Mock<IHubClients<IMultiplayerClient>>();
        Groups = new Mock<IGroupManager>();

        Receiver = new Mock<DelegatingMultiplayerClient> { CallBase = true };
        Receiver.Setup(c => c.Clients).Returns(getClientsForGroup(MultiplayerEventDispatcher.GetGroupId(ROOMID)));

        // Generic group receiver.
        Clients.Setup(clients => clients.Group(It.IsAny<string>())).Returns<string>(groupName =>
        {
            var groupReceiver = new Mock<DelegatingMultiplayerClient> { CallBase = true };
            groupReceiver.Setup(c => c.Clients).Returns(getClientsForGroup(groupName));
            return groupReceiver.Object;
        });

        // Room-specific group receiver.
        Clients.Setup(clients => clients.Group(MultiplayerEventDispatcher.GetGroupId(ROOMID))).Returns(Receiver.Object);

        // Per-user receivers.
        Clients.Setup(clients => clients.User(It.IsAny<string>())).Returns<string>(userId =>
        {
            if (!int.TryParse(userId, out int id))
            {
                return Mock.Of<IMultiplayerClient>();
            }

            return clientMapping.TryGetValue(id, out var client) ? client : Mock.Of<IMultiplayerClient>();
        });

        Clients.Setup(clients => clients.Client(It.IsAny<string>())).Returns<string>(connectionId =>
        {
            if (!int.TryParse(connectionId, out int id))
            {
                return Mock.Of<IMultiplayerClient>();
            }

            return clientMapping.TryGetValue(id, out var client) ? client : Mock.Of<IMultiplayerClient>();
        });

        Groups.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Callback<string, string, CancellationToken>((connectionId, groupId, _) =>
              {
                  if (!groupMapping.TryGetValue(groupId, out var connectionIds))
                  {
                      groupMapping[groupId] = connectionIds = new List<string>();
                  }

                  connectionIds.Add(connectionId);
              });
        Groups.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Callback<string, string, CancellationToken>((connectionId, groupId, _) =>
              {
                  if (!groupMapping.TryGetValue(groupId, out var connectionIds))
                  {
                      groupMapping[groupId] = connectionIds = new List<string>();
                  }

                  connectionIds.Remove(connectionId);
              });

        var hubContext = new Mock<IHubContext<MultiplayerHub, IMultiplayerClient>>();
        hubContext.Setup(ctx => ctx.Groups).Returns(Groups.Object);
        hubContext.Setup(ctx => ctx.Clients).Returns(Clients.Object);

        var eventDispatcher = new MultiplayerEventDispatcher(hubContext.Object, NullLogger<MultiplayerEventDispatcher>.Instance);
        roomFacade.EventDispatcher = eventDispatcher;

        Hub = new TestMultiplayerHub(PlayerManager, RoomManager, eventDispatcher, scopeFactory);

        CreateUser(USERID, out Mock<HubCallerContext> contextUser, out Mock<DelegatingMultiplayerClient> userReceiver);
        ContextUser = contextUser;
        UserReceiver = userReceiver;

        CreateUser(USERID2, out Mock<HubCallerContext> contextUser2, out Mock<DelegatingMultiplayerClient> user2Receiver);
        ContextUser2 = contextUser2;
        User2Receiver = user2Receiver;

        SetUserContext(ContextUser);

        IEnumerable<IMultiplayerClient> getClientsForGroup(string groupName)
        {
            if (!groupMapping.TryGetValue(groupName, out var connectionIds))
            {
                yield break;
            }

            foreach (var id in connectionIds)
            {
                if (clientMapping.TryGetValue(int.Parse(id), out var client))
                {
                    yield return client;
                }
            }
        }
    }

    protected void CreateUser(int userId, out Mock<HubCallerContext> context, out Mock<DelegatingMultiplayerClient> client)
    {
        context = new Mock<HubCallerContext>();
        context.Setup(c => c.UserIdentifier).Returns(userId.ToString());
        context.Setup(c => c.ConnectionId).Returns(userId.ToString());

        client = new Mock<DelegatingMultiplayerClient>();
        clientMapping[userId] = client.Object;

        Clients.Setup(clients => clients.Client(userId.ToString())).Returns(client.Object);
        Clients.Setup(clients => clients.User(userId.ToString())).Returns(client.Object);

        // Create the new state for the user by invoking OnConnectedAsync().
        var lastContext = Hub.Context;
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
        foreach (var u in users)
        {
            SetUserContext(u);
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.ReadyForGameplay);
        }
    }

    protected async Task FinishGameplay(params Mock<HubCallerContext>[] users)
    {
        foreach (var u in users)
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
    private readonly RoomManager roomManager;

    public TestMultiplayerHub(
        PlayerManager playerManager,
        RoomManager roomManager,
        MultiplayerEventDispatcher eventDispatcher,
        IServiceScopeFactory scopeFactory)
        : base(playerManager, NullLogger<MultiplayerHub>.Instance, roomManager, eventDispatcher, scopeFactory)
    {
        this.roomManager = roomManager;
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
        var newRoom = new MultiplayerRoom(roomId)
        {
            Settings = { Password = password },
            Playlist = new List<MultiplayerPlaylistItem>
            {
                new()
                {
                    OwnerID = 1234,
                    BeatmapID = 1234,
                    RulesetID = 0,
                    RequiredMods = Array.Empty<APIMod>(),
                    AllowedMods = Array.Empty<APIMod>(),
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
    public IRoom? GetRoom(long roomId) => roomManager.GetRoom(roomId);

    /// <summary>
    /// Determines whether a room with the given ID is currently tracked.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns><see langword="true"/> when the room exists.</returns>
    public bool CheckRoomExists(long roomId) => roomManager.GetRoom(roomId) != null;
}

/// <summary>
/// Scope factory resolving the fake room repository.
/// </summary>
internal sealed class FakeMultiplayerScopeFactory(
    FakeRoomRepository roomRepository,
    FakeBeatmapRepository? beatmapRepository = null,
    FakeRelationshipRepository? relationshipRepository = null,
    FakeUserRepository? userRepository = null) : IServiceScopeFactory
{
    public IServiceScope CreateScope() => new FakeScope(roomRepository, beatmapRepository, relationshipRepository, userRepository);

    private sealed class FakeScope(
        FakeRoomRepository roomRepository,
        FakeBeatmapRepository? beatmapRepository,
        FakeRelationshipRepository? relationshipRepository,
        FakeUserRepository? userRepository) : IServiceScope
    {
        public IServiceProvider ServiceProvider => new FakeProvider(roomRepository, beatmapRepository, relationshipRepository, userRepository);

        public void Dispose()
        {
        }
    }

    private sealed class FakeProvider(
        FakeRoomRepository roomRepository,
        FakeBeatmapRepository? beatmapRepository,
        FakeRelationshipRepository? relationshipRepository,
        FakeUserRepository? userRepository) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IRoomRepository))
            {
                return roomRepository;
            }

            if (serviceType == typeof(IBeatmapRepository))
            {
                return beatmapRepository;
            }

            if (serviceType == typeof(IRelationshipRepository))
            {
                return relationshipRepository;
            }

            if (serviceType == typeof(IUserRepository))
            {
                return userRepository;
            }

            return null;
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
    public List<Beatmap> Beatmaps { get; } = new()
    {
        new Beatmap { Id = 1234, BeatmapSetId = 1, Mode = 0, Checksum = "checksum", DifficultyRating = 4.2 },
        new Beatmap { Id = 12345, BeatmapSetId = 1, Mode = 0, Checksum = "checksum2", DifficultyRating = 5.5 },
        new Beatmap { Id = 123456, BeatmapSetId = 1, Mode = 0, Checksum = "checksum3", DifficultyRating = 6.0 },
        new Beatmap { Id = 3333, BeatmapSetId = 4, Mode = 0, Checksum = "3333", DifficultyRating = 6.5 },
        new Beatmap { Id = 4444, BeatmapSetId = 5, Mode = 0, Checksum = "4444", DifficultyRating = 7.1 },
        new Beatmap { Id = 100, BeatmapSetId = 6, Mode = 1, Checksum = "checksum", DifficultyRating = 5.0 },
    };

    /// <summary>
    /// Gets the set of beatmap IDs which resolve to <see langword="null"/>.
    /// </summary>
    public HashSet<int> MissingBeatmapIds { get; } = new();

    public Task<Beatmap?> GetByIdAsync(int beatmapId)
    {
        if (MissingBeatmapIds.Contains(beatmapId))
        {
            return Task.FromResult<Beatmap?>(null);
        }

        return Task.FromResult<Beatmap?>(
            Beatmaps.FirstOrDefault(b => b.Id == beatmapId)
            ?? new Beatmap { Id = beatmapId, BeatmapSetId = beatmapId, Mode = 0, Checksum = "checksum", DifficultyRating = 4.2 });
    }

    public Task<Beatmap?> GetByChecksumAsync(string checksum)
        => Task.FromResult(Beatmaps.FirstOrDefault(b => b.Checksum == checksum));

    public Task<IReadOnlyList<Beatmap>> GetByBeatmapSetIdAsync(long beatmapSetId)
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
    private readonly List<Relationship> relationships = new();

    public void AddBlock(int userId, int targetId)
        => relationships.Add(new Relationship { UserId = userId, TargetId = targetId, Type = RelationshipType.Block });

    public void AddFollow(int userId, int targetId)
        => relationships.Add(new Relationship { UserId = userId, TargetId = targetId, Type = RelationshipType.Follow });

    public Task<int[]> GetAllFriendIds(int userId)
        => Task.FromResult(relationships.Where(r => r.UserId == userId && r.Type == RelationshipType.Follow).Select(r => r.TargetId).ToArray());

    public Task<IReadOnlyList<Relationship>> GetByUserIdAsync(int userId)
        => Task.FromResult<IReadOnlyList<Relationship>>(relationships.Where(r => r.UserId == userId).ToList());

    public Task<IReadOnlyList<Relationship>> GetByTargetIdAsync(int targetId)
        => Task.FromResult<IReadOnlyList<Relationship>>(relationships.Where(r => r.TargetId == targetId).ToList());

    public Task<Relationship?> GetRelationshipAsync(int userId, int targetId)
        => Task.FromResult(relationships.FirstOrDefault(r => r.UserId == userId && r.TargetId == targetId));

    public Task<bool> IsFollowingAsync(int userId, int targetId)
        => Task.FromResult(relationships.Any(r => r.UserId == userId && r.TargetId == targetId && r.Type == RelationshipType.Follow));

    public Task<bool> IsBlockedAsync(int userId, int targetId)
        => Task.FromResult(relationships.Any(r => r.UserId == userId && r.TargetId == targetId && r.Type == RelationshipType.Block));

    public Task CreateAsync(Relationship relationship)
    {
        relationships.Add(relationship);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Relationship relationship) => Task.CompletedTask;

    public Task DeleteAsync(Relationship relationship)
    {
        relationships.Remove(relationship);
        return Task.CompletedTask;
    }
}

/// <summary>
/// In-memory <see cref="IUserRepository"/> with a configurable set of users.
/// </summary>
internal sealed class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<int, User> users = new();

    public void AddUser(User user) => users[user.Id] = user;

    public Task<User?> GetByIdAsync(int userId)
        => Task.FromResult(users.TryGetValue(userId, out var user) ? user : null);

    public Task<string?> GetUsernameByIdAsync(int userId)
        => Task.FromResult(users.TryGetValue(userId, out var user) ? user.Username : null);

    public Task<User?> GetByUsernameAsync(string username)
        => Task.FromResult(users.Values.FirstOrDefault(u => u.Username == username));

    public Task<bool> UsernameExistsAsync(string username)
        => Task.FromResult(users.Values.Any(u => u.Username == username));

    public Task CreateAsync(User user)
    {
        users[user.Id] = user;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(User user)
    {
        users[user.Id] = user;
        return Task.CompletedTask;
    }
}

/// <summary>
/// No-op IPC transport for multiplayer tests.
/// </summary>
internal sealed class NoOpMultiplayerTransport : IInterProcessCommunicationTransport
{
    public void Subscribe(string channel, Func<string, Task> handler)
    {
    }

    public void Unsubscribe(string channel)
    {
    }

    public Task PublishAsync(string channel, string payload) => Task.CompletedTask;
}

/// <summary>
/// Test hub caller context with configurable identity.
/// </summary>
internal sealed class TestMultiplayerCallerContext(string connectionId, string userIdentifier) : HubCallerContext
{
    private readonly FeatureCollection features = new();
    private readonly Dictionary<object, object?> items = [];

    public override string ConnectionId => connectionId;

    public override string UserIdentifier => userIdentifier;

    public override System.Security.Claims.ClaimsPrincipal? User => null;

    public override IDictionary<object, object?> Items => items;

    public override IFeatureCollection Features => features;

    public override CancellationToken ConnectionAborted => CancellationToken.None;

    public override void Abort()
    {
    }
}