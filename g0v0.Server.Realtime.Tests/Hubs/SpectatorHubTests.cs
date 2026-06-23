// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Common.Storage;
using g0v0.Server.Realtime.Hubs;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Players;
using g0v0.Server.Realtime.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Online.Spectator;
using DbBeatmap = g0v0.Server.Common.Database.Models.Beatmap;

namespace g0v0.Server.Realtime.Tests.Hubs;

[TestFixture]
public class SpectatorHubTests
{
    private SpectatorHubTestEnvironment _environment = null!;

    [SetUp]
    public void SetUp()
    {
        _environment = new SpectatorHubTestEnvironment();
    }

    [TearDown]
    public void TearDown()
    {
        _environment.Dispose();
    }

    [Test]
    public async Task OnConnectedAsync_ShouldCreatePlayerAndInstallSpectatorCallbacks()
    {
        await _environment.ConnectAsync(3, "connection-3");

        var player = _environment.PlayerManager.GetPlayer<LazerPlayer>(3);

        Assert.Multiple(() =>
        {
            Assert.That(player, Is.Not.Null);
            Assert.That(player!.OnUserBeganPlayingForSpectatorHub, Is.Not.Null);
            Assert.That(player.OnUserFinishedPlayingForSpectatorHub, Is.Not.Null);
            Assert.That(player.OnUserSentFramesForSpectatorHub, Is.Not.Null);
            Assert.That(player.OnWatchedForSpectatorHub, Is.Not.Null);
            Assert.That(player.OnWatchedStoppedForSpectatorHub, Is.Not.Null);
            Assert.That(player.OnScoreProcessedForSpectatorHub, Is.Not.Null);
        });
    }

    [Test]
    public async Task OnScoreProcessedCallback_ShouldNotifyCurrentUser()
    {
        TestHubHandle handle = await _environment.ConnectAsync(3, "connection-3");
        var player = _environment.PlayerManager.GetPlayer<LazerPlayer>(3)!;

        await player.OnScoreProcessed(777);

        Assert.That(handle.Recorder.ScoreProcessedEvents, Is.EqualTo([(3, 777L)]));
    }

    [Test]
    public async Task BeginPlaySession_WhenRulesetIsMissing_ShouldNotStartSession()
    {
        await _environment.ConnectAsync(3, "connection-3");
        var player = _environment.PlayerManager.GetPlayer<LazerPlayer>(3)!;

        await _environment.CurrentHub.BeginPlaySession(100, new SpectatorState
        {
            BeatmapID = 42,
            State = SpectatedUserState.Playing,
        });

        Assert.Multiple(() =>
        {
            Assert.That(player.State.ScoreToken, Is.Null);
            Assert.That(player.State.SpectatorState, Is.Null);
        });
    }

    [Test]
    public async Task BeginPlaySession_WhenBeatmapIsMissing_ShouldNotStartSession()
    {
        await _environment.ConnectAsync(3, "connection-3");
        var player = _environment.PlayerManager.GetPlayer<LazerPlayer>(3)!;

        await _environment.CurrentHub.BeginPlaySession(100, new SpectatorState
        {
            RulesetID = 0,
            BeatmapID = 404,
            State = SpectatedUserState.Playing,
        });

        Assert.Multiple(() =>
        {
            Assert.That(player.State.ScoreToken, Is.Null);
            Assert.That(player.State.SpectatorState, Is.Null);
        });
    }

