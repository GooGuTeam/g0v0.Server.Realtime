// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using osu.Game.Scoring;

namespace g0v0.Server.Realtime.Tests.Services;

[TestFixture]
public class ScoreBufferTests
{
    private MemoryCache _memoryCache = null!;
    private ScoreBuffer _buffer = null!;

    [SetUp]
    public void SetUp()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _buffer = new ScoreBuffer(_memoryCache, NullLogger<ScoreBuffer>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _memoryCache.Dispose();
    }

    [Test]
    public async Task TryAddAsync_WhenTokenIsNew_ShouldStoreScore()
    {
        var score = new Score();

        bool added = await _buffer.TryAddAsync(100, score);
        Score? result = await _buffer.DequeueAsync(100);

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.True);
            Assert.That(result, Is.SameAs(score));
        });
    }

    [Test]
    public async Task TryAddAsync_WhenTokenAlreadyExists_ShouldReturnFalseAndKeepOriginalScore()
    {
        var first = new Score();
        var second = new Score();
        await _buffer.TryAddAsync(101, first);

        bool added = await _buffer.TryAddAsync(101, second);
        Score? result = await _buffer.DequeueAsync(101);

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.False);
            Assert.That(result, Is.SameAs(first));
        });
    }

    [Test]
    public async Task DequeueAsync_WhenTokenIsMissing_ShouldReturnNull()
    {
        Score? result = await _buffer.DequeueAsync(404);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task DequeueAsync_ShouldRemoveBufferedScore()
    {
        await _buffer.TryAddAsync(102, new Score());

        _ = await _buffer.DequeueAsync(102);
        Score? secondDequeue = await _buffer.DequeueAsync(102);

        Assert.That(secondDequeue, Is.Null);
    }
}