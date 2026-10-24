// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Hubs;
using Microsoft.AspNetCore.SignalR;
using osu.Game.Online.Metadata;
using MatchType = osu.Game.Online.Rooms.MatchType;

namespace g0v0.Server.Realtime.Services;

/// <summary>
/// Provides multiplayer playlist statistics and broadcasts processed scores to room watchers.
/// </summary>
/// <param name="scopeFactory">The factory for scoped repositories.</param>
/// <param name="hubContext">The metadata hub used to notify watchers.</param>
public sealed class MultiplayerRoomMetadataService(
    IServiceScopeFactory scopeFactory,
    IHubContext<MetadataHub, IMetadataClient> hubContext)
{
    private readonly Lock _subscriptionLock = new();
    private readonly Dictionary<long, HashSet<string>> _subscriptions = new();

    internal void Register(string connectionId, long roomId)
    {
        lock (_subscriptionLock)
        {
            if (!_subscriptions.TryGetValue(roomId, out HashSet<string>? connections))
            {
                connections = new HashSet<string>(StringComparer.Ordinal);
                _subscriptions.Add(roomId, connections);
            }

            connections.Add(connectionId);
        }
    }

    internal void Unregister(string connectionId, long roomId)
    {
        lock (_subscriptionLock)
        {
            if (!_subscriptions.TryGetValue(roomId, out HashSet<string>? connections))
            {
                return;
            }

            connections.Remove(connectionId);
            if (connections.Count == 0)
            {
                _subscriptions.Remove(roomId);
            }
        }
    }

    internal void UnregisterConnection(string connectionId)
    {
        lock (_subscriptionLock)
        {
            foreach (long roomId in _subscriptions.Keys.ToArray())
            {
                Unregister(connectionId, roomId);
            }
        }
    }

    internal async Task<MultiplayerPlaylistItemStats[]> GetStatsAsync(long roomId)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IRoomRepository rooms = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
        Room? room = await rooms.GetRoom(roomId);
        if (room == null)
        {
            return [];
        }

        IScoreRepository scores = scope.ServiceProvider.GetRequiredService<IScoreRepository>();
        IReadOnlyList<Score> passingScores = await scores.GetPassingScoresForRoomAsync(roomId);
        ILookup<long?, Score> scoresByItem = passingScores.ToLookup(score => score.PlaylistItemId);

        return room.Playlists.Select(item =>
        {
            MultiplayerPlaylistItemStats stats = new() { PlaylistItemID = item.Id };
            foreach (Score score in scoresByItem[item.Id])
            {
                int bin = (int)Math.Clamp((long)Math.Floor(score.TotalScore / 100000.0), 0, MultiplayerPlaylistItemStats.TOTAL_SCORE_DISTRIBUTION_BINS - 1);
                stats.TotalScoreDistribution[bin]++;
                stats.CumulativeScore += score.TotalScore;
                stats.LastProcessedScoreID = Math.Max(stats.LastProcessedScoreID, (ulong)score.Id);
            }

            return stats;
        }).ToArray();
    }

    internal async Task NotifyScoreProcessedAsync(long scoreId)
    {
        lock (_subscriptionLock)
        {
            if (_subscriptions.Count == 0)
            {
                return;
            }
        }

        using IServiceScope scope = scopeFactory.CreateScope();
        IScoreRepository scores = scope.ServiceProvider.GetRequiredService<IScoreRepository>();
        Score? score = await scores.GetByIdAsync(scoreId);
        if (score is not { Passed: true, RoomId: { } roomId, PlaylistItemId: { } itemId } || !IsWatched(roomId))
        {
            return;
        }

        IRoomRepository rooms = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
        Room? room = await rooms.GetRoom(roomId);
        if (room?.Type != MatchType.Playlists)
        {
            return;
        }

        IReadOnlyList<Score> passingScores = await scores.GetPassingScoresForRoomAsync(roomId);
        Score? bestScore = passingScores
            .Where(candidate => candidate.UserId == score.UserId && candidate.PlaylistItemId == itemId)
            .OrderByDescending(candidate => candidate.TotalScore)
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefault();

        int? newRank = null;
        if (bestScore?.Id == score.Id)
        {
            var standings = passingScores
                .GroupBy(candidate => (candidate.UserId, candidate.PlaylistItemId))
                .Select(group => group.OrderByDescending(candidate => candidate.TotalScore).ThenBy(candidate => candidate.Id).First())
                .GroupBy(candidate => candidate.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    TotalScore = group.Sum(candidate => (long)candidate.TotalScore),
                    LastScoreId = group.Max(candidate => candidate.Id),
                })
                .OrderByDescending(entry => entry.TotalScore)
                .ThenBy(entry => entry.LastScoreId)
                .ToArray();
            newRank = Array.FindIndex(standings, entry => entry.UserId == score.UserId) + 1;
        }

        if (IsWatched(roomId))
        {
            await hubContext.Clients.Group(MetadataHub.MultiplayerRoomWatchersGroup(roomId))
                .MultiplayerRoomScoreSet(new MultiplayerRoomScoreSetEvent
                {
                    RoomID = roomId,
                    PlaylistItemID = itemId,
                    ScoreID = score.Id,
                    UserID = score.UserId,
                    TotalScore = score.TotalScore,
                    NewRank = newRank,
                });
        }
    }

    private bool IsWatched(long roomId)
    {
        lock (_subscriptionLock)
        {
            return _subscriptions.ContainsKey(roomId);
        }
    }
}