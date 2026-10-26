// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;

namespace g0v0.Server.Realtime.Objects.Rooms;

/// <summary>
/// Defines a multiplayer room instance managed by the realtime server.
/// </summary>
public interface IRoom
{
    /// <summary>
    /// Gets the room ID.
    /// </summary>
    long RoomId { get; }

    /// <summary>
    /// Gets the current room state.
    /// </summary>
    MultiplayerRoomState State { get; }

    /// <summary>
    /// Gets the current room settings.
    /// </summary>
    MultiplayerRoomSettings Settings { get; }

    /// <summary>
    /// Gets the users currently in the room.
    /// </summary>
    IList<MultiplayerRoomUser> Users { get; }

    /// <summary>
    /// Gets the current room host.
    /// </summary>
    MultiplayerRoomUser? Host { get; }

    /// <summary>
    /// Gets the match-type specific room state.
    /// </summary>
    MatchRoomState? MatchState { get; }

    /// <summary>
    /// Gets the room playlist.
    /// </summary>
    IList<MultiplayerPlaylistItem> Playlist { get; }

    /// <summary>
    /// Gets the active countdowns.
    /// </summary>
    IList<MultiplayerCountdown> ActiveCountdowns { get; }

    /// <summary>
    /// Gets or sets the chat channel ID associated with the room.
    /// </summary>
    int ChannelId { get; set; }

    /// <summary>
    /// Gets a value indicating whether the room is a tournament room.
    /// </summary>
    bool TournamentMode { get; }

    /// <summary>
    /// Gets the date at which an empty tournament room will be destroyed, or
    /// <see langword="null"/> when no destruction is pending.
    /// </summary>
    DateTimeOffset? EndDate { get; }

    /// <summary>
    /// Gets the per-room lock used to serialise all mutating operations.
    /// </summary>
    SemaphoreSlim RoomLock { get; }

    /// <summary>
    /// Gets the set of banned user IDs.
    /// </summary>
    IReadOnlySet<int> BannedUsers { get; }

    /// <summary>
    /// Gets the currently selected playlist item.
    /// </summary>
    MultiplayerPlaylistItem CurrentItem { get; }

    /// <summary>
    /// Gets the user ID of the persisted room host, or <see langword="null"/> when
    /// the room has no host.
    /// </summary>
    /// <remarks>
    /// This mirrors the persisted host rather than <see cref="Host"/>, which is
    /// only populated once a user actually joins the room. It is therefore the
    /// authoritative owner of a room which was created but not joined yet.
    /// </remarks>
    int? HostUserId { get; }

    /// <summary>
    /// Disbands the room: persists an immediate end date, closes it and removes
    /// it from the room manager.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Disband();

    /// <summary>
    /// Determines whether a user may join the room with the given password.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="password">The room password.</param>
    /// <returns>The join eligibility and rejection reason.</returns>
    Task<RoomJoinResult> UserCanJoinWithPassword(int userId, string password);

    /// <summary>
    /// Determines whether a user may join the room.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>The join eligibility and rejection reason.</returns>
    Task<RoomJoinResult> UserCanJoin(int userId);

    /// <summary>
    /// Adds a user to the room.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="role">The role to join with.</param>
    /// <param name="password">The supplied room password, or <see langword="null"/> for trusted internal joins.</param>
    /// <returns>The join result. A failed result leaves the room unchanged.</returns>
    Task<RoomJoinResult> JoinRoom(
        int userId,
        MultiplayerRoomUserRole role = MultiplayerRoomUserRole.Player,
        string? password = null);

    /// <summary>
    /// Removes a user from the room.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task LeaveRoom(int userId);

    /// <summary>
    /// Kicks a user from the room.
    /// </summary>
    /// <param name="userId">The user to kick.</param>
    /// <param name="kickedBy">The user performing the kick.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task KickUser(int userId, int kickedBy);

    /// <summary>
    /// Bans a user from the room.
    /// </summary>
    /// <param name="userId">The user to ban.</param>
    void BanUser(int userId);

    /// <summary>
    /// Transfers the host role to another user.
    /// </summary>
    /// <param name="source">The current host.</param>
    /// <param name="target">The new host.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task TransferHost(int source, int target);

    /// <summary>
    /// Sets the host of the room.
    /// </summary>
    /// <param name="userId">The new host user ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetHost(int userId);

    /// <summary>
    /// Changes the role of a user in the room.
    /// </summary>
    /// <param name="userId">The user whose role is changing.</param>
    /// <param name="role">The new role.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// The updated user is re-broadcast as a join event, which is how clients
    /// learn about role changes.
    /// </remarks>
    Task ChangeUserRole(int userId, MultiplayerRoomUserRole role);

    /// <summary>
    /// Changes the room settings.
    /// </summary>
    /// <param name="newSettings">The new settings.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ChangeRoomSettings(MultiplayerRoomSettings newSettings);

    /// <summary>
    /// Changes a user's state.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="newState">The new state.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ChangeUserState(int userId, MultiplayerUserState newState);

    /// <summary>
    /// Changes a user's beatmap availability.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="newBeatmapAvailability">The new availability.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ChangeUserBeatmapAvailability(int userId, BeatmapAvailability newBeatmapAvailability);

    /// <summary>
    /// Changes a user's selected style.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="beatmapId">The selected beatmap ID.</param>
    /// <param name="rulesetId">The selected ruleset ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ChangeUserStyle(int userId, int? beatmapId, int? rulesetId);

    /// <summary>
    /// Changes a user's selected mods.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="newMods">The new mods.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ChangeUserMods(int userId, IEnumerable<APIMod> newMods);

    /// <summary>
    /// Starts a match-start countdown.
    /// </summary>
    /// <param name="duration">The countdown duration.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StartMatchCountdown(TimeSpan duration);

    /// <summary>
    /// Stops a countdown by ID.
    /// </summary>
    /// <param name="countdownId">The countdown ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StopCountdown(int countdownId);

    /// <summary>
    /// Starts the given countdown.
    /// </summary>
    /// <param name="countdown">The countdown to start.</param>
    /// <param name="onComplete">The callback invoked when the countdown finishes, if any.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StartCountdown(MultiplayerCountdown countdown, Func<Task>? onComplete = null);

    /// <summary>
    /// Stops every active countdown except the server-managed gameplay-load one.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StopAllCountdowns();

    /// <summary>
    /// Starts the match.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StartMatch();

    /// <summary>
    /// Aborts the ongoing match.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AbortMatch();

    /// <summary>
    /// Aborts a user's gameplay.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AbortGameplay(int userId);

    /// <summary>
    /// Votes to skip the beatmap intro.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task VoteToSkipIntro(int userId);

    /// <summary>
    /// Adds a playlist item.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="item">The playlist item to add.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AddPlaylistItem(int userId, MultiplayerPlaylistItem item);

    /// <summary>
    /// Edits a playlist item.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="item">The playlist item to edit.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EditPlaylistItem(int userId, MultiplayerPlaylistItem item);

    /// <summary>
    /// Removes a playlist item.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="playlistItemId">The playlist item ID to remove.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RemovePlaylistItem(int userId, long playlistItemId);

    /// <summary>
    /// Handles a match-type specific user request.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="request">The match user request.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleUserRequest(int userId, MatchUserRequest request);
}