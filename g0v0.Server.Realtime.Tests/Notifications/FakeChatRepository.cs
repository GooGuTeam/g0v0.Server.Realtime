// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using osu.Game.Online.Chat;

namespace g0v0.Server.Realtime.Tests.Notifications;

/// <summary>
/// In-memory <see cref="IChatRepository"/> used by chat tests.
/// </summary>
internal sealed class FakeChatRepository : IChatRepository
{
    private readonly List<ChatChannel> _channels = new();
    private readonly List<ChatMessage> _messages = new();
    private readonly List<SilenceUser> _silences = new();
    private readonly List<ChatUserChannel> _userChannels = new();
    private int _nextChannelId = 1;
    private int _nextMessageId = 1;
    private int _nextSilenceId = 1;

    public IReadOnlyList<ChatChannel> Channels => _channels;

    public IReadOnlyList<ChatMessage> Messages => _messages;

    public IReadOnlyList<SilenceUser> Silences => _silences;

    public IReadOnlyList<ChatUserChannel> UserChannels => _userChannels;

    public void AddChannel(ChatChannel channel)
    {
        if (channel.ChannelId == 0)
        {
            channel.ChannelId = _nextChannelId++;
        }
        else
        {
            _nextChannelId = Math.Max(_nextChannelId, channel.ChannelId + 1);
        }

        _channels.Add(channel);
    }

    public Task<ChatChannel?> GetChannelAsync(int channelId)
    {
        return Task.FromResult(_channels.Find(channel => channel.ChannelId == channelId));
    }

    public Task<ChatChannel?> GetChannelByNameAsync(string name)
    {
        return Task.FromResult(_channels.Find(channel => string.Equals(channel.Name, name, StringComparison.Ordinal)));
    }

    public Task<ChatChannel?> GetChannelAsync(string channel)
    {
        return int.TryParse(channel, out int channelId)
            ? GetChannelAsync(channelId)
            : GetChannelByNameAsync(channel);
    }

    public Task<ChatChannel?> GetPmChannelAsync(int user1Id, int user2Id)
    {
        ChatChannel? first = _channels.Find(channel => string.Equals(channel.Name, $"pm_{user1Id}_{user2Id}", StringComparison.Ordinal));
        return Task.FromResult(first ?? _channels.Find(channel => string.Equals(channel.Name, $"pm_{user2Id}_{user1Id}", StringComparison.Ordinal)));
    }

    public Task<IReadOnlyList<ChatChannel>> GetPmChannelsForUserAsync(int userId)
    {
        string prefix = $"pm_{userId}_";
        string suffix = $"_{userId}";
        return Task.FromResult<IReadOnlyList<ChatChannel>>(
            _channels
                .Where(channel => channel.Type == ChannelType.PM
                    && (channel.Name.StartsWith(prefix, StringComparison.Ordinal)
                        || channel.Name.EndsWith(suffix, StringComparison.Ordinal)))
                .OrderBy(channel => channel.ChannelId)
                .ToList());
    }

    public Task<ChatUserChannel?> GetUserChannelAsync(int channelId, int userId)
    {
        return Task.FromResult(_userChannels.Find(userChannel =>
            userChannel.ChannelId == channelId && userChannel.UserId == userId));
    }

    public Task<IReadOnlyList<ChatUserChannel>> GetUserChannelsAsync(int userId, bool includeHidden = false, int? limit = null)
    {
        IEnumerable<ChatUserChannel> query = _userChannels
            .Where(userChannel => userChannel.UserId == userId && (includeHidden || !userChannel.Hidden))
            .OrderBy(userChannel => userChannel.ChannelId);

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        return Task.FromResult<IReadOnlyList<ChatUserChannel>>(query.ToList());
    }

