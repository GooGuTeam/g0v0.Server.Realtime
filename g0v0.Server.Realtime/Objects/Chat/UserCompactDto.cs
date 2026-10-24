// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Chat;

/// <summary>
/// User payload embedded in chat responses.
/// </summary>
public sealed class UserCompactDto
{
    /// <summary>
    /// Gets or sets the user ID.
    /// </summary>
    [JsonProperty("id")]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the username.
    /// </summary>
    [JsonProperty("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the avatar URL.
    /// </summary>
    [JsonProperty("avatar_url")]
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Gets or sets the two-letter country code.
    /// </summary>
    [JsonProperty("country_code")]
    public string? CountryCode { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user's default group.
    /// </summary>
    [JsonProperty("default_group")]
    public string? DefaultGroup { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the account is active.
    /// </summary>
    [JsonProperty("is_active")]
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user is a bot.
    /// </summary>
    [JsonProperty("is_bot")]
    public bool IsBot { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the account has been deleted.
    /// </summary>
    [JsonProperty("is_deleted")]
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user is online.
    /// </summary>
    [JsonProperty("is_online")]
    public bool IsOnline { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user is a supporter.
    /// </summary>
    [JsonProperty("is_supporter")]
    public bool IsSupporter { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the user's last visit.
    /// </summary>
    [JsonProperty("last_visit")]
    public DateTimeOffset? LastVisit { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user only accepts private
    /// messages from friends.
    /// </summary>
    [JsonProperty("pm_friends_only")]
    public bool PmFriendsOnly { get; set; }

    /// <summary>
    /// Gets or sets the profile colour of the user.
    /// </summary>
    [JsonProperty("profile_colour")]
    public string? ProfileColour { get; set; }

    /// <summary>
    /// Builds the DTO from a database user.
    /// </summary>
    /// <param name="user">
    /// The database user.
    /// </param>
    /// <returns>
    /// The user compact DTO.
    /// </returns>
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