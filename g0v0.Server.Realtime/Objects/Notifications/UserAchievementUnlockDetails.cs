// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// Details of a <c>user_achievement_unlock</c> notification.
/// </summary>
public sealed class UserAchievementUnlockDetails : NotificationDetails
{
    /// <summary>
    /// Gets or sets the achievement ID.
    /// </summary>
    [JsonProperty("achievement_id")]
    [JsonPropertyName("achievement_id")]
    public int AchievementId { get; set; }

    /// <summary>
    /// Gets or sets the ruleset the achievement was unlocked in.
    /// </summary>
    [JsonProperty("achievement_mode")]
    [JsonPropertyName("achievement_mode")]
    public string AchievementMode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the achievement icon URL.
    /// </summary>
    [JsonProperty("cover_url")]
    [JsonPropertyName("cover_url")]
    public string CoverUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the achievement slug.
    /// </summary>
    [JsonProperty("slug")]
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the achievement name.
    /// </summary>
    [JsonProperty("title")]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the achievement description.
    /// </summary>
    [JsonProperty("description")]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ID of the user that unlocked the achievement.
    /// </summary>
    [JsonProperty("user_id")]
    [JsonPropertyName("user_id")]
    public int UserId { get; set; }

    /// <summary>
    /// Gets or sets the username of the achieving user.
    /// </summary>
    [JsonProperty("username")]
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;
}