    [Test]
    public async Task BeginPlaySession_WhenValid_ShouldStartSessionAndBufferScore()
    {
        await _environment.ConnectAsync(3, "connection-3");
        _environment.UserRepository.SetUsername(3, "Watcher");
        _environment.BeatmapRepository.SetBeatmap(new DbBeatmap
        {
            Id = 42,
            Checksum = "checksum",
            Status = BeatmapOnlineStatus.Ranked,
        });
        var state = new SpectatorState
        {
            RulesetID = 0,
            BeatmapID = 42,
            State = SpectatedUserState.Playing,
        };

        await _environment.CurrentHub.BeginPlaySession(100, state);
        var player = _environment.PlayerManager.GetPlayer<LazerPlayer>(3)!;
        var bufferedScore = await _environment.ScoreBuffer.DequeueAsync(100);

        Assert.Multiple(() =>
        {
            Assert.That(player.State.ScoreToken, Is.EqualTo(100));
            Assert.That(player.State.SpectatorState, Is.SameAs(state));
            Assert.That(bufferedScore, Is.Not.Null);
            Assert.That(bufferedScore!.ScoreInfo.BeatmapInfo!.OnlineID, Is.EqualTo(42));
            Assert.That(bufferedScore.ScoreInfo.User.Id, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task StartWatchingUser_WhenTargetIsOnline_ShouldRegisterWatcherAndNotifyTarget()
    {
        TestHubHandle target = await _environment.ConnectAsync(9, "target-connection");
        await _environment.ConnectAsync(3, "watcher-connection");
        _environment.UserRepository.SetUsername(3, "Watcher");

        await _environment.CurrentHub.StartWatchingUser(9);

        var targetPlayer = _environment.PlayerManager.GetPlayer<LazerPlayer>(9)!;
        Assert.Multiple(() =>
        {
            Assert.That(_environment.PlayerManager.GetWatchingPlayers(targetPlayer).Select(p => p.PlayerId), Is.EqualTo([3]));
            Assert.That(target.Recorder.StartedWatchingUsers, Has.Count.EqualTo(1));
            Assert.That(target.Recorder.StartedWatchingUsers[0].OnlineID, Is.EqualTo(3));
            Assert.That(target.Recorder.StartedWatchingUsers[0].Username, Is.EqualTo("Watcher"));
        });
    }

    [Test]
    public async Task EndWatchingUser_ShouldUnregisterAllTargetInstances()
    {
        TestHubHandle target = await _environment.ConnectAsync(9, "target-connection");
        await _environment.ConnectAsync(3, "watcher-connection");
        _environment.UserRepository.SetUsername(3, "Watcher");
        await _environment.CurrentHub.StartWatchingUser(9);

        await _environment.CurrentHub.EndWatchingUser(9);

        var targetPlayer = _environment.PlayerManager.GetPlayer<LazerPlayer>(9)!;
        Assert.Multiple(() =>
        {
            Assert.That(_environment.PlayerManager.GetWatchingPlayers(targetPlayer), Is.Empty);
            Assert.That(target.Recorder.EndedWatchingUsers, Is.EqualTo([3]));
        });
    }

    private sealed class SpectatorHubTestEnvironment : IDisposable
    {
        private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
        private readonly ServiceProvider _serviceProvider;
        private readonly string _basePath;
        private readonly InterProcessCommunicationClient _ipcClient;
        private readonly NoOpTransport _transport = new();

        public SpectatorHubTestEnvironment()
        {
            _basePath = Path.Combine(Path.GetTempPath(), "g0v0-spectator-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_basePath, "config"));
            File.WriteAllText(Path.Combine(_basePath, "config", "game.json"), "{\"EnableAllBeatmapLeaderboard\":true}");
            File.WriteAllText(
                Path.Combine(_basePath, "config", "realtime.json"),
                "{\"SaveReplays\":false,\"ReplayUploaderConcurrency\":0}");

            ConfigManager = new ConfigurationManager(_basePath);
            UserRepository = new FakeUserRepository();
            BeatmapRepository = new FakeBeatmapRepository();
            ScoreRepository = new FakeScoreRepository();
            _ipcClient = new InterProcessCommunicationClient(_transport, "realtime");
            PlayerManager = new PlayerManager(NullLogger<PlayerManager>.Instance, _ipcClient);
            Router = new SpectatorClientRouter();
            HubContext = new FakeHubContext(Router);
            ScoreBuffer = new ScoreBuffer(_memoryCache, NullLogger<ScoreBuffer>.Instance);

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddScoped<IUserRepository>(_ => UserRepository);
            serviceCollection.AddScoped<IBeatmapRepository>(_ => BeatmapRepository);
            serviceCollection.AddScoped<IScoreRepository>(_ => ScoreRepository);
            _serviceProvider = serviceCollection.BuildServiceProvider();
            ScopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
            ScoreProcessedNotificationService = new ScoreProcessedNotificationService(
                _ipcClient,
                ScopeFactory,
                NullLogger<ScoreProcessedNotificationService>.Instance)
            {
                ScoreLookupTimeout = TimeSpan.FromMilliseconds(20),
                ScoreLookupRetryDelay = TimeSpan.FromMilliseconds(1),
            };
            ScoreUploader = new ScoreUploader(
                ConfigManager,
                ScopeFactory,
                NullLogger<ScoreUploader>.Instance,
                new FakeStorageService());
            RulesetManager = new RulesetManager(_basePath);
        }

        public ConfigurationManager ConfigManager { get; }

        public FakeBeatmapRepository BeatmapRepository { get; }

        public FakeUserRepository UserRepository { get; }

        public FakeScoreRepository ScoreRepository { get; }

        public PlayerManager PlayerManager { get; }

        public SpectatorClientRouter Router { get; }

        public FakeHubContext HubContext { get; }

        public ScoreBuffer ScoreBuffer { get; }

        public IServiceScopeFactory ScopeFactory { get; }

        public ScoreProcessedNotificationService ScoreProcessedNotificationService { get; }

        public ScoreUploader ScoreUploader { get; }

        public RulesetManager RulesetManager { get; }

        public SpectatorHub CurrentHub { get; private set; } = null!;

        public async Task<TestHubHandle> ConnectAsync(int userId, string connectionId)
        {
            Router.EnsureUser(userId.ToString());
            var hub = new SpectatorHub(
                PlayerManager,
                ConfigManager,
                HubContext,
                UserRepository,
                BeatmapRepository,
                ScoreBuffer,
                RulesetManager,
                ScoreUploader,
                ScoreProcessedNotificationService,
                NullLogger<SpectatorHub>.Instance,
                ScopeFactory)
            {
                Context = new TestHubCallerContext(connectionId, userId.ToString()),
                Clients = new FakeHubCallerClients(Router, userId.ToString()),
                Groups = new FakeGroupManager(),
            };

            await hub.OnConnectedAsync();
            CurrentHub = hub;
            return new TestHubHandle(hub, Router.GetRecorder(userId.ToString()));
        }

        public void Dispose()
        {
            ScoreUploader.Dispose();
            _serviceProvider.Dispose();
            _memoryCache.Dispose();
            if (Directory.Exists(_basePath))
            {
                Directory.Delete(_basePath, recursive: true);
            }
        }
    }

    private sealed record TestHubHandle(SpectatorHub Hub, RecordingSpectatorClient Recorder);

    private sealed class TestHubCallerContext(string connectionId, string userIdentifier) : HubCallerContext
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

    private sealed class FakeHubContext(SpectatorClientRouter router) : IHubContext<SpectatorHub, ISpectatorClient>
    {
        public IHubClients<ISpectatorClient> Clients { get; } = new FakeHubClients(router);

        public IGroupManager Groups { get; } = new FakeGroupManager();
    }

    private sealed class FakeHubCallerClients(SpectatorClientRouter router, string callerUserId) : IHubCallerClients<ISpectatorClient>
    {
        public ISpectatorClient All => router.CreateProxy(static state => state.UserIds);

        public ISpectatorClient Caller => router.CreateProxy(_ => [callerUserId]);

        public ISpectatorClient Others => router.CreateProxy(state => state.UserIds.Where(id => id != callerUserId));

        public ISpectatorClient OthersInGroup(string groupName) => throw new NotSupportedException();

        public ISpectatorClient AllExcept(IReadOnlyList<string> excludedConnectionIds) =>
            router.CreateProxy(state => state.UserIds.Except(excludedConnectionIds, StringComparer.Ordinal));

        public ISpectatorClient Client(string connectionId) => router.CreateProxy(_ => [connectionId]);

        public ISpectatorClient Clients(IReadOnlyList<string> connectionIds) => router.CreateProxy(_ => connectionIds);

        public ISpectatorClient Group(string groupName) => throw new NotSupportedException();

        public ISpectatorClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public ISpectatorClient Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

        public ISpectatorClient User(string userId) => router.CreateProxy(_ => [userId]);

        public ISpectatorClient Users(IReadOnlyList<string> userIds) => router.CreateProxy(_ => userIds);
    }

    private sealed class FakeHubClients(SpectatorClientRouter router) : IHubClients<ISpectatorClient>
    {
        public ISpectatorClient All => router.CreateProxy(static state => state.UserIds);

        public ISpectatorClient AllExcept(IReadOnlyList<string> excludedConnectionIds) =>
            router.CreateProxy(state => state.UserIds.Except(excludedConnectionIds, StringComparer.Ordinal));

        public ISpectatorClient Client(string connectionId) => router.CreateProxy(_ => [connectionId]);

        public ISpectatorClient Clients(IReadOnlyList<string> connectionIds) => router.CreateProxy(_ => connectionIds);

        public ISpectatorClient Group(string groupName) => throw new NotSupportedException();

        public ISpectatorClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public ISpectatorClient Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

        public ISpectatorClient User(string userId) => router.CreateProxy(_ => [userId]);

        public ISpectatorClient Users(IReadOnlyList<string> userIds) => router.CreateProxy(_ => userIds);
    }

    private sealed class FakeGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SpectatorClientRouter
    {
        private readonly Dictionary<string, RecordingSpectatorClient> _users = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> UserIds => _users.Keys;

        public ISpectatorClient CreateProxy(Func<SpectatorClientRouter, IEnumerable<string>> targetSelector)
            => new RoutedSpectatorClient(this, targetSelector);

        public void EnsureUser(string userId)
        {
            if (!_users.ContainsKey(userId))
            {
                _users[userId] = new RecordingSpectatorClient();
            }
        }

        public RecordingSpectatorClient GetRecorder(string userId)
        {
            EnsureUser(userId);
            return _users[userId];
        }

        private sealed class RoutedSpectatorClient(
            SpectatorClientRouter router,
            Func<SpectatorClientRouter, IEnumerable<string>> targetSelector) : ISpectatorClient
        {
            public Task DisconnectRequested() =>
                Task.WhenAll(Targets().Select(static target => target.DisconnectRequested()));

            public Task UserBeganPlaying(int userId, SpectatorState state) =>
                Task.WhenAll(Targets().Select(target => target.UserBeganPlaying(userId, state)));

            public Task UserFinishedPlaying(int userId, SpectatorState state) =>
                Task.WhenAll(Targets().Select(target => target.UserFinishedPlaying(userId, state)));

            public Task UserSentFrames(int userId, FrameDataBundle data) =>
                Task.WhenAll(Targets().Select(target => target.UserSentFrames(userId, data)));

            public Task UserScoreProcessed(int userId, long scoreId) =>
                Task.WhenAll(Targets().Select(target => target.UserScoreProcessed(userId, scoreId)));

            public Task UserStartedWatching(SpectatorUser[] users) =>
                Task.WhenAll(Targets().Select(target => target.UserStartedWatching(users)));

            public Task UserEndedWatching(int userId) =>
                Task.WhenAll(Targets().Select(target => target.UserEndedWatching(userId)));

            private IEnumerable<RecordingSpectatorClient> Targets()
                => targetSelector(router).Distinct(StringComparer.Ordinal).Select(router.GetRecorder);
        }
    }

    private sealed class RecordingSpectatorClient : ISpectatorClient
    {
        public List<(int UserId, SpectatorState State)> BeganPlayingEvents { get; } = [];

        public List<(int UserId, SpectatorState State)> FinishedPlayingEvents { get; } = [];

        public List<(int UserId, FrameDataBundle Data)> SentFramesEvents { get; } = [];

        public List<(int UserId, long ScoreId)> ScoreProcessedEvents { get; } = [];

        public List<SpectatorUser> StartedWatchingUsers { get; } = [];

        public List<int> EndedWatchingUsers { get; } = [];

        public int DisconnectRequestCount { get; private set; }

        public Task DisconnectRequested()
        {
            DisconnectRequestCount++;
            return Task.CompletedTask;
        }

        public Task UserBeganPlaying(int userId, SpectatorState state)
        {
            BeganPlayingEvents.Add((userId, state));
            return Task.CompletedTask;
        }

        public Task UserFinishedPlaying(int userId, SpectatorState state)
        {
            FinishedPlayingEvents.Add((userId, state));
            return Task.CompletedTask;
        }

        public Task UserSentFrames(int userId, FrameDataBundle data)
        {
            SentFramesEvents.Add((userId, data));
            return Task.CompletedTask;
        }

        public Task UserScoreProcessed(int userId, long scoreId)
        {
            ScoreProcessedEvents.Add((userId, scoreId));
            return Task.CompletedTask;
        }

        public Task UserStartedWatching(SpectatorUser[] users)
        {
            StartedWatchingUsers.AddRange(users);
            return Task.CompletedTask;
        }

        public Task UserEndedWatching(int userId)
        {
            EndedWatchingUsers.Add(userId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly ConcurrentDictionary<long, string> _usernames = new();

        public void SetUsername(long userId, string username) => _usernames[userId] = username;

        public Task<User?> GetByIdAsync(long userId) => throw new NotSupportedException();

        public Task<string?> GetUsernameByIdAsync(long userId) =>
            Task.FromResult(_usernames.TryGetValue(userId, out string? username) ? username : null);

        public Task<User?> GetByUsernameAsync(string username) => throw new NotSupportedException();

        public Task<bool> UsernameExistsAsync(string username) => throw new NotSupportedException();

        public Task CreateAsync(User user) => throw new NotSupportedException();

        public Task UpdateAsync(User user) => throw new NotSupportedException();
    }

    private sealed class FakeBeatmapRepository : IBeatmapRepository
    {
        private readonly ConcurrentDictionary<int, DbBeatmap> _beatmapsById = new();
        private readonly ConcurrentDictionary<string, DbBeatmap> _beatmapsByChecksum = new(StringComparer.Ordinal);

        public void SetBeatmap(DbBeatmap beatmap)
        {
            _beatmapsById[beatmap.Id] = beatmap;
            if (beatmap.Checksum != null)
            {
                _beatmapsByChecksum[beatmap.Checksum] = beatmap;
            }
        }

        public Task<DbBeatmap?> GetByIdAsync(int beatmapId) =>
            Task.FromResult(_beatmapsById.TryGetValue(beatmapId, out DbBeatmap? value) ? value : null);

        public Task<DbBeatmap?> GetByChecksumAsync(string checksum) =>
            Task.FromResult(_beatmapsByChecksum.TryGetValue(checksum, out DbBeatmap? value) ? value : null);

        public Task<IReadOnlyList<DbBeatmap>> GetByBeatmapSetIdAsync(long beatmapSetId) => throw new NotSupportedException();

        public Task<IReadOnlyList<DbBeatmap>> GetByMapperIdAsync(long mapperId) => throw new NotSupportedException();

        public Task CreateAsync(DbBeatmap beatmap) => throw new NotSupportedException();

        public Task UpdateAsync(DbBeatmap beatmap) => throw new NotSupportedException();

        public Task DeleteAsync(DbBeatmap beatmap) => throw new NotSupportedException();
    }

    private sealed class FakeScoreRepository : IScoreRepository
    {
        public Task<Score?> GetByIdAsync(long scoreId) => throw new NotSupportedException();

        public Task<IReadOnlyList<Score>> GetByBeatmapIdAsync(int beatmapId) => throw new NotSupportedException();

        public Task<IReadOnlyList<Score>> GetByUserIdAsync(long userId) => throw new NotSupportedException();

        public Task<IReadOnlyList<Score>> GetRecentByUserIdAndModeAsync(long userId, int mode, int limit) => throw new NotSupportedException();

        public Task<IReadOnlyList<Score>> GetBestByUserIdAndModeAsync(long userId, int mode, int limit) => throw new NotSupportedException();

        public Task<Score?> GetScoreByToken(long scoreToken) => Task.FromResult<Score?>(null);

        public Task CreateAsync(Score score) => throw new NotSupportedException();

        public Task UpdateAsync(Score score) => throw new NotSupportedException();

        public Task DeleteAsync(Score score) => throw new NotSupportedException();

        public Task MarkScoreHasReplay(long scoreId) => throw new NotSupportedException();
    }

    private sealed class FakeStorageService : IStorageService
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task WriteFileAsync(
            string filePath,
            byte[] content,
            string contentType = "application/octet-stream",
            string cacheControl = "public, max-age=31536000",
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<byte[]> ReadFileAsync(string filePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsExistsAsync(string filePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> GetFileUrlAsync(string filePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public string? GetFileNameByUrl(string url) => throw new NotSupportedException();

        public Task CloseAsync() => Task.CompletedTask;
    }

    private sealed class NoOpTransport : IInterProcessCommunicationTransport
    {
        public Task PublishAsync(string channel, string payload) => Task.CompletedTask;

        public void Subscribe(string channel, Func<string, Task> handler)
        {
        }
    }
}