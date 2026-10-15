// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Controllers;

/// <summary>
/// A notification identity parameter of the mark-read request: every field is
/// optional and only the set fields constrain the affected notifications.
/// </summary>
public sealed class NotificationIdentityParameter
{
    /// <summary>
    /// Gets or sets the notification category filter.
    /// </summary>
    [JsonProperty("category")]
    public string? Category { get; set; }

    /// <summary>
    /// Gets or sets the notification ID filter.
    /// </summary>
    [JsonProperty("id")]
    public int? Id { get; set; }

    /// <summary>
    /// Gets or sets the object ID filter.
    /// </summary>
    [JsonProperty("object_id")]
    public long? ObjectId { get; set; }

    /// <summary>
    /// Gets or sets the object type filter.
    /// </summary>
    [JsonProperty("object_type")]
    public string? ObjectType { get; set; }
}