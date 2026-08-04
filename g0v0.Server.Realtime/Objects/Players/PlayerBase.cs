// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Realtime.Objects.States;
using g0v0.Server.Realtime.Objects.States.Activity;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.Spectator;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Users;

namespace g0v0.Server.Realtime.Objects.Players;

/// <summary>
/// Provides shared state and lifecycle handling for connected players.
/// </summary>
/// <param name="playerId">The player ID.</param>
/// <param name="facade">The player dependency facade.</param>
/// <param name="state">The initial player state.</param>
public abstract class PlayerBase(int playerId, IPlayerFacade facade, PlayerState? state = null) : IPlayer
{
    /// <inheritdoc />
    public int PlayerId { get; } = playerId;

    /// <inheritdoc />
    public abstract string Server { get; }

    /// <summary>
    /// Gets the dependency facade used by this player.
    /// </summary>
    public IPlayerFacade Facade => facade;

    /// <inheritdoc />
    public PlayerState State { get; } = state ?? new PlayerState(new IdleActivity());

    /// <inheritdoc />
    public abstract Task OnPlayerOnline(IPlayer player);

    /// <inheritdoc />
    public abstract Task OnPlayerOffline(IPlayer player, bool isKicked = false);

    /// <inheritdoc />
    public abstract Task OnPlayerChangeActivity(IPlayer player, IUserActivity activity);

    /// <inheritdoc />
    public abstract Task OnPlayerChangeStatus(IPlayer player, UserStatus? status);

    /// <inheritdoc />
    public async Task Online()
    {
        await facade._manager.AddPlayer(this);
    }

    /// <inheritdoc />
    public async Task Offline(bool isKicked = false)
    {
        await facade._manager.RemovePlayer(this, isKicked);
    }

    /// <inheritdoc />
    public async Task ChangePlayerActivityAsync(IUserActivity newActivity)
    {
        if (newActivity == State.UserActivity)
        {
            return;
        }

        State.UserActivity = newActivity;
        await facade._manager.BroadcastPlayerChangeActivity(this, newActivity);
    }

    /// <inheritdoc />
    public async Task ChangePlayerStatusAsync(UserStatus? newStatus)
    {
        if (newStatus == State.UserStatus)
        {
            return;
        }

        State.UserStatus = newStatus;
        await facade._manager.BroadcastPlayerChangeStatus(this, newStatus);
    }

    /// <inheritdoc />
    public abstract Task OnUserBeganPlaying(IPlayer player);

    /// <inheritdoc />
    public abstract Task OnUserFinishedPlaying(IPlayer player);

    /// <inheritdoc />
    public abstract Task OnUserSentFrames(IPlayer player, FrameDataBundle frame);

    /// <inheritdoc />
    public abstract Task OnWatched(IPlayer source);

    /// <inheritdoc />
    public abstract Task OnWatchedStopped(IPlayer source);

    /// <inheritdoc />
    public abstract Task OnScoreProcessed(long scoreId);

    /// <inheritdoc />
    public async Task BeingPlaying(long? scoreToken, Score score, SpectatorState spectatorState)
    {
        if (spectatorState.RulesetID == null)
        {
            return;
        }

        if (spectatorState.BeatmapID == null)
        {
            return;
        }

        State.SpectatorState = spectatorState;

        if (scoreToken != null)
        {
            if (!State.ScoreTokens.Contains(scoreToken.Value))
            {
                // Read per call so hot-reloaded configuration takes effect without a restart.
                int maxStartedScores = Math.Max(
                    1,
                    facade._configManager?.Get<RealtimeConfig>().MaxStartedScores ?? RealtimeConfig.DefaultMaxStartedScores);

                while (State.ScoreTokens.Count >= maxStartedScores)
                {
                    long expiredToken = State.ScoreTokens[0];
                    State.ScoreTokens.RemoveAt(0);
                    if (facade._scoreBuffer != null)
                    {
                        Score? expiredScore = await facade._scoreBuffer.DequeueAsync(expiredToken);
                        if (expiredScore != null)
                        {
                            await ProcessScore(expiredToken, expiredScore);
                        }
                    }

                    facade._logger?.LogWarning(
                        "Score for token {ScoreToken} was dropped from buffer due to exceeding limit.",
                        expiredToken);
                }

                State.ScoreTokens.Add(scoreToken.Value);
            }

            if (facade._scoreBuffer != null)
            {
                await facade._scoreBuffer.TryAddAsync(scoreToken.Value, score);
            }
        }

        // The ambient score token is kept in sync with the most recently started score.
        State.ScoreToken = scoreToken;

        await facade._manager.BroadcastUserBeganPlaying(this);
    }

