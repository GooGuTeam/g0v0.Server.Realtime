// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Realtime.Objects.States.Activity;

/// <summary>
/// Enumerates the normalized activity types used by the realtime server.
/// </summary>
public enum UserActivityType
{
    /// <summary>
    /// The user is idle.
    /// </summary>
    Idle,

    /// <summary>
    /// The user is choosing a beatmap.
    /// </summary>
    ChoosingBeatmap,

    /// <summary>
    /// The user is playing a solo game.
    /// </summary>
    Solo,

    /// <summary>
    /// The user is watching a replay.
    /// </summary>
    WatchingReplay,

    /// <summary>
    /// The user is spectating another user.
    /// </summary>
    SpectatingUser,

    /// <summary>
    /// The user is searching for a lobby.
    /// </summary>
    SearchingForLobby,

    /// <summary>
    /// The user is inside a multiplayer lobby.
    /// </summary>
    InLobby,

    /// <summary>
    /// The user is playing a multiplayer game.
    /// </summary>
    InMultiplayerGame,

    /// <summary>
    /// The user is spectating a multiplayer game.
    /// </summary>
    SpectatingMultiplayerGame,

    /// <summary>
    /// The user is playing a playlist game.
    /// </summary>
    InPlaylistGame,

    /// <summary>
    /// The user is editing a beatmap.
    /// </summary>
    EditingBeatmap,

    /// <summary>
    /// The user is modding a beatmap.
    /// </summary>
    ModdingBeatmap,

    /// <summary>
    /// The user is testing a beatmap.
    /// </summary>
    TestingBeatmap,

    /// <summary>
    /// The user is inside the daily challenge lobby. Used for osu!lazer.
    /// </summary>
    InDailyChallengeLobby,

    /// <summary>
    /// The user is playing the daily challenge. Used for osu!lazer.
    /// </summary>
    PlayingDailyChallenge,

    /// <summary>
    /// The user is away from keyboard. Used for osu!stable.
    /// </summary>
    Afk,
}
