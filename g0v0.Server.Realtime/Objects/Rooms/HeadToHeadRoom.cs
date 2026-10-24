// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Online.Multiplayer;
using Room = g0v0.Server.Common.Database.Models.Room;

namespace g0v0.Server.Realtime.Objects.Rooms;

/// <summary>
/// A head-to-head multiplayer room where every player competes individually.
/// </summary>
/// <remarks>
/// Head-to-head rooms have no match-type specific state: joining or switching to
/// this room type clears any user match state so the client can react to the
/// change and drop team-specific UI.
/// </remarks>
public class HeadToHeadRoom : StandardRoom
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HeadToHeadRoom"/> class.
    /// </summary>
    /// <param name="room">
    /// The database room.
    /// </param>
    /// <param name="facade">
    /// The room dependency facade.
    /// </param>
    public HeadToHeadRoom(Room room, IRoomFacade facade)
        : base(room, facade)
    {
    }

    /// <inheritdoc />
    internal override async Task InitializeMatchState()
    {
        MatchState = StandardMatchRoomState.Create(Settings.MaxParticipants);

        foreach (MultiplayerRoomUser user in Users)
        {
            user.MatchState = null;
            await HandleUserJoined(user);
        }

        await Facade.EventDispatcher.PostMatchRoomStateChangedAsync(RoomId, MatchState);
    }

    /// <inheritdoc />
    protected override async Task HandleUserJoined(MultiplayerRoomUser user)
    {
        if (user.MatchState != null)
        {
            // Head-to-head has no match state; clearing it lets clients see the
            // change and drop any team-specific UI.
            user.MatchState = null;
            await Facade.EventDispatcher.PostMatchUserStateChangedAsync(RoomId, user.UserID, newMatchUserState: null);
        }

        // Assign the user a slot.
        await base.HandleUserJoined(user);
    }
}