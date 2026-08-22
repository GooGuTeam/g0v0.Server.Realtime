// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Rulesets;
using g0v0.Server.Realtime.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace g0v0.Server.Realtime.Tests.Services;

[TestFixture]
public class RulesetHashValidatorTests
{
    private Dictionary<string, RulesetVersionEntry> _metadata = null!;
    private MemoryCache _memoryCache = null!;
    private RulesetHashValidator _validator = null!;
    private int _requestCount;

    [SetUp]
    public void SetUp()
    {
        _requestCount = 0;
        _metadata = new Dictionary<string, RulesetVersionEntry>(StringComparer.Ordinal)
        {
            ["custom"] = new RulesetVersionEntry
            {
                LatestVersion = "20250101",
                Versions = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["20250101"] = "expected-hash",
                },
            },
        };

        InMemoryInterProcessCommunicationTransport transport = new();
        InterProcessCommunicationClient realtimeClient = new(transport, "realtime");
        InterProcessCommunicationClient lazerClient = new(transport, "lazer");
        lazerClient.RegisterResponder<object, Dictionary<string, RulesetVersionEntry>>(
            "get_ruleset_hashes",
            _ =>
            {
                Interlocked.Increment(ref _requestCount);
                return _metadata;
            });

        PlayerManager playerManager = new(NullLogger<PlayerManager>.Instance, realtimeClient);
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _validator = new RulesetHashValidator(
            new RulesetManager(Path.GetTempPath()),
            realtimeClient,
            playerManager,
            _memoryCache,
            NullLogger<RulesetHashValidator>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _memoryCache.Dispose();
    }

    [Test]
    public async Task ValidateAsync_OfficialRuleset_DoesNotRequestMetadata()
    {
        RulesetHashValidationFailure? result = await _validator.ValidateAsync("osu", string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(_requestCount, Is.Zero);
        });
    }

    [Test]
    public async Task ValidateAsync_CustomRulesetWithCurrentHash_Succeeds()
    {
        RulesetHashValidationFailure? result = await _validator.ValidateAsync("custom", "expected-hash");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ValidateAsync_CustomRulesetWithOutdatedHash_ReturnsLatestVersion()
    {
        RulesetHashValidationFailure? result = await _validator.ValidateAsync("custom", "outdated-hash");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.RulesetName, Is.EqualTo("custom"));
            Assert.That(result.ServerVersion, Is.EqualTo("20250101"));
        });
    }

    [Test]
    public async Task ValidateAsync_UnknownRuleset_ReturnsServerNotSupported()
    {
        RulesetHashValidationFailure? result = await _validator.ValidateAsync("unknown", "some-hash");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.RulesetName, Is.EqualTo("unknown"));
            Assert.That(result.ServerVersion, Is.EqualTo("server-not-supported"));
        });
    }

    [Test]
    public async Task ValidateAsync_MissingLatestVersionHash_ReturnsServerNotSupported()
    {
        _metadata["custom"].LatestVersion = "20250202";

        RulesetHashValidationFailure? result = await _validator.ValidateAsync("custom", "expected-hash");

        Assert.That(result?.ServerVersion, Is.EqualTo("server-not-supported"));
    }

    [Test]
    public async Task ValidateAsync_AfterInitialRequest_ReusesCachedMetadata()
    {
        await _validator.ValidateAsync("custom", "expected-hash");
        await _validator.ValidateAsync("custom", "expected-hash");

        Assert.That(_requestCount, Is.EqualTo(1));
    }

    private sealed class InMemoryInterProcessCommunicationTransport : IInterProcessCommunicationTransport
    {
        private readonly ConcurrentDictionary<string, ConcurrentBag<Func<string, Task>>> _subscriptions =
            new(StringComparer.Ordinal);

        public Task PublishAsync(string channel, string payload)
        {
            return !_subscriptions.TryGetValue(channel, out ConcurrentBag<Func<string, Task>>? handlers)
                ? Task.CompletedTask
                : Task.WhenAll(handlers.Select(handler => handler(payload)));
        }

        public void Subscribe(string channel, Func<string, Task> handler)
        {
            _subscriptions.GetOrAdd(channel, _ => []).Add(handler);
        }

        public void Unsubscribe(string channel)
        {
            _subscriptions.TryRemove(channel, out _);
        }
    }
}