// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Communication;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Players;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace g0v0.Server.Realtime.Tests.Objects.Players;

[TestFixture]
public class LazerPlayerTests
{
    private PlayerManager _playerManager = null!;

    [SetUp]
    public void SetUp()
    {
        _playerManager = new PlayerManager(
            NullLogger<PlayerManager>.Instance,
            new InterProcessCommunicationClient(new NoOpTransport(), "realtime-tests"));
    }

    [Test]
    public async Task ChatConnection_ShouldBringPlayerOnlineAndBackOffline()
    {
        LazerPlayer player = CreatePlayer();
        Assert.That(IsOnline(player), Is.False);

        await player.ChatConnected();
        Assert.That(IsOnline(player), Is.True);

        await player.ChatDisconnected();
        Assert.That(IsOnline(player), Is.False);
    }

    [Test]
    public async Task HubDisconnect_ShouldKeepPlayerOnlineWhileChatConnectionExists()
    {
        LazerPlayer player = CreatePlayer();
        await player.ChatConnected();
        await player.HubConnected("MetadataHub", "metadata-1");
        await player.HubConnected("SpectatorHub", "spectator-1");
        Assert.That(IsOnline(player), Is.True);

        await player.HubDisconnected("MetadataHub", "metadata-1", exception: null);
        Assert.That(IsOnline(player), Is.True);

        await player.HubDisconnected("SpectatorHub", "spectator-1", exception: null);
        Assert.That(IsOnline(player), Is.True, "the chat WebSocket still keeps the player online");

        await player.ChatDisconnected();
        Assert.That(IsOnline(player), Is.False);
    }

    [Test]
    public async Task Hubs_ShouldKeepPlayerOnlineUntilLastHubDisconnects()
    {
        LazerPlayer player = CreatePlayer();
        await player.HubConnected("MetadataHub", "metadata-1");
        await player.HubConnected("SpectatorHub", "spectator-1");
        Assert.That(IsOnline(player), Is.True);

        await player.HubDisconnected("MetadataHub", "metadata-1", exception: null);
        Assert.That(IsOnline(player), Is.True, "the spectator hub connection still keeps the player online");

        await player.HubDisconnected("SpectatorHub", "spectator-1", exception: null);
        Assert.That(IsOnline(player), Is.False);
    }

    [Test]
    public async Task MultipleChatConnections_ShouldOnlyGoOfflineOnTheLastOne()
    {
        LazerPlayer player = CreatePlayer();
        await player.ChatConnected();
        await player.ChatConnected();
        Assert.That(IsOnline(player), Is.True);

        await player.ChatDisconnected();
        Assert.That(IsOnline(player), Is.True, "another chat WebSocket is still connected");

        await player.ChatDisconnected();
        Assert.That(IsOnline(player), Is.False);
    }

    private LazerPlayer CreatePlayer() =>
        LazerPlayer.GetOrCreate(
            _playerManager,
            3,
            new PlayerFacade(_playerManager) { Logger = NullLogger.Instance });

    private bool IsOnline(LazerPlayer player) =>
        _playerManager.GetPlayerAllInstances(player.PlayerId).Any(online => ReferenceEquals(online, player));

    private sealed class NoOpTransport : IInterProcessCommunicationTransport
    {
        public Task PublishAsync(string channel, string payload) => Task.CompletedTask;

        public void Subscribe(string channel, Func<string, Task> handler)
        {
        }
    }
}