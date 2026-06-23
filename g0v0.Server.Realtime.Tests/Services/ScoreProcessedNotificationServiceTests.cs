// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Objects.Players;
using g0v0.Server.Realtime.Objects.States;
using g0v0.Server.Realtime.Objects.States.Activity;
using g0v0.Server.Realtime.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using osu.Game.Online.Spectator;
using osu.Game.Users;
using DbScore = g0v0.Server.Common.Database.Models.Score;
using osuScore = osu.Game.Scoring.Score;

namespace g0v0.Server.Realtime.Tests.Services;

[TestFixture]
public class ScoreProcessedNotificationServiceTests
{
    private static readonly long[] ExpectedScoreId42 = [42L];

    private InMemoryTransport _transport = null!;
    private InterProcessCommunicationClient _senderIpc = null!;
    private InterProcessCommunicationClient _receiverIpc = null!;
    private FakeScoreRepository _scoreRepository = null!;
    private ScoreProcessedNotificationService _service = null!;
    private IServiceScopeFactory _scopeFactory = null!;

    [SetUp]
    public void SetUp()
    {
        _transport = new InMemoryTransport();
        _senderIpc = new InterProcessCommunicationClient(_transport, "lazer");
        _receiverIpc = new InterProcessCommunicationClient(_transport, "realtime");
        _scoreRepository = new FakeScoreRepository();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped<IScoreRepository>(_ => _scoreRepository);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        _scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        _service = new ScoreProcessedNotificationService(
            _receiverIpc,
            _scopeFactory,
            NullLogger<ScoreProcessedNotificationService>.Instance)
        {
            ScoreLookupTimeout = TimeSpan.FromMilliseconds(200),
            ScoreLookupRetryDelay = TimeSpan.FromMilliseconds(10),
        };
    }

    [Test]
    public async Task RegisterForSingleScoreAsync_WhenScoreAlreadyProcessed_NotifyImmediately()
    {
        var player = new TestPlayer(1);
        var score = new DbScore { Id = 42, Processed = true };
        _scoreRepository.SetScoreByToken(100, score);

        await _service.RegisterForSingleScoreAsync(player, 100);

        Assert.That(player.ProcessedScoreIds, Is.EqualTo(ExpectedScoreId42));
    }

    [Test]
    public async Task RegisterForSingleScoreAsync_WhenScoreNotProcessed_AwaitsIpcNotice()
    {
        var player = new TestPlayer(1);
        var score = new DbScore { Id = 42, Processed = false };
        _scoreRepository.SetScoreByToken(100, score);

        await _service.RegisterForSingleScoreAsync(player, 100);

        await _senderIpc.SendNoticeAsync("realtime", "score_processed", new { score_id = 42 });

        long processedScoreId = await player.WaitForProcessedScoreAsync();

        Assert.Multiple(() =>
        {
            Assert.That(processedScoreId, Is.EqualTo(42));
            Assert.That(player.ProcessedScoreIds, Is.EqualTo(ExpectedScoreId42));
        });
    }

    [Test]
    public async Task RegisterForSingleScoreAsync_WhenScoreAppearsDuringPolling_AwaitsIpcNotice()
    {
        var player = new TestPlayer(1);
        var registrationTask = _service.RegisterForSingleScoreAsync(player, 100);

        await Task.Delay(50);
        _scoreRepository.SetScoreByToken(100, new DbScore { Id = 42, Processed = false });

        await registrationTask;
        await _senderIpc.SendNoticeAsync("realtime", "score_processed", new { score_id = 42 });

        long processedScoreId = await player.WaitForProcessedScoreAsync();

        Assert.Multiple(() =>
        {
            Assert.That(processedScoreId, Is.EqualTo(42));
            Assert.That(player.ProcessedScoreIds, Is.EqualTo(ExpectedScoreId42));
        });
    }

    [Test]
    public async Task RegisterForSingleScoreAsync_WhenScoreNotFound_TimeoutWithoutNotification()
    {
        var player = new TestPlayer(1);

        await _service.RegisterForSingleScoreAsync(player, 100);

        Assert.That(player.ProcessedScoreIds, Is.Empty);
    }

    [Test]
    public async Task HandleScoreProcessed_FromNonLazerSource_Ignored()
    {
        var player = new TestPlayer(1);
        var score = new DbScore { Id = 42, Processed = false };
        _scoreRepository.SetScoreByToken(100, score);

        await _service.RegisterForSingleScoreAsync(player, 100);

        var gatewayIpc = new InterProcessCommunicationClient(_transport, "gateway");
        await gatewayIpc.SendNoticeAsync("realtime", "score_processed", new { score_id = 42 });

        Assert.That(player.ProcessedScoreIds, Is.Empty);
    }

    [Test]
    public async Task OnScoreProcessed_WhenNoPendingPlayer_DoesNotThrow()
    {
        await _senderIpc.SendNoticeAsync("realtime", "score_processed", new { score_id = 999 });

        Assert.That(true);
    }

    [TearDown]
    public void TearDown()
    {
        _transport.Dispose();
    }

