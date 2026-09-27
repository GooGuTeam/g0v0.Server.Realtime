// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace g0v0.Server.Realtime.Objects.Chat;

/// <summary>
/// User-specific attributes for a chat channel, mirroring osu-web's
/// <c>includeCurrentUserAttributes</c> transformer output.
/// </summary>
public sealed class ChatUserAttributes
{
    /// <summary>
    /// Gets or sets a value indicating whether the user can send messages in the channel.
    /// </summary>
    [JsonPropertyName("can_message")]
    public bool CanMessage { get; set; }

    /// <summary>
    /// Gets or sets the error message when <see cref="CanMessage"/> is <see langword="false"/>.
    /// </summary>
    [JsonPropertyName("can_message_error")]
    public string? CanMessageError { get; set; }

    /// <summary>
    /// Gets or sets the last message ID read by the user.
    /// </summary>
    [JsonPropertyName("last_read_id")]
    public int LastReadId { get; set; }
}