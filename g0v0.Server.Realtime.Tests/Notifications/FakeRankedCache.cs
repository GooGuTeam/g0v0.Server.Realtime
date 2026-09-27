// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Caching;

namespace g0v0.Server.Realtime.Tests.Notifications;

/// <summary>
/// In-memory <see cref="IRankedCache"/> used by chat tests.
/// </summary>
internal sealed class FakeRankedCache : IRankedCache
{
    private readonly Dictionary<string, Dictionary<string, double>> _sets = new(StringComparer.Ordinal);

    public Task SetScoreAsync(string key, string member, double score, TimeSpan expiry)
    {
        GetSet(key)[member] = score;
        return Task.CompletedTask;
    }

    public Task<int> CountWindowAndAddAsync(string key, string member, double score, double windowStart, TimeSpan expiry)
    {
        Dictionary<string, double> set = GetSet(key);
        foreach (string stale in set.Where(entry => entry.Value < windowStart).Select(static entry => entry.Key).ToList())
        {
            set.Remove(stale);
        }

        int count = set.Count;
        set[member] = score;
        return Task.FromResult(count);
    }

    public Task<IReadOnlyList<string>> GetByScoreAsync(string key, double minScore, double maxScore = double.PositiveInfinity)
    {
        IReadOnlyList<string> members = GetSet(key)
            .Where(entry => entry.Value >= minScore && entry.Value <= maxScore)
            .OrderBy(static entry => entry.Value)
            .Select(static entry => entry.Key)
            .ToList();

        return Task.FromResult(members);
    }

    private Dictionary<string, double> GetSet(string key)
    {
        if (_sets.TryGetValue(key, out Dictionary<string, double>? set))
        {
            return set;
        }

        set = new Dictionary<string, double>(StringComparer.Ordinal);
        _sets[key] = set;
        return set;
    }
}