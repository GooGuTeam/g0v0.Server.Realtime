// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

// StandardRoom groups members by domain responsibility (queue, slots, requests).
#pragma warning disable SA1201 // Elements should appear in the correct order
#pragma warning disable SA1202 // Elements should be ordered by access

using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using Room = g0v0.Server.Common.Database.Models.Room;

namespace g0v0.Server.Realtime.Objects.Rooms;

/// <summary>
/// Base class for standard realtime room types (head-to-head and team versus).
/// </summary>
/// <remarks>
/// The currently-selected playlist item is the first non-expired item in
/// playlist order, falling back to the last-played item when all items have
/// been played, matching the client's <c>GetCurrentItem</c> semantics.
///
/// Standard rooms maintain a slot array in <see cref="StandardMatchRoomState.Slots"/>
/// linked to <see cref="MultiplayerRoomSettings.MaxParticipants"/>: users are
/// assigned a slot on join, released on leave, may request a slot change, and
/// the host may lock/unlock slots and teams.
/// </remarks>
public abstract class StandardRoom : RoomBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StandardRoom"/> class.
    /// </summary>
    /// <param name="room">The database room.</param>
    /// <param name="facade">The room dependency facade.</param>
    protected StandardRoom(Room room, IRoomFacade facade)
        : base(room, facade)
    {
        // Keep Settings.PlaylistItemId pointing at a valid playlist item so the
        // client's MultiplayerRoom.CurrentPlaylistItem (which uses Single()) never
        // fails when the room snapshot is first received. Inlined to avoid calling
        // the overridable CurrentItem from the constructor.
        if (Playlist.Count > 0)
        {
            Settings.PlaylistItemId = Playlist.All(item => item.Expired)
                ? Playlist.OrderBy(item => item.PlayedAt).Last().ID
                : Playlist.Where(item => !item.Expired).OrderBy(item => item.PlaylistOrder).First().ID;
        }
    }

    /// <inheritdoc />
    public override MultiplayerPlaylistItem CurrentItem
    {
        get
        {
            if (Playlist.Count == 0)
            {
                throw new InvalidOperationException("Room has no playlist items.");
            }

            return Playlist.All(item => item.Expired)
                ? Playlist.OrderBy(item => item.PlayedAt).Last()
                : Playlist.Where(item => !item.Expired).OrderBy(item => item.PlaylistOrder).First();
        }
    }

    /// <inheritdoc />
    public override Task<bool> UserCanJoin(int userId)
        => Task.FromResult(Settings.MaxParticipants == null || Users.Count < Settings.MaxParticipants);

    /// <inheritdoc />
    protected override Task HandleUserJoined(MultiplayerRoomUser user)
        => AssignUserSlot(user);

    /// <inheritdoc />
    protected override Task HandleUserLeft(MultiplayerRoomUser user)
        => ClearUserSlot(user);

    /// <inheritdoc />
    protected override async Task HandleMatchStateSettingsChanged(MultiplayerRoomSettings previousSettings)
    {
        bool queueModeChanged = previousSettings.QueueMode != Settings.QueueMode;

        // When changing to host-only mode, ensure that at least one non-expired
        // playlist item exists by duplicating the current item.
        if (queueModeChanged
            && Settings.QueueMode == QueueMode.HostOnly
            && Playlist.All(item => item.Expired))
        {
            await AddPlaylistItemInternal(CurrentItem.Clone(), CurrentItem.OwnerID);
        }

        // Reorder the queue immediately when the queue mode changes (aligned
        // with StandardMatchController.updateQueueFromModeChange): in round-robin
        // mode this interleaves per-owner groups, and the current item is
        // repointed to the first non-expired item under the new ordering.
        if (queueModeChanged && State == MultiplayerRoomState.Open)
        {
            await UpdatePlaylistOrder();
            await UpdateCurrentItem();
        }

        if (UpdateSlotsFromSettings())
        {
            await Facade.EventDispatcher.PostMatchRoomStateChangedAsync(RoomId, MatchState);
        }
    }

    /// <inheritdoc />
    protected override async Task HandleMatchUserRequest(MultiplayerRoomUser user, MatchUserRequest request)
    {
        switch (request)
        {
            case RollRequest rollRequest:
                if (rollRequest.Max is < 2 or > 100)
                {
                    throw new InvalidStateException("Invalid roll request. Max must be in [2, 100] range inclusive.");
                }

                uint max = rollRequest.Max ?? 100;
                uint result = (uint)Random.Shared.Next(1, 1 + (int)max);
                var resultEvent = new RollEvent { UserID = user.UserID, Max = max, Result = result };
                await Facade.EventDispatcher.PostMatchEventAsync(RoomId, resultEvent);
                break;

            case SetLockStateRequest setRoomLock:
                if (GetStandardState().Locked == setRoomLock.Locked)
                {
                    break;
                }

                GetStandardState().Locked = setRoomLock.Locked;
                await Facade.EventDispatcher.PostMatchRoomStateChangedAsync(RoomId, MatchState);
                break;

            case ChangeSlotRequest changeSlotRequest:
                if (GetStandardState().Locked)
                {
                    throw new InvalidStateException("Slots are currently locked.");
                }

                await ChangeUserSlot(user, changeSlotRequest.SlotID);
                break;

            default:
                await base.HandleMatchUserRequest(user, request);
                break;
        }
    }

    /// <summary>
    /// Gets the room's match state as a <see cref="StandardMatchRoomState"/>.
    /// </summary>
    /// <returns>The standard match room state.</returns>
    protected StandardMatchRoomState GetStandardState()
        => MatchState as StandardMatchRoomState
           ?? throw new InvalidStateException("Room does not have a standard match state.");

    /// <summary>
    /// Updates the slot array from the current <see cref="MultiplayerRoomSettings.MaxParticipants"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the slot array changed.</returns>
    protected bool UpdateSlotsFromSettings()
    {
        StandardMatchRoomState state = GetStandardState();

        // Participant limit has been unset.
        if (Settings.MaxParticipants == null)
        {
            // No slots in state => nothing to do.
            if (state.Slots == null)
            {
                return false;
            }

            // Slots in state => discard slots.
            state.Slots = null;
            return true;
        }

        // By this point it is guaranteed that a participant limit has been set.
        if (Settings.MaxParticipants < Users.Count)
        {
            throw new InvalidStateException("There are more players currently in the room than your new requested max participant limit. Please kick some players first.");
        }

        // No slots in state => initialise slots.
        if (state.Slots == null)
        {
            state.Slots = new int?[Settings.MaxParticipants.Value];

            for (int j = 0; j < Users.Count; j++)
            {
                var user = Users[j];
                int slot = GetNextBestSlot(user, state.Slots);
                state.Slots[slot] = user.UserID;
            }

            return true;
        }

        // No change in slot count => nothing to do.
        if (Settings.MaxParticipants.Value == state.Slots.Length)
        {
            return false;
        }

        int?[] oldSlots = state.Slots;
        state.Slots = new int?[Settings.MaxParticipants.Value];

        // Slot count increased => pad with empty slots.
        if (Settings.MaxParticipants > oldSlots.Length)
        {
            Array.Copy(oldSlots, state.Slots, oldSlots.Length);
            return true;
        }

        // Slot count decreased => move users around from removed slots into previously-empty slots.
        int i;

        for (i = 0; i < state.Slots.Length; ++i)
        {
            if (oldSlots[i] != null)
            {
                state.Slots[i] = oldSlots[i];
            }
        }

        for (i = state.Slots.Length; i < oldSlots.Length; ++i)
        {
            int? userId = oldSlots[i];

            if (userId == null)
            {
                continue;
            }

            var user = Users.SingleOrDefault(u => u.UserID == userId.Value);
            if (user == null)
            {
                continue;
            }

            int newSlot = GetNextBestSlot(user, state.Slots);
            state.Slots[newSlot] = user.UserID;
        }

        return true;
    }

    /// <summary>
    /// Changes the slot of the given user.
    /// </summary>
    /// <param name="user">The user whose slot is changing.</param>
    /// <param name="newSlotId">The new slot ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidStateException">The room has no slots, the slot is invalid or taken.</exception>
    public async Task ChangeUserSlot(MultiplayerRoomUser user, byte newSlotId)
    {
        StandardMatchRoomState state = GetStandardState();

        if (state.Slots == null)
        {
            throw new InvalidStateException("The room does not have slots.");
        }

        if (newSlotId >= state.Slots.Length)
        {
            throw new InvalidStateException("Invalid slot.");
        }

        int oldSlotId = Array.IndexOf(state.Slots, user.UserID);

        if (oldSlotId < 0)
        {
            throw new InvalidStateException("User is not in the room.");
        }

        if (state.Slots[newSlotId] != null)
        {
            throw new InvalidStateException("The requested slot is already taken.");
        }

        (state.Slots[oldSlotId], state.Slots[newSlotId]) = (state.Slots[newSlotId], state.Slots[oldSlotId]);
        await Facade.EventDispatcher.PostMatchRoomStateChangedAsync(RoomId, MatchState);
    }

    /// <summary>
    /// Assigns a slot to the given user if they do not already have one.
    /// </summary>
    /// <param name="user">The user to assign a slot to.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AssignUserSlot(MultiplayerRoomUser user)
    {
        StandardMatchRoomState state = GetStandardState();

        // Assign a slot to the user if they already don't have one.
        // Them having one can be the case particularly on match type changes, as they also call this method on every user in the room.
        if (state.Slots != null && Array.IndexOf(state.Slots, user.UserID) < 0)
        {
            int slot = GetNextBestSlot(user, state.Slots);
            state.Slots[slot] = user.UserID;
            await Facade.EventDispatcher.PostMatchRoomStateChangedAsync(RoomId, MatchState);
        }
    }

    /// <summary>
    /// Clears the slot of the given user.
    /// </summary>
    /// <param name="user">The user whose slot is being cleared.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ClearUserSlot(MultiplayerRoomUser user)
    {
        StandardMatchRoomState state = GetStandardState();

        if (state.Slots == null)
        {
            return;
        }

        int userSlot = Array.IndexOf(state.Slots, user.UserID);
        if (userSlot >= 0)
        {
            state.Slots[userSlot] = null;
        }

        await Facade.EventDispatcher.PostMatchRoomStateChangedAsync(RoomId, MatchState);
    }

    /// <summary>
    /// Computes the next best slot for the given user, keeping the room balanced.
    /// </summary>
    /// <param name="user">The user to assign a slot to.</param>
    /// <param name="slots">The current slot array.</param>
    /// <returns>The next empty slot index.</returns>
    protected virtual int GetNextBestSlot(MultiplayerRoomUser user, int?[] slots)
    {
        int nextEmptySlot = Array.FindIndex(slots, item => item == null);
        if (nextEmptySlot < 0)
        {
            throw new InvalidOperationException("Ran out of slots in the room. This should never happen, as the user should not have been able to join the room to begin with.");
        }

        return nextEmptySlot;
    }
}