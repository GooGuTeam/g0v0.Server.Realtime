// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Extensions;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Players;
using g0v0.Server.Realtime.Objects.States;
using Microsoft.AspNetCore.SignalR;
using osu.Game.Online.Metadata;
using osu.Game.Users;

namespace g0v0.Server.Realtime.Hubs;

/// <summary>
/// Handles lazer metadata presence updates and friend presence subscriptions.
/// </summary>
public class MetadataHub(
    PlayerManager playerManager,
    IRelationshipRepository relationshipRepository,
    IHubContext<MetadataHub, IMetadataClient> hubContext)
    : LazerRealtimeHub<IMetadataClient>(playerManager), IMetadataServer
{
    internal const string OnlinePresenceWatchersGroup = "metadata:online-presence-watchers";

    /// <inheritdoc />
    public async override Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        var player = GetOrCreatePlayer(new PlayerFacade(PlayerManager));
        string connectionId = Context.ConnectionId;

        await player.HubConnected(nameof(MetadataHub));

        player.OnPlayerOnlineForMetadataHub = p => BroadcastUserPresenceUpdate(hubContext, connectionId, p);
        player.OnPlayerOfflineForMetadataHub = (p, _) => BroadcastUserPresenceUpdate(hubContext, connectionId, p);
        player.OnPlayerChangeActivityForMetadataHub =
            (p, _) => BroadcastUserPresenceUpdate(hubContext, connectionId, p);
        player.OnPlayerChangeStatusForMetadataHub = (p, _) => BroadcastUserPresenceUpdate(hubContext, connectionId, p);

        await RefreshFriendsAsync(player);
    }

    /// <inheritdoc />
    public async override Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);

        var player = GetPlayer();
        player.OnPlayerOnlineForMetadataHub = null;
        player.OnPlayerOfflineForMetadataHub = null;
        player.OnPlayerChangeActivityForMetadataHub = null;
        player.OnPlayerChangeStatusForMetadataHub = null;

        await player.HubDisconnected(nameof(MetadataHub));
    }

    /// <inheritdoc />
    public Task<BeatmapUpdates> GetChangesSince(int queueId)
    {
        throw new NotSupportedException("Beatmap change tracking is not implemented by the realtime server.");
    }

    /// <inheritdoc />
    public async Task UpdateActivity(UserActivity? activity)
    {
        var player = GetPlayer();
        await player.ChangePlayerActivityAsync(activity.ToRealtimeActivity());
    }

    /// <inheritdoc />
    public async Task UpdateStatus(UserStatus? status)
    {
        var player = GetPlayer();
        await player.ChangePlayerStatusAsync(status ?? UserStatus.Offline);

        // TODO: Toggle user visibility.
    }

    /// <inheritdoc />
    public async Task BeginWatchingUserPresence()
    {
        foreach (var player in PlayerManager.GetAllPlayers<LazerPlayer>())
        {
            if (ShouldBroadcastPresenceToOtherUsers(player.State))
            {
                await Clients.Caller.UserPresenceUpdated(player.PlayerId, player.State.ToLazerUserPresence());
            }
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, OnlinePresenceWatchersGroup);
    }

    /// <inheritdoc />
    public Task EndWatchingUserPresence() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, OnlinePresenceWatchersGroup);

    /// <inheritdoc />
    public Task<MultiplayerPlaylistItemStats[]> BeginWatchingMultiplayerRoom(long id)
    {
        throw new NotSupportedException(
            "Multiplayer room metadata streaming is not implemented by the realtime server.");
    }

    /// <inheritdoc />
    public Task EndWatchingMultiplayerRoom(long id)
    {
        throw new NotSupportedException(
            "Multiplayer room metadata streaming is not implemented by the realtime server.");
    }

    /// <inheritdoc />
    public Task RefreshFriends()
    {
        var player = GetPlayer();
        return RefreshFriendsAsync(player);
    }

    internal static bool ShouldBroadcastPresenceToOtherUsers(PlayerState state)
    {
        if (state.UserStatus == null)
        {
            return false;
        }

        switch (state.UserStatus.Value)
        {
            case UserStatus.Offline:
                return false;

            case UserStatus.DoNotDisturb:
            case UserStatus.Online:
                return true;

            default:
                throw new ArgumentOutOfRangeException(
                    paramName: nameof(state),
                    message: $"Unexpected user status {state.UserStatus}");
        }
    }

    internal static string FriendPresenceWatchersGroup(int userId) => $"metadata:online-presence-watchers:{userId}";

    private static Task BroadcastUserPresenceUpdate(
        IHubContext<MetadataHub, IMetadataClient> hubContext,
        string connectionId,
        IPlayer player)
    {
        var userId = player.PlayerId;
        var userPresence = player.State.ToLazerUserPresence();

        return Task.WhenAll(
            ShouldBroadcastPresenceToOtherUsers(player.State)
                ? Task.WhenAll(
                    hubContext.Clients.Group(OnlinePresenceWatchersGroup).UserPresenceUpdated(userId, userPresence),
                    hubContext.Clients.Group(FriendPresenceWatchersGroup(userId))
                        .FriendPresenceUpdated(userId, userPresence))
                : Task.CompletedTask,
            hubContext.Clients.Client(connectionId).UserPresenceUpdated(userId, userPresence));
    }

    private async Task RefreshFriendsAsync(LazerPlayer player)
    {
        // Remove the caller from any friend tracking groups.
        foreach (int friendId in player.FriendIds)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, FriendPresenceWatchersGroup(friendId));
        }

        int[] newFriendIds = await relationshipRepository.GetAllFriendIds(player.PlayerId);

        // Add the caller to the friend tracking groups.
        foreach (int friendId in newFriendIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, FriendPresenceWatchersGroup(friendId));
        }

        // Broadcast presence from any online friends to the caller.
        foreach (int friendId in newFriendIds.Except(player.FriendIds))
        {
            var friendPlayer = PlayerManager.GetPlayer<LazerPlayer>(friendId);
            if (friendPlayer != null && ShouldBroadcastPresenceToOtherUsers(friendPlayer.State))
            {
                await Clients.Caller.FriendPresenceUpdated(friendId, friendPlayer.State.ToLazerUserPresence());
            }
        }

        player.FriendIds = newFriendIds;
    }
}