    public Task<IReadOnlyList<int>> GetChannelUserIdsAsync(int channelId)
    {
        return Task.FromResult<IReadOnlyList<int>>(_userChannels
            .Where(userChannel => userChannel.ChannelId == channelId)
            .Select(userChannel => userChannel.UserId)
            .ToList());
    }

    public Task<bool> AddUserChannelAsync(int channelId, int userId)
    {
        ChatUserChannel? existing = _userChannels.Find(userChannel =>
            userChannel.ChannelId == channelId && userChannel.UserId == userId);

        if (existing == null)
        {
            _userChannels.Add(new ChatUserChannel { ChannelId = channelId, UserId = userId });
            return Task.FromResult(true);
        }

        if (!existing.Hidden)
        {
            return Task.FromResult(false);
        }

        existing.Hidden = false;
        return Task.FromResult(true);
    }

    public Task<bool> RemoveUserChannelAsync(int channelId, int userId, bool hide)
    {
        ChatUserChannel? existing = _userChannels.Find(userChannel =>
            userChannel.ChannelId == channelId && userChannel.UserId == userId);

        if (existing == null || (hide && existing.Hidden))
        {
            return Task.FromResult(false);
        }

        if (hide)
        {
            existing.Hidden = true;
        }
        else
        {
            _userChannels.Remove(existing);
        }

        return Task.FromResult(true);
    }

    public Task<bool> MarkUserChannelAsReadAsync(int channelId, int userId, int messageId)
    {
        ChatUserChannel? existing = _userChannels.Find(userChannel =>
            userChannel.ChannelId == channelId && userChannel.UserId == userId);

        if (existing == null)
        {
            return Task.FromResult(false);
        }

        existing.LastReadId = Math.Max(existing.LastReadId ?? 0, messageId);
        return Task.FromResult(true);
    }

    public Task<int> UnhideChannelAsync(int channelId)
    {
        List<ChatUserChannel> hidden = _userChannels
            .Where(userChannel => userChannel.ChannelId == channelId && userChannel.Hidden)
            .ToList();

        foreach (ChatUserChannel userChannel in hidden)
        {
            userChannel.Hidden = false;
        }

        return Task.FromResult(hidden.Count);
    }

    public Task<IReadOnlyList<ChatChannel>> GetChannelsAsync(IReadOnlyList<int> channelIds)
    {
        return Task.FromResult<IReadOnlyList<ChatChannel>>(_channels
            .Where(channel => channelIds.Contains(channel.ChannelId))
            .OrderBy(channel => channel.ChannelId)
            .ToList());
    }

