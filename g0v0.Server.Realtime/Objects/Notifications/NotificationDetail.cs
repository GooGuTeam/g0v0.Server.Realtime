// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// Base payload for a private notification, mirroring the lazer API
/// <c>NotificationDetail</c> model.
/// </summary>
/// <remarks>
/// The notification is used on both IPC and the notification WebSocket endpoint,
/// both serialized with Newtonsoft.Json. <see cref="Id"/>,
/// <see cref="CreatedAt"/> and <see cref="IsRead"/> carry the persisted
/// notification's identity so the <c>new</c> WebSocket event carries them.
/// </remarks>
[JsonConverter(typeof(NotificationDetailJsonConverter))]
public abstract class NotificationDetail
{
    /// <summary>
    /// Gets or sets the persisted notification ID. <c>0</c> when the notification
    /// has not been persisted.
    /// </summary>
    [JsonProperty("id")]
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp of the persisted notification.
    /// </summary>
    [JsonProperty("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the persisted notification has been read.
    /// </summary>
    [JsonProperty("is_read")]
    public bool IsRead { get; set; }

    /// <summary>
    /// Gets or sets the notification title.
    /// </summary>
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the cover image URL.
    /// </summary>
    [JsonProperty("cover_url")]
    public string CoverUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the source user is removed from the
    /// receivers. Notifications that notify their source user (for example user
    /// achievement unlocks) override this.
    /// </summary>
    [JsonIgnore]
    public virtual bool ExcludeSourceFromReceivers => true;

    /// <summary>
    /// Gets the notification name.
    /// </summary>
    [JsonProperty("name")]
    public abstract NotificationName Name { get; }

    /// <summary>
    /// Gets the related object type.
    /// </summary>
    [JsonProperty("object_type")]
    public abstract string ObjectType { get; }

    /// <summary>
    /// Gets the related object ID.
    /// </summary>
    [JsonProperty("object_id")]
    public abstract int ObjectId { get; }

    /// <summary>
    /// Gets the ID of the user that triggered the notification.
    /// </summary>
    [JsonProperty("source_user_id")]
    public abstract int SourceUserId { get; }

    /// <summary>
    /// Gets the user IDs that should receive the notification.
    /// </summary>
    [JsonProperty("receiver_ids")]
    public abstract IReadOnlyList<int> ReceiverIds { get; }

    /// <summary>
    /// Gets the notification details. The details are derived from the
    /// notification's own fields so both IPC senders and in-process callers
    /// produce them.
    /// </summary>
    /// <remarks>
    /// Declared as <see cref="object"/> because the serializer emits the runtime
    /// type only for such members; a property declared as
    /// <see cref="NotificationDetails"/> would serialize as an empty object.
    /// </remarks>
    [JsonProperty("details")]
    public abstract object Details { get; }
}