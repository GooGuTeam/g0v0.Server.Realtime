// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// A user achievement unlock notification, mirroring the lazer API
/// <c>UserAchievementUnlock</c>.
/// </summary>
public sealed class UserAchievementUnlockNotification : NotificationDetail
{
    /// <summary>
    /// Gets or sets the achievement ID.
    /// </summary>
    [JsonProperty("achievement_id")]
    [JsonPropertyName("achievement_id")]
    public int AchievementId { get; set; }

    /// <summary>
    /// Gets or sets the achievement mode.
    /// </summary>
    [JsonProperty("achievement_mode")]
    [JsonPropertyName("achievement_mode")]
    public string AchievementMode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the achievement slug.
    /// </summary>
    [JsonProperty("slug")]
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the achievement description.
    /// </summary>
    [JsonProperty("description")]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the achieving user ID.
    /// </summary>
    [JsonProperty("user_id")]
    [JsonPropertyName("user_id")]
    public int UserId { get; set; }

    /// <summary>
    /// Gets or sets the username of the achieving user, carried into the
    /// details payload.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public string Username { get; set; } = string.Empty;

    /// <inheritdoc />
    /// <remarks>
    /// User achievement unlocks notify the achieving user themself, so the
    /// source user is kept in the receivers.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public override bool ExcludeSourceFromReceivers => false;

    /// <inheritdoc />
    [JsonProperty("name")]
    [JsonPropertyName("name")]
    public override NotificationName Name => NotificationName.UserAchievementUnlock;

    /// <inheritdoc />
    [JsonProperty("object_type")]
    [JsonPropertyName("object_type")]
    public override string ObjectType => "achievement";

    /// <inheritdoc />
    [JsonProperty("object_id")]
    [JsonPropertyName("object_id")]
    public override int ObjectId => AchievementId;

    /// <inheritdoc />
    [JsonProperty("source_user_id")]
    [JsonPropertyName("source_user_id")]
    public override int SourceUserId => UserId;

    /// <inheritdoc />
    [JsonProperty("receiver_ids")]
    [JsonPropertyName("receiver_ids")]
    public override IReadOnlyList<int> ReceiverIds => [UserId];

    /// <inheritdoc />
    public override object Details => new UserAchievementUnlockDetails
    {
        AchievementId = AchievementId,
        AchievementMode = AchievementMode,
        CoverUrl = CoverUrl,
        Slug = Slug,
        Title = Title,
        Description = Description,
        UserId = UserId,
        Username = Username,
    };
}