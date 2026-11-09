// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Caching;
using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Realtime.Controllers;
using g0v0.Server.Realtime.Json;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Notifications;
using g0v0.Server.Realtime.Objects.Players;
using g0v0.Server.Realtime.Objects.Rooms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace g0v0.Server.Realtime.Tests.Controllers;

[TestFixture]
public class HealthControllerTests
{
    private PlayerManager _playerManager = null!;
    private NotificationServer _notificationServer = null!;

    [SetUp]
    public void SetUp()
    {
        _playerManager = new PlayerManager(
            NullLogger<PlayerManager>.Instance,
            new InterProcessCommunicationClient(new NoOpTransport(), "realtime-tests"));
        _notificationServer = new NotificationServer(
            NullLogger<NotificationServer>.Instance,
            Mock.Of<IRankedCache>(),
            new ConfigManager(Path.GetTempPath()),
            _playerManager,
            Mock.Of<IServiceScopeFactory>());
    }

    [Test]
    public async Task Get_ReportsRealtimeIdentifyAndLiveCounters()
    {
        await new LazerPlayer(1, new PlayerFacade(_playerManager)).Online();

        RoomManager roomManager = new(
            NullLogger<RoomManager>.Instance,
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<IRoomFacade>());
        roomManager.AddRoom(Mock.Of<IRoom>(room => room.RoomId == 42L));

        LazerPlayer connected = new(1, new PlayerFacade(_playerManager));
        await _notificationServer.Connect(connected);
        await _notificationServer.Connect(connected);
        await _notificationServer.Connect(new LazerPlayer(2, new PlayerFacade(_playerManager)));

        HealthController controller = new(_playerManager, roomManager, _notificationServer);

        OkObjectResult result = (OkObjectResult)controller.Get();
        JObject payload = JObject.Parse(
            JsonConvert.SerializeObject(
                result.Value,
                new JsonSerializerSettings { ContractResolver = RealtimeJson.MvcContractResolver }));

        Assert.Multiple(() =>
        {
            Assert.That(payload["identify"]!.Value<string>(), Is.EqualTo("realtime"));
            Assert.That(payload["players_online"]!.Value<int>(), Is.EqualTo(1));
            Assert.That(payload["multiplayer_rooms"]!.Value<int>(), Is.EqualTo(1));
            Assert.That(payload["websocket_connections"]!.Value<int>(), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task ConnectionCount_CountsEverySocketAndKeepsSiblingsOnDisconnect()
    {
        LazerPlayer player = new(1, new PlayerFacade(_playerManager));
        await _notificationServer.Connect(player);
        await _notificationServer.Connect(player);
        await _notificationServer.Connect(new LazerPlayer(2, new PlayerFacade(_playerManager)));

        await _notificationServer.Disconnect(player);

        Assert.That(_notificationServer.ConnectionCount, Is.EqualTo(2));
    }

    private sealed class NoOpTransport : IInterProcessCommunicationTransport
    {
        public Task PublishAsync(string channel, string payload) => Task.CompletedTask;

        public void Subscribe(string channel, Func<string, Task> handler)
        {
        }
    }
}