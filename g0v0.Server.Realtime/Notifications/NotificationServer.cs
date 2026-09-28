// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using g0v0.Server.Common.Caching;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Chat;
using g0v0.Server.Realtime.Objects.Notifications;
using g0v0.Server.Realtime.Objects.Players;
using osu.Game.Online.Chat;

namespace g0v0.Server.Realtime.Notifications;

/// <summary>
/// Tracks real-time chat connections, persists messages and memberships, and
/// broadcasts chat/notification events to the connected <see cref="IPlayer"/> instances.
/// </summary>
/// <param name="logger">The logger.</param>
/// <param name="rankedCache">The sorted-set cache holding channel activity markers and message throttles.</param>
/// <param name="configManager">The configuration manager used to read the chat limits.</param>
/// <param name="playerManager">The player registry.</param>
/// <param name="scopeFactory">The scope factory used to resolve repositories.</param>
public class NotificationServer(
    ILogger<NotificationServer> logger,
    IRankedCache rankedCache,
    ConfigManager configManager,
    PlayerManager playerManager,
    IServiceScopeFactory scopeFactory) : INotificationServer
{
    /// <summary>
    /// Serializer options for the persisted <c>details</c> JSON payload; the
    /// details types carry explicit snake_case names.
    /// </summary>
    private static readonly JsonSerializerOptions DetailsSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<int, Dictionary<IPlayer, int>> _connections = new();

    /// <inheritdoc />
    public Task Connect(IPlayer player)
    {
        Dictionary<IPlayer, int> userConnections = _connections.GetOrAdd(player.PlayerId, static _ => []);
        lock (userConnections)
        {
            userConnections.TryGetValue(player, out int connectionCount);
            userConnections[player] = connectionCount + 1;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task Disconnect(IPlayer player, bool kicked = false)
    {
        // Memberships and activity markers outlive the connection: osu-web keeps
        // the user_channels rows and lets the public channel markers expire.
        _ = TryDecrementConnection(player);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task JoinChannel(IPlayer player, int channelId)
    {
        IChatRepository chatRepository = CreateScope(out IServiceScope scope);
        using (scope)
        {
            ChatChannel? channel = await chatRepository.GetChannelAsync(channelId);
            bool joined = await chatRepository.AddUserChannelAsync(channelId, player.PlayerId);

            // osu-web's Channel::addUser acknowledges public channels.
            if (channel?.Type == ChannelType.Public)
            {
                await AcknowledgeChannelAsync(channelId, player.PlayerId);
            }

            if (joined)
            {
                await DeliverChannelJoinAsync(player, channelId);
            }
        }
    }

    /// <inheritdoc />
    public async Task LeaveChannel(IPlayer player, int channelId)
    {
        IChatRepository chatRepository = CreateScope(out IServiceScope scope);
        using (scope)
        {
            ChatChannel? channel = await chatRepository.GetChannelAsync(channelId);
            bool parted = await chatRepository.RemoveUserChannelAsync(
                channelId,
                player.PlayerId,
                channel != null && ChatConventions.IsHideable(channel.Type));

            if (parted)
            {
                await DeliverChannelLeaveAsync(player, channelId);
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> UserInChannel(int channelId, int userId)
    {
        IChatRepository chatRepository = CreateScope(out IServiceScope scope);
        using (scope)
        {
            return await chatRepository.GetUserChannelAsync(channelId, userId) != null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> GetUserJoinedChannels(int userId)
    {
        IChatRepository chatRepository = CreateScope(out IServiceScope scope);
        using (scope)
        {
            IReadOnlyList<ChatUserChannel> membership = await chatRepository.GetUserChannelsAsync(userId);
            return membership.Select(static userChannel => userChannel.ChannelId).ToList();
        }
    }

    /// <inheritdoc />
    public virtual async Task<ChatMessage> SendMessage(int channelId, int senderId, string content, bool isAction, string? uuid = null)
    {
        ChatConfig chatConfig = GetChatConfig();
        IChatRepository chatRepository = CreateScope(out IServiceScope scope);
        using (scope)
        {
            ChatChannel? channel = await chatRepository.GetChannelAsync(channelId);
            bool isAnnouncement = channel?.Type == ChannelType.Announce;

            string normalized = isAnnouncement
                ? content
                : content.Replace('\r', ' ').Replace('\n', ' ').Trim();

            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ChatMessageRejectedException(ChatMessageRejectedException.RejectionReason.Empty, "Cannot send blank message.");
            }

            int maxLength = isAnnouncement ? chatConfig.AnnounceMessageLengthLimit : chatConfig.MessageLengthLimit;
            if (normalized.EnumerateRunes().Count() > maxLength)
            {
                throw new ChatMessageRejectedException(ChatMessageRejectedException.RejectionReason.TooLong, "The message you are trying to send is too long.");
            }

            await ThrottleAsync(senderId, channel?.Type == ChannelType.PM);

            ChatMessage message = await chatRepository.CreateMessageAsync(
                channelId,
                senderId,
                normalized,
                isAction ? MessageType.Action : MessageType.Plain,
                uuid);

            await chatRepository.UpdateLastMessageIdAsync(channelId, message.MessageId);
            await chatRepository.MarkUserChannelAsReadAsync(channelId, senderId, message.MessageId);
            await chatRepository.UnhideChannelAsync(channelId);

            await BroadcastMessageAsync(channel, message);
            await DispatchNotificationAsync(channel, message);
            return message;
        }
    }

    /// <inheritdoc />
    public async Task<ChatChannel?> GetOrCreateChannel(string channel)
    {
        IChatRepository chatRepository = CreateScope(out IServiceScope scope);
        using (scope)
        {
            return await chatRepository.GetChannelAsync(channel);
        }
    }

    /// <inheritdoc />
    public async Task MarkAsRead(IPlayer player, int channelId, int messageId)
    {
        IChatRepository chatRepository = CreateScope(out IServiceScope scope);
        using (scope)
        {
            await chatRepository.MarkUserChannelAsReadAsync(channelId, player.PlayerId, messageId);
        }
    }

    /// <inheritdoc />
    public async Task Ack(IPlayer player)
    {
        IChatRepository chatRepository = CreateScope(out IServiceScope scope);
        using (scope)
        {
            IReadOnlyList<ChatUserChannel> memberships = await chatRepository.GetUserChannelsAsync(
                player.PlayerId,
                includeHidden: true);

            if (memberships.Count == 0)
            {
                return;
            }

            List<int> channelIds = memberships.Select(static membership => membership.ChannelId).ToList();
            Dictionary<int, ChatChannel> channels = (await chatRepository.GetChannelsAsync(channelIds))
                .ToDictionary(static channel => channel.ChannelId);

            foreach (int channelId in channelIds)
            {
                if (channels.TryGetValue(channelId, out ChatChannel? channel) && channel.Type == ChannelType.Public)
                {
                    await AcknowledgeChannelAsync(channelId, player.PlayerId);
                }
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The source user is removed from the receivers (unless the notification
    /// overrides that), bot users and users blocked by the source are excluded,
    /// the notification and its per-user delivery records are persisted, and the
    /// <c>new</c> event is pushed to every connected receiver. When no receiver
    /// remains, nothing is persisted and nothing is pushed.
    /// </remarks>
    public async Task SendNotification(NotificationDetail detail)
    {
        IEnumerable<int> receivers = detail.ReceiverIds.Distinct();
        if (detail.ExcludeSourceFromReceivers)
        {
            receivers = receivers.Where(receiverId => receiverId != detail.SourceUserId);
        }

        List<int> receiverIds = receivers.ToList();
        if (receiverIds.Count == 0)
        {
            return;
        }

        IServiceScope scope = scopeFactory.CreateScope();
        using (scope)
        {
            IUserRepository userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            IRelationshipRepository relationshipRepository = scope.ServiceProvider.GetRequiredService<IRelationshipRepository>();
            INotificationRepository notificationRepository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

            // osu-web's excludeBotUserIds: delivery records are not created for bots.
            Dictionary<int, User> users = (await userRepository.GetByIdsAsync(receiverIds))
                .ToDictionary(user => user.Id);
            receiverIds = receiverIds
                .Where(receiverId => !users.TryGetValue(receiverId, out User? user) || !user.IsBot)
                .ToList();

            if (receiverIds.Count > 0)
            {
                IReadOnlyList<int> blockedUserIds = await relationshipRepository.GetBlockedUserIdsAsync(detail.SourceUserId);
                receiverIds = receiverIds.Except(blockedUserIds).ToList();
            }

            if (receiverIds.Count == 0)
            {
                return;
            }

            Notification notification = new()
            {
                Name = NotificationCategories.ToDatabaseName(detail.Name),
                Category = NotificationCategories.CategoryOf(detail.Name),
                CreatedAt = DateTimeOffset.UtcNow,
                ObjectType = detail.ObjectType,
                ObjectId = detail.ObjectId,
                SourceUserId = detail.SourceUserId,
                Details = JsonSerializer.Serialize(detail.Details, DetailsSerializerOptions),
            };

            notification = await notificationRepository.CreateAsync(notification, receiverIds);

            detail.Id = notification.Id;
            detail.CreatedAt = notification.CreatedAt;
            detail.IsRead = false;
        }

        foreach (int receiverId in receiverIds)
        {
            foreach (IPlayer receiver in GetConnectedPlayer(receiverId))
            {
                try
                {
                    await receiver.OnNewNotification(receiver, detail);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Failed to deliver notification to user {UserId}.",
                        receiverId);
                }
            }
        }
    }

    /// <summary>
    /// Creates and dispatches the notifications of a sent message: user commands
    /// are skipped, PM/team/announcement channels notify their users, and public
    /// channels only notify mentioned users.
    /// </summary>
    /// <param name="channel">The channel the message was posted in.</param>
    /// <param name="message">The persisted message.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task DispatchNotificationAsync(ChatChannel? channel, ChatMessage message)
    {
        if (channel == null || ChatConventions.IsUserCommand(message.Content))
        {
            return;
        }

        NotificationDetail? detail = await CreateMessageNotificationAsync(channel, message);
        if (detail == null)
        {
            return;
        }

        await SendNotification(detail);
    }

    /// <summary>
    /// Builds the notification detail of a sent message, or
    /// <see langword="null"/> when the message must not notify anyone.
    /// </summary>
    /// <param name="channel">The channel the message was posted in.</param>
    /// <param name="message">The persisted message.</param>
    /// <returns>The notification detail, or <see langword="null"/>.</returns>
    private async Task<NotificationDetail?> CreateMessageNotificationAsync(ChatChannel channel, ChatMessage message)
    {
        switch (channel.Type)
        {
            case ChannelType.PM when ChatConventions.TryGetPmUserIds(channel.Name, out int[] pmUserIds):
                return await FillNotificationAsync(
                    new ChannelMessageNotification(message.SenderId, pmUserIds),
                    channel,
                    message,
                    ChatConventions.MessageTitleTruncateLimit);

            case ChannelType.Team or ChannelType.Announce:
                IServiceScope scope = scopeFactory.CreateScope();
                using (scope)
                {
                    IChatRepository chatRepository = scope.ServiceProvider.GetRequiredService<IChatRepository>();
                    IReadOnlyList<int> memberIds = await chatRepository.GetChannelUserIdsAsync(message.ChannelId);

                    return channel.Type == ChannelType.Team
                        ? await FillNotificationAsync(
                            new ChannelTeamNotification(message.SenderId, memberIds),
                            channel,
                            message,
                            ChatConventions.MessageTitleTruncateLimit)
                        : await FillNotificationAsync(
                            new ChannelAnnouncementNotification(message.SenderId, memberIds),
                            channel,
                            message,
                            ChatConventions.MessageTitleTruncateLimit);
                }

            case ChannelType.Public when message.Content.Contains('@'):
                return await CreateMentionNotificationAsync(channel, message);

            default:
                return null;
        }
    }

    /// <summary>
    /// Builds the mention notification of a public channel message, or
    /// <see langword="null"/> when nothing mentions a known user.
    /// </summary>
    /// <param name="channel">The public channel the message was posted in.</param>
    /// <param name="message">The persisted message.</param>
    /// <returns>The mention notification, or <see langword="null"/>.</returns>
    private async Task<ChannelMentionNotification?> CreateMentionNotificationAsync(ChatChannel channel, ChatMessage message)
    {
        IReadOnlyList<string> usernames = NotificationCategories.MentionUsernames(message.Content);
        if (usernames.Count == 0)
        {
            return null;
        }

        IServiceScope scope = scopeFactory.CreateScope();
        using (scope)
        {
            IUserRepository userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

            List<int> mentionedUserIds = [];
            foreach (string username in usernames)
            {
                User? mentionedUser = await userRepository.GetByUsernameAsync(username);
                if (mentionedUser != null && !mentionedUserIds.Contains(mentionedUser.Id))
                {
                    mentionedUserIds.Add(mentionedUser.Id);
                }
            }

            if (mentionedUserIds.Count == 0)
            {
                return null;
            }

            ChannelMentionNotification detail = await FillNotificationAsync(
                new ChannelMentionNotification(message.SenderId, mentionedUserIds),
                channel,
                message,
                ChatConventions.MentionTitleTruncateLimit);
            detail.MessageId = message.MessageId;
            return detail;
        }
    }

    /// <summary>
    /// Fills the shared fields of a channel message notification: the truncated
    /// message content, the lower-cased channel type, the sender's avatar and
    /// username, and the channel name.
    /// </summary>
    /// <param name="detail">The notification to fill.</param>
    /// <param name="channel">The channel the message was posted in.</param>
    /// <param name="message">The persisted message.</param>
    /// <param name="titleLimit">The title truncation limit; <c>null</c> uses the mention limit of 200 or the default of 36.</param>
    /// <typeparam name="TDetail">The concrete notification type.</typeparam>
    /// <returns>The filled notification.</returns>
    private async Task<TDetail> FillNotificationAsync<TDetail>(TDetail detail, ChatChannel channel, ChatMessage message, int titleLimit)
        where TDetail : ChannelNotificationBase
    {
        IServiceScope scope = scopeFactory.CreateScope();
        using (scope)
        {
            IUserRepository userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            User? sender = await userRepository.GetByIdAsync(message.SenderId);

            detail.ChannelId = channel.ChannelId;
            detail.Type = ChatConventions.GetChannelTypeName(channel.Type).ToLowerInvariant();
            detail.Title = ChatConventions.Truncate(message.Content, titleLimit);
            detail.CoverUrl = sender?.AvatarUrl ?? string.Empty;
            detail.Username = sender?.Username ?? string.Empty;
            detail.ChannelName = channel.Name;
            return detail;
        }
    }

    private IChatRepository CreateScope(out IServiceScope scope)
    {
        scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IChatRepository>();
    }

    private ChatConfig GetChatConfig() =>
        configManager.TryGet<ChatConfig>() ?? new ChatConfig();

    private async Task ThrottleAsync(int userId, bool isPrivate)
    {
        ChatConfig chatConfig = GetChatConfig();
        int limit = isPrivate ? chatConfig.PrivateMessageLimit : chatConfig.PublicMessageLimit;
        int window = isPrivate ? chatConfig.PrivateMessageWindowSeconds : chatConfig.PublicMessageWindowSeconds;
        double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;

        int sent = await rankedCache.CountWindowAndAddAsync(
            ChatConventions.ThrottleKey(userId, isPrivate),
            Guid.NewGuid().ToString(),
            now,
            now - window,
            TimeSpan.FromSeconds(window));

        if (sent >= limit)
        {
            throw new ChatMessageRejectedException(
                ChatMessageRejectedException.RejectionReason.Throttled,
                "You are sending messages too quickly, please wait a bit before trying again.");
        }
    }

    private Task AcknowledgeChannelAsync(int channelId, int userId)
    {
        ChatConfig chatConfig = GetChatConfig();
        double score = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
        return rankedCache.SetScoreAsync(
            ChatConventions.AckKey(channelId),
            userId.ToString(CultureInfo.InvariantCulture),
            score,
            chatConfig.ActivityMarkerExpiry);
    }

    private async Task DeliverChannelJoinAsync(IPlayer player, int channelId)
    {
        foreach (IPlayer receiver in GetConnectedPlayer(player.PlayerId))
        {
            try
            {
                await receiver.OnChannelJoin(player, channelId);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to deliver channel-join event to user {UserId} in channel {ChannelId}.",
                    receiver.PlayerId,
                    channelId);
            }
        }
    }

    private async Task DeliverChannelLeaveAsync(IPlayer player, int channelId)
    {
        foreach (IPlayer receiver in GetConnectedPlayer(player.PlayerId))
        {
            try
            {
                await receiver.OnChannelLeave(player, channelId);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to deliver channel-leave event to user {UserId} in channel {ChannelId}.",
                    receiver.PlayerId,
                    channelId);
            }
        }
    }

    private IPlayer[] GetConnectedPlayer(int userId)
    {
        if (!_connections.TryGetValue(userId, out Dictionary<IPlayer, int>? userConnections))
        {
            return [];
        }

        lock (userConnections)
        {
            return userConnections.Keys.ToArray();
        }
    }

    private IPlayer? GetBroadcastSource(int userId)
    {
        IPlayer[] connectedPlayers = GetConnectedPlayer(userId);
        return connectedPlayers.Length > 0 ? connectedPlayers[0] : playerManager.GetPlayer<IPlayer>(userId);
    }

    private bool TryDecrementConnection(IPlayer player)
    {
        if (!_connections.TryGetValue(player.PlayerId, out Dictionary<IPlayer, int>? userConnections))
        {
            return false;
        }

        bool hasRemainingConnections;
        lock (userConnections)
        {
            if (userConnections.TryGetValue(player, out int connectionCount) && connectionCount > 1)
            {
                userConnections[player] = connectionCount - 1;
                return true;
            }

            userConnections.Remove(player);
            hasRemainingConnections = userConnections.Count > 0;
        }

        if (!hasRemainingConnections)
        {
            _connections.TryRemove(player.PlayerId, out _);
        }

        return hasRemainingConnections;
    }

    /// <summary>
    /// Resolves the recipients of a message.
    /// </summary>
    /// <param name="channel">The channel the message was posted in.</param>
    /// <param name="message">The message.</param>
    /// <returns>The user IDs the message is delivered to.</returns>
    private async Task<int[]> GetMessageReceiverIdsAsync(ChatChannel? channel, ChatMessage message)
    {
        ChatConfig chatConfig = GetChatConfig();
        if (ChatConventions.IsUserCommand(message.Content))
        {
            return [message.SenderId];
        }

        switch (channel?.Type)
        {
            case ChannelType.Public:
                {
                    double minScore = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d) - chatConfig.ActivityTimeoutSeconds;
                    IReadOnlyList<string> activeUserIds = await rankedCache.GetByScoreAsync(ChatConventions.AckKey(channel.ChannelId), minScore);

                    return activeUserIds
                        .Select(static value => int.TryParse(value, CultureInfo.InvariantCulture, out int userId) ? userId : 0)
                        .Where(static userId => userId != 0)
                        .ToArray();
                }

            case ChannelType.PM when ChatConventions.TryGetPmUserIds(channel.Name, out int[] pmUserIds):
                return pmUserIds;
        }

        IChatRepository chatRepository = CreateScope(out IServiceScope scope);
        using (scope)
        {
            IReadOnlyList<int> memberIds = await chatRepository.GetChannelUserIdsAsync(message.ChannelId);
            return memberIds.ToArray();
        }
    }

    private async Task BroadcastMessageAsync(ChatChannel? channel, ChatMessage message)
    {
        IPlayer? sender = GetBroadcastSource(message.SenderId);
        int[] receiverIds = await GetMessageReceiverIdsAsync(channel, message);

        foreach (IPlayer member in receiverIds.SelectMany(GetConnectedPlayer))
        {
            try
            {
                await member.OnNewMessage(sender ?? member, message);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to deliver chat message {MessageId} to user {UserId} in channel {ChannelId}.",
                    message.MessageId,
                    member.PlayerId,
                    message.ChannelId);
            }
        }
    }
}