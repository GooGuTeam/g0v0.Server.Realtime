// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// A single notification of the bundle, mirroring osu-web's
/// <c>NotificationTransformer</c> output and lazer's <c>APINotification</c>.
/// </summary>
public sealed class ApiNotification
{
    /// <summary>
    /// Gets or sets the notification ID.
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the notification name (for example <c>channel_message</c>).
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the type of the object the notification is about.
    /// </summary>
    [JsonPropertyName("object_type")]
    public string ObjectType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ID of the object the notification is about.
    /// </summary>
    [JsonPropertyName("object_id")]
    public long ObjectId { get; set; }

    /// <summary>
    /// Gets or sets the ID of the user that triggered the notification.
    /// </summary>
    [JsonPropertyName("source_user_id")]
    public int SourceUserId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the notification has been read.
    /// </summary>
    [JsonPropertyName("is_read")]
    public bool IsRead { get; set; }

    /// <summary>
    /// Gets or sets the details payload.
    /// </summary>
    [JsonPropertyName("details")]
    public object? Details { get; set; }
}