// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Caching;
using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Notifications;
using g0v0.Server.Realtime.Notifications.Bot;
using g0v0.Server.Realtime.Notifications.Bot.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using osu.Game.Online.Chat;

namespace g0v0.Server.Realtime.Tests.Notifications;

/// <summary>
/// Tests for the <c>!faq</c> command as registered on <see cref="BanchoBot"/>.
/// </summary>
[TestFixture]
public class FaqCommandsTests
{
    private const int RoomChannelId = 400;

    private FakeChatRepository _chatRepository = null!;
    private PlayerManager _playerManager = null!;
    private ConfigManager _configManager = null!;
    private IServiceScopeFactory _scopeFactory = null!;
    private RecordingNotificationServer _notificationServer = null!;
    private BanchoBot _bot = null!;
    private string _basePath = null!;

    [SetUp]
    public void SetUp()
    {
        _basePath = Path.Combine(Path.GetTempPath(), "g0v0-faq-command-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_basePath, "config"));

        _chatRepository = new FakeChatRepository();
        _chatRepository.AddChannel(new ChatChannel
        {
            ChannelId = RoomChannelId,
            Name = "mp_1",
            Description = "Multiplayer room 1 chat",
            Type = ChannelType.Multiplayer,
        });

        _playerManager = new PlayerManager(
            NullLogger<PlayerManager>.Instance,
            new InterProcessCommunicationClient(new NoOpTransport(), "realtime-tests"));

        ServiceCollection services = new();
        services.AddSingleton<IChatRepository>(_chatRepository);
        services.AddSingleton<IUserRepository>(new Multiplayer.FakeUserRepository());
        services.AddSingleton<IRelationshipRepository>(new Multiplayer.FakeRelationshipRepository());
        services.AddSingleton<INotificationRepository>(new FakeNotificationRepository());
        ServiceProvider serviceProvider = services.BuildServiceProvider();
        _scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        _configManager = new ConfigManager(_basePath);
        _notificationServer = new RecordingNotificationServer(
            new FakeRankedCache(),
            _playerManager,
            _configManager,
            _scopeFactory);

        _bot = new BanchoBot(
            NullLogger<BanchoBot>.Instance,
            _configManager,
            _scopeFactory,
            _notificationServer,
            _playerManager);
        _bot.Initialize(
        [
            new UtilityCommands(_scopeFactory),
            new FaqCommands(_scopeFactory, _configManager, NullLogger<FaqCommands>.Instance),
        ]);
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
    public async Task Faq_WithoutArguments_ShouldReplyWithUsageInTheSameChannel()
    {
        await _bot.TryHandleAsync(1, RoomChannelId, "!faq", ChannelType.Multiplayer);

        Assert.Multiple(() =>
        {
            Assert.That(_notificationServer.SentMessages, Has.Count.EqualTo(1));
            Assert.That(_notificationServer.SentMessages[0].Content, Is.EqualTo(FaqCatalog.UsageReply));
            Assert.That(_notificationServer.SentMessages[0].ChannelId, Is.EqualTo(RoomChannelId));
            Assert.That(_notificationServer.SentMessages[0].SenderId, Is.EqualTo(IBanchoBot.BanchoBotId));
        });
    }

    [Test]
    public async Task Faq_ShouldReplyWithTheConfiguredAnswer()
    {
        WriteFaqJson(
            """
            {
              "DefaultLanguage": "en",
              "Entries": [
                {
                  "Id": "how-can-i-help",
                  "Translations": { "en": "Use !help.", "zh": "使用 !help。" }
                }
              ]
            }
            """);

        await _bot.TryHandleAsync(1, RoomChannelId, "!faq zh:how-can-i-help", ChannelType.Multiplayer);

        Assert.That(_notificationServer.SentMessages, Has.Count.EqualTo(1));
        Assert.That(_notificationServer.SentMessages[0].Content, Is.EqualTo("使用 !help。"));
    }

    [Test]
    public async Task FaqList_ShouldReportTheConfiguredEntries()
    {
        WriteFaqJson(
            """
            {
              "Entries": [
                { "Id": "list-item-a", "Translations": { "en": "A" } },
                { "Id": "how-can-i-help", "Translations": { "en": "B" } }
              ]
            }
            """);

        await _bot.TryHandleAsync(1, RoomChannelId, "!faq list", ChannelType.Multiplayer);

        Assert.That(_notificationServer.SentMessages, Has.Count.EqualTo(1));
        Assert.That(
            _notificationServer.SentMessages[0].Content,
            Is.EqualTo("Available entries: list-item-a, how-can-i-help."));
    }

    [Test]
    public async Task Faq_WhenEntryIsMissing_ShouldReplyNotFound()
    {
        WriteFaqJson("""{"Entries": [{"Id": "known", "Translations": {"en": "A"}}]}""");

        await _bot.TryHandleAsync(1, RoomChannelId, "!faq unknown", ChannelType.Multiplayer);

        Assert.That(_notificationServer.SentMessages, Has.Count.EqualTo(1));
        Assert.That(_notificationServer.SentMessages[0].Content, Is.EqualTo(FaqCatalog.NotFoundReply));
    }

    [Test]
    public async Task FaqList_WithoutConfigurationFile_ShouldReportEmpty()
    {
        await _bot.TryHandleAsync(1, RoomChannelId, "!faq list", ChannelType.Multiplayer);

        Assert.That(_notificationServer.SentMessages, Has.Count.EqualTo(1));
        Assert.That(_notificationServer.SentMessages[0].Content, Is.EqualTo(FaqCatalog.EmptyReply));
    }

    [Test]
    public async Task Faq_WithMalformedConfiguration_ShouldReplyUnavailableInsteadOfFailing()
    {
        WriteFaqJson("""{"Entries": [}""");

        Assert.That(
            async () => await _bot.TryHandleAsync(1, RoomChannelId, "!faq list", ChannelType.Multiplayer),
            Throws.Nothing);

        Assert.That(_notificationServer.SentMessages, Has.Count.EqualTo(1));
        Assert.That(_notificationServer.SentMessages[0].Content, Is.EqualTo(FaqCatalog.UnavailableReply));
    }

    [Test]
    public async Task Faq_WithLongAnswer_ShouldBeSplitIntoMessagesThatFitTheLimit()
    {
        string answer = string.Join(
            ' ',
            Enumerable.Range(0, 300).Select(static index => $"tip {index}"));

        WriteFaqJson(
            $$"""
            {
              "Entries": [
                { "Id": "long-answer", "Translations": { "en": "{{answer}}" } }
              ]
            }
            """);

        await _bot.TryHandleAsync(1, RoomChannelId, "!faq long-answer", ChannelType.Multiplayer);

        Assert.Multiple(() =>
        {
            Assert.That(_notificationServer.SentMessages, Has.Count.GreaterThan(1));
            Assert.That(
                _notificationServer.SentMessages.Select(message => message.Content.Length),
                Is.All.LessThanOrEqualTo(ChatConfig.DefaultMessageLengthLimit));
            Assert.That(
                string.Join(' ', _notificationServer.SentMessages.Select(message => message.Content)),
                Is.EqualTo(answer));
        });
    }

    [Test]
    public async Task Faq_FromPublicChannel_ShouldReplyThroughTheBotPmChannel()
    {
        WriteFaqJson("""{"Entries": [{"Id": "known", "Translations": {"en": "A"}}]}""");

        await _bot.TryHandleAsync(1, 100, "!faq known", ChannelType.Public);

        Assert.Multiple(() =>
        {
            Assert.That(_notificationServer.SentMessages, Has.Count.EqualTo(1));
            Assert.That(_notificationServer.SentMessages[0].SenderId, Is.EqualTo(IBanchoBot.BanchoBotId));
            Assert.That(
                _chatRepository.Channels.Any(channel =>
                    string.Equals(channel.Name, $"pm_1_{IBanchoBot.BanchoBotId}", StringComparison.Ordinal)),
                Is.True);
        });
    }

    [Test]
    public async Task Help_ShouldListTheFaqCommand()
    {
        await _bot.TryHandleAsync(1, RoomChannelId, "!help", ChannelType.Multiplayer);

        Assert.That(_notificationServer.SentMessages, Has.Count.EqualTo(1));
        Assert.That(_notificationServer.SentMessages[0].Content, Does.Contain("!faq"));
    }

    private void WriteFaqJson(string json)
    {
        File.WriteAllText(Path.Combine(_basePath, "config", "faq.json"), json);
    }

    private sealed class RecordingNotificationServer(
        IRankedCache rankedCache,
        PlayerManager playerManager,
        ConfigManager configManager,
        IServiceScopeFactory scopeFactory) : NotificationServer(
            NullLogger<NotificationServer>.Instance,
            rankedCache,
            configManager,
            playerManager,
            scopeFactory)
    {
        public List<ChatMessage> SentMessages { get; } = new();

        public override async Task<ChatMessage> SendMessage(int channelId, int senderId, string content, bool isAction, string? uuid = null)
        {
            ChatMessage message = await base.SendMessage(channelId, senderId, content, isAction, uuid);
            SentMessages.Add(message);
            return message;
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