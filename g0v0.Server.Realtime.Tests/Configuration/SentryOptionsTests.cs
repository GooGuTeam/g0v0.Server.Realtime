// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration;
using NUnit.Framework;

namespace g0v0.Server.Realtime.Tests.Configuration;

/// <summary>
/// Tests for the Sentry settings managed by <see cref="RealtimeConfig"/>.
/// </summary>
[TestFixture]
public class SentryOptionsTests
{
    private string _basePath = null!;

    [SetUp]
    public void SetUp()
    {
        _basePath = Path.Combine(Path.GetTempPath(), "g0v0-sentry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_basePath, "config"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, recursive: true);
        }
    }

    [Test]
    public void DefaultsKeepSentryDisabledUntilDsnIsProvided()
    {
        RealtimeConfig config = new();

        Assert.That(config.Sentry.Enabled, Is.False);
        Assert.That(config.Sentry.Dsn, Is.Empty);
        Assert.That(config.Sentry.Environment, Is.Empty);
        Assert.That(config.Sentry.TracesSampleRate, Is.EqualTo(0.1));
        Assert.That(config.Sentry.SendDefaultPii, Is.False);
    }

    [Test]
    public void RealtimeConfigLoadsSentrySectionFromJson()
    {
        string realtimeJson = """
            {
              "SaveReplays": true,
              "Sentry": {
                "Dsn": "https://key@o123.ingest.sentry.io/456",
                "Environment": "staging",
                "TracesSampleRate": 0.25,
                "SendDefaultPii": true
              }
            }
            """;
        File.WriteAllText(Path.Combine(_basePath, "config", "realtime.json"), realtimeJson);

        RealtimeConfig config = new ConfigManager(_basePath).Get<RealtimeConfig>();

        Assert.That(config.Sentry.Enabled, Is.True);
        Assert.That(config.Sentry.Dsn, Is.EqualTo("https://key@o123.ingest.sentry.io/456"));
        Assert.That(config.Sentry.Environment, Is.EqualTo("staging"));
        Assert.That(config.Sentry.TracesSampleRate, Is.EqualTo(0.25));
        Assert.That(config.Sentry.SendDefaultPii, Is.True);
    }

    [Test]
    public void MissingSentrySectionFallsBackToDefaults()
    {
        File.WriteAllText(
            Path.Combine(_basePath, "config", "realtime.json"),
            "{\"SaveReplays\": true}");

        RealtimeConfig config = new ConfigManager(_basePath).Get<RealtimeConfig>();

        Assert.That(config.Sentry.Enabled, Is.False);
        Assert.That(config.Sentry.Dsn, Is.Empty);
        Assert.That(config.Sentry.TracesSampleRate, Is.EqualTo(0.1));
        Assert.That(config.Sentry.SendDefaultPii, Is.False);
    }
}