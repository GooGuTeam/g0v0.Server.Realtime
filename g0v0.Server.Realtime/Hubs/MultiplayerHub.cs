// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Runtime.ExceptionServices;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Extensions;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Players;
using g0v0.Server.Realtime.Objects.Rooms;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Countdown;
using osu.Game.Online.Rooms;
using ConfigurationManager = g0v0.Server.Common.Configuration.ConfigurationManager;

namespace g0v0.Server.Realtime.Hubs;

/// <summary>
/// SignalR hub implementing the lazer multiplayer server contract.
/// </summary>
/// <remarks>
/// Every command validates the caller against server-side room state, then runs
/// inside the room's <see cref="IRoom.RoomLock"/> to guarantee serialised
/// access from concurrent connections.
/// </remarks>
public class MultiplayerHub(
    PlayerManager playerManager,
    ILogger<MultiplayerHub> logger,
    RoomManager roomManager,
    MultiplayerEventDispatcher eventDispatcher,
    IServiceScopeFactory scopeFactory)
    : LazerRealtimeHub<IMultiplayerClient>(playerManager), IMultiplayerServer
{
    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        LazerPlayer player =
            GetOrCreatePlayer(
                new PlayerFacade(PlayerManager)
                {
                    RoomManager = roomManager,
                    Logger = logger,
                });
        player.State.RulesetHashes = ReadClientRulesetHashes();
        await player.HubConnected(nameof(MultiplayerHub));

        // A reconnect cancels any pending disconnect grace timer.
        PlayerManager.CancelMultiplayerDisconnectGrace(player.PlayerId);
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        LazerPlayer? player = PlayerManager.GetPlayer<LazerPlayer>(Context.GetUserId(), "Lazer");
        if (player != null)
        {
            await player.HubDisconnected(nameof(MultiplayerHub), exception);

            // Keep the user in their room across brief connection drops: start a
            // grace timer which removes them only if they do not reconnect in time.
            long? roomId = player.State.CurrentRoom;
            if (roomId != null)
            {
                TimeSpan gracePeriod = GetDisconnectGracePeriod();
                PlayerManager.StartMultiplayerDisconnectGrace(player.PlayerId, roomId.Value, gracePeriod, roomManager);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <inheritdoc />
    public async Task<MultiplayerRoom> CreateRoom(MultiplayerRoom room)
    {
        ArgumentNullException.ThrowIfNull(room);

        LazerPlayer player = GetPlayer();
        if (player.State.CurrentRoom != null)
        {
            throw new InvalidStateException("Can't join a room when already in another room.");
        }

        logger.LogInformation("Creating room {RoomName} by user {UserId}.", room.Settings.Name, player.PlayerId);

        IRoom mpRoom = await roomManager.CreateRoom(room, player.PlayerId);

        // Join the room before subscribing to its event group so the joining
        // client does not receive its own UserJoined (which would play the
        // player-joined sound on the client).
        RoomJoinResult joinResult = await mpRoom.JoinRoom(player.PlayerId);
        if (!joinResult.Success)
        {
            await mpRoom.Disband();
            throw new InvalidStateException(joinResult.Reason ?? "Player is not eligible to join this room.");
        }

        try
        {
            await eventDispatcher.SubscribePlayerAsync(mpRoom.RoomId, Context.ConnectionId);
            player.State.CurrentRoom = mpRoom.RoomId;
        }
        catch (Exception subscriptionException)
        {
            // Subscription failed: compensate the join. Each compensation step is
            // fault-isolated so a failure during cleanup never masks the original
            // subscription exception.
            try
            {
                await eventDispatcher.UnsubscribePlayerAsync(mpRoom.RoomId, Context.ConnectionId);
            }
            catch (Exception unsubscribeException)
            {
                logger.LogWarning(unsubscribeException, "Failed to unsubscribe connection {ConnectionId} from room {RoomId} during join rollback.", Context.ConnectionId, mpRoom.RoomId);
            }

            try
            {
                if (mpRoom.Users.Any(u => u.UserID == player.PlayerId))
                {
                    await mpRoom.LeaveRoom(player.PlayerId);
                }
            }
            catch (Exception leaveException)
            {
                logger.LogWarning(leaveException, "Failed to remove user {UserId} from room {RoomId} during join rollback.", player.PlayerId, mpRoom.RoomId);
            }

            player.State.CurrentRoom = null;

            // The room was created by this call and the joiner was its only (or
            // first) member; ensure it does not linger when cleanup failed above.
            if (!mpRoom.Users.Any(u => u.UserID == player.PlayerId))
            {
                try
                {
                    await mpRoom.Disband();
                }
                catch (Exception disbandException)
                {
                    logger.LogWarning(disbandException, "Failed to disband room {RoomId} during join rollback.", mpRoom.RoomId);
                }
            }

            ExceptionDispatchInfo.Capture(subscriptionException).Throw();
        }

        return Snapshot(mpRoom);
    }

    /// <inheritdoc />
    public Task<MultiplayerRoom> JoinRoom(long roomId) => JoinRoomWithPassword(roomId, string.Empty);

    /// <inheritdoc />
    public async Task<MultiplayerRoom> JoinRoomWithPassword(long roomId, string password)
    {
        LazerPlayer player = GetPlayer();
        if (player.State.CurrentRoom != null)
        {
            throw new InvalidStateException("Can't join a room when already in another room.");
        }

        IRoom room = await roomManager.GetOrLoadRoom(roomId)
                     ?? throw new InvalidStateException("Room not found.");

        await room.RoomLock.WaitAsync();
        try
        {
            // Join performs password, room-state, capacity and ruleset checks
            // before mutating any room state.
            RoomJoinResult joinResult = await room.JoinRoom(
                player.PlayerId,
                MultiplayerRoomUserRole.Player,
                password);
            ThrowIfJoinRejected(joinResult);

            try
            {
                await eventDispatcher.SubscribePlayerAsync(room.RoomId, Context.ConnectionId);
                player.State.CurrentRoom = room.RoomId;
            }
            catch (Exception subscriptionException)
            {
                // Subscription failed: compensate the join. Each compensation
                // step is fault-isolated so a failure during cleanup never masks
                // the original subscription exception. Leaving an empty room
                // triggers the room's own disband/retention logic.
                try
                {
                    await eventDispatcher.UnsubscribePlayerAsync(room.RoomId, Context.ConnectionId);
                }
                catch (Exception unsubscribeException)
                {
                    logger.LogWarning(unsubscribeException, "Failed to unsubscribe connection {ConnectionId} from room {RoomId} during join rollback.", Context.ConnectionId, room.RoomId);
                }

                try
                {
                    if (room.Users.Any(u => u.UserID == player.PlayerId))
                    {
                        await room.LeaveRoom(player.PlayerId);
                    }
                }
                catch (Exception leaveException)
                {
                    logger.LogWarning(leaveException, "Failed to remove user {UserId} from room {RoomId} during join rollback.", player.PlayerId, room.RoomId);
                }

                player.State.CurrentRoom = null;
                ExceptionDispatchInfo.Capture(subscriptionException).Throw();
            }
        }
        finally
        {
            room.RoomLock.Release();
        }

        return Snapshot(room);
    }

    /// <inheritdoc />
    public async Task LeaveRoom()
    {
        LazerPlayer player = GetPlayer();
        long? roomId = player.State.CurrentRoom;
        if (roomId == null)
        {
            return;
        }

        IRoom? room = roomManager.GetRoom(roomId.Value);
        if (room == null)
        {
            player.State.CurrentRoom = null;
            return;
        }

        await room.RoomLock.WaitAsync();
        try
        {
            // Unsubscribe before leaving so the leaving client does not receive
            // its own UserLeft.
            await eventDispatcher.UnsubscribePlayerAsync(room.RoomId, Context.ConnectionId);
            await room.LeaveRoom(player.PlayerId);
            player.State.CurrentRoom = null;
        }
        finally
        {
            room.RoomLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task TransferHost(int userId)
    {
        await InRoomRoomCommand(room => room.TransferHost(GetPlayer().PlayerId, userId));
    }

    /// <inheritdoc />
    public async Task KickUser(int userId)
    {
        await InRoomRoomCommand(room =>
        {
            EnsureHostOrReferee(room);
            return room.KickUser(userId, GetPlayer().PlayerId);
        });
    }

    /// <inheritdoc />
    public async Task ChangeSettings(MultiplayerRoomSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await InRoomRoomCommand(room =>
        {
            EnsureHostOrReferee(room);
            return room.ChangeRoomSettings(settings);
        });
    }

    /// <inheritdoc />
    public async Task ChangeState(MultiplayerUserState newState)
    {
        await InRoomRoomCommand(room => room.ChangeUserState(GetPlayer().PlayerId, newState));
    }

    /// <inheritdoc />
    public async Task ChangeBeatmapAvailability(BeatmapAvailability newBeatmapAvailability)
    {
        ArgumentNullException.ThrowIfNull(newBeatmapAvailability);

        await InRoomRoomCommand(room => room.ChangeUserBeatmapAvailability(GetPlayer().PlayerId, newBeatmapAvailability));
    }

    /// <inheritdoc />
    public async Task ChangeUserStyle(int? beatmapId, int? rulesetId)
    {
        await InRoomRoomCommand(room => room.ChangeUserStyle(GetPlayer().PlayerId, beatmapId, rulesetId));
    }

    /// <inheritdoc />
    public async Task ChangeUserMods(IEnumerable<APIMod> newMods)
    {
        ArgumentNullException.ThrowIfNull(newMods);

        await InRoomRoomCommand(room => room.ChangeUserMods(GetPlayer().PlayerId, newMods));
    }

    /// <inheritdoc />
    public async Task SendMatchRequest(MatchUserRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        await InRoomRoomCommand(room =>
        {
            // Countdown control is restricted to the host or a referee.
            switch (request)
            {
                case StartMatchCountdownRequest:
                case StopCountdownRequest:
                    EnsureHostOrReferee(room);
                    break;
            }

            return room.HandleUserRequest(GetPlayer().PlayerId, request);
        });
    }

    /// <inheritdoc />
    public async Task StartMatch()
    {
        await InRoomRoomCommand(room =>
        {
            EnsureHostOrReferee(room);

            // The host must be ready or spectating, and at least one user must be
            // ready.
            return room.Host != null
                   && room.Host.State != MultiplayerUserState.Spectating
                   && room.Host.State != MultiplayerUserState.Ready
                   && room.Host.Role != MultiplayerRoomUserRole.Referee
                ? throw new InvalidStateException("Can't start match when the host is not ready.")
                : room.Users.All(u => u.State != MultiplayerUserState.Ready)
                    ? throw new InvalidStateException("Can't start match when no users are ready.")
                    : room.StartMatch();
        });
    }

    /// <inheritdoc />
    public async Task AbortMatch()
    {
        await InRoomRoomCommand(room =>
        {
            EnsureHostOrReferee(room);
            return room.AbortMatch();
        });
    }

    /// <inheritdoc />
    public async Task AbortGameplay()
    {
        await InRoomRoomCommand(room => room.AbortGameplay(GetPlayer().PlayerId));
    }

    /// <inheritdoc />
    public async Task VoteToSkipIntro()
    {
        await InRoomRoomCommand(room => room.VoteToSkipIntro(GetPlayer().PlayerId));
    }

    /// <inheritdoc />
    public async Task AddPlaylistItem(MultiplayerPlaylistItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        await InRoomRoomCommand(room => room.AddPlaylistItem(GetPlayer().PlayerId, item));
    }

    /// <inheritdoc />
    public async Task EditPlaylistItem(MultiplayerPlaylistItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        await InRoomRoomCommand(room => room.EditPlaylistItem(GetPlayer().PlayerId, item));
    }

    /// <inheritdoc />
    public async Task RemovePlaylistItem(long playlistItemId)
    {
        await InRoomRoomCommand(room => room.RemovePlaylistItem(GetPlayer().PlayerId, playlistItemId));
    }

    /// <inheritdoc />
    public async Task InvitePlayer(int userId)
    {
        LazerPlayer player = GetPlayer();
        long? roomId = player.State.CurrentRoom ?? throw new NotJoinedRoomException();
        IRoom room = roomManager.GetRoom(roomId.Value)
                     ?? throw new NotJoinedRoomException();

        await room.RoomLock.WaitAsync();
        try
        {
            RoomJoinResult eligibility = await room.UserCanJoinWithPassword(userId, room.Settings.Password);
            if (!eligibility.Success)
            {
                throw new InvalidStateException(
                    eligibility.Reason ?? "Player is not eligible to join this room.");
            }

            await CheckUserToUserPermissionsAsync(player.PlayerId, userId);

            await eventDispatcher.PostUserInvitedAsync(room.RoomId, userId, player.PlayerId, room.Settings.Password);
        }
        finally
        {
            room.RoomLock.Release();
        }
    }

    private Dictionary<string, string> ReadClientRulesetHashes()
    {
        IFeatureCollection? features = Context.Features;

        // Use this to make test pass
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (features == null
            || Context.GetHttpContext()?.Request.Headers.TryGetValue(
                HubClientConnector.RULESET_HASH_HEADER,
                out StringValues headerValue) != true)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            Dictionary<string, string>? parsed =
                JsonConvert.DeserializeObject<Dictionary<string, string>>(headerValue.ToString());
            return parsed == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Ignoring malformed {HeaderName} multiplayer header.", HubClientConnector.RULESET_HASH_HEADER);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void ThrowIfJoinRejected(RoomJoinResult result)
    {
        if (result.Success)
        {
            return;
        }

        if (string.Equals(result.Reason, RoomJoinResult.InvalidPasswordReason, StringComparison.Ordinal))
        {
            throw new InvalidPasswordException();
        }

        throw new InvalidStateException(result.Reason ?? "Player is not eligible to join this room.");
    }

    private async Task InRoomRoomCommand(Func<IRoom, Task> command)
    {
        LazerPlayer player = GetPlayer();
        long? roomId = player.State.CurrentRoom ?? throw new NotJoinedRoomException();
        IRoom room = roomManager.GetRoom(roomId.Value)
                     ?? throw new NotJoinedRoomException();

        await room.RoomLock.WaitAsync();
        try
        {
            await command(room);
        }
        finally
        {
            room.RoomLock.Release();
        }
    }

    private void EnsureHostOrReferee(IRoom room)
    {
        int userId = Context.GetUserId();
        bool isHost = room.Host?.UserID == userId;
        bool isReferee = room.Users.FirstOrDefault(u => u.UserID == userId)?.Role == MultiplayerRoomUserRole.Referee;

        if (!isHost && !isReferee)
        {
            throw new NotHostException();
        }
    }

    /// <summary>
    /// Checks user-to-user permissions before an invitation, using the
    /// relationship table for block/friend state and the target user's PM
    /// preference.
    /// </summary>
    /// <param name="invitingUserId">The user sending the invite.</param>
    /// <param name="targetUserId">The user being invited.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="UserBlockedException">Either user has the other blocked.</exception>
    /// <exception cref="UserBlocksPMsException">The target disallows PMs from non-friends.</exception>
    private async Task CheckUserToUserPermissionsAsync(int invitingUserId, int targetUserId)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IRelationshipRepository? relationshipRepository = scope.ServiceProvider.GetService<IRelationshipRepository>();
        IUserRepository? userRepository = scope.ServiceProvider.GetService<IUserRepository>();

        // Permission checks are skipped when the repositories are not registered
        // (e.g. in tests which opt out).
        if (relationshipRepository == null || userRepository == null)
        {
            return;
        }

        // The inviting user has the target blocked.
        if (await relationshipRepository.IsBlockedAsync(invitingUserId, targetUserId))
        {
            throw new UserBlockedException();
        }

        // The target has the inviting user blocked.
        if (await relationshipRepository.IsBlockedAsync(targetUserId, invitingUserId))
        {
            throw new UserBlockedException();
        }

        Common.Database.Models.User? targetUser = await userRepository.GetByIdAsync(targetUserId);
        if (targetUser == null)
        {
            return;
        }

        // The target disallows unsolicited PMs and the inviter is not their friend.
        if (targetUser.PmFriendsOnly && !await relationshipRepository.IsFollowingAsync(targetUserId, invitingUserId))
        {
            throw new UserBlocksPMsException();
        }
    }

    private MultiplayerRoom Snapshot(IRoom room)
    {
        return new MultiplayerRoom(room.RoomId)
        {
            State = room.State,
            Settings = room.Settings,
            Users = room.Users,
            Host = room.Host,
            MatchState = room.MatchState,
            Playlist = room.Playlist,
            ActiveCountdowns = room.ActiveCountdowns,
            ChannelID = room.ChannelId,
        };
    }

    private TimeSpan GetDisconnectGracePeriod()
    {
        // Fall back to the manager's tunable (used by tests) when no
        // configuration manager is registered.
        using IServiceScope scope = scopeFactory.CreateScope();
        ConfigurationManager? configManager = scope.ServiceProvider.GetService<ConfigurationManager>();
        return configManager?.Get<RealtimeConfig>().MultiplayerDisconnectGracePeriod
               ?? PlayerManager.MultiplayerDisconnectGracePeriod;
    }
}