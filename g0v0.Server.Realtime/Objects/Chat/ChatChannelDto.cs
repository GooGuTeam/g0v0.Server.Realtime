// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;
using g0v0.Server.Common.Database.Models;

namespace g0v0.Server.Realtime.Objects.Chat;

/// <summary>
/// Chat channel response DTO, mirroring osu-web's <c>ChannelTransformer</c> output.
/// </summary>
public sealed class ChatChannelDto
{
    /// <summary>
    /// Gets or sets the channel ID.
    /// </summary>
    [JsonPropertyName("channel_id")]
    public int? ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the channel description/topic.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the channel icon URL, when available.
    /// </summary>
    [JsonPropertyName("icon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    /// <summary>
    /// Gets or sets the channel name as displayed to the requesting user
    /// (PM channels show the target user's username).
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the channel type name (<c>PUBLIC</c>, <c>PM</c>, ...).
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the channel UUID, when one exists.
    /// </summary>
    [JsonPropertyName("uuid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Uuid { get; set; }

    /// <summary>
    /// Gets or sets the maximum message length allowed in this channel.
    /// </summary>
    [JsonPropertyName("message_length_limit")]
    public int MessageLengthLimit { get; set; } = 1000;

    /// <summary>
    /// Gets or sets a value indicating whether the channel is moderated.
    /// </summary>
    [JsonPropertyName("moderated")]
    public bool Moderated { get; set; }

    /// <summary>
    /// Gets or sets the requesting user's attributes for this channel.
    /// </summary>
    [JsonPropertyName("current_user_attributes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChatUserAttributes? CurrentUserAttributes { get; set; }

    /// <summary>
    /// Gets or sets the last message ID the user has read.
    /// </summary>
    [JsonPropertyName("last_read_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LastReadId { get; set; }

    /// <summary>
    /// Gets or sets the last message ID in the channel.
    /// </summary>
    [JsonPropertyName("last_message_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LastMessageId { get; set; }

    /// <summary>
    /// Gets or sets the recent messages in the channel.
    /// </summary>
    [JsonPropertyName("recent_messages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ChatMessageDto>? RecentMessages { get; set; }

    /// <summary>
    /// Gets or sets the user IDs currently in the channel (PM channels only).
    /// </summary>
    [JsonPropertyName("users")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? Users { get; set; }

    /// <summary>
    /// Builds a channel DTO from a database channel and the requesting user.
    /// </summary>
    /// <param name="channel">The database channel.</param>
    /// <param name="requestingUserId">The requesting user ID.</param>
    /// <param name="channelName">The display name of the channel.</param>
    /// <param name="userIds">The user IDs in the channel, when applicable.</param>
    /// <returns>The DTO.</returns>
    public static ChatChannelDto FromDb(
        ChatChannel channel,
        int requestingUserId,
        string? channelName = null,
        IReadOnlyList<int>? userIds = null)
    {
        return new ChatChannelDto
        {
            ChannelId = channel.ChannelId,
            Description = channel.Description,
            Name = channelName ?? channel.Name,
            Type = ChatConventions.GetChannelTypeName(channel.Type),
            Moderated = channel.Moderated,
            LastMessageId = channel.LastMessageId,
            Users = userIds?.ToList(),
        };
    }
}