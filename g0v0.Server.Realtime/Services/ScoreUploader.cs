// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Threading.Channels;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Storage;
using g0v0.Server.Realtime.Objects;
using osu.Game.Beatmaps;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;

namespace g0v0.Server.Realtime.Services;

/// <summary>
/// Uploads spectator replay files after the corresponding score is persisted.
/// </summary>
public class ScoreUploader : IDisposable
{
    private readonly CancellationTokenSource _cancellationSource;
    private readonly CancellationToken _cancellationToken;

    private readonly ConfigManager _configManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScoreUploader> _logger;
    private readonly IStorageService _scoreStorage;
    private readonly Channel<ReplayUploadItem> _channel = Channel.CreateUnbounded<ReplayUploadItem>();

    private int _remainingUsages;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScoreUploader"/> class.
    /// </summary>
    /// <param name="configManager">
    /// The configuration manager.
    /// </param>
    /// <param name="scopeFactory">
    /// The service scope factory.
    /// </param>
    /// <param name="logger">
    /// The logger.
    /// </param>
    /// <param name="scoreStorage">
    /// The storage service used for replay files.
    /// </param>
    public ScoreUploader(
        ConfigManager configManager,
        IServiceScopeFactory scopeFactory,
        ILogger<ScoreUploader> logger,
        IStorageService scoreStorage)
    {
        this._configManager = configManager;
        this._scopeFactory = scopeFactory;
        this._logger = logger;
        this._scoreStorage = scoreStorage;

        _cancellationSource = new CancellationTokenSource();
        _cancellationToken = _cancellationSource.Token;

        for (int i = 0; i < configManager.Get<RealtimeConfig>().ReplayUploaderConcurrency; ++i)
        {
            _ = Task.Factory.StartNew(ReadLoop, TaskCreationOptions.LongRunning);
        }

        _ = Task.Factory.StartNew(MonitorLoop, TaskCreationOptions.LongRunning);
    }

    /// <summary>
    /// Gets the number of queued or active replay uploads.
    /// </summary>
    public int RemainingUsages => _remainingUsages;

    /// <summary>
    /// Gets or sets the upload timeout interval in milliseconds.
    /// </summary>
    public double TimeoutInterval { get; set; } = 30000;

    /// <summary>
    /// Enqueues a score replay for upload if replay persistence is enabled and the beatmap exists.
    /// </summary>
    /// <param name="token">
    /// The score token ID.
    /// </param>
    /// <param name="score">
    /// The score containing replay frames.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    public async Task EnqueueAsync(long token, Score score)
    {
        if (!_configManager.Get<RealtimeConfig>().SaveReplays)
        {
            _logger.LogDebug(
                "Skipping spectator replay enqueue because replay saving is disabled. ScoreToken: {ScoreToken}.",
                token);
            return;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        IBeatmapRepository beatmapRepository = scope.ServiceProvider.GetRequiredService<IBeatmapRepository>();
        string beatmapChecksum = score.ScoreInfo.BeatmapInfo!.MD5Hash;
        Common.Database.Models.Beatmap? beatmap = await beatmapRepository.GetByChecksumAsync(beatmapChecksum);
        if (beatmap == null)
        {
            _logger.LogWarning(
                "Skipping spectator replay enqueue because beatmap checksum {BeatmapChecksum} was not found. ScoreToken: {ScoreToken}.",
                beatmapChecksum,
                token);
            return;
        }

        Interlocked.Increment(ref _remainingUsages);

        CancellationTokenSource uploadCancellation = new();
        uploadCancellation.CancelAfter(TimeSpan.FromMilliseconds(TimeoutInterval));

        await _channel.Writer.WriteAsync(
            new ReplayUploadItem(token, score, beatmap, uploadCancellation),
            _cancellationToken);
        _logger.LogInformation(
            "Enqueued spectator replay upload for token {ScoreToken}. BeatmapId: {BeatmapId}, RemainingUsages: {RemainingUsages}.",
            token,
            beatmap.Id,
            RemainingUsages);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _cancellationSource.Cancel();
        _cancellationSource.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ReadLoop()
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            ReplayUploadItem item = await _channel.Reader.ReadAsync(_cancellationToken);

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IScoreRepository scoreRepository = scope.ServiceProvider.GetRequiredService<IScoreRepository>();
                Common.Database.Models.Score? dbScore = await scoreRepository.GetScoreByToken(item.Token);

                // Timeout occurred, drop item.
                // This could be from score upload not completing in time, or from exception handling.
                if (item.CancellationToken.IsCancellationRequested)
                {
                    if (dbScore == null)
                    {
                        _logger.LogError("Score upload timed out for token: {TokenId}", item.Token);
                    }
                    else
                    {
                        _logger.LogError(
                            "Score failed to upload successfully for token {ScoreToken}. ScoreId: {ScoreId}.",
                            item.Token,
                            dbScore.Id);
                    }

                    DropItem(item);
                    continue;
                }

                if (dbScore == null)
                {
                    // Still waiting for score upload, queue for retry.
                    _logger.LogDebug(
                        "Score upload is still pending for spectator replay token {ScoreToken}. Queueing retry.",
                        item.Token);
                    QueueForRetry(item);
                    continue;
                }

                if (!dbScore.Passed)
                {
                    _logger.LogInformation(
                        "Dropping spectator replay upload for failed score. ScoreToken: {ScoreToken}, ScoreId: {ScoreId}.",
                        item.Token,
                        dbScore.Id);
                    DropItem(item);
                    continue;
                }

                item.Score.ScoreInfo.OnlineID = dbScore.Id;
                item.Score.ScoreInfo.Passed = dbScore.Passed;

                // TODO: g0v0-server v1 doesn't store the beatmap version.
                using (MemoryStream outStream = new())
                {
                    new LegacyScoreEncoder(item.Score, new Beatmap { BeatmapVersion = 14 }).Encode(
                        outStream,
                        leaveOpen: true);
                    outStream.Seek(0, SeekOrigin.Begin);
                    _logger.LogInformation(
                        "Spectator replay upload completed for token {ScoreToken}. ScoreId: {ScoreId}.",
                        item.Token,
                        dbScore.Id);

                    // TODO: `_lazer_replay` is backward compatible with g0v0-server v1.
                    string replayPath = $"replays/{dbScore.Id}_{dbScore.BeatmapId}_{dbScore.UserId}_lazer_replay.osr";
                    await _scoreStorage.WriteFileAsync(replayPath, outStream.GetBuffer(), "application/x-osu-replay", cancellationToken: _cancellationToken);
                }

                await scoreRepository.MarkScoreHasReplay(item.Score.ScoreInfo.OnlineID);
                DropItem(item);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error during score upload");

                // Retry in the case of a transient failure.
                // If things are really borked, items will still be dropped after the timeout interval.
                QueueForRetry(item);
            }
        }

        void QueueForRetry(ReplayUploadItem item)
        {
            _ = Task.Run(
                async () =>
                {
                    // retry after a short delay (to avoid super-tight database query loop)
                    await Task.Delay(100, _cancellationToken);
                    await _channel.Writer.WriteAsync(item, _cancellationToken);
                },
                _cancellationToken).ContinueWith(
                t =>
                {
                    if (t.IsFaulted)
                    {
                        _logger.LogError(t.Exception, "Error attempting to re-queue score upload");
                    }
                },
                _cancellationToken);
        }

        void DropItem(ReplayUploadItem item)
        {
            item.Dispose();
            Interlocked.Decrement(ref _remainingUsages);
        }
    }

    private async Task MonitorLoop()
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(1000, _cancellationToken);
        }
    }
}