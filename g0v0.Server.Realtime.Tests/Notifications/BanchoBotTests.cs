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

[TestFixture]
public class BanchoBotTests
{
    private FakeChatRepository _chatRepository = null!;
    private PlayerManager _playerManager = null!;
    private ConfigManager _configManager = null!;
    private RecordingNotificationServer _notificationServer = null!;
    private BanchoBot _bot = null!;

    [SetUp]
    public void SetUp()
    {
        _chatRepository = new FakeChatRepository();
        _playerManager = new PlayerManager(
            NullLogger<PlayerManager>.Instance,
            new InterProcessCommunicationClient(new NoOpTransport(), "realtime-tests"));
        ServiceCollection services = new();
        services.AddSingleton<IChatRepository>(_chatRepository);
        services.AddSingleton<IUserRepository>(new FakeUserRepository());
        services.AddSingleton<IRelationshipRepository>(new Multiplayer.FakeRelationshipRepository());
        services.AddSingleton<INotificationRepository>(new FakeNotificationRepository());
        services.AddSingleton<IScoreRepository>(new FakeScoreRepository());
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        string basePath = Path.Combine(Path.GetTempPath(), "g0v0-bancho-bot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(basePath);
        _configManager = new ConfigManager(basePath);

        _notificationServer = new RecordingNotificationServer(
            new FakeRankedCache(),
            _playerManager,
            _configManager,
            serviceProvider.GetRequiredService<IServiceScopeFactory>());

        _bot = new BanchoBot(
            NullLogger<BanchoBot>.Instance,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _notificationServer,
            _playerManager);
        _bot.Initialize(
        [
            new UtilityCommands(serviceProvider.GetRequiredService<IServiceScopeFactory>()),
            new ScoreCommands(serviceProvider.GetRequiredService<IServiceScopeFactory>()),
        ]);
    }

    [Test]
    public async Task Help_ShouldListCommands()
    {
        await _bot.TryHandleAsync(1, 100, "!help", ChannelType.Public);

        Assert.That(_notificationServer.SentMessages, Has.Count.EqualTo(1));
        Assert.That(_notificationServer.SentMessages[0].Content, Does.Contain("!roll"));
        Assert.That(_notificationServer.SentMessages[0].Content, Does.Contain("!re"));
        Assert.That(_notificationServer.SentMessages[0].Content, Does.Contain("!pr"));
        Assert.That(_notificationServer.SentMessages[0].Content, Does.Not.Contain("!stats"));
    }

    [Test]
    public async Task Roll_ShouldReturnRandomNumber()
    {
        await _bot.TryHandleAsync(1, 100, "!roll 10", ChannelType.Public);

        Assert.That(_notificationServer.SentMessages, Has.Count.EqualTo(1));
        Assert.That(_notificationServer.SentMessages[0].Content, Does.Match(@"^user1 rolls [1-9][0-9]* point\(s\)$"));
    }

    [Test]
    public async Task UnknownCommand_ShouldBeIgnored()
    {
        await _bot.TryHandleAsync(1, 100, "!doesnotexist", ChannelType.Public);

        Assert.That(_notificationServer.SentMessages, Is.Empty);
    }

    [Test]
    public async Task NonCommand_ShouldBeIgnored()
    {
        await _bot.TryHandleAsync(1, 100, "just a normal message", ChannelType.Public);

        Assert.That(_notificationServer.SentMessages, Is.Empty);
    }

    [Test]
    public async Task PublicChannelReply_ShouldCreatePmChannel()
    {
        await _bot.TryHandleAsync(1, 100, "!roll 10", ChannelType.Public);

        Assert.That(_notificationServer.SentMessages, Has.Count.EqualTo(1));
        Assert.That(_notificationServer.SentMessages[0].SenderId, Is.EqualTo(IBanchoBot.BanchoBotId));
        Assert.That(_chatRepository.Channels.Any(channel => string.Equals(channel.Name, $"pm_1_{IBanchoBot.BanchoBotId}", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public async Task PrivateChannelReply_ShouldReplyInSameChannel()
    {
        _chatRepository.AddChannel(new ChatChannel
        {
            ChannelId = 200,
            Name = "somechannel",
            Description = "A private channel",
            Type = ChannelType.Private,
        });

        await _bot.TryHandleAsync(1, 200, "!roll 10", ChannelType.Private);

        Assert.That(_notificationServer.SentMessages, Has.Count.EqualTo(1));
        Assert.That(_notificationServer.SentMessages[0].ChannelId, Is.EqualTo(200));
        Assert.That(_notificationServer.SentMessages[0].SenderId, Is.EqualTo(IBanchoBot.BanchoBotId));
    }

    [Test]
    public async Task AnnounceCommand_ShouldReplyInSameChannel()
    {
        _chatRepository.AddChannel(new ChatChannel
        {
            ChannelId = 300,
            Name = "announce",
            Description = "Official announcements",
            Type = ChannelType.Announce,
        });

        // Only public-channel commands are replied via PM; every other channel
        // type receives the bot reply in the same channel.
        await _bot.TryHandleAsync(1, 300, "!roll 10", ChannelType.Announce);

        Assert.That(_notificationServer.SentMessages, Has.Count.EqualTo(1));
        Assert.That(_notificationServer.SentMessages[0].ChannelId, Is.EqualTo(300));
        Assert.That(_notificationServer.SentMessages[0].SenderId, Is.EqualTo(IBanchoBot.BanchoBotId));
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public Task<Common.Database.Models.User?> GetByIdAsync(int userId)
            => Task.FromResult<Common.Database.Models.User?>(new Common.Database.Models.User { Id = userId, Username = $"user{userId}" });

        public Task<IReadOnlyList<Common.Database.Models.User>> GetByIdsAsync(IReadOnlyList<int> userIds)
            => Task.FromResult<IReadOnlyList<Common.Database.Models.User>>(
                userIds.Select(userId => new Common.Database.Models.User { Id = userId, Username = $"user{userId}" }).ToList());

        public Task<string?> GetUsernameByIdAsync(int userId)
            => Task.FromResult<string?>($"user{userId}");

        public Task<Common.Database.Models.User?> GetByUsernameAsync(string username)
            => Task.FromResult<Common.Database.Models.User?>(new Common.Database.Models.User { Id = 1, Username = username });

        public Task<bool> UsernameExistsAsync(string username) => Task.FromResult(true);

        public Task<Common.Database.Models.User?> GetByEmailAsync(string email) => Task.FromResult<Common.Database.Models.User?>(null);

        public Task CreateAsync(Common.Database.Models.User user) => Task.CompletedTask;

        public Task CreateAsync(Common.Database.Models.User user, IReadOnlyList<int> statisticsModes, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateAsync(Common.Database.Models.User user) => Task.CompletedTask;
    }

    private sealed class FakeScoreRepository : IScoreRepository
    {
        public Task<IReadOnlyList<g0v0.Server.Common.Database.Models.Score>> GetUnprocessedScoresAsync(long afterId, int limit, string? type = null) => throw new NotSupportedException();

        public Task CompleteProcessingAsync(g0v0.Server.Common.Database.Models.Score score, int playTime = 0) => throw new NotSupportedException();

        public Task<Score?> GetByIdAsync(long scoreId) => Task.FromResult<Score?>(null);

        public Task<IReadOnlyList<Score>> GetPassingScoresForRoomAsync(long roomId)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Score>> GetByBeatmapIdAsync(int beatmapId) => Task.FromResult<IReadOnlyList<Score>>([]);

        public Task<IReadOnlyList<Score>> GetByUserIdAsync(int userId) => Task.FromResult<IReadOnlyList<Score>>([]);

        public Task<IReadOnlyList<Score>> GetRecentByUserIdAndModeAsync(int userId, int mode, int limit)
            => Task.FromResult<IReadOnlyList<Score>>(new List<Score>
            {
                new()
                {
                    Id = 1,
                    UserId = userId,
                    TotalScore = 1000,
                    Accuracy = 0.95,
                    Pp = 123.45,
                    Passed = true,
                    EndedAt = DateTimeOffset.UtcNow,
                },
            });

        public Task<IReadOnlyList<Score>> GetBestByUserIdAndModeAsync(int userId, int mode, int limit)
            => Task.FromResult<IReadOnlyList<Score>>([]);

        public Task<IReadOnlyList<Score>> GetBeatmapLeaderboardAsync(
            int beatmapId,
            int mode,
            int limit,
            bool legacyOnly = false,
            IReadOnlyCollection<int>? userIds = null,
            string? countryCode = null,
            IReadOnlyCollection<string>? modAcronyms = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> CountBeatmapLeaderboardAsync(
            int beatmapId,
            int mode,
            bool legacyOnly = false,
            IReadOnlyCollection<int>? userIds = null,
            string? countryCode = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Score?> GetBestBeatmapScoreAsync(
            int beatmapId,
            int mode,
            int userId,
            bool legacyOnly = false,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> GetBeatmapLeaderboardRankAsync(
            int beatmapId,
            int mode,
            int userId,
            bool legacyOnly = false,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Score?> GetScoreByToken(long scoreToken) => Task.FromResult<Score?>(null);

        public Task CreateAsync(Score score) => Task.CompletedTask;

        public Task UpdateAsync(Score score) => Task.CompletedTask;

        public Task DeleteAsync(Score score) => Task.CompletedTask;

        public Task MarkScoreHasReplay(long scoreId) => Task.CompletedTask;
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