    private sealed class TestPlayer : IPlayer
    {
        private readonly TaskCompletionSource<long> _processedScoreCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TestPlayer(int playerId)
        {
            PlayerId = playerId;
            State = new PlayerState(new IdleActivity());
        }

        public int PlayerId { get; }

        public string Server => "Lazer";

        public PlayerState State { get; }

        public List<long> ProcessedScoreIds { get; } = [];

        public Task OnPlayerOnline(IPlayer player) => Task.CompletedTask;

        public Task OnPlayerOffline(IPlayer player, bool isKicked = false) => Task.CompletedTask;

        public Task OnPlayerChangeActivity(IPlayer player, IUserActivity activity) => Task.CompletedTask;

        public Task OnPlayerChangeStatus(IPlayer player, UserStatus? status) => Task.CompletedTask;

        public Task Online() => Task.CompletedTask;

        public Task Offline(bool isKicked = false) => Task.CompletedTask;

        public Task ChangePlayerActivityAsync(IUserActivity newActivity) => Task.CompletedTask;

        public Task ChangePlayerStatusAsync(UserStatus? newStatus) => Task.CompletedTask;

        public Task OnUserBeganPlaying(IPlayer player) => Task.CompletedTask;

        public Task OnUserFinishedPlaying(IPlayer player) => Task.CompletedTask;

        public Task OnUserSentFrames(IPlayer player, FrameDataBundle frame) => Task.CompletedTask;

        public Task OnWatched(IPlayer source) => Task.CompletedTask;

        public Task OnWatchedStopped(IPlayer source) => Task.CompletedTask;

        public Task OnScoreProcessed(long scoreId)
        {
            ProcessedScoreIds.Add(scoreId);
            _processedScoreCompletion.TrySetResult(scoreId);
            return Task.CompletedTask;
        }

        public Task<long> WaitForProcessedScoreAsync() =>
            _processedScoreCompletion.Task.WaitAsync(TimeSpan.FromSeconds(1));

        public Task BeingPlaying(long? scoreToken, osuScore score, SpectatorState spectatorState) => Task.CompletedTask;

        public Task FinishPlaying(SpectatorState spectatorState) => Task.CompletedTask;

        public Task SendFrames(FrameDataBundle data) => Task.CompletedTask;

        public Task WatchPlayer(IPlayer target) => Task.CompletedTask;

        public Task StopWatchPlayer(IPlayer target) => Task.CompletedTask;
    }

    private sealed class FakeScoreRepository : IScoreRepository
    {
        private readonly ConcurrentDictionary<long, DbScore> _scoresByToken = new();
        private readonly ConcurrentDictionary<long, DbScore> _scoresById = new();

        public void SetScoreByToken(long token, DbScore score)
        {
            _scoresByToken[token] = score;
            _scoresById[score.Id] = score;
        }

        public Task<DbScore?> GetScoreByToken(long scoreToken)
        {
            _scoresByToken.TryGetValue(scoreToken, out DbScore? value);
            return Task.FromResult(value);
        }

        public Task<DbScore?> GetByIdAsync(long scoreId)
        {
            _scoresById.TryGetValue(scoreId, out DbScore? value);
            return Task.FromResult(value);
        }

        public Task<IReadOnlyList<DbScore>> GetByBeatmapIdAsync(int beatmapId) => throw new NotSupportedException();

        public Task<IReadOnlyList<DbScore>> GetByUserIdAsync(long userId) => throw new NotSupportedException();

        public Task<IReadOnlyList<DbScore>> GetRecentByUserIdAndModeAsync(long userId, int mode, int limit) => throw new NotSupportedException();

        public Task<IReadOnlyList<DbScore>> GetBestByUserIdAndModeAsync(long userId, int mode, int limit) => throw new NotSupportedException();

        public Task CreateAsync(DbScore score) => throw new NotSupportedException();

        public Task UpdateAsync(DbScore score) => throw new NotSupportedException();

        public Task DeleteAsync(DbScore score) => throw new NotSupportedException();

        public Task MarkScoreHasReplay(long scoreId) => throw new NotSupportedException();
    }

    private sealed class InMemoryTransport : IInterProcessCommunicationTransport, IDisposable
    {
        private readonly ConcurrentDictionary<string, ConcurrentBag<Func<string, Task>>> _subscriptions =
            new(StringComparer.Ordinal);

        public Task PublishAsync(string channel, string payload)
        {
            if (!_subscriptions.TryGetValue(channel, out ConcurrentBag<Func<string, Task>>? handlers))
            {
                return Task.CompletedTask;
            }

            return Task.WhenAll(handlers.Select(handler => Task.Run(async () =>
            {
                try
                {
                    await handler(payload).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore handler errors.
                }
            })));
        }

        public void Subscribe(string channel, Func<string, Task> handler)
        {
            ConcurrentBag<Func<string, Task>> handlers = _subscriptions.GetOrAdd(
                channel,
                static _ => new ConcurrentBag<Func<string, Task>>());
            handlers.Add(handler);
        }

        public void Dispose()
        {
            _subscriptions.Clear();
        }
    }
}