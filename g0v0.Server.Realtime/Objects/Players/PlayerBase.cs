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

        State.ScoreToken = scoreToken;
        State.SpectatorState = spectatorState;

        await facade._manager.BroadcastUserBeganPlaying(this);

        if (scoreToken != null && facade._scoreBuffer != null)
        {
            await facade._scoreBuffer.TryAddAsync(scoreToken.Value, score);
        }
    }

    /// <inheritdoc />
    public async Task SendFrames(FrameDataBundle data)
    {
        if (State.ScoreToken != null && facade._scoreBuffer != null)
        {
            await facade._scoreBuffer.UpdateAsync(State.ScoreToken.Value, data);
        }

        await facade._manager.BroadcastUserSentFrames(this, data);
    }

    /// <inheritdoc />
    public async Task FinishPlaying(SpectatorState spectatorState)
    {
        long? scoreToken = State.ScoreToken;
        if (scoreToken == null)
        {
            return;
        }

        try
        {
            if (facade._scoreBuffer == null)
            {
                return;
            }

            var score = await facade._scoreBuffer.DequeueAsync(scoreToken.Value);
            if (score != null)
            {
                await ProcessScore(score);
            }
        }
        finally
        {
            State.SpectatorState = spectatorState;
            State.ScoreToken = null;
        }

        await EndPlaySession();
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

    private async Task ProcessScore(Score score)
    {
        Debug.Assert(score != null && State.ScoreToken != null, "score != null && State.ScoreToken != null");

        long scoreToken = State.ScoreToken.Value;

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