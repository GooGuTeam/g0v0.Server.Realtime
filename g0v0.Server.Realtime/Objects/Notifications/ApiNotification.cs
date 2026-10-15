// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// A single notification of the bundle.
/// </summary>
public sealed class ApiNotification
{
    /// <summary>
    /// Gets or sets the notification ID.
    /// </summary>
    [JsonProperty("id")]
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the notification name (for example <c>channel_message</c>).
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    [JsonProperty("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the type of the object the notification is about.
    /// </summary>
    [JsonProperty("object_type")]
    public string ObjectType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ID of the object the notification is about.
    /// </summary>
    [JsonProperty("object_id")]
    public long ObjectId { get; set; }

    /// <summary>
    /// Gets or sets the ID of the user that triggered the notification.
    /// </summary>
    [JsonProperty("source_user_id")]
    public int SourceUserId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the notification has been read.
    /// </summary>
    [JsonProperty("is_read")]
    public bool IsRead { get; set; }

    /// <summary>
    /// Gets or sets the details payload.
    /// </summary>
    [JsonProperty("details")]
    public object? Details { get; set; }
}