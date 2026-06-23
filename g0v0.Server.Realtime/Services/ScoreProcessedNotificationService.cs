// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Objects.Players;
using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Services;

/// <summary>
/// Listens for <c>score_processed</c> IPC notices from the lazer server and
/// dispatches them to the registered <see cref="IPlayer"/> instance, so the
/// spectator hub can notify the client that the score has been fully processed.
/// </summary>
public sealed class ScoreProcessedNotificationService
{
    private const string ScoreProcessedNoticeName = "score_processed";
    private const string LazerServerIdentifier = "lazer";

    private static readonly Action<ILogger, long, TimeSpan, Exception?> LogScoreLookupTimedOut =
        LoggerMessage.Define<long, TimeSpan>(
            LogLevel.Warning,
            new EventId(1, nameof(RegisterForSingleScoreAsync)),
            "Score not found for token {ScoreToken} within lookup timeout of {Timeout}.");

    private static readonly Action<ILogger, long, long, int, Exception?> LogScoreAlreadyProcessed =
        LoggerMessage.Define<long, long, int>(
            LogLevel.Debug,
            new EventId(2, nameof(RegisterForSingleScoreAsync)),
            "Score {ScoreId} is already processed for token {ScoreToken}. Notifying player {PlayerId} immediately.");

    private static readonly Action<ILogger, long, long, Exception?> LogPendingScoreAlreadyRegistered =
        LoggerMessage.Define<long, long>(
            LogLevel.Debug,
            new EventId(3, nameof(RegisterForSingleScoreAsync)),
            "Score {ScoreId} already has a pending registration for token {ScoreToken}.");

    private static readonly Action<ILogger, int, long, long, Exception?> LogPlayerRegisteredForScore =
        LoggerMessage.Define<int, long, long>(
            LogLevel.Debug,
            new EventId(4, nameof(RegisterForSingleScoreAsync)),
            "Registered player {PlayerId} for score {ScoreId} via token {ScoreToken}.");

    private static readonly Action<ILogger, string, Exception?> LogNonLazerScoreProcessedIgnored =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(5, nameof(HandleScoreProcessedAsync)),
            "Ignoring score_processed notice from non-lazer source '{SourceServer}'.");

    private static readonly Action<ILogger, long, int, Exception?> LogDispatchingScoreProcessed =
        LoggerMessage.Define<long, int>(
            LogLevel.Information,
            new EventId(6, nameof(HandleScoreProcessedAsync)),
            "Dispatching score_processed for score {ScoreId} to player {PlayerId}.");

    private static readonly Action<ILogger, long, Exception?> LogNoPendingScoreRegistration =
        LoggerMessage.Define<long>(
            LogLevel.Debug,
            new EventId(7, nameof(HandleScoreProcessedAsync)),
            "No pending player registration found for score {ScoreId}.");

    private readonly ConcurrentDictionary<long, IPlayer> _pendingScores = new();
    private readonly InterProcessCommunicationClient _ipcClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScoreProcessedNotificationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScoreProcessedNotificationService"/> class
    /// and registers the <c>score_processed</c> IPC notice handler.
    /// </summary>
    /// <param name="ipcClient">The inter-process communication client.</param>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <param name="logger">The logger.</param>
    public ScoreProcessedNotificationService(
        InterProcessCommunicationClient ipcClient,
        IServiceScopeFactory scopeFactory,
        ILogger<ScoreProcessedNotificationService> logger)
    {
        _ipcClient = ipcClient;
        _scopeFactory = scopeFactory;
        _logger = logger;

        _ipcClient.RegisterNoticeHandler<ScoreProcessedPayload>(ScoreProcessedNoticeName, HandleScoreProcessedAsync);
    }

    /// <summary>
    /// Gets or sets the maximum time to poll for a score by its upload token
    /// before giving up. Defaults to 10 seconds.
    /// </summary>
    public TimeSpan ScoreLookupTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the delay between score lookup retries.
    /// Defaults to 100 milliseconds.
    /// </summary>
    public TimeSpan ScoreLookupRetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Registers a player to be notified when the score associated with
    /// <paramref name="scoreToken"/> is processed.
    /// </summary>
    /// <param name="player">The player to notify once the score is processed.</param>
    /// <param name="scoreToken">The score upload token ID.</param>
    /// <returns>A task representing the asynchronous registration.</returns>
    /// <remarks>
    /// If the score already exists and has <see cref="Common.Database.Models.Score.Processed"/>
    /// set to <see langword="true"/>, <paramref name="player"/> is notified immediately.
    /// Otherwise, the <paramref name="scoreToken"/> is polled until a score is found
    /// (up to <see cref="ScoreLookupTimeout"/>), and the registration waits for the
    /// next <c>score_processed</c> IPC notice from the lazer server.
    /// </remarks>
    public async Task RegisterForSingleScoreAsync(IPlayer player, long scoreToken)
    {
        ArgumentNullException.ThrowIfNull(player);

        Common.Database.Models.Score? score = null;
        var deadline = DateTime.UtcNow + ScoreLookupTimeout;

        while (DateTime.UtcNow < deadline)
        {
            using var scope = _scopeFactory.CreateScope();
            var scoreRepository = scope.ServiceProvider.GetRequiredService<IScoreRepository>();
            score = await scoreRepository.GetScoreByToken(scoreToken).ConfigureAwait(false);
            if (score != null)
            {
                break;
            }

            await Task.Delay(ScoreLookupRetryDelay).ConfigureAwait(false);
        }

        if (score == null)
        {
            LogScoreLookupTimedOut(_logger, scoreToken, ScoreLookupTimeout, null);
            return;
        }

        if (score.Processed)
        {
            LogScoreAlreadyProcessed(_logger, score.Id, scoreToken, player.PlayerId, null);
            await player.OnScoreProcessed(score.Id).ConfigureAwait(false);
            return;
        }

        if (!_pendingScores.TryAdd(score.Id, player))
        {
            LogPendingScoreAlreadyRegistered(_logger, score.Id, scoreToken, null);
            return;
        }

        LogPlayerRegisteredForScore(_logger, player.PlayerId, score.Id, scoreToken, null);
    }

    private async Task HandleScoreProcessedAsync(string sourceServer, ScoreProcessedPayload? payload)
    {
        if (payload == null)
        {
            return;
        }

        if (!string.Equals(sourceServer, LazerServerIdentifier, StringComparison.OrdinalIgnoreCase))
        {
            LogNonLazerScoreProcessedIgnored(_logger, sourceServer, null);
            return;
        }

        if (_pendingScores.TryRemove(payload.ScoreId, out IPlayer? player))
        {
            LogDispatchingScoreProcessed(_logger, payload.ScoreId, player.PlayerId, null);
            await player.OnScoreProcessed(payload.ScoreId).ConfigureAwait(false);
        }
        else
        {
            LogNoPendingScoreRegistration(_logger, payload.ScoreId, null);
        }
    }

    private sealed class ScoreProcessedPayload
    {
        [JsonProperty("score_id")]
        public long ScoreId { get; set; }
    }
}