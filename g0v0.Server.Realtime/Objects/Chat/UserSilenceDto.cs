// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Chat;

/// <summary>
/// User silence response DTO.
/// </summary>
public sealed class UserSilenceDto
{
    /// <summary>
    /// Gets or sets the silence record ID.
    /// </summary>
    [JsonProperty("id")]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the silenced user ID.
    /// </summary>
    [JsonProperty("user_id")]
    public int UserId { get; set; }
}