    public Task UpdateLastMessageIdAsync(int channelId, int messageId)
    {
        ChatChannel? channel = _channels.Find(candidate => candidate.ChannelId == channelId);
        if (channel != null)
        {
            channel.LastMessageId = messageId;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(IReadOnlyList<int> channelIds, int limitPerChannel)
    {
        List<ChatMessage> recent = _messages
            .Where(message => channelIds.Contains(message.ChannelId))
            .GroupBy(message => message.ChannelId)
            .SelectMany(group => group.OrderByDescending(message => message.MessageId).Take(limitPerChannel).Reverse())
            .OrderBy(message => message.ChannelId)
            .ThenBy(message => message.MessageId)
            .ToList();

        return Task.FromResult<IReadOnlyList<ChatMessage>>(recent);
    }

    public Task<IReadOnlyList<ChatChannel>> GetPublicChannelsAsync()
    {
        return Task.FromResult<IReadOnlyList<ChatChannel>>(
            _channels.Where(channel => channel.Type == ChannelType.Public).OrderBy(channel => channel.ChannelId).ToList());
    }

    public Task<ChatChannel> CreateChannelAsync(string name, string description, ChannelType type)
    {
        ChatChannel channel = new()
        {
            ChannelId = _nextChannelId++,
            Name = name,
            Description = description,
            Type = type,
        };
        _channels.Add(channel);
        return Task.FromResult(channel);
    }

    public async Task<ChatChannel> GetOrCreatePmChannelAsync(int user1Id, int user2Id)
    {
        return await GetPmChannelAsync(user1Id, user2Id) ?? await CreatePmChannelAsync(user1Id, user2Id);
    }

    public Task<ChatChannel> CreatePmChannelAsync(int user1Id, int user2Id)
    {
        return CreateChannelAsync($"pm_{user1Id}_{user2Id}", "Private message channel", ChannelType.PM);
    }

    public async Task<ChatChannel> GetOrCreateMultiplayerChannelAsync(long roomId)
    {
        ChatChannel? existing = await GetChannelByNameAsync($"mp_{roomId}");
        return existing ?? await CreateChannelAsync($"mp_{roomId}", $"Multiplayer room {roomId} chat", ChannelType.Multiplayer);
    }

    public Task<ChatMessage?> GetMessageAsync(int messageId)
    {
        return Task.FromResult(_messages.Find(message => message.MessageId == messageId));
    }

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(int channelId, int limit, int? since = null, int? until = null)
    {
        IEnumerable<ChatMessage> query = _messages.Where(message => message.ChannelId == channelId);

        if (since.HasValue)
        {
            query = query.Where(message => message.MessageId > since.Value);
            return Task.FromResult<IReadOnlyList<ChatMessage>>(query.OrderBy(message => message.MessageId).Take(limit).ToList());
        }

        if (until.HasValue)
        {
            List<ChatMessage> messages = query.Where(message => message.MessageId < until.Value)
                .OrderByDescending(message => message.MessageId).Take(limit).ToList();
            messages.Reverse();
            return Task.FromResult<IReadOnlyList<ChatMessage>>(messages);
        }

        List<ChatMessage> recent = query.OrderByDescending(message => message.MessageId).Take(limit).ToList();
        recent.Reverse();
        return Task.FromResult<IReadOnlyList<ChatMessage>>(recent);
    }

    public Task<ChatMessage> CreateMessageAsync(int channelId, int senderId, string content, MessageType type, string? uuid = null)
    {
        ChatMessage message = new()
        {
            MessageId = _nextMessageId++,
            ChannelId = channelId,
            SenderId = senderId,
            Content = content,
            Timestamp = DateTimeOffset.UtcNow,
            Type = type,
            Uuid = uuid,
        };
        _messages.Add(message);
        return Task.FromResult(message);
    }

    public Task<SilenceUser?> GetSilenceAsync(int channelId, int userId)
    {
        return Task.FromResult(_silences.Find(silence => silence.ChannelId == channelId && silence.UserId == userId));
    }

    public Task<bool> IsUserSilencedAsync(int channelId, int userId)
    {
        return Task.FromResult(_silences.Exists(silence => silence.ChannelId == channelId && silence.UserId == userId));
    }

    public Task<IReadOnlyList<SilenceUser>> GetSilencesAsync(int? historySince = null, DateTimeOffset? bannedSince = null)
    {
        IEnumerable<SilenceUser> query = _silences.AsEnumerable();
        if (historySince.HasValue)
        {
            query = query.Where(silence => silence.Id > historySince.Value);
        }
        else if (bannedSince.HasValue)
        {
            query = query.Where(silence => silence.BannedAt > bannedSince.Value);
        }

        return Task.FromResult<IReadOnlyList<SilenceUser>>(query.OrderBy(silence => silence.Id).ToList());
    }

    public Task<SilenceUser> CreateSilenceAsync(int channelId, int userId, DateTimeOffset bannedAt, DateTimeOffset? until = null, string? reason = null)
    {
        SilenceUser silence = new()
        {
            Id = _nextSilenceId++,
            ChannelId = channelId,
            UserId = userId,
            BannedAt = bannedAt,
            Until = until ?? DateTimeOffset.MaxValue,
            Reason = reason,
        };
        _silences.Add(silence);
        return Task.FromResult(silence);
    }
}