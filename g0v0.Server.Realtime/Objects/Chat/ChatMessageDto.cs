// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Chat;

/// <summary>
/// Chat message response DTO.
/// </summary>
public sealed class ChatMessageDto
{
    /// <summary>
    /// Gets or sets the channel ID the message belongs to.
    /// </summary>
    [JsonProperty("channel_id")]
    public int ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the message content.
    /// </summary>
    [JsonProperty("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the message is an action (<c>/me</c>).
    /// </summary>
    [JsonProperty("is_action")]
    public bool IsAction { get; set; }

    /// <summary>
    /// Gets or sets the message ID.
    /// </summary>
    [JsonProperty("message_id")]
    public int MessageId { get; set; }

    /// <summary>
    /// Gets or sets the sender user ID.
    /// </summary>
    [JsonProperty("sender_id")]
    public int SenderId { get; set; }

    /// <summary>
    /// Gets or sets the message timestamp.
    /// </summary>
    [JsonProperty("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the message type name (<c>plain</c>, <c>action</c> or <c>markdown</c>).
    /// </summary>
    [JsonProperty("type")]
    public string Type { get; set; } = "plain";

    /// <summary>
    /// Gets or sets the client-generated UUID, when present.
    /// </summary>
    [JsonProperty("uuid", NullValueHandling = NullValueHandling.Ignore)]
    public string? Uuid { get; set; }

    /// <summary>
    /// Gets or sets the sender payload, when requested.
    /// </summary>
    [JsonProperty("sender", NullValueHandling = NullValueHandling.Ignore)]
    public UserCompactDto? Sender { get; set; }

    /// <summary>
    /// Builds a message DTO from a database message.
    /// </summary>
    /// <param name="message">
    /// The database message.
    /// </param>
    /// <param name="isAnnouncement">
    /// Whether the message was posted in an announcement channel.
    /// </param>
    /// <param name="sender">
    /// The sender payload, when it should be included.
    /// </param>
    /// <returns>
    /// The DTO.
    /// </returns>
    public static ChatMessageDto FromDb(ChatMessage message, bool isAnnouncement = false, UserCompactDto? sender = null)
    {
        return new ChatMessageDto
        {
            ChannelId = message.ChannelId,
            Content = message.Content,
            IsAction = message.Type == MessageType.Action,
            MessageId = message.MessageId,
            SenderId = message.SenderId,
            Timestamp = message.Timestamp,
            Type = ChatConventions.GetMessageTypeName(message.Type, isAnnouncement),
            Uuid = message.Uuid,
            Sender = sender,
        };
    }
}