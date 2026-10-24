// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using Room = g0v0.Server.Common.Database.Models.Room;

namespace g0v0.Server.Realtime.Objects.Rooms;

/// <summary>
/// A team-versus multiplayer room where players are split into balanced teams.
/// </summary>
/// <remarks>
/// New players are placed in the least-populated team (preferring teams with no
/// players at all). Team changes are only allowed while the room is open and the
/// teams are not locked; teams lock once the match starts.
/// </remarks>
public class TeamVersusRoom : StandardRoom
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TeamVersusRoom"/> class.
    /// </summary>
    /// <param name="room">
    /// The database room.
    /// </param>
    /// <param name="facade">
    /// The room dependency facade.
    /// </param>
    public TeamVersusRoom(Room room, IRoomFacade facade)
        : base(room, facade)
    {
    }

    /// <inheritdoc />
    internal override async Task InitializeMatchState()
    {
        MatchState = TeamVersusRoomState.CreateDefault(Settings.MaxParticipants);

        // Assign teams through HandleUserJoined (which calls GetBestAvailableTeam)
        // rather than assigning here: doing both would re-roll the team of already
        // assigned users and can put everyone into the same team.
        foreach (MultiplayerRoomUser user in Users)
        {
            await HandleUserJoined(user);
        }

        await Facade.EventDispatcher.PostMatchRoomStateChangedAsync(RoomId, MatchState);
    }

    /// <inheritdoc />
    protected override async Task HandleUserJoined(MultiplayerRoomUser user)
    {
        if (user.Role != MultiplayerRoomUserRole.Referee)
        {
            user.MatchState = new TeamVersusUserState { TeamID = GetBestAvailableTeam() };
            await Facade.EventDispatcher.PostMatchUserStateChangedAsync(RoomId, user.UserID, user.MatchState);
        }

        // Assign the user a slot within their team.
        await base.HandleUserJoined(user);
    }

    /// <inheritdoc />
    protected override async Task HandleMatchUserRequest(MultiplayerRoomUser user, MatchUserRequest request)
    {
        switch (request)
        {
            case ChangeTeamRequest changeTeam:
                if (MatchState is not TeamVersusRoomState teamState)
                {
                    throw new InvalidStateException("Room is not a team-versus room.");
                }

                // A manually-locked room rejects team changes.
                if (teamState.Locked)
                {
                    throw new InvalidStateException("Teams are currently locked.");
                }

                if (teamState.Teams.TrueForAll(t => t.ID != changeTeam.TeamID))
                {
                    throw new InvalidStateException("Attempted to set team out of valid range.");
                }

                if (user.Role == MultiplayerRoomUserRole.Referee)
                {
                    throw new InvalidStateException("Referees cannot join teams.");
                }

                if (user.MatchState is TeamVersusUserState userState)
                {
                    userState.TeamID = changeTeam.TeamID;
                }

                await Facade.EventDispatcher.PostMatchUserStateChangedAsync(RoomId, user.UserID, user.MatchState);
                break;

            default:
                await base.HandleMatchUserRequest(user, request);
                break;
        }
    }

    /// <inheritdoc />
    protected override int GetNextBestSlot(MultiplayerRoomUser user, int?[] slots)
    {
        if (user.MatchState is not TeamVersusUserState userState
            || MatchState is not TeamVersusRoomState teamState)
        {
            return base.GetNextBestSlot(user, slots);
        }

        int teamSize = slots.Length / teamState.Teams.Count;
        int teamStartIndex = userState.TeamID * teamSize;

        int nextEmptySlotInTeam = Array.FindIndex(slots, teamStartIndex, teamSize, item => item == null);

        return nextEmptySlotInTeam >= 0 ? nextEmptySlotInTeam : base.GetNextBestSlot(user, slots);
    }

    /// <summary>
    /// Computes the best available team for a newly-joined user, keeping the room balanced.
    /// </summary>
    /// <returns>
    /// The ID of the best available team.
    /// </returns>
    private int GetBestAvailableTeam()
    {
        if (MatchState is not TeamVersusRoomState teamState)
        {
            return 0;
        }

        // Initially check for any teams which don't yet have players.
        foreach (MultiplayerTeam team in teamState.Teams.Where(team => Users.All(u => (u.MatchState as TeamVersusUserState)?.TeamID != team.ID)))
        {
            return team.ID;
        }

        IOrderedEnumerable<IGrouping<int?, MultiplayerRoomUser>> countsByTeams = Users
            .GroupBy(u => (u.MatchState as TeamVersusUserState)?.TeamID)
            .Where(g => g.Key.HasValue)
            .OrderBy(g => g.Count());

        return countsByTeams.First().Key ?? 0;
    }
}