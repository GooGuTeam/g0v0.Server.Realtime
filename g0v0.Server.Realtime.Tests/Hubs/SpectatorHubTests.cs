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
using osu.Game.Replays.Legacy;
using osu.Game.Rulesets.Scoring;
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

        LazerPlayer? player = _environment.PlayerManager.GetPlayer<LazerPlayer>(3);

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
        LazerPlayer player = _environment.PlayerManager.GetPlayer<LazerPlayer>(3)!;

        await player.OnScoreProcessed(777);

        Assert.That(handle.Recorder.ScoreProcessedEvents, Is.EqualTo([(3, 777L)]));
    }

    [Test]
    public async Task BeginPlaySession_WhenRulesetIsMissing_ShouldNotStartSession()
    {
        await _environment.ConnectAsync(3, "connection-3");
        LazerPlayer player = _environment.PlayerManager.GetPlayer<LazerPlayer>(3)!;

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
        LazerPlayer player = _environment.PlayerManager.GetPlayer<LazerPlayer>(3)!;

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
        SpectatorState state = new()
        {
            RulesetID = 0,
            BeatmapID = 42,
            State = SpectatedUserState.Playing,
        };

        await _environment.CurrentHub.BeginPlaySession(100, state);
        LazerPlayer player = _environment.PlayerManager.GetPlayer<LazerPlayer>(3)!;
        osu.Game.Scoring.Score? bufferedScore = await _environment.ScoreBuffer.DequeueAsync(100);

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
    public async Task BeginPlaySessionV2_WhenValid_ShouldStartSessionAndBufferScore()
    {
        await _environment.ConnectAsync(3, "connection-3");
        _environment.UserRepository.SetUsername(3, "Watcher");
        _environment.BeatmapRepository.SetBeatmap(new DbBeatmap
        {
            Id = 42,
            Checksum = "checksum",
            Status = BeatmapOnlineStatus.Ranked,
        });
        SpectatorState state = new()
        {
            RulesetID = 0,
            BeatmapID = 42,
            State = SpectatedUserState.Playing,
        };

        await _environment.CurrentHub.BeginPlaySessionV2(100, state);
        LazerPlayer player = _environment.PlayerManager.GetPlayer<LazerPlayer>(3)!;
        osu.Game.Scoring.Score? bufferedScore = await _environment.ScoreBuffer.DequeueAsync(100);

        Assert.Multiple(() =>
        {
            Assert.That(player.State.ScoreTokens, Is.EqualTo([100L]));
            Assert.That(player.State.ScoreToken, Is.EqualTo(100));
            Assert.That(player.State.SpectatorState, Is.SameAs(state));
            Assert.That(bufferedScore, Is.Not.Null);
            Assert.That(bufferedScore!.ScoreInfo.BeatmapInfo!.OnlineID, Is.EqualTo(42));
            Assert.That(bufferedScore.ScoreInfo.User.Id, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task BeginPlaySessionV2_WhenSameTokenRestarted_ShouldKeepSingleTokenEntry()
    {
        await _environment.ConnectAsync(3, "connection-3");
        _environment.UserRepository.SetUsername(3, "Watcher");
        _environment.BeatmapRepository.SetBeatmap(new DbBeatmap
        {
            Id = 42,
            Checksum = "checksum",
            Status = BeatmapOnlineStatus.Ranked,
        });
        SpectatorState state = new()
        {
            RulesetID = 0,
            BeatmapID = 42,
            State = SpectatedUserState.Playing,
        };

        // The client re-sends the same token on reconnection.
        await _environment.CurrentHub.BeginPlaySessionV2(100, state);
        await _environment.CurrentHub.BeginPlaySessionV2(100, state);
        LazerPlayer player = _environment.PlayerManager.GetPlayer<LazerPlayer>(3)!;
        osu.Game.Scoring.Score? bufferedScore = await _environment.ScoreBuffer.DequeueAsync(100);

        Assert.Multiple(() =>
        {
            Assert.That(player.State.ScoreTokens, Is.EqualTo([100L]));
            Assert.That(bufferedScore, Is.Not.Null);
        });
    }

    [Test]
    public async Task BeginPlaySessionV2_WhenTooManyScoresStarted_ShouldEvictOldest()
    {
        await _environment.ConnectAsync(3, "connection-3");
        _environment.UserRepository.SetUsername(3, "Watcher");
        _environment.BeatmapRepository.SetBeatmap(new DbBeatmap
        {
            Id = 42,
            Checksum = "checksum",
            Status = BeatmapOnlineStatus.Ranked,
        });
        SpectatorState state = new()
        {
            RulesetID = 0,
            BeatmapID = 42,
            State = SpectatedUserState.Playing,
        };

        for (int i = 0; i < RealtimeConfig.DefaultMaxStartedScores + 1; ++i)
        {
            await _environment.CurrentHub.BeginPlaySessionV2(100 + i, state);
        }

        LazerPlayer player = _environment.PlayerManager.GetPlayer<LazerPlayer>(3)!;
        osu.Game.Scoring.Score? evictedScore = await _environment.ScoreBuffer.DequeueAsync(100);

        Assert.Multiple(() =>
        {
            Assert.That(player.State.ScoreTokens, Is.EqualTo([101L, 102L]));
            Assert.That(player.State.ScoreToken, Is.EqualTo(102));
            Assert.That(evictedScore, Is.Null);
        });
    }

    [Test]
    public async Task BeginPlaySessionV2_WhenMaxStartedScoresReloaded_ShouldUseNewLimit()
    {
        // Hot-reload the limit from the default of 2 down to 1.
        await File.WriteAllTextAsync(
            Path.Combine(_environment.BasePath, "config", "realtime.json"),
            "{\"SaveReplays\":false,\"ReplayUploaderConcurrency\":0,\"MaxStartedScores\":1}");
        _environment.ConfigManager.Reload<RealtimeConfig>();

        await _environment.ConnectAsync(3, "connection-3");
        _environment.UserRepository.SetUsername(3, "Watcher");
        _environment.BeatmapRepository.SetBeatmap(new DbBeatmap
        {
            Id = 42,
            Checksum = "checksum",
            Status = BeatmapOnlineStatus.Ranked,
        });
        SpectatorState state = new()
        {
            RulesetID = 0,
            BeatmapID = 42,
            State = SpectatedUserState.Playing,
        };

        await _environment.CurrentHub.BeginPlaySessionV2(100, state);
        await _environment.CurrentHub.BeginPlaySessionV2(200, state);

        LazerPlayer player = _environment.PlayerManager.GetPlayer<LazerPlayer>(3)!;
        Assert.Multiple(() =>
        {
            Assert.That(player.State.ScoreTokens, Is.EqualTo([200L]));
            Assert.That(player.State.ScoreToken, Is.EqualTo(200));
        });
    }

    [Test]
    public async Task SendFrameDataV2_WhenTokenNotStarted_ShouldThrow()
    {
        await _environment.ConnectAsync(3, "connection-3");

        Assert.ThrowsAsync<InvalidOperationException>(async () => await _environment.CurrentHub.SendFrameDataV2(100, CreateFrameDataBundle(1000)));
    }

    [Test]
    public async Task SendFrameDataV2_WhenTokenStarted_ShouldUpdateBufferedScore()
    {
        await _environment.ConnectAsync(3, "connection-3");
        _environment.UserRepository.SetUsername(3, "Watcher");
        _environment.BeatmapRepository.SetBeatmap(new DbBeatmap
        {
            Id = 42,
            Checksum = "checksum",
            Status = BeatmapOnlineStatus.Ranked,
        });
        SpectatorState state = new()
        {
            RulesetID = 0,
            BeatmapID = 42,
            State = SpectatedUserState.Playing,
        };
        await _environment.CurrentHub.BeginPlaySessionV2(100, state);

        await _environment.CurrentHub.SendFrameDataV2(100, CreateFrameDataBundle(5000));
        osu.Game.Scoring.Score? bufferedScore = await _environment.ScoreBuffer.DequeueAsync(100);

        Assert.Multiple(() =>
        {
            Assert.That(bufferedScore, Is.Not.Null);
            Assert.That(bufferedScore!.ScoreInfo.TotalScore, Is.EqualTo(5000));
        });
    }

    [Test]
    public async Task EndPlaySessionV2_WhenTokenNotStarted_ShouldThrow()
    {
        await _environment.ConnectAsync(3, "connection-3");

        Assert.ThrowsAsync<InvalidOperationException>(async () => await _environment.CurrentHub.EndPlaySessionV2(100, SpectatedUserState.Passed));
    }

    [Test]
    public async Task EndPlaySessionV2_WhenLastToken_ShouldBroadcastFinishedAndClearState()
    {
        TestHubHandle player = await _environment.ConnectAsync(3, "connection-3");
        TestHubHandle watcher = await _environment.ConnectAsync(9, "watcher-connection");
        _environment.UserRepository.SetUsername(9, "Watcher");
        _environment.BeatmapRepository.SetBeatmap(new DbBeatmap
        {
            Id = 42,
            Checksum = "checksum",
            Status = BeatmapOnlineStatus.Ranked,
        });
        await watcher.Hub.StartWatchingUser(3);
        SpectatorState state = new()
        {
            RulesetID = 0,
            BeatmapID = 42,
            State = SpectatedUserState.Playing,
        };
        await player.Hub.BeginPlaySessionV2(100, state);

        await player.Hub.EndPlaySessionV2(100, SpectatedUserState.Passed);

        Realtime.Objects.States.PlayerState playerState = _environment.PlayerManager.GetPlayer<LazerPlayer>(3)!.State;
        Assert.Multiple(() =>
        {
            Assert.That(watcher.Recorder.FinishedPlayingEvents, Is.EqualTo([(3, state)]));
            Assert.That(state.State, Is.EqualTo(SpectatedUserState.Passed));
            Assert.That(playerState.ScoreTokens, Is.Empty);
            Assert.That(playerState.ScoreToken, Is.Null);
            Assert.That(playerState.SpectatorState, Is.Null);
        });
    }

    [Test]
    public async Task EndPlaySessionV2_WhenNotLastToken_ShouldNotBroadcastFinished()
    {
        TestHubHandle player = await _environment.ConnectAsync(3, "connection-3");
        TestHubHandle watcher = await _environment.ConnectAsync(9, "watcher-connection");
        _environment.UserRepository.SetUsername(9, "Watcher");
        _environment.BeatmapRepository.SetBeatmap(new DbBeatmap
        {
            Id = 42,
            Checksum = "checksum",
            Status = BeatmapOnlineStatus.Ranked,
        });
        await watcher.Hub.StartWatchingUser(3);
        SpectatorState state = new()
        {
            RulesetID = 0,
            BeatmapID = 42,
            State = SpectatedUserState.Playing,
        };
        await player.Hub.BeginPlaySessionV2(1234, state);
        await player.Hub.BeginPlaySessionV2(5678, state);

        await player.Hub.EndPlaySessionV2(1234, SpectatedUserState.Passed);
        Assert.That(watcher.Recorder.FinishedPlayingEvents, Is.Empty);
        Assert.That(_environment.PlayerManager.GetPlayer<LazerPlayer>(3)!.State.ScoreTokens, Is.EqualTo([5678L]));

        await player.Hub.EndPlaySessionV2(5678, SpectatedUserState.Passed);
        Assert.That(watcher.Recorder.FinishedPlayingEvents, Is.EqualTo([(3, state)]));
    }

    [Test]
    public async Task MultipleInFlightScores_ShouldInterleaveFramesAndEnds()
    {
        TestHubHandle player = await _environment.ConnectAsync(3, "connection-3");
        TestHubHandle watcher = await _environment.ConnectAsync(9, "watcher-connection");
        _environment.UserRepository.SetUsername(9, "Watcher");
        _environment.BeatmapRepository.SetBeatmap(new DbBeatmap
        {
            Id = 42,
            Checksum = "checksum",
            Status = BeatmapOnlineStatus.Ranked,
        });
        await watcher.Hub.StartWatchingUser(3);
        SpectatorState state = new()
        {
            RulesetID = 0,
            BeatmapID = 42,
            State = SpectatedUserState.Playing,
        };
        await player.Hub.BeginPlaySessionV2(1234, state);
        await player.Hub.BeginPlaySessionV2(5678, state);

        await player.Hub.SendFrameDataV2(1234, CreateFrameDataBundle(1000));
        await player.Hub.SendFrameDataV2(5678, CreateFrameDataBundle(2000));

        osu.Game.Scoring.Score? score1234 = await _environment.ScoreBuffer.DequeueAsync(1234);
        osu.Game.Scoring.Score? score5678 = await _environment.ScoreBuffer.DequeueAsync(5678);
        Assert.Multiple(() =>
        {
            Assert.That(score1234, Is.Not.Null);
            Assert.That(score1234!.ScoreInfo.TotalScore, Is.EqualTo(1000));
            Assert.That(score5678, Is.Not.Null);
            Assert.That(score5678!.ScoreInfo.TotalScore, Is.EqualTo(2000));
            Assert.That(watcher.Recorder.SentFramesEvents, Has.Count.EqualTo(2));
        });

        await player.Hub.EndPlaySessionV2(1234, SpectatedUserState.Passed);
        Assert.That(watcher.Recorder.FinishedPlayingEvents, Is.Empty);
    }

    [Test]
    public async Task EndPlaySessionV2_WithNullScoreToken_ShouldBroadcastEndWithoutProcessing()
    {
        TestHubHandle player = await _environment.ConnectAsync(3, "connection-3");
        TestHubHandle watcher = await _environment.ConnectAsync(9, "watcher-connection");
        _environment.UserRepository.SetUsername(9, "Watcher");
        _environment.BeatmapRepository.SetBeatmap(new DbBeatmap
        {
            Id = 42,
            Checksum = "checksum",
            Status = BeatmapOnlineStatus.Ranked,
        });
        await watcher.Hub.StartWatchingUser(3);
        SpectatorState state = new()
        {
            RulesetID = 0,
            BeatmapID = 42,
            State = SpectatedUserState.Playing,
        };
        await player.Hub.BeginPlaySessionV2(scoreToken: null, state);

        await player.Hub.EndPlaySessionV2(scoreToken: null, finalState: SpectatedUserState.Quit);

        Realtime.Objects.States.PlayerState playerState = _environment.PlayerManager.GetPlayer<LazerPlayer>(3)!.State;
        Assert.Multiple(() =>
        {
            Assert.That(watcher.Recorder.FinishedPlayingEvents, Is.EqualTo([(3, state)]));
            Assert.That(state.State, Is.EqualTo(SpectatedUserState.Quit));
            Assert.That(playerState.SpectatorState, Is.Null);
        });
    }

    [Test]
    public async Task StartWatchingUser_WhenTargetIsOnline_ShouldRegisterWatcherAndNotifyTarget()
    {
        TestHubHandle target = await _environment.ConnectAsync(9, "target-connection");
        await _environment.ConnectAsync(3, "watcher-connection");
        _environment.UserRepository.SetUsername(3, "Watcher");

        await _environment.CurrentHub.StartWatchingUser(9);

        LazerPlayer targetPlayer = _environment.PlayerManager.GetPlayer<LazerPlayer>(9)!;
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

        LazerPlayer targetPlayer = _environment.PlayerManager.GetPlayer<LazerPlayer>(9)!;
        Assert.Multiple(() =>
        {
            Assert.That(_environment.PlayerManager.GetWatchingPlayers(targetPlayer), Is.Empty);
            Assert.That(target.Recorder.EndedWatchingUsers, Is.EqualTo([3]));
        });
    }

    private static FrameDataBundle CreateFrameDataBundle(long totalScore)
    {
        return new FrameDataBundle(
            new FrameHeader(
                totalScore,
                0.5,
                100,
                200,
                new Dictionary<HitResult, int>(),
                new ScoreProcessorStatistics(),
                DateTimeOffset.Now,
                [],
                totalScoreWithoutMods: null,
                pauses: null),
            new List<LegacyReplayFrame>());
    }

    private sealed class SpectatorHubTestEnvironment : IDisposable
    {
        private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
        private readonly ServiceProvider _serviceProvider;
        private readonly InterProcessCommunicationClient _ipcClient;
        private readonly NoOpTransport _transport = new();

        public SpectatorHubTestEnvironment()
        {
            BasePath = Path.Combine(Path.GetTempPath(), "g0v0-spectator-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(BasePath, "config"));
            File.WriteAllText(Path.Combine(BasePath, "config", "game.json"), "{\"EnableAllBeatmapLeaderboard\":true}");
            File.WriteAllText(
                Path.Combine(BasePath, "config", "realtime.json"),
                "{\"SaveReplays\":false,\"ReplayUploaderConcurrency\":0}");

            ConfigManager = new ConfigurationManager(BasePath);
            UserRepository = new FakeUserRepository();
            BeatmapRepository = new FakeBeatmapRepository();
            ScoreRepository = new FakeScoreRepository();
            _ipcClient = new InterProcessCommunicationClient(_transport, "realtime");
            PlayerManager = new PlayerManager(NullLogger<PlayerManager>.Instance, _ipcClient);
            Router = new SpectatorClientRouter();
            HubContext = new FakeHubContext(Router);
            ScoreBuffer = new ScoreBuffer(_memoryCache, NullLogger<ScoreBuffer>.Instance);

            ServiceCollection serviceCollection = new();
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
            RulesetManager = new RulesetManager(BasePath);
        }

        public ConfigurationManager ConfigManager { get; }

        public string BasePath { get; }

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
            SpectatorHub hub = new(
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
            if (Directory.Exists(BasePath))
            {
                Directory.Delete(BasePath, recursive: true);
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

        public ISpectatorClient Others => router.CreateProxy(state => state.UserIds.Where(id => !string.Equals(id, callerUserId, StringComparison.Ordinal)));

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

            public Task ServerShuttingDown() =>
                Task.WhenAll(Targets().Select(static target => target.ServerShuttingDown()));

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

        public Task ServerShuttingDown() => Task.CompletedTask;

        public Task UserBeganPlaying(int userId, SpectatorState state)
        {
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
        private readonly ConcurrentDictionary<int, string> _usernames = new();

        public void SetUsername(int userId, string username) => _usernames[userId] = username;

        public Task<User?> GetByIdAsync(int userId) => throw new NotSupportedException();

        public Task<string?> GetUsernameByIdAsync(int userId) =>
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

        public Task<DbBeatmap> GetOrFetchByIdAsync(int beatmapId, g0v0.Server.Common.Fetching.IFetcher fetcher, CancellationToken cancellationToken = default)
            => Task.FromResult(_beatmapsById.TryGetValue(beatmapId, out DbBeatmap? value) ? value : throw new NotSupportedException());

        public Task<DbBeatmap> GetOrFetchByChecksumAsync(string checksum, g0v0.Server.Common.Fetching.IFetcher fetcher, CancellationToken cancellationToken = default)
            => Task.FromResult(_beatmapsByChecksum.TryGetValue(checksum, out DbBeatmap? value) ? value : throw new NotSupportedException());

        public Task<IReadOnlyList<DbBeatmap>> GetByBeatmapSetIdAsync(int beatmapSetId) => throw new NotSupportedException();

        public Task<IReadOnlyList<DbBeatmap>> GetByMapperIdAsync(int mapperId) => throw new NotSupportedException();

        public Task CreateAsync(DbBeatmap beatmap) => throw new NotSupportedException();

        public Task UpdateAsync(DbBeatmap beatmap) => throw new NotSupportedException();

        public Task DeleteAsync(DbBeatmap beatmap) => throw new NotSupportedException();
    }

    private sealed class FakeScoreRepository : IScoreRepository
    {
        public Task<Score?> GetByIdAsync(long scoreId) => throw new NotSupportedException();

        public Task<IReadOnlyList<Score>> GetByBeatmapIdAsync(int beatmapId) => throw new NotSupportedException();

        public Task<IReadOnlyList<Score>> GetByUserIdAsync(int userId) => throw new NotSupportedException();

        public Task<IReadOnlyList<Score>> GetRecentByUserIdAndModeAsync(int userId, int mode, int limit) => throw new NotSupportedException();

        public Task<IReadOnlyList<Score>> GetBestByUserIdAndModeAsync(int userId, int mode, int limit) => throw new NotSupportedException();

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