// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Realtime.Objects.Chat;
using g0v0.Server.Realtime.Services;
using g0v0.Server.Realtime.Tests.Multiplayer;
using g0v0.Server.Realtime.Tests.Notifications;
using NUnit.Framework;
using osu.Game.Online.Chat;

namespace g0v0.Server.Realtime.Tests.Objects.Chat;

[TestFixture]
public class ChatChannelDtoFactoryTests
{
    private FakeChatRepository _chatRepository = null!;
    private FakeUserRepository _userRepository = null!;
    private FakeRelationshipRepository _relationshipRepository = null!;
    private ConfigManager _configManager = null!;
    private ChatChannelDtoFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _chatRepository = new FakeChatRepository();
        _userRepository = new FakeUserRepository();
        _userRepository.AddUser(new User { Id = 1, Username = "user1" });
        _userRepository.AddUser(new User { Id = 2, Username = "user2" });
        _userRepository.AddUser(new User { Id = 3, Username = "user3", IsBot = true });
        _relationshipRepository = new FakeRelationshipRepository();
        _configManager = new ConfigManager(Path.Combine(Path.GetTempPath(), "g0v0-chat-dto-factory-tests", Guid.NewGuid().ToString("N")));

        _factory = new ChatChannelDtoFactory(_chatRepository, _userRepository, _relationshipRepository, _configManager);
    }

    [Test]
    public async Task CreateChannelListAsync_ShouldReturnVisibleMembershipsOnly()
    {
        ChatChannel pm = await _chatRepository.CreatePmChannelAsync(1, 2);
        ChatChannel publicChannel = await _chatRepository.CreateChannelAsync("#osu", "The official osu! channel.", ChannelType.Public);
        ChatChannel unrelated = await _chatRepository.CreatePmChannelAsync(2, 3);
        await _chatRepository.AddUserChannelAsync(pm.ChannelId, 1);
        await _chatRepository.AddUserChannelAsync(publicChannel.ChannelId, 1);
        await _chatRepository.AddUserChannelAsync(unrelated.ChannelId, 2);
        await _chatRepository.AddUserChannelAsync(publicChannel.ChannelId, 1);

        // A hidden conversation stays out of the listing.
        ChatChannel hidden = await _chatRepository.CreatePmChannelAsync(1, 3);
        await _chatRepository.AddUserChannelAsync(hidden.ChannelId, 1);
        await _chatRepository.RemoveUserChannelAsync(hidden.ChannelId, 1, hide: true);

        IReadOnlyList<ChatChannelDto> channels = await _factory.CreateChannelListAsync(1);

        int[] expectedChannelIds = [pm.ChannelId, publicChannel.ChannelId];
        Assert.That(channels.Select(channel => channel.ChannelId), Is.EqualTo(expectedChannelIds));
    }

    [Test]
    public async Task CreateChannelListAsync_ShouldHideConversationsWithBlockedUsers()
    {
        ChatChannel pm = await _chatRepository.CreatePmChannelAsync(1, 2);
        ChatChannel botPm = await _chatRepository.CreatePmChannelAsync(1, 3);
        await _chatRepository.AddUserChannelAsync(pm.ChannelId, 1);
        await _chatRepository.AddUserChannelAsync(botPm.ChannelId, 1);
        _relationshipRepository.AddBlock(1, 2);
        _relationshipRepository.AddBlock(1, 3);

        IReadOnlyList<ChatChannelDto> channels = await _factory.CreateChannelListAsync(1);

        // osu-web's Channel::isVisibleFor keeps bot conversations visible.
        int[] expectedChannelIds = [botPm.ChannelId];
        Assert.That(channels.Select(channel => channel.ChannelId), Is.EqualTo(expectedChannelIds));
    }

    [Test]
    public async Task CreateChannelListAsync_WithRecentMessages_ShouldFilterUserCommandsAndPublicBacklog()
    {
        ChatChannel publicChannel = await _chatRepository.CreateChannelAsync("#osu", "The official osu! channel.", ChannelType.Public);
        ChatMessage ancient = await _chatRepository.CreateMessageAsync(publicChannel.ChannelId, 2, "ancient", MessageType.Plain);
        ancient.Timestamp = DateTimeOffset.UtcNow.AddDays(-2);
        await _chatRepository.CreateMessageAsync(publicChannel.ChannelId, 2, "!roll 100", MessageType.Plain);
        await _chatRepository.CreateMessageAsync(publicChannel.ChannelId, 1, "!roll 100", MessageType.Plain);
        ChatMessage recent = await _chatRepository.CreateMessageAsync(publicChannel.ChannelId, 2, "hello", MessageType.Plain);
        await _chatRepository.UpdateLastMessageIdAsync(publicChannel.ChannelId, recent.MessageId);
        await _chatRepository.AddUserChannelAsync(publicChannel.ChannelId, 1);

        IReadOnlyList<ChatChannelDto> channels = await _factory.CreateChannelListAsync(1, includeRecentMessages: true);

        Assert.That(channels, Has.Count.EqualTo(1));
        string[] expectedContents = ["!roll 100", "hello"];
        Assert.That(channels[0].RecentMessages!.Select(message => message.Content), Is.EqualTo(expectedContents));
        Assert.That(channels[0].RecentMessages!.All(message => message.Sender != null), Is.True);
        Assert.That(channels[0].LastMessageId, Is.EqualTo(recent.MessageId));
    }

    [Test]
    public async Task CreateChannelListAsync_ShouldKeepOldPrivateMessages()
    {
        ChatChannel pm = await _chatRepository.CreatePmChannelAsync(1, 2);
        ChatMessage old = await _chatRepository.CreateMessageAsync(pm.ChannelId, 2, "old but private", MessageType.Plain);
        old.Timestamp = DateTimeOffset.UtcNow.AddDays(-7);
        await _chatRepository.AddUserChannelAsync(pm.ChannelId, 1);

        IReadOnlyList<ChatChannelDto> channels = await _factory.CreateChannelListAsync(1, includeRecentMessages: true);

        Assert.That(channels, Has.Count.EqualTo(1));
        string[] expectedContents = ["old but private"];
        Assert.That(channels[0].RecentMessages!.Select(message => message.Content), Is.EqualTo(expectedContents));
    }

    [Test]
    public async Task CreateChannelAsync_PmChannel_ShouldResolveDisplayNameIconAndParticipants()
    {
        ChatChannel pm = await _chatRepository.CreatePmChannelAsync(2, 1);

        ChatChannelDto dto = await _factory.CreateChannelAsync(pm, 1);

        Assert.That(dto.Name, Is.EqualTo("user2"));
        Assert.That(dto.Icon, Is.EqualTo("https://a.ppy.sh/2"));
        int[] expectedParticipants = [2, 1];
        Assert.That(dto.Users, Is.EqualTo(expectedParticipants));
        Assert.That(dto.MessageLengthLimit, Is.EqualTo(ChatConfig.DefaultMessageLengthLimit));
        Assert.That(dto.Type, Is.EqualTo("PM"));
    }

    [Test]
    public async Task CreateChannelAsync_ShouldReadMarkersFromMembershipAndChannel()
    {
        ChatChannel channel = await _chatRepository.CreateChannelAsync("#osu", "The official osu! channel.", ChannelType.Public);
        await _chatRepository.AddUserChannelAsync(channel.ChannelId, 1);
        await _chatRepository.MarkUserChannelAsReadAsync(channel.ChannelId, 1, 41);
        await _chatRepository.CreateMessageAsync(channel.ChannelId, 2, "hello", MessageType.Plain);
        await _chatRepository.UpdateLastMessageIdAsync(channel.ChannelId, 42);
        ChatUserChannel membership = (await _chatRepository.GetUserChannelAsync(channel.ChannelId, 1))!;

        ChatChannelDto dto = await _factory.CreateChannelAsync(channel, 1, membership);

        Assert.That(dto.Name, Is.EqualTo("#osu"));
        Assert.That(dto.Icon, Is.Null);
        Assert.That(dto.Users, Is.Empty);
        Assert.That(dto.LastMessageId, Is.EqualTo(42));
        Assert.That(dto.LastReadId, Is.EqualTo(41));
        Assert.That(dto.CurrentUserAttributes!.LastReadId, Is.EqualTo(41));
        Assert.That(dto.CurrentUserAttributes.CanMessage, Is.True);
        Assert.That(dto.CurrentUserAttributes.CanMessageError, Is.Null);
    }

    [Test]
    public async Task CreateChannelAsync_WhenModerated_ShouldReportCannotMessage()
    {
        ChatChannel channel = await _chatRepository.CreateChannelAsync("#osu", "The official osu! channel.", ChannelType.Public);
        channel.Moderated = true;

        ChatChannelDto dto = await _factory.CreateChannelAsync(channel, 1);

        Assert.That(dto.CurrentUserAttributes!.CanMessage, Is.False);
        Assert.That(dto.CurrentUserAttributes.CanMessageError, Is.EqualTo("This channel is currently moderated."));
    }

    [Test]
    public async Task CreateChannelAsync_WhenBlocked_ShouldReportCannotMessage()
    {
        ChatChannel pm = await _chatRepository.CreatePmChannelAsync(1, 2);
        _relationshipRepository.AddBlock(2, 1);

        ChatChannelDto dto = await _factory.CreateChannelAsync(pm, 1);

        Assert.That(dto.CurrentUserAttributes!.CanMessage, Is.False);
        Assert.That(dto.CurrentUserAttributes.CanMessageError, Is.EqualTo(ChatPmStartChecker.BlockedMessage));
    }
}