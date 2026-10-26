// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Microsoft.AspNetCore.SignalR;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;

namespace g0v0.Server.Realtime.Hubs;

/// <summary>
/// Centralises delivery of multiplayer room events to connected lazer clients.
/// </summary>
/// <remarks>
/// All room-wide events are delivered to the SignalR group named
/// <c>multiplayer:room:{roomId}</c> which every room member joins on connect.
/// Transient events (countdowns) use <see cref="IMultiplayerClient.MatchEvent"/>,
/// gameplay lifecycle events use dedicated client callbacks, while persistent
/// state changes use the state callbacks so late joiners can reconstruct the
/// room from the snapshot.
/// </remarks>
public class MultiplayerEventDispatcher(
    IHubContext<MultiplayerHub, IMultiplayerClient> hubContext,
    ILogger<MultiplayerEventDispatcher> logger)
{
    /// <summary>
    /// Builds the SignalR group name for a room.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>The group name.</returns>
    public static string GetGroupId(long roomId) => $"multiplayer:room:{roomId}";

    /// <summary>
    /// Subscribes a connection to a room's event group.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="connectionId">The connection ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SubscribePlayerAsync(long roomId, string connectionId)
    {
        await hubContext.Groups.AddToGroupAsync(connectionId, GetGroupId(roomId));
    }

    /// <summary>
    /// Unsubscribes a connection from a room's event group.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="connectionId">The connection ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UnsubscribePlayerAsync(long roomId, string connectionId)
    {
        await hubContext.Groups.RemoveFromGroupAsync(connectionId, GetGroupId(roomId));
    }

    /// <summary>
    /// Sends the room state change to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="newState">The new room state.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostRoomStateChangedAsync(long roomId, MultiplayerRoomState newState)
    {
        logger.LogDebug("Room {RoomId} state changed to {State}.", roomId, newState);
        return hubContext.Clients.Group(GetGroupId(roomId)).RoomStateChanged(newState);
    }

    /// <summary>
    /// Sends the room settings change to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="newSettings">The new room settings.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostRoomSettingsChangedAsync(long roomId, MultiplayerRoomSettings newSettings)
    {
        logger.LogDebug("Room {RoomId} settings changed.", roomId);
        return hubContext.Clients.Group(GetGroupId(roomId)).SettingsChanged(newSettings);
    }

    /// <summary>
    /// Sends a user-joined event to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="user">The user who joined.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostUserJoinedAsync(long roomId, MultiplayerRoomUser user)
    {
        logger.LogDebug("User {UserId} joined room {RoomId}.", user.UserID, roomId);
        return hubContext.Clients.Group(GetGroupId(roomId)).UserJoined(user);
    }

    /// <summary>
    /// Sends a user-left event to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="user">The user who left.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostUserLeftAsync(long roomId, MultiplayerRoomUser user)
    {
        logger.LogDebug("User {UserId} left room {RoomId}.", user.UserID, roomId);
        return hubContext.Clients.Group(GetGroupId(roomId)).UserLeft(user);
    }

    /// <summary>
    /// Sends a user-kicked event to all members and the kicked user.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="user">The user who was kicked.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostUserKickedAsync(long roomId, MultiplayerRoomUser user)
    {
        logger.LogDebug("User {UserId} was kicked from room {RoomId}.", user.UserID, roomId);
        return Task.WhenAll(
            hubContext.Clients.User(user.UserID.ToString()).UserKicked(user),
            hubContext.Clients.Group(GetGroupId(roomId)).UserKicked(user));
    }

    /// <summary>
    /// Sends a host-changed event to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="userId">The ID of the new host.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostHostChangedAsync(long roomId, int userId)
    {
        logger.LogDebug("Host of room {RoomId} changed to user {UserId}.", roomId, userId);
        return hubContext.Clients.Group(GetGroupId(roomId)).HostChanged(userId);
    }

    /// <summary>
    /// Sends a user-role change to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="userId">The user whose role changed.</param>
    /// <param name="newRole">The new role.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostUserRoleChangedAsync(long roomId, int userId, MultiplayerRoomUserRole newRole)
    {
        logger.LogDebug("User {UserId} in room {RoomId} changed role to {Role}.", userId, roomId, newRole);
        return hubContext.Clients.Group(GetGroupId(roomId)).UserRoleChanged(userId, newRole);
    }

    /// <summary>
    /// Sends a user-state change to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="newUserState">The new user state.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostUserStateChangedAsync(long roomId, int userId, MultiplayerUserState newUserState)
    {
        logger.LogDebug("User {UserId} in room {RoomId} changed state to {State}.", userId, roomId, newUserState);
        return hubContext.Clients.Group(GetGroupId(roomId)).UserStateChanged(userId, newUserState);
    }

    /// <summary>
    /// Sends a match user-state change to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="newMatchUserState">The new match user state.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostMatchUserStateChangedAsync(long roomId, int userId, MatchUserState? newMatchUserState)
    {
        logger.LogDebug("User {UserId} in room {RoomId} changed match state.", userId, roomId);
        return hubContext.Clients.Group(GetGroupId(roomId)).MatchUserStateChanged(userId, newMatchUserState!);
    }

    /// <summary>
    /// Sends a match room-state change to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="state">The new match room state.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostMatchRoomStateChangedAsync(long roomId, MatchRoomState? state)
    {
        logger.LogDebug("Match state of room {RoomId} changed.", roomId);
        return hubContext.Clients.Group(GetGroupId(roomId)).MatchRoomStateChanged(state!);
    }

    /// <summary>
    /// Sends a transient match event (roll, countdown) to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="e">The match event.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostMatchEventAsync(long roomId, MatchServerEvent e)
    {
        logger.LogDebug("Match event {EventType} in room {RoomId}.", e.GetType().Name, roomId);
        return hubContext.Clients.Group(GetGroupId(roomId)).MatchEvent(e);
    }

    /// <summary>
    /// Sends a countdown-started event to all members.
    /// </summary>
    /// <typeparam name="T">The countdown type.</typeparam>
    /// <param name="roomId">The room ID.</param>
    /// <param name="countdown">The started countdown.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostCountdownStartedAsync<T>(long roomId, T countdown)
        where T : MultiplayerCountdown
    {
        logger.LogDebug("Countdown {CountdownId} started in room {RoomId}.", countdown.ID, roomId);
        return hubContext.Clients.Group(GetGroupId(roomId)).MatchEvent(
            new osu.Game.Online.Multiplayer.Countdown.CountdownStartedEvent(countdown));
    }

    /// <summary>
    /// Sends a countdown-stopped event to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="countdown">The stopped countdown.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostCountdownStoppedAsync(long roomId, MultiplayerCountdown countdown)
    {
        logger.LogDebug("Countdown {CountdownId} stopped in room {RoomId}.", countdown.ID, roomId);
        return hubContext.Clients.Group(GetGroupId(roomId)).MatchEvent(
            new osu.Game.Online.Multiplayer.Countdown.CountdownStoppedEvent(countdown.ID));
    }

    /// <summary>
    /// Sends a user beatmap-availability change to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="beatmapAvailability">The new beatmap availability.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostUserBeatmapAvailabilityChangedAsync(long roomId, int userId, BeatmapAvailability beatmapAvailability)
    {
        return hubContext.Clients.Group(GetGroupId(roomId)).UserBeatmapAvailabilityChanged(userId, beatmapAvailability);
    }

    /// <summary>
    /// Sends a user style change to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="beatmapId">The selected beatmap ID.</param>
    /// <param name="rulesetId">The selected ruleset ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostUserStyleChangedAsync(long roomId, int userId, int? beatmapId, int? rulesetId)
    {
        return hubContext.Clients.Group(GetGroupId(roomId)).UserStyleChanged(userId, beatmapId, rulesetId);
    }

    /// <summary>
    /// Sends a user mods change to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="mods">The new mods.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostUserModsChangedAsync(long roomId, int userId, IEnumerable<APIMod> mods)
    {
        return hubContext.Clients.Group(GetGroupId(roomId)).UserModsChanged(userId, mods);
    }

    /// <summary>
    /// Sends a playlist-item-added event to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="item">The added playlist item.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostPlaylistItemAddedAsync(long roomId, MultiplayerPlaylistItem item)
    {
        logger.LogDebug("Playlist item {ItemId} added to room {RoomId}.", item.ID, roomId);
        return hubContext.Clients.Group(GetGroupId(roomId)).PlaylistItemAdded(item);
    }

    /// <summary>
    /// Sends a playlist-item-changed event to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="item">The changed playlist item.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostPlaylistItemChangedAsync(long roomId, MultiplayerPlaylistItem item)
    {
        logger.LogDebug("Playlist item {ItemId} changed in room {RoomId}.", item.ID, roomId);
        return hubContext.Clients.Group(GetGroupId(roomId)).PlaylistItemChanged(item);
    }

    /// <summary>
    /// Sends a playlist-item-removed event to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="playlistItemId">The removed playlist item ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostPlaylistItemRemovedAsync(long roomId, long playlistItemId)
    {
        logger.LogDebug("Playlist item {ItemId} removed from room {RoomId}.", playlistItemId, roomId);
        return hubContext.Clients.Group(GetGroupId(roomId)).PlaylistItemRemoved(playlistItemId);
    }

    /// <summary>
    /// Sends a user-voted-to-skip-intro event to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="voted">Whether the user voted to skip.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostUserVotedToSkipIntroAsync(long roomId, int userId, bool voted)
    {
        return hubContext.Clients.Group(GetGroupId(roomId)).UserVotedToSkipIntro(userId, voted);
    }

    /// <summary>
    /// Sends a vote-to-skip-intro-passed event to all members.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostVoteToSkipIntroPassedAsync(long roomId)
    {
        return hubContext.Clients.Group(GetGroupId(roomId)).VoteToSkipIntroPassed();
    }

    /// <summary>
    /// Sends an invite to a single user.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="invitedUserId">The invited user ID.</param>
    /// <param name="invitedBy">The inviting user ID.</param>
    /// <param name="password">The room password.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostUserInvitedAsync(long roomId, int invitedUserId, int invitedBy, string password)
    {
        logger.LogDebug("User {InvitedBy} invited user {InvitedUserId} to room {RoomId}.", invitedBy, invitedUserId, roomId);
        return hubContext.Clients.User(invitedUserId.ToString()).Invited(invitedBy, roomId, password);
    }

    /// <summary>
    /// Sends the load-requested event to a single user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostLoadRequestedAsync(int userId)
    {
        return hubContext.Clients.User(userId.ToString()).LoadRequested();
    }

    /// <summary>
    /// Broadcasts the load-requested event to all members of a room.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostLoadRequestedToGroupAsync(long roomId)
    {
        return hubContext.Clients.Group(GetGroupId(roomId)).LoadRequested();
    }

    /// <summary>
    /// Sends the gameplay-started event to a single user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostGameplayStartedAsync(int userId)
    {
        return hubContext.Clients.User(userId.ToString()).GameplayStarted();
    }

    /// <summary>
    /// Sends the gameplay-aborted event to a single user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="reason">The abort reason.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostGameplayAbortedAsync(int userId, GameplayAbortReason reason)
    {
        return hubContext.Clients.User(userId.ToString()).GameplayAborted(reason);
    }

    /// <summary>
    /// Broadcasts the gameplay-aborted event to all members of a room.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <param name="reason">The abort reason.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostGameplayAbortedToGroupAsync(long roomId, GameplayAbortReason reason)
    {
        return hubContext.Clients.Group(GetGroupId(roomId)).GameplayAborted(reason);
    }

    /// <summary>
    /// Sends the results-ready event to a single user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostResultsReadyAsync(int userId)
    {
        return hubContext.Clients.User(userId.ToString()).ResultsReady();
    }

    /// <summary>
    /// Broadcasts the results-ready event to all members of a room.
    /// </summary>
    /// <param name="roomId">The room ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task PostResultsReadyToGroupAsync(long roomId)
    {
        return hubContext.Clients.Group(GetGroupId(roomId)).ResultsReady();
    }
}