    /// <inheritdoc />
    public async Task SendFrames(long? scoreToken, FrameDataBundle data)
    {
        if (scoreToken != null)
        {
            if (!State.ScoreTokens.Contains(scoreToken.Value))
            {
                throw new InvalidOperationException("Incorrect score token supplied.");
            }

            if (facade._scoreBuffer != null)
            {
                await facade._scoreBuffer.UpdateAsync(scoreToken.Value, data);
            }
        }

        await facade._manager.BroadcastUserSentFrames(this, data);
    }

    /// <inheritdoc />
    public async Task FinishPlaying(long? scoreToken, SpectatedUserState finalState)
    {
        bool shouldBroadcastEnd = false;

        try
        {
            shouldBroadcastEnd = scoreToken == null || scoreToken == State.ScoreTokens.LastOrDefault();

            if (scoreToken != null)
            {
                if (!State.ScoreTokens.Remove(scoreToken.Value))
                {
                    throw new InvalidOperationException("Incorrect score token supplied.");
                }

                if (facade._scoreBuffer != null)
                {
                    Score? score = await facade._scoreBuffer.DequeueAsync(scoreToken.Value);
                    if (score == null)
                    {
                        return;
                    }

                    await ProcessScore(scoreToken.Value, score);
                }
            }

            if (State.SpectatorState != null && shouldBroadcastEnd)
            {
                State.SpectatorState.State = finalState;
                await EndPlaySession();
            }
        }
        finally
        {
            if (shouldBroadcastEnd)
            {
                State.SpectatorState = null;
                State.ScoreToken = null;
            }
        }
    }

    /// <inheritdoc />
    public async Task WatchPlayer(IPlayer target)
    {
        await facade._manager.UserWatchingPlayer(this, target);
    }

    /// <inheritdoc />
    public async Task StopWatchPlayer(IPlayer target)
    {
        await facade._manager.UserStoppedWatchingPlayer(this, target);
    }

    /// <summary>
    /// Determines whether another player has the same identity.
    /// </summary>
    /// <param name="other">The player to compare.</param>
    /// <returns><see langword="true"/> when both players have the same ID and server.</returns>
    public bool Equals(IPlayer? other)
    {
        return other != null && (other.PlayerId == PlayerId && other.Server == Server);
    }

    private async Task ProcessScore(long scoreToken, Score score)
    {
        Debug.Assert(score != null, "score != null");

        // Do nothing with scores on unranked beatmaps.
        var status = score.ScoreInfo.BeatmapInfo!.Status;
        bool allRanked = false;
        if (facade._configManager != null)
        {
            allRanked = facade._configManager.Get<GameConfiguration>().EnableAllBeatmapLeaderboard;
        }

        if (!allRanked && status is < BeatmapOnlineStatus.Ranked or > BeatmapOnlineStatus.Loved)
        {
            return;
        }

        // if the user never hit anything, further processing that depends on the score existing can be waived because the client won't have submitted the score anyway.
        // see: https://github.com/ppy/osu/blob/a47ccb8edd2392258b6b7e176b222a9ecd511fc0/osu.Game/Screens/Play/SubmittingPlayer.cs#L281
        if (!score.ScoreInfo.Statistics.Any(s => s.Key.IsHit() && s.Value > 0))
        {
            return;
        }

        score.ScoreInfo.Date = DateTimeOffset.UtcNow;

        // this call is a little expensive due to reflection usage, so only run it at the end of score processing
        // even though in theory the rank could be recomputed after every replay frame.
        score.ScoreInfo.Rank = StandardisedScoreMigrationTools.ComputeRank(score.ScoreInfo);

        if (facade._scoreUploader != null)
        {
            await facade._scoreUploader.EnqueueAsync(scoreToken, score);
        }

        // await scoreProcessedSubscriber.RegisterForSingleScoreAsync(Context.ConnectionId, Context.GetUserId(),
        //     scoreToken);
        if (facade._scoreProcessedNotificationService != null)
        {
            await facade._scoreProcessedNotificationService.RegisterForSingleScoreAsync(this, scoreToken);
        }
    }

    private async Task EndPlaySession()
    {
        // Ensure that the state is no longer playing (e.g. if client crashes).
        if (State.SpectatorState?.State == SpectatedUserState.Playing)
        {
            State.SpectatorState.State = SpectatedUserState.Quit;
        }

        await facade._manager.BroadcastUserFinishedPlaying(this);
    }
}