// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;
using g0v0.Server.Common.Database.Models;

namespace g0v0.Server.Realtime.Objects.Chat;

/// <summary>
/// User payload embedded in chat responses.
/// </summary>
public sealed class UserCompactDto
{
    /// <summary>
    /// Gets or sets the user ID.
    /// </summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the username.
    /// </summary>
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the avatar URL.
    /// </summary>
    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Gets or sets the two-letter country code.
    /// </summary>
    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user's default group.
    /// </summary>
    [JsonPropertyName("default_group")]
    public string? DefaultGroup { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the account is active.
    /// </summary>
    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user is a bot.
    /// </summary>
    [JsonPropertyName("is_bot")]
    public bool IsBot { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the account has been deleted.
    /// </summary>
    [JsonPropertyName("is_deleted")]
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user is online.
    /// </summary>
    [JsonPropertyName("is_online")]
    public bool IsOnline { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user is a supporter.
    /// </summary>
    [JsonPropertyName("is_supporter")]
    public bool IsSupporter { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the user's last visit.
    /// </summary>
    [JsonPropertyName("last_visit")]
    public DateTimeOffset? LastVisit { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user only accepts private
    /// messages from friends.
    /// </summary>
    [JsonPropertyName("pm_friends_only")]
    public bool PmFriendsOnly { get; set; }

    /// <summary>
    /// Gets or sets the profile colour of the user.
    /// </summary>
    [JsonPropertyName("profile_colour")]
    public string? ProfileColour { get; set; }

    /// <summary>
    /// Builds the DTO from a database user.
    /// </summary>
    /// <param name="user">The database user.</param>
    /// <returns>The user compact DTO.</returns>
    /// <remarks>
    /// The server has no user groups or account deletion yet, so
    /// <c>default_group</c> is always the fallback group and <c>is_deleted</c>
    /// is always <see langword="false"/>.
    /// </remarks>
    public static UserCompactDto FromDb(User user)
    {
        return new UserCompactDto
        {
            Id = user.Id,
            Username = user.Username,
            AvatarUrl = user.AvatarUrl,
            CountryCode = user.CountryCode,
            DefaultGroup = "default",
            IsActive = user.IsActive,
            IsBot = user.IsBot,
            IsDeleted = false,
            IsOnline = user.IsOnline,
            IsSupporter = user.IsSupporter,
            LastVisit = user.LastVisit,
            PmFriendsOnly = user.PmFriendsOnly,
            ProfileColour = user.ProfileColour,
        };
    }
}