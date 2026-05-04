// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Objects.States.Activity;
using osu.Game.Users;

namespace g0v0.Server.Realtime.Extensions;

/// <summary>
/// Converts lazer activity payloads into realtime activity models.
/// </summary>
public static class LazerUserActivityExtension
{
    /// <summary>
    /// Converts a lazer activity payload into the corresponding realtime activity.
    /// </summary>
    /// <param name="activity">The lazer activity payload.</param>
    /// <returns>The equivalent realtime activity.</returns>
    /// <exception cref="NotSupportedException">Thrown when the activity type is unknown.</exception>
    public static IUserActivity ToRealtimeActivity(this UserActivity? activity)
    {
        return activity switch
        {
            null => new IdleActivity(),
            UserActivity.ChoosingBeatmap => new ChoosingBeatmapActivity(),
            UserActivity.InSoloGame solo => new SoloActivity()
            {
                BeatmapId = solo.BeatmapID,
                BeatmapDisplayTitle = solo.BeatmapDisplayTitle,
                RulesetId = solo.RulesetID,
                RulesetPlayingVerb = solo.RulesetPlayingVerb,
            },
            UserActivity.SpectatingUser spectator => new SpectatingUserActivity()
            {
                ScoreID = spectator.ScoreID,
                PlayerName = spectator.PlayerName,
                BeatmapID = spectator.BeatmapID,
                BeatmapDisplayTitle = spectator.BeatmapDisplayTitle ?? string.Empty,
            },
            UserActivity.WatchingReplay replay => new WatchingReplayActivity()
            {
                ScoreID = replay.ScoreID,
                PlayerName = replay.PlayerName,
                BeatmapID = replay.BeatmapID,
                BeatmapDisplayTitle = replay.BeatmapDisplayTitle ?? string.Empty,
            },
            UserActivity.SearchingForLobby => new SearchingForLobbyActivity(),
            UserActivity.InLobby lobby => new InLobbyActivity() { RoomID = lobby.RoomID, RoomName = lobby.RoomName, },
            UserActivity.InMultiplayerGame mpGame => new InMultiplayerGameActivity()
            {
                BeatmapId = mpGame.BeatmapID,
                BeatmapDisplayTitle = mpGame.BeatmapDisplayTitle,
                RulesetId = mpGame.RulesetID,
                RulesetPlayingVerb = mpGame.RulesetPlayingVerb,
            },
            UserActivity.SpectatingMultiplayerGame specMp => new SpectatingMultiplayerGameActivity()
            {
                BeatmapId = specMp.BeatmapID,
                BeatmapDisplayTitle = specMp.BeatmapDisplayTitle,
                RulesetId = specMp.RulesetID,
                RulesetPlayingVerb = specMp.RulesetPlayingVerb,
            },
            UserActivity.InPlaylistGame playlist => new InPlaylistGameActivity()
            {
                BeatmapId = playlist.BeatmapID,
                BeatmapDisplayTitle = playlist.BeatmapDisplayTitle,
                RulesetId = playlist.RulesetID,
                RulesetPlayingVerb = playlist.RulesetPlayingVerb,
            },
            UserActivity.ModdingBeatmap modding => new ModdingBeatmapActivity()
            {
                BeatmapId = modding.BeatmapID,
                BeatmapDisplayTitle = modding.BeatmapDisplayTitle,
            },
            UserActivity.TestingBeatmap testing => new TestingBeatmapActivity()
            {
                BeatmapId = testing.BeatmapID,
                BeatmapDisplayTitle = testing.BeatmapDisplayTitle,
            },
            UserActivity.EditingBeatmap editing => new EditingBeatmapActivity()
            {
                BeatmapId = editing.BeatmapID,
                BeatmapDisplayTitle = editing.BeatmapDisplayTitle,
            },
            UserActivity.InDailyChallengeLobby => new InDailyChallengeLobbyActivity(),
            UserActivity.PlayingDailyChallenge dailyChallenge => new PlayingDailyChallengeActivity()
            {
                BeatmapId = dailyChallenge.BeatmapID,
                BeatmapDisplayTitle = dailyChallenge.BeatmapDisplayTitle,
                RulesetId = dailyChallenge.RulesetID,
                RulesetPlayingVerb = dailyChallenge.RulesetPlayingVerb,
            },
            _ => throw new NotSupportedException($"Unknown activity type: {activity.GetType()}")
        };
    }
}