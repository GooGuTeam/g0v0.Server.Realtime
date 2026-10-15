// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

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
    public int AchievementId { get; set; }

    /// <summary>
    /// Gets or sets the achievement mode.
    /// </summary>
    [JsonProperty("achievement_mode")]
    public string AchievementMode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the achievement slug.
    /// </summary>
    [JsonProperty("slug")]
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the achievement description.
    /// </summary>
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the achieving user ID.
    /// </summary>
    [JsonProperty("user_id")]
    public int UserId { get; set; }

    /// <summary>
    /// Gets or sets the username of the achieving user, carried into the
    /// details payload.
    /// </summary>
    [JsonIgnore]
    public string Username { get; set; } = string.Empty;

    /// <inheritdoc />
    /// <remarks>
    /// User achievement unlocks notify the achieving user themself, so the
    /// source user is kept in the receivers.
    /// </remarks>
    [JsonIgnore]
    public override bool ExcludeSourceFromReceivers => false;

    /// <inheritdoc />
    [JsonProperty("name")]
    public override NotificationName Name => NotificationName.UserAchievementUnlock;

    /// <inheritdoc />
    [JsonProperty("object_type")]
    public override string ObjectType => "achievement";

    /// <inheritdoc />
    [JsonProperty("object_id")]
    public override int ObjectId => AchievementId;

    /// <inheritdoc />
    [JsonProperty("source_user_id")]
    public override int SourceUserId => UserId;

    /// <inheritdoc />
    [JsonProperty("receiver_ids")]
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