// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// Base payload for a private notification, mirroring the lazer API
/// <c>NotificationDetail</c> model.
/// </summary>
/// <remarks>
/// The notification is used on both IPC and the notification WebSocket endpoint.
/// Newtonsoft.Json attributes define its IPC wire shape, while System.Text.Json
/// attributes define its ASP.NET response shape.
/// </remarks>
[Newtonsoft.Json.JsonConverter(typeof(NotificationDetailJsonConverter))]
public abstract class NotificationDetail
{
    /// <summary>
    /// Gets or sets the notification title.
    /// </summary>
    [JsonProperty("title")]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the cover image URL.
    /// </summary>
    [JsonProperty("cover_url")]
    [JsonPropertyName("cover_url")]
    public string CoverUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets the notification name.
    /// </summary>
    [JsonProperty("name")]
    [JsonPropertyName("name")]
    public abstract NotificationName Name { get; }

    /// <summary>
    /// Gets the related object type.
    /// </summary>
    [JsonProperty("object_type")]
    [JsonPropertyName("object_type")]
    public abstract string ObjectType { get; }

    /// <summary>
    /// Gets the related object ID.
    /// </summary>
    [JsonProperty("object_id")]
    [JsonPropertyName("object_id")]
    public abstract int ObjectId { get; }

    /// <summary>
    /// Gets the ID of the user that triggered the notification.
    /// </summary>
    [JsonProperty("source_user_id")]
    [JsonPropertyName("source_user_id")]
    public abstract int SourceUserId { get; }

    /// <summary>
    /// Gets the user IDs that should receive the notification.
    /// </summary>
    [JsonProperty("receiver_ids")]
    [JsonPropertyName("receiver_ids")]
    public abstract IReadOnlyList<int> ReceiverIds { get; }

    /// <summary>
    /// Gets the notification details, mirroring the <c>details</c> of osu-web's
    /// <c>NotificationTransformer</c> output. The details are derived from the
    /// notification's own fields so both IPC senders and in-process callers
    /// produce them.
    /// </summary>
    /// <remarks>
    /// Declared as <see cref="object"/> because both serializers emit the runtime
    /// type only for such members; a property declared as
    /// <see cref="NotificationDetails"/> would serialize as an empty object.
    /// </remarks>
    [JsonProperty("details")]
    [JsonPropertyName("details")]
    public abstract object Details { get; }
}