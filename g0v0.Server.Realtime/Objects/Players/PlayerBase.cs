// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Realtime.Objects.Notifications;
using g0v0.Server.Realtime.Objects.States;
using g0v0.Server.Realtime.Objects.States.Activity;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.Spectator;
using osu.Game.Rulesets.Scoring;
using osu.Game.Users;
using Score = osu.Game.Scoring.Score;

namespace g0v0.Server.Realtime.Objects.Players;

/// <summary>
/// Provides shared state and lifecycle handling for connected players.
/// </summary>
/// <param name="playerId">
/// The player ID.
/// </param>
/// <param name="facade">
/// The player dependency facade.
/// </param>
/// <param name="state">
/// The initial player state.
/// </param>
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
        await facade.Manager.AddPlayer(this);
    }

    /// <inheritdoc />
    public async Task Offline(bool isKicked = false)
    {
        await facade.Manager.RemovePlayer(this, isKicked);
    }

    /// <inheritdoc />
    public async Task ChangePlayerActivityAsync(IUserActivity newActivity)
    {
        if (newActivity == State.UserActivity)
        {
            return;
        }

        State.UserActivity = newActivity;
        await facade.Manager.BroadcastPlayerChangeActivity(this, newActivity);
    }

    /// <inheritdoc />
    public async Task ChangePlayerStatusAsync(UserStatus? newStatus)
    {
        if (newStatus == State.UserStatus)
        {
            return;
        }

        State.UserStatus = newStatus;
        await facade.Manager.BroadcastPlayerChangeStatus(this, newStatus);
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
                    facade.ConfigManager?.Get<RealtimeConfig>().MaxStartedScores ?? RealtimeConfig.DefaultMaxStartedScores);

                while (State.ScoreTokens.Count >= maxStartedScores)
                {
                    long expiredToken = State.ScoreTokens[0];
                    State.ScoreTokens.RemoveAt(0);
                    if (facade.ScoreBuffer != null)
                    {
                        Score? expiredScore = await facade.ScoreBuffer.DequeueAsync(expiredToken);
                        if (expiredScore != null)
                        {
                            await ProcessScore(expiredToken, expiredScore);
                        }
                    }

                    facade.Logger?.LogWarning(
                        "Score for token {ScoreToken} was dropped from buffer due to exceeding limit.",
                        expiredToken);
                }

                State.ScoreTokens.Add(scoreToken.Value);
            }

            if (facade.ScoreBuffer != null)
            {
                await facade.ScoreBuffer.TryAddAsync(scoreToken.Value, score);
            }
        }

        // The ambient score token is kept in sync with the most recently started score.
        State.ScoreToken = scoreToken;

        await facade.Manager.BroadcastUserBeganPlaying(this);
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

            if (facade.ScoreBuffer != null)
            {
                await facade.ScoreBuffer.UpdateAsync(scoreToken.Value, data);
            }
        }

        await facade.Manager.BroadcastUserSentFrames(this, data);
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

                if (facade.ScoreBuffer != null)
                {
                    Score? score = await facade.ScoreBuffer.DequeueAsync(scoreToken.Value);
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
        await facade.Manager.UserWatchingPlayer(this, target);
    }

    /// <inheritdoc />
    public async Task StopWatchPlayer(IPlayer target)
    {
        await facade.Manager.UserStoppedWatchingPlayer(this, target);
    }

    /// <inheritdoc />
    public async Task JoinRoom(long roomId)
    {
        if (facade.RoomManager == null)
        {
            throw new InvalidOperationException("Room manager is not available on this player facade.");
        }

        await facade.Manager.UserJoinRoom(facade.RoomManager, this, roomId);
    }

    /// <inheritdoc />
    public async Task LeaveRoom(long roomId)
    {
        if (facade.RoomManager == null)
        {
            throw new InvalidOperationException("Room manager is not available on this player facade.");
        }

        await facade.Manager.UserLeaveRoom(facade.RoomManager, this);
    }

    /// <inheritdoc />
    public virtual Task OnNewMessage(IPlayer player, ChatMessage message)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task OnChannelJoin(IPlayer player, int channelId)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task OnChannelLeave(IPlayer player, int channelId)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task OnUserSilent(IPlayer player, int channelId, DateTimeOffset? until = null)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Actual message sending is performed by the chat controllers through the
    /// notification server (which persists and broadcasts); this generic
    /// implementation only delegates when a notification server is available.
    /// </remarks>
    public virtual async Task SendMessage(int channelId, string message, bool isAction, string? callbackUuid = null)
    {
        if (facade.NotificationServer == null)
        {
            return;
        }

        await facade.NotificationServer.SendMessage(channelId, PlayerId, message, isAction, callbackUuid);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Actual channel joining is performed by the chat controllers through the
    /// notification server; this generic implementation only delegates when a
    /// notification server is available.
    /// </remarks>
    public virtual async Task JoinChannel(IPlayer player, int channelId)
    {
        if (facade.NotificationServer == null)
        {
            return;
        }

        await facade.NotificationServer.JoinChannel(player, channelId);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Actual channel leaving is performed by the chat controllers through the
    /// notification server; this generic implementation only delegates when a
    /// notification server is available.
    /// </remarks>
    public virtual async Task LeaveChannel(IPlayer player, int channelId)
    {
        if (facade.NotificationServer == null)
        {
            return;
        }

        await facade.NotificationServer.LeaveChannel(player, channelId);
    }

    /// <inheritdoc />
    public virtual async Task Ack()
    {
        if (facade.NotificationServer == null)
        {
            return;
        }

        await facade.NotificationServer.Ack(this);
    }

    /// <inheritdoc />
    public virtual Task OnNewNotification(IPlayer player, NotificationDetail detail)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Determines whether another player has the same identity.
    /// </summary>
    /// <param name="other">
    /// The player to compare.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both players have the same ID and server.
    /// </returns>
    public bool Equals(IPlayer? other)
    {
        return other != null && (other.PlayerId == PlayerId && string.Equals(other.Server, Server, StringComparison.Ordinal));
    }

    private async Task ProcessScore(long scoreToken, Score score)
    {
        Debug.Assert(score != null, "The score to process must not be null.");

        // Do nothing with scores on unranked beatmaps.
        BeatmapOnlineStatus status = score.ScoreInfo.BeatmapInfo!.Status;
        bool allRanked = false;
        if (facade.ConfigManager != null)
        {
            allRanked = facade.ConfigManager.Get<GameConfig>().EnableAllBeatmapLeaderboard;
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

        if (facade.ScoreUploader != null)
        {
            await facade.ScoreUploader.EnqueueAsync(scoreToken, score);
        }

        // await scoreProcessedSubscriber.RegisterForSingleScoreAsync(Context.ConnectionId, Context.GetUserId(),
        //     scoreToken);
        if (facade.ScoreProcessedNotificationService != null)
        {
            await facade.ScoreProcessedNotificationService.RegisterForSingleScoreAsync(this, scoreToken);
        }
    }

    private async Task EndPlaySession()
    {
        // Ensure that the state is no longer playing (e.g. if client crashes).
        if (State.SpectatorState?.State == SpectatedUserState.Playing)
        {
            State.SpectatorState.State = SpectatedUserState.Quit;
        }

        await facade.Manager.BroadcastUserFinishedPlaying(this);
    }
}