// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Threading.Channels;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Storage;
using g0v0.Server.Realtime.Objects;
using osu.Game.Beatmaps;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;
using ConfigurationManager = g0v0.Server.Common.Configuration.ConfigurationManager;

namespace g0v0.Server.Realtime.Services;

/// <summary>
/// Uploads spectator replay files after the corresponding score is persisted.
/// </summary>
public class ScoreUploader : IDisposable
{
    private readonly CancellationTokenSource cancellationSource;
    private readonly CancellationToken cancellationToken;

    private readonly ConfigurationManager configManager;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<ScoreUploader> logger;
    private readonly IStorageService scoreStorage;
    private readonly Channel<ReplayUploadItem> _channel = Channel.CreateUnbounded<ReplayUploadItem>();

    private int _remainingUsages;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScoreUploader"/> class.
    /// </summary>
    /// <param name="configManager">The configuration manager.</param>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="scoreStorage">The storage service used for replay files.</param>
    public ScoreUploader(
        ConfigurationManager configManager,
        IServiceScopeFactory scopeFactory,
        ILogger<ScoreUploader> logger,
        IStorageService scoreStorage)
    {
        this.configManager = configManager;
        this.scopeFactory = scopeFactory;
        this.logger = logger;
        this.scoreStorage = scoreStorage;

        cancellationSource = new CancellationTokenSource();
        cancellationToken = cancellationSource.Token;

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
    /// <param name="token">The score token ID.</param>
    /// <param name="score">The score containing replay frames.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task EnqueueAsync(long token, Score score)
    {
        if (!configManager.Get<RealtimeConfig>().SaveReplays)
        {
            logger.LogDebug(
                "Skipping spectator replay enqueue because replay saving is disabled. ScoreToken: {ScoreToken}.",
                token);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var beatmapRepository = scope.ServiceProvider.GetRequiredService<IBeatmapRepository>();
        var beatmapChecksum = score.ScoreInfo.BeatmapInfo!.MD5Hash;
        var beatmap = await beatmapRepository.GetByChecksumAsync(beatmapChecksum);
        if (beatmap == null)
        {
            logger.LogWarning(
                "Skipping spectator replay enqueue because beatmap checksum {BeatmapChecksum} was not found. ScoreToken: {ScoreToken}.",
                beatmapChecksum,
                token);
            return;
        }

        Interlocked.Increment(ref _remainingUsages);

        var uploadCancellation = new CancellationTokenSource();
        uploadCancellation.CancelAfter(TimeSpan.FromMilliseconds(TimeoutInterval));

        await _channel.Writer.WriteAsync(
            new ReplayUploadItem(token, score, beatmap, uploadCancellation),
            cancellationToken);
        logger.LogInformation(
            "Enqueued spectator replay upload for token {ScoreToken}. BeatmapId: {BeatmapId}, RemainingUsages: {RemainingUsages}.",
            token,
            beatmap.Id,
            RemainingUsages);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        cancellationSource.Cancel();
        cancellationSource.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ReadLoop()
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var item = await _channel.Reader.ReadAsync(cancellationToken);

            try
            {
                using var scope = scopeFactory.CreateScope();
                var scoreRepository = scope.ServiceProvider.GetRequiredService<IScoreRepository>();
                Common.Database.Models.Score? dbScore = await scoreRepository.GetScoreByToken(item.Token);

                // Timeout occurred, drop item.
                // This could be from score upload not completing in time, or from exception handling.
                if (item.CancellationToken.IsCancellationRequested)
                {
                    if (dbScore == null)
                    {
                        logger.LogError("Score upload timed out for token: {TokenId}", item.Token);
                    }
                    else
                    {
                        logger.LogError(
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
                    logger.LogDebug(
                        "Score upload is still pending for spectator replay token {ScoreToken}. Queueing retry.",
                        item.Token);
                    QueueForRetry(item);
                    continue;
                }

                if (!dbScore.Passed)
                {
                    logger.LogInformation(
                        "Dropping spectator replay upload for failed score. ScoreToken: {ScoreToken}, ScoreId: {ScoreId}.",
                        item.Token,
                        dbScore.Id);
                    DropItem(item);
                    continue;
                }

                item.Score.ScoreInfo.OnlineID = dbScore.Id;
                item.Score.ScoreInfo.Passed = dbScore.Passed;

                // TODO: g0v0-server v1 doesn't store the beatmap version.
                using (var outStream = new MemoryStream())
                {
                    new LegacyScoreEncoder(item.Score, new Beatmap { BeatmapVersion = 14 }).Encode(
                        outStream,
                        leaveOpen: true);
                    outStream.Seek(0, SeekOrigin.Begin);
                    logger.LogInformation(
                        "Spectator replay upload completed for token {ScoreToken}. ScoreId: {ScoreId}.",
                        item.Token,
                        dbScore.Id);

                    // TODO: `_lazer_replay` is backward compatible with g0v0-server v1.
                    var replayPath = $"replays/{dbScore.Id}_{dbScore.BeatmapId}_{dbScore.UserId}_lazer_replay.osr";
                    await scoreStorage.WriteFileAsync(replayPath, outStream.GetBuffer(), "application/x-osu-replay");
                }

                await scoreRepository.MarkScoreHasReplay(item.Score.ScoreInfo.OnlineID);
                DropItem(item);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error during score upload");

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
                    await Task.Delay(100, cancellationToken);
                    await _channel.Writer.WriteAsync(item, cancellationToken);
                },
                cancellationToken).ContinueWith(
                t =>
                {
                    if (t.IsFaulted)
                    {
                        logger.LogError(t.Exception, "Error attempting to re-queue score upload");
                    }
                },
                cancellationToken);
        }

        void DropItem(ReplayUploadItem item)
        {
            item.Dispose();
            Interlocked.Decrement(ref _remainingUsages);
        }
    }

    private async Task MonitorLoop()
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(1000, cancellationToken);
        }
    }
}