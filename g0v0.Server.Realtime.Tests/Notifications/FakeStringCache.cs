// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Caching;

namespace g0v0.Server.Realtime.Tests.Notifications;

/// <summary>
/// In-memory <see cref="IStringCache"/> used by chat tests.
/// </summary>
internal sealed class FakeStringCache : IStringCache
{
    private readonly Dictionary<string, (string Value, DateTimeOffset Expiry)> _values = new(StringComparer.Ordinal);

    public int GetCount { get; private set; }

    public int SetCount { get; private set; }

    public int DeleteCount { get; private set; }

    public Task<string?> GetAsync(string key)
    {
        GetCount++;
        return Task.FromResult(_values.TryGetValue(key, out (string Value, DateTimeOffset Expiry) entry) && entry.Expiry > DateTimeOffset.UtcNow
            ? entry.Value
            : null);
    }

    public Task SetAsync(string key, string value, TimeSpan expiry)
    {
        SetCount++;
        _values[key] = (value, DateTimeOffset.UtcNow.Add(expiry));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key)
    {
        DeleteCount++;
        _values.Remove(key);
        return Task.CompletedTask;
    }

    public Task RefreshAsync(string key, TimeSpan expiry)
    {
        if (_values.TryGetValue(key, out (string Value, DateTimeOffset Expiry) entry))
        {
            _values[key] = (entry.Value, DateTimeOffset.UtcNow.Add(expiry));
        }

        return Task.CompletedTask;
    }

    public void Seed(string key, string value, TimeSpan? ttl = null)
    {
        _values[key] = (value, DateTimeOffset.UtcNow.Add(ttl ?? TimeSpan.FromHours(1)));
    }
}