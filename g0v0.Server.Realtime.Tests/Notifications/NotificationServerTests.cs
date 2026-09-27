// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Notifications;
using g0v0.Server.Realtime.Objects.Chat;
using g0v0.Server.Realtime.Objects.Notifications;
using g0v0.Server.Realtime.Objects.Players;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using osu.Game.Online.Chat;

namespace g0v0.Server.Realtime.Tests.Notifications;

[TestFixture]
public class NotificationServerTests
{
    private IServiceScopeFactory _scopeFactory = null!;
    private FakeChatRepository _chatRepository = null!;
    private PlayerManager _playerManager = null!;
    private NotificationServer _server = null!;
    private string _basePath = null!;

    [SetUp]
    public void SetUp()
    {
        _chatRepository = new FakeChatRepository();
        _playerManager = new PlayerManager(
            NullLogger<PlayerManager>.Instance,
            new InterProcessCommunicationClient(new NoOpTransport(), "realtime-tests"));

        _basePath = Path.Combine(Path.GetTempPath(), "g0v0-notification-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_basePath, "config"));
        File.WriteAllText(
            Path.Combine(_basePath, "config", "chat.json"),
            "{\"PublicMessageLimit\":100,\"PrivateMessageLimit\":100}");
        ConfigurationManager configurationManager = new(_basePath);

        ServiceCollection services = new();
        services.AddSingleton<IChatRepository>(_chatRepository);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        _scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        _server = new NotificationServer(
            NullLogger<NotificationServer>.Instance,
            new FakeRankedCache(),
            configurationManager,
            _playerManager,
            _scopeFactory);
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
    public async Task JoinAndLeaveChannel_ShouldTrackMembership()
    {
        TestPlayer player = new(1, _playerManager);

        await _server.Connect(player);
        await _server.JoinChannel(player, 100);

        Assert.That(await _server.UserInChannel(100, 1), Is.True);
        Assert.That(await _server.GetUserJoinedChannels(1), Is.EquivalentTo([100]));

        await _server.LeaveChannel(player, 100);

        Assert.That(await _server.UserInChannel(100, 1), Is.False);
        Assert.That(await _server.GetUserJoinedChannels(1), Is.Empty);
    }

    [Test]
    public async Task Disconnect_ShouldKeepMemberships()
    {
        TestPlayer player = new(1, _playerManager);

        await _server.Connect(player);
        await _server.JoinChannel(player, 100);
        await _server.JoinChannel(player, 200);

        await _server.Disconnect(player);

        // osu-web only drops the connection; the membership rows survive and the
        // public channel activity markers expire on their own.
        int[] expectedChannels = [100, 200];
        Assert.That(await _server.GetUserJoinedChannels(1), Is.EquivalentTo(expectedChannels));
        Assert.That(await _server.UserInChannel(100, 1), Is.True);
        Assert.That(await _server.UserInChannel(200, 1), Is.True);
    }

    [Test]
    public async Task SendMessage_ShouldPersistAndDeliverToMembers()
    {
        _chatRepository.AddChannel(new ChatChannel { ChannelId = 100, Name = "#osu", Description = "test", Type = ChannelType.Public });

        TestPlayer sender = new(1, _playerManager);
        TestPlayer receiver = new(2, _playerManager);
        await _playerManager.AddPlayer(sender);
        await _playerManager.AddPlayer(receiver);
        await _server.Connect(sender);
        await _server.Connect(receiver);
        await _server.JoinChannel(sender, 100);
        await _server.JoinChannel(receiver, 100);

        ChatMessage message = await _server.SendMessage(100, 1, "hello", isAction: false);

        Assert.That(message.MessageId, Is.GreaterThan(0));
        Assert.That(_chatRepository.Messages, Has.Count.EqualTo(1));
        Assert.That(receiver.ReceivedMessages, Has.Count.EqualTo(1));
        Assert.That(receiver.ReceivedMessages[0].MessageId, Is.EqualTo(message.MessageId));

        ChatChannel? channel = await _chatRepository.GetChannelAsync(100);
        Assert.That(channel?.LastMessageId, Is.EqualTo(message.MessageId));
    }

    [Test]
    public async Task SendMessage_WhenContentIsRejected_ShouldThrow()
    {
        Assert.That(
            async () => await _server.SendMessage(100, 1, "   ", isAction: false),
            Throws.TypeOf<ChatMessageRejectedException>()
                .With.Property("Reason").EqualTo(ChatMessageRejectedException.RejectionReason.Empty));

        Assert.That(
            async () => await _server.SendMessage(100, 1, new string('a', 451), isAction: false),
            Throws.TypeOf<ChatMessageRejectedException>()
                .With.Property("Reason").EqualTo(ChatMessageRejectedException.RejectionReason.TooLong));
    }

    [Test]
    public async Task SendMessage_ShouldCollapseNewlines()
    {
        ChatMessage message = await _server.SendMessage(100, 1, "  hello\r\nworld  ", isAction: false);

        Assert.That(message.Content, Is.EqualTo("hello  world"));
    }

    [Test]
    public async Task SendMessage_WhenSentTooQuickly_ShouldThrow()
    {
        // The production rate limit is one message per second and bucket; a
        // config directory without chat.json keeps those defaults.
        string basePath = Path.Combine(Path.GetTempPath(), "g0v0-notification-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(basePath);
        NotificationServer throttledServer = new(
            NullLogger<NotificationServer>.Instance,
            new FakeRankedCache(),
            new ConfigurationManager(basePath),
            _playerManager,
            _scopeFactory);

        await throttledServer.SendMessage(100, 1, "first", isAction: false);

        Assert.That(
            async () => await throttledServer.SendMessage(100, 1, "second", isAction: false),
            Throws.TypeOf<ChatMessageRejectedException>()
                .With.Property("Reason").EqualTo(ChatMessageRejectedException.RejectionReason.Throttled));
    }

    [Test]
    public async Task MarkAsRead_ShouldPersistReadState()
    {
        TestPlayer player = new(1, _playerManager);
        await _server.Connect(player);
        await _server.JoinChannel(player, 100);

        await _server.MarkAsRead(player, 100, 42);

        ChatUserChannel? membership = await _chatRepository.GetUserChannelAsync(100, 1);
        Assert.That(membership, Is.Not.Null);
        Assert.That(membership!.LastReadId, Is.EqualTo(42));

        // The marker never moves backwards.
        await _server.MarkAsRead(player, 100, 41);
        Assert.That((await _chatRepository.GetUserChannelAsync(100, 1))!.LastReadId, Is.EqualTo(42));
    }

    [Test]
    public async Task Ack_ShouldTrackConnectionAlive()
    {
        TestPlayer player = new(1, _playerManager);
        await _server.Connect(player);

        Assert.DoesNotThrowAsync(async () => await _server.Ack(player));
    }

    [Test]
    public async Task SendNotification_ShouldDeliverToReceivers()
    {
        TestPlayer receiver = new(7, _playerManager);
        await _playerManager.AddPlayer(receiver);
        await _server.Connect(receiver);

        ChannelMessageNotification detail = new(sourceUserId: 1, receiverIds: [7])
        {
            Title = "New message",
            CoverUrl = "https://example.com/a.png",
            Type = "pm",
            ChannelId = 100,
        };

        await _server.SendNotification(detail);

        Assert.That(receiver.ReceivedNotifications, Has.Count.EqualTo(1));
        Assert.That(receiver.ReceivedNotifications[0], Is.SameAs(detail));
    }

    [Test]
    public async Task SendMessage_WithNoMembers_ShouldStillPersist()
    {
        ChatMessage message = await _server.SendMessage(999, 1, "hi", isAction: false);

        Assert.That(message.MessageId, Is.GreaterThan(0));
        Assert.That(_chatRepository.Messages, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task JoinChannel_ShouldNotifyEveryConnectionOfTheUser()
    {
        TestPlayer firstConnection = new(1, _playerManager);
        TestPlayer secondConnection = new(1, _playerManager);
        await _server.Connect(firstConnection);
        await _server.Connect(secondConnection);

        await _server.JoinChannel(firstConnection, 100);

        Assert.Multiple(() =>
        {
            Assert.That(firstConnection.JoinedChannels, Is.EquivalentTo([100]));
            Assert.That(secondConnection.JoinedChannels, Is.EquivalentTo([100]));
        });
    }

    [Test]
    public async Task LeaveChannel_ShouldNotifyEveryConnectionOfTheUser()
    {
        TestPlayer firstConnection = new(1, _playerManager);
        TestPlayer secondConnection = new(1, _playerManager);
        await _server.Connect(firstConnection);
        await _server.Connect(secondConnection);
        await _server.JoinChannel(firstConnection, 100);
        firstConnection.JoinedChannels.Clear();
        secondConnection.JoinedChannels.Clear();

        await _server.LeaveChannel(firstConnection, 100);

        Assert.Multiple(() =>
        {
            Assert.That(firstConnection.LeftChannels, Is.EquivalentTo([100]));
            Assert.That(secondConnection.LeftChannels, Is.EquivalentTo([100]));
        });
    }

    [Test]
    public async Task JoinChannel_ShouldNotNotifyOtherUser()
    {
        TestPlayer joiner = new(1, _playerManager);
        TestPlayer other = new(2, _playerManager);
        await _server.Connect(joiner);
        await _server.Connect(other);

        await _server.JoinChannel(joiner, 100);

        Assert.Multiple(() =>
        {
            Assert.That(joiner.JoinedChannels, Is.EquivalentTo([100]));
            Assert.That(other.JoinedChannels, Is.Empty);
        });
    }

    [Test]
    public async Task SendMessage_UserCommand_ShouldOnlyReachSender()
    {
        TestPlayer sender = new(1, _playerManager);
        TestPlayer receiver = new(2, _playerManager);
        await _playerManager.AddPlayer(sender);
        await _playerManager.AddPlayer(receiver);
        await _server.Connect(sender);
        await _server.Connect(receiver);
        await _server.JoinChannel(sender, 100);
        await _server.JoinChannel(receiver, 100);

        await _server.SendMessage(100, 1, "!roll", isAction: false);

        Assert.Multiple(() =>
        {
            Assert.That(sender.ReceivedMessages, Has.Count.EqualTo(1));
            Assert.That(receiver.ReceivedMessages, Is.Empty);
        });
    }

    [Test]
    public async Task SendMessage_PmChannel_ShouldReachBothMembersWithoutJoining()
    {
        ChatChannel pmChannel = new()
        {
            ChannelId = 500,
            Name = "pm_1_2",
            Description = "Private message channel",
            Type = ChannelType.PM,
        };
        _chatRepository.AddChannel(pmChannel);
        TestPlayer sender = new(1, _playerManager);
        TestPlayer receiver = new(2, _playerManager);
        await _playerManager.AddPlayer(sender);
        await _playerManager.AddPlayer(receiver);
        await _server.Connect(sender);
        await _server.Connect(receiver);

        await _server.SendMessage(500, 1, "hello", isAction: false);

        Assert.Multiple(() =>
        {
            Assert.That(sender.ReceivedMessages, Has.Count.EqualTo(1));
            Assert.That(receiver.ReceivedMessages, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task SendMessage_ShouldReachEveryConnectionOfEachMember()
    {
        TestPlayer sender = new(1, _playerManager);
        TestPlayer receiverFirst = new(2, _playerManager);
        TestPlayer receiverSecond = new(2, _playerManager);
        await _playerManager.AddPlayer(sender);
        await _playerManager.AddPlayer(receiverFirst);
        await _server.Connect(sender);
        await _server.Connect(receiverFirst);
        await _server.Connect(receiverSecond);
        await _server.JoinChannel(sender, 100);
        await _server.JoinChannel(receiverFirst, 100);

        await _server.SendMessage(100, 1, "hello", isAction: false);

        Assert.Multiple(() =>
        {
            Assert.That(receiverFirst.ReceivedMessages, Has.Count.EqualTo(1));
            Assert.That(receiverSecond.ReceivedMessages, Has.Count.EqualTo(1));
        });
    }

    private sealed class TestPlayer(int playerId, PlayerManager manager) : PlayerBase(playerId, new PlayerFacade(manager))
    {
        public List<ChatMessage> ReceivedMessages { get; } = new();

        public List<NotificationDetail> ReceivedNotifications { get; } = new();

        public List<int> JoinedChannels { get; } = new();

        public List<int> LeftChannels { get; } = new();

        public override string Server => "Test";

        public override Task OnPlayerOnline(IPlayer player) => Task.CompletedTask;

        public override Task OnPlayerOffline(IPlayer player, bool isKicked = false) => Task.CompletedTask;

        public override Task OnPlayerChangeActivity(IPlayer player, g0v0.Server.Realtime.Objects.States.Activity.IUserActivity activity) => Task.CompletedTask;

        public override Task OnPlayerChangeStatus(IPlayer player, osu.Game.Users.UserStatus? status) => Task.CompletedTask;

        public override Task OnUserBeganPlaying(IPlayer player) => Task.CompletedTask;

        public override Task OnUserFinishedPlaying(IPlayer player) => Task.CompletedTask;

        public override Task OnUserSentFrames(IPlayer player, osu.Game.Online.Spectator.FrameDataBundle frame) => Task.CompletedTask;

        public override Task OnWatched(IPlayer source) => Task.CompletedTask;

        public override Task OnWatchedStopped(IPlayer source) => Task.CompletedTask;

        public override Task OnScoreProcessed(long scoreId) => Task.CompletedTask;

        public override Task OnNewMessage(IPlayer player, ChatMessage message)
        {
            ReceivedMessages.Add(message);
            return Task.CompletedTask;
        }

        public override Task OnNewNotification(IPlayer player, NotificationDetail detail)
        {
            ReceivedNotifications.Add(detail);
            return Task.CompletedTask;
        }

        public override Task OnChannelJoin(IPlayer player, int channelId)
        {
            JoinedChannels.Add(channelId);
            return Task.CompletedTask;
        }

        public override Task OnChannelLeave(IPlayer player, int channelId)
        {
            LeftChannels.Add(channelId);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpTransport : IInterProcessCommunicationTransport
    {
        public Task PublishAsync(string channel, string payload) => Task.CompletedTask;

        public void Subscribe(string channel, Func<string, Task> handler)
        {
        }
    }
}