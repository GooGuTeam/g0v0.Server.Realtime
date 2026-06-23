// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using osu.Game.Online.Spectator;
using osu.Game.Scoring;

namespace g0v0.Server.Realtime.Services;

/// <summary>
/// Stores partial spectator scores while their frame data is being streamed.
/// </summary>
/// <param name="memoryCache">The memory cache used to store buffered scores.</param>
/// <param name="logger">The logger used for spectator score buffer events.</param>
public class ScoreBuffer(IMemoryCache memoryCache, ILogger<ScoreBuffer> logger)
{
    private const int LockCount = 1024;

    private static readonly SemaphoreSlim[] ScoreLocks = Enumerable.Range(0, LockCount)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    private static readonly PropertyInfo? HeaderTotalScoreWithoutModsProperty =
        typeof(FrameHeader).GetProperty("TotalScoreWithoutMods");

    private static readonly PropertyInfo? HeaderPausesProperty =
        typeof(FrameHeader).GetProperty("Pauses");

    private static readonly PropertyInfo? ScoreInfoPausesProperty =
        typeof(ScoreInfo).GetProperty("Pauses");

    /// <summary>
    /// Gets or sets the amount of time after which a score can be dropped from the buffer.
    /// </summary>
    public TimeSpan TimeoutInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Attempts to add a score to the buffer.
    /// </summary>
    /// <param name="scoreTokenId">The score token ID.</param>
    /// <param name="score">The score to buffer.</param>
    /// <returns><see langword="true" /> when the score was added; otherwise <see langword="false" />.</returns>
    public async Task<bool> TryAddAsync(long scoreTokenId, Score score)
    {
        var semaphore = GetLock(scoreTokenId);
        await semaphore.WaitAsync();

        try
        {
            if (memoryCache.TryGetValue(GetCacheKey(scoreTokenId), out BufferedScore? _))
            {
                logger.LogWarning(
                    "Could not add spectator score buffer because token {ScoreToken} already exists.",
                    scoreTokenId);
                return false;
            }

            SetBufferedScore(scoreTokenId, new BufferedScore(score));
            logger.LogInformation(
                "Added spectator score buffer for token {ScoreToken}.",
                scoreTokenId);
            return true;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Updates a buffered score with newly received spectator frame data.
    /// </summary>
    /// <param name="scoreTokenId">The score token ID.</param>
    /// <param name="data">The frame data bundle.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UpdateAsync(long scoreTokenId, FrameDataBundle data)
    {
        var semaphore = GetLock(scoreTokenId);
        await semaphore.WaitAsync();

        try
        {
            if (!memoryCache.TryGetValue(GetCacheKey(scoreTokenId), out BufferedScore? bufferedScore) || bufferedScore == null)
            {
                logger.LogWarning(
                    "Could not update spectator score buffer because token {ScoreToken} was not found.",
                    scoreTokenId);
                return;
            }

            var scoreInfo = bufferedScore.Score.ScoreInfo;

            scoreInfo.Accuracy = data.Header.Accuracy;
            scoreInfo.Statistics = data.Header.Statistics;
            scoreInfo.MaxCombo = data.Header.MaxCombo;
            scoreInfo.Combo = data.Header.Combo;
            scoreInfo.TotalScore = data.Header.TotalScore;

            if (data.Header.Mods != null)
            {
                scoreInfo.APIMods = data.Header.Mods;
            }

            UpdateTotalScoreWithoutMods(scoreInfo, data.Header);
            UpdatePauses(scoreInfo, data.Header);

            bufferedScore.Score.Replay.Frames.AddRange(data.Frames);
            bufferedScore.LastUpdated = DateTimeOffset.Now;

            SetBufferedScore(scoreTokenId, bufferedScore);
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "Updated spectator score buffer for token {ScoreToken}. FrameCount: {FrameCount}, TotalBufferedFrames: {TotalBufferedFrames}, TotalScore: {TotalScore}.",
                    scoreTokenId,
                    data.Frames.Count,
                    bufferedScore.Score.Replay.Frames.Count,
                    scoreInfo.TotalScore);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Removes and returns a buffered score.
    /// </summary>
    /// <param name="scoreTokenId">The score token ID.</param>
    /// <returns>The buffered score, or <see langword="null" /> if no score was buffered for the token.</returns>
    public async Task<Score?> DequeueAsync(long scoreTokenId)
    {
        var semaphore = GetLock(scoreTokenId);
        await semaphore.WaitAsync();

        try
        {
            if (!memoryCache.TryGetValue(GetCacheKey(scoreTokenId), out BufferedScore? bufferedScore) || bufferedScore == null)
            {
                logger.LogWarning(
                    "Could not dequeue spectator score buffer because token {ScoreToken} was not found.",
                    scoreTokenId);
                return null;
            }

            memoryCache.Remove(GetCacheKey(scoreTokenId));
            logger.LogInformation(
                "Dequeued spectator score buffer for token {ScoreToken}. TotalBufferedFrames: {TotalBufferedFrames}.",
                scoreTokenId,
                bufferedScore.Score.Replay.Frames.Count);
            return bufferedScore.Score;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static string GetCacheKey(long scoreTokenId) => $"score-buffer:{scoreTokenId}";

    private static SemaphoreSlim GetLock(long scoreTokenId) => ScoreLocks[(int)((ulong)scoreTokenId % LockCount)];

    private static void UpdateTotalScoreWithoutMods(ScoreInfo scoreInfo, FrameHeader header)
    {
        if (HeaderTotalScoreWithoutModsProperty?.GetValue(header) is int totalScoreWithoutMods)
        {
            scoreInfo.TotalScoreWithoutMods = totalScoreWithoutMods;
        }
    }

    private static void UpdatePauses(ScoreInfo scoreInfo, FrameHeader header)
    {
        if (HeaderPausesProperty?.GetValue(header) is not { } pauses || ScoreInfoPausesProperty?.GetValue(scoreInfo) is not { } scorePauses)
        {
            return;
        }

        scorePauses.GetType().GetMethod(nameof(List<object>.Clear), Type.EmptyTypes)?.Invoke(scorePauses, null);
        scorePauses.GetType().GetMethod(nameof(List<object>.AddRange))?.Invoke(scorePauses, [pauses]);
    }

    private void SetBufferedScore(long scoreTokenId, BufferedScore bufferedScore)
    {
        memoryCache.Set(
            GetCacheKey(scoreTokenId),
            bufferedScore,
            new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeoutInterval,
            });
    }

    /// <summary>
    /// A buffered partial score and the timestamp at which it was last updated.
    /// </summary>
    /// <param name="score">The buffered score.</param>
    public sealed class BufferedScore(Score score)
    {
        /// <summary>
        /// Gets the buffered score.
        /// </summary>
        public Score Score { get; } = score;

        /// <summary>
        /// Gets or sets the timestamp at which the score was last updated.
        /// </summary>
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.Now;
    }
}