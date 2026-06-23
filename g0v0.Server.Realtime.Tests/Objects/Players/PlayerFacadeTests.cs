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
        var ipcClient = new InterProcessCommunicationClient(new NoOpTransport(), "realtime-tests");
        var manager = new PlayerManager(
            NullLogger<PlayerManager>.Instance,
            ipcClient);
        var existing = new PlayerFacade(manager);
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var scoreBuffer = new ScoreBuffer(memoryCache, NullLogger<ScoreBuffer>.Instance);

        var serviceCollection = new ServiceCollection();
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var scoreProcessedService = new ScoreProcessedNotificationService(
            ipcClient,
            scopeFactory,
            NullLogger<ScoreProcessedNotificationService>.Instance);

        var incoming = new PlayerFacade(manager)
        {
            _scoreBuffer = scoreBuffer,
            _scoreProcessedNotificationService = scoreProcessedService,
        };

        ((IPlayerFacade)existing).ApplyNonNullDependenciesFrom(incoming);

        Assert.Multiple(() =>
        {
            Assert.That(existing._manager, Is.SameAs(manager));
            Assert.That(existing._scoreBuffer, Is.SameAs(scoreBuffer));
            Assert.That(existing._scoreProcessedNotificationService, Is.SameAs(scoreProcessedService));
            Assert.That(existing._scoreUploader, Is.Null);
            Assert.That(existing._configManager, Is.Null);
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