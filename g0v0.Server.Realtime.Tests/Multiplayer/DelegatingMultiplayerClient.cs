// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

// This file intentionally hosts a single test helper type.
#pragma warning disable MA0048 // File name must match type name

using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Used in testing. Delegates calls to one or more <see cref="IMultiplayerClient"/>s.
/// Note: All members must be virtual so Moq can intercept them.
/// </summary>
public class DelegatingMultiplayerClient : IMultiplayerClient
{
    public virtual IEnumerable<IMultiplayerClient> Clients => [];

    public virtual Task RoomStateChanged(MultiplayerRoomState state)
        => Task.WhenAll(Clients.Select(c => c.RoomStateChanged(state)));

    public virtual Task UserJoined(MultiplayerRoomUser user)
        => Task.WhenAll(Clients.Select(c => c.UserJoined(user)));

    public virtual Task UserLeft(MultiplayerRoomUser user)
        => Task.WhenAll(Clients.Select(c => c.UserLeft(user)));

    public virtual Task UserKicked(MultiplayerRoomUser user)
        => Task.WhenAll(Clients.Select(c => c.UserKicked(user)));

#pragma warning disable CA1725
    public virtual Task Invited(int invitedBy, long roomId, string password)
#pragma warning restore CA1725
        => Task.WhenAll(Clients.Select(c => c.Invited(invitedBy, roomId, password)));

    public virtual Task HostChanged(int userId)
        => Task.WhenAll(Clients.Select(c => c.HostChanged(userId)));

    public virtual Task SettingsChanged(MultiplayerRoomSettings newSettings)
        => Task.WhenAll(Clients.Select(c => c.SettingsChanged(newSettings)));

    public virtual Task UserStateChanged(int userId, MultiplayerUserState state)
        => Task.WhenAll(Clients.Select(c => c.UserStateChanged(userId, state)));

    public virtual Task MatchUserStateChanged(int userId, MatchUserState state)
        => Task.WhenAll(Clients.Select(c => c.MatchUserStateChanged(userId, state)));

    public virtual Task MatchRoomStateChanged(MatchRoomState state)
        => Task.WhenAll(Clients.Select(c => c.MatchRoomStateChanged(state)));

    public virtual Task MatchEvent(MatchServerEvent e)
        => Task.WhenAll(Clients.Select(c => c.MatchEvent(e)));

    public virtual Task UserBeatmapAvailabilityChanged(int userId, BeatmapAvailability beatmapAvailability)
        => Task.WhenAll(Clients.Select(c => c.UserBeatmapAvailabilityChanged(userId, beatmapAvailability)));

    public virtual Task UserStyleChanged(int userId, int? beatmapId, int? rulesetId)
        => Task.WhenAll(Clients.Select(c => c.UserStyleChanged(userId, beatmapId, rulesetId)));

    public virtual Task UserModsChanged(int userId, IEnumerable<APIMod> mods)
        => Task.WhenAll(Clients.Select(c => c.UserModsChanged(userId, mods)));

    public virtual Task LoadRequested()
        => Task.WhenAll(Clients.Select(c => c.LoadRequested()));

    public virtual Task GameplayStarted()
        => Task.WhenAll(Clients.Select(c => c.GameplayStarted()));

    public virtual Task GameplayAborted(GameplayAbortReason reason)
        => Task.WhenAll(Clients.Select(c => c.GameplayAborted(reason)));

    public virtual Task ResultsReady()
        => Task.WhenAll(Clients.Select(c => c.ResultsReady()));

    public virtual Task PlaylistItemAdded(MultiplayerPlaylistItem item)
        => Task.WhenAll(Clients.Select(c => c.PlaylistItemAdded(item)));

    public virtual Task PlaylistItemRemoved(long playlistItemId)
        => Task.WhenAll(Clients.Select(c => c.PlaylistItemRemoved(playlistItemId)));

    public virtual Task PlaylistItemChanged(MultiplayerPlaylistItem item)
        => Task.WhenAll(Clients.Select(c => c.PlaylistItemChanged(item)));

    public virtual Task UserVotedToSkipIntro(int userId, bool voted)
        => Task.WhenAll(Clients.Select(c => c.UserVotedToSkipIntro(userId, voted)));

    public virtual Task VoteToSkipIntroPassed()
        => Task.WhenAll(Clients.Select(c => c.VoteToSkipIntroPassed()));

    public virtual Task UserRoleChanged(int userId, MultiplayerRoomUserRole newRole)
        => Task.WhenAll(Clients.Select(c => c.UserRoleChanged(userId, newRole)));

    public virtual Task DisconnectRequested()
        => Task.WhenAll(Clients.Select(c => c.DisconnectRequested()));

    public virtual Task ServerShuttingDown()
        => Task.WhenAll(Clients.Select(c => c.ServerShuttingDown()));
}