// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Communication;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Players;
using g0v0.Server.Realtime.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace g0v0.Server.Realtime.Tests.Objects.Players;

[TestFixture]
public class PlayerFacadeTests
{
    [Test]
    public void ApplyNonNullDependenciesFrom_ShouldCopyOnlyProvidedOptionalDependencies()
    {
        InterProcessCommunicationClient ipcClient = new(new NoOpTransport(), "realtime-tests");
        PlayerManager manager = new(
            NullLogger<PlayerManager>.Instance,
            ipcClient);
        PlayerFacade existing = new(manager);
        using MemoryCache memoryCache = new(new MemoryCacheOptions());
        ScoreBuffer scoreBuffer = new(memoryCache, NullLogger<ScoreBuffer>.Instance);

        ServiceCollection serviceCollection = new();
        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        IServiceScopeFactory scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        ScoreProcessedNotificationService scoreProcessedService = new(
            ipcClient,
            scopeFactory,
            NullLogger<ScoreProcessedNotificationService>.Instance);

        PlayerFacade incoming = new(manager)
        {
            ScoreBuffer = scoreBuffer,
            ScoreProcessedNotificationService = scoreProcessedService,
        };

        ((IPlayerFacade)existing).ApplyNonNullDependenciesFrom(incoming);

        Assert.Multiple(() =>
        {
            Assert.That(existing.Manager, Is.SameAs(manager));
            Assert.That(existing.ScoreBuffer, Is.SameAs(scoreBuffer));
            Assert.That(existing.ScoreProcessedNotificationService, Is.SameAs(scoreProcessedService));
            Assert.That(existing.ScoreUploader, Is.Null);
            Assert.That(existing.ConfigManager, Is.Null);
        });
    }

    private sealed class NoOpTransport : IInterProcessCommunicationTransport
    {
        public Task PublishAsync(string channel, string payload) => Task.CompletedTask;

        public void Subscribe(string channel, Func<string, Task> handler)
        {
        }
    }
}