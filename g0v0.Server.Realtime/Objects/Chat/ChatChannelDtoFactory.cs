// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Services;
using osu.Game.Online.Chat;

namespace g0v0.Server.Realtime.Objects.Chat;

/// <summary>
/// Builds chat channel DTOs for the chat REST and WebSocket endpoints.
/// </summary>
/// <remarks>
/// Mirrors osu-web's <c>ChannelTransformer</c> and <c>UserChannelList</c>: the
/// channel fields, the requesting user's attributes and read marker, the
/// participants of PM channels, and - when requested - the newest messages of
/// the channel filtered like <c>Message::filter</c>. Blocking and
/// friends-only rules follow <c>Channel::isVisibleFor</c> and
/// <c>OsuAuthorize::checkChatPmStart</c>.
/// </remarks>
/// <param name="chatRepository">The chat persistence repository.</param>
/// <param name="userRepository">The user repository used for display names, message senders and PM checks.</param>
/// <param name="relationshipRepository">The relationship repository backing the block and friend checks.</param>
/// <param name="configManager">The configuration manager used to read the chat limits.</param>
public sealed class ChatChannelDtoFactory(
    IChatRepository chatRepository,
    IUserRepository userRepository,
    IRelationshipRepository relationshipRepository,
    ConfigManager configManager)
{
    private const string ModeratedMessage = "This channel is currently moderated.";

    private const string NoAccessMessage = "You do not have access to that channel.";

    private const string NoAnnounceMessage = "You do not have permission to post announcement.";

    private const string DefaultChannelName = "osu!";

    /// <summary>
    /// Builds the DTO of a single channel for the requesting user.
    /// </summary>
    /// <param name="channel">The database channel.</param>
    /// <param name="requestingUserId">The user the response is built for.</param>
    /// <param name="membership">The membership row of the user, when known.</param>
    /// <param name="includeRecentMessages">Whether to attach the recent messages of the channel.</param>
    /// <returns>The channel DTO.</returns>
    public async Task<ChatChannelDto> CreateChannelAsync(
        ChatChannel channel,
        int requestingUserId,
        ChatUserChannel? membership = null,
        bool includeRecentMessages = false)
    {
        ChatConfig chatConfig = GetChatConfig();
        IReadOnlyList<ChatMessage> messages = includeRecentMessages
            ? await chatRepository.GetMessagesAsync(channel.ChannelId, chatConfig.RecentMessageLimit)
            : [];

        ChatRelationships relationships = await LoadRelationshipsAsync(requestingUserId);
        Dictionary<int, User> users = await LoadUsersAsync(GetReferencedUserIds(channel, messages, requestingUserId));

        return CreateChannel(channel, requestingUserId, membership, messages, users, relationships, includeRecentMessages);
    }

    /// <summary>
    /// Lists every joinable public channel, mirroring osu-web's
    /// <c>ChannelsController::index</c>: the plain channel transform without any
    /// include, ordered by channel ID.
    /// </summary>
    /// <param name="requestingUserId">The user the response is built for.</param>
    /// <returns>The public channels.</returns>
    public async Task<IReadOnlyList<ChatChannelDto>> CreatePublicChannelListAsync(int requestingUserId)
    {
        IReadOnlyList<ChatChannel> channels = await chatRepository.GetPublicChannelsAsync();

        return channels
            .Select(channel => ChatChannelDto.FromDb(channel, requestingUserId))
            .ToList();
    }

    /// <summary>
    /// Lists every channel the user takes part in, mirroring osu-web's
    /// <c>UserChannelList</c>: the visible membership rows ordered by channel ID,
    /// with hidden channels and channels of blocked users left out.
    /// </summary>
    /// <param name="userId">The user the response is built for.</param>
    /// <param name="includeRecentMessages">Whether to attach the recent messages of each channel.</param>
    /// <returns>The channel DTOs.</returns>
    public async Task<IReadOnlyList<ChatChannelDto>> CreateChannelListAsync(int userId, bool includeRecentMessages = false)
    {
        ChatConfig chatConfig = GetChatConfig();
        await EnsureDefaultChannelJoinedAsync(userId);

        IReadOnlyList<ChatUserChannel> memberships = await chatRepository.GetUserChannelsAsync(
            userId,
            includeHidden: false,
            limit: chatConfig.ChannelLimit);

        if (memberships.Count == 0)
        {
            return [];
        }

        List<int> channelIds = memberships.Select(static membership => membership.ChannelId).ToList();
        Dictionary<int, ChatChannel> channels = (await chatRepository.GetChannelsAsync(channelIds))
            .ToDictionary(static channel => channel.ChannelId);

        // whereHas('channel'): channels that disappeared are skipped.
        List<(ChatChannel Channel, ChatUserChannel Membership)> entries = memberships
            .Where(membership => channels.ContainsKey(membership.ChannelId))
            .Select(membership => (Channel: channels[membership.ChannelId], Membership: membership))
            .ToList();

        if (entries.Count == 0)
        {
            return [];
        }

        ChatRelationships relationships = await LoadRelationshipsAsync(userId);
        Dictionary<int, User> users = await LoadUsersAsync(GetPmTargetIds(entries, userId));

        List<(ChatChannel Channel, ChatUserChannel Membership)> visible = entries
            .Where(entry => IsVisibleFor(entry.Channel, userId, users, relationships))
            .ToList();

        Dictionary<int, List<ChatMessage>> messagesByChannel = [];
        if (includeRecentMessages && visible.Count > 0)
        {
            List<int> visibleChannelIds = visible.Select(static entry => entry.Channel.ChannelId).ToList();
            IReadOnlyList<ChatMessage> messages = await chatRepository.GetRecentMessagesAsync(
                visibleChannelIds,
                chatConfig.RecentMessageLimit);

            foreach (ChatMessage message in messages)
            {
                if (!messagesByChannel.TryGetValue(message.ChannelId, out List<ChatMessage>? channelMessages))
                {
                    channelMessages = [];
                    messagesByChannel[message.ChannelId] = channelMessages;
                }

                channelMessages.Add(message);
            }

            users = await MergeUsersAsync(users, messages.Select(static message => message.SenderId));
        }

        List<ChatChannelDto> result = new(visible.Count);
        foreach ((ChatChannel channel, ChatUserChannel membership) in visible)
        {
            IReadOnlyList<ChatMessage> messages = messagesByChannel.GetValueOrDefault(channel.ChannelId, []);
            result.Add(CreateChannel(channel, userId, membership, messages, users, relationships, includeRecentMessages));
        }

        return result;
    }

    /// <summary>
    /// Gets the reason a user cannot post in a channel, mirroring osu-web's
    /// <c>ChatChannelCanMessage</c> check.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="userId">The user posting.</param>
    /// <returns><see langword="null"/> when posting is allowed; otherwise the error message.</returns>
    public async Task<string?> GetCanMessageErrorAsync(ChatChannel channel, int userId)
    {
        if (channel.Type != ChannelType.PM)
        {
            return GetCanMessageError(channel, userId, [], new ChatRelationships([], [], [], []));
        }

        List<int> userIds = [userId];
        int? targetId = GetPmTargetId(channel, userId);
        if (targetId.HasValue)
        {
            userIds.Add(targetId.Value);
        }

        Dictionary<int, User> users = await LoadUsersAsync(userIds);
        ChatRelationships relationships = await LoadRelationshipsAsync(userId);

        return GetCanMessageError(channel, userId, users, relationships);
    }

    private static int[] GetParticipantIds(ChatChannel channel)
    {
        return channel.Type == ChannelType.PM && ChatConventions.TryGetPmUserIds(channel.Name, out int[] userIds)
            ? userIds
            : [];
    }

    /// <summary>
    /// Gets the PM participant that is not the requesting user, mirroring
    /// osu-web's <c>Channel::pmTargetFor</c>.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="requestingUserId">The user the response is built for.</param>
    /// <returns>The target user ID, or <see langword="null"/> for non-PM channels.</returns>
    private static int? GetPmTargetId(ChatChannel channel, int requestingUserId)
    {
        return channel.Type == ChannelType.PM && ChatConventions.TryGetPmUserIds(channel.Name, out int[] userIds)
            ? userIds.Cast<int?>().FirstOrDefault(userId => userId != requestingUserId)
            : null;
    }

    private static List<int> GetPmTargetIds(IEnumerable<(ChatChannel Channel, ChatUserChannel Membership)> entries, int userId)
    {
        List<int> targetIds = [];
        foreach ((ChatChannel channel, _) in entries)
        {
            int? targetId = GetPmTargetId(channel, userId);
            if (targetId.HasValue)
            {
                targetIds.Add(targetId.Value);
            }
        }

        return targetIds;
    }

    private static UserCompactDto? CreateSender(int senderId, Dictionary<int, User> users)
    {
        return users.TryGetValue(senderId, out User? sender) ? UserCompactDto.FromDb(sender) : null;
    }

    private static List<int> GetReferencedUserIds(ChatChannel channel, IReadOnlyList<ChatMessage> messages, int requestingUserId)
    {
        List<int> userIds = [requestingUserId];
        int? targetId = GetPmTargetId(channel, requestingUserId);
        if (targetId.HasValue)
        {
            userIds.Add(targetId.Value);
        }

        userIds.AddRange(messages.Select(static message => message.SenderId));
        return userIds;
    }

    /// <summary>
    /// Determines whether a channel is listed for the user, mirroring
    /// osu-web's <c>Channel::isVisibleFor</c>: PM channels with a blocked
    /// (non-bot) target are hidden.
    /// </summary>
    private static bool IsVisibleFor(
        ChatChannel channel,
        int requestingUserId,
        Dictionary<int, User> users,
        ChatRelationships relationships)
    {
        if (channel.Type != ChannelType.PM)
        {
            return true;
        }

        int? targetId = GetPmTargetId(channel, requestingUserId);

        return targetId.HasValue
               && users.TryGetValue(targetId.Value, out User? target)
               && (!relationships.BlockedByMe(targetId.Value) || target.IsBot);
    }

    private static string? GetCanMessageError(
        ChatChannel channel,
        int requestingUserId,
        Dictionary<int, User> users,
        ChatRelationships relationships)
    {
        if (channel.Type == ChannelType.Announce)
        {
            // Posting announcements requires the ChatAnnounce privilege, which
            // this server cannot grant yet.
            return NoAnnounceMessage;
        }

        if (channel.Type != ChannelType.PM)
        {
            return channel.Moderated ? ModeratedMessage : null;
        }

        int? targetId = GetPmTargetId(channel, requestingUserId);

        return !targetId.HasValue
               || !users.TryGetValue(targetId.Value, out User? target)
               || !users.TryGetValue(requestingUserId, out User? requester)
            ? NoAccessMessage
            : ChatPmStartChecker.Evaluate(
                requester,
                target,
                relationships.Follows(targetId.Value),
                relationships.FollowedBy(targetId.Value),
                relationships.AnyBlock(targetId.Value));
    }

    private ChatChannelDto CreateChannel(
        ChatChannel channel,
        int requestingUserId,
        ChatUserChannel? membership,
        IReadOnlyList<ChatMessage> messages,
        Dictionary<int, User> users,
        ChatRelationships relationships,
        bool includeRecentMessages)
    {
        ChatConfig chatConfig = GetChatConfig();
        int[] participantIds = GetParticipantIds(channel);
        int? targetUserId = GetPmTargetId(channel, requestingUserId);
        bool isAnnouncement = channel.Type == ChannelType.Announce;

        string displayName = targetUserId.HasValue && users.TryGetValue(targetUserId.Value, out User? target)
            ? target.Username
            : channel.Name;

        string? canMessageError = GetCanMessageError(channel, requestingUserId, users, relationships);

        ChatChannelDto dto = ChatChannelDto.FromDb(channel, requestingUserId, displayName, participantIds);
        dto.Icon = targetUserId.HasValue ? $"https://a.ppy.sh/{targetUserId.Value}" : null;
        dto.LastReadId = membership?.LastReadId;
        dto.MessageLengthLimit = isAnnouncement ? chatConfig.AnnounceMessageLengthLimit : chatConfig.MessageLengthLimit;
        dto.CurrentUserAttributes = new ChatUserAttributes
        {
            CanMessage = canMessageError == null,
            CanMessageError = canMessageError,
            LastReadId = membership?.LastReadId ?? 0,
        };

        if (includeRecentMessages)
        {
            dto.RecentMessages = ChatMessageFilter.Apply(messages, channel, requestingUserId, chatConfig.PublicBacklogLimit)
                .Select(message => ChatMessageDto.FromDb(message, isAnnouncement, CreateSender(message.SenderId, users)))
                .ToList();
        }

        return dto;
    }

    private ChatConfig GetChatConfig() =>
        configManager.TryGet<ChatConfig>() ?? new ChatConfig();

    /// <summary>
    /// Joins the user to the default public channel when they have no membership
    /// row for it yet, as in osu-web where every user starts in <c>#osu</c>.
    /// </summary>
    /// <param name="userId">The user to seed the membership for.</param>
    private async Task EnsureDefaultChannelJoinedAsync(int userId)
    {
        ChatChannel? defaultChannel = await chatRepository.GetChannelByNameAsync(DefaultChannelName);
        if (defaultChannel == null)
        {
            return;
        }

        if (await chatRepository.GetUserChannelAsync(defaultChannel.ChannelId, userId) == null)
        {
            await chatRepository.AddUserChannelAsync(defaultChannel.ChannelId, userId);
        }
    }

    private async Task<ChatRelationships> LoadRelationshipsAsync(int userId)
    {
        IReadOnlyList<Relationship> mine = await relationshipRepository.GetByUserIdAsync(userId);
        IReadOnlyList<Relationship> theirs = await relationshipRepository.GetByTargetIdAsync(userId);

        return new ChatRelationships(
            mine.Where(static relationship => relationship.Type == RelationshipType.Follow)
                .Select(static relationship => relationship.TargetId)
                .ToHashSet(),
            theirs.Where(static relationship => relationship.Type == RelationshipType.Follow)
                .Select(static relationship => relationship.UserId)
                .ToHashSet(),
            mine.Where(static relationship => relationship.Type == RelationshipType.Block)
                .Select(static relationship => relationship.TargetId)
                .ToHashSet(),
            theirs.Where(static relationship => relationship.Type == RelationshipType.Block)
                .Select(static relationship => relationship.UserId)
                .ToHashSet());
    }

    private async Task<Dictionary<int, User>> LoadUsersAsync(IEnumerable<int> userIds)
    {
        List<int> distinctIds = userIds.Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return [];
        }

        IReadOnlyList<User> users = await userRepository.GetByIdsAsync(distinctIds);
        return users.ToDictionary(static user => user.Id);
    }

    private async Task<Dictionary<int, User>> MergeUsersAsync(Dictionary<int, User> users, IEnumerable<int> additionalUserIds)
    {
        List<int> missingIds = additionalUserIds.Where(userId => !users.ContainsKey(userId)).Distinct().ToList();
        if (missingIds.Count == 0)
        {
            return users;
        }

        IReadOnlyList<User> additional = await userRepository.GetByIdsAsync(missingIds);
        foreach (User user in additional)
        {
            users[user.Id] = user;
        }

        return users;
    }

    /// <summary>
    /// The relationship sets the listing needs, loaded with osu-web's two
    /// preloading queries.
    /// </summary>
    /// <param name="FollowedIds">Users the requesting user has on their friends list.</param>
    /// <param name="FollowerIds">Users that have the requesting user on their friends list.</param>
    /// <param name="BlockedIds">Users the requesting user blocked.</param>
    /// <param name="BlockerIds">Users that blocked the requesting user.</param>
    private sealed record ChatRelationships(
        HashSet<int> FollowedIds,
        HashSet<int> FollowerIds,
        HashSet<int> BlockedIds,
        HashSet<int> BlockerIds)
    {
        /// <summary>
        /// Gets a value indicating whether the requesting user follows the given user.
        /// </summary>
        /// <param name="userId">The other user ID.</param>
        /// <returns><see langword="true"/> when the user is on the friends list.</returns>
        public bool Follows(int userId) => FollowedIds.Contains(userId);

        /// <summary>
        /// Gets a value indicating whether the given user follows the requesting user.
        /// </summary>
        /// <param name="userId">The other user ID.</param>
        /// <returns><see langword="true"/> when the requesting user is on their friends list.</returns>
        public bool FollowedBy(int userId) => FollowerIds.Contains(userId);

        /// <summary>
        /// Gets a value indicating whether the requesting user blocked the given user.
        /// </summary>
        /// <param name="userId">The other user ID.</param>
        /// <returns><see langword="true"/> when the user is blocked by the requesting user.</returns>
        public bool BlockedByMe(int userId) => BlockedIds.Contains(userId);

        /// <summary>
        /// Gets a value indicating whether either user blocked the other.
        /// </summary>
        /// <param name="userId">The other user ID.</param>
        /// <returns><see langword="true"/> when a block exists in either direction.</returns>
        public bool AnyBlock(int userId) => BlockedIds.Contains(userId) || BlockerIds.Contains(userId);
    }
}