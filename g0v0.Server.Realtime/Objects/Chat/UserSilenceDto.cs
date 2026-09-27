// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace g0v0.Server.Realtime.Objects.Chat;

/// <summary>
/// User silence response DTO, mirroring osu-web's <c>UserSilenceTransformer</c> output.
/// </summary>
public sealed class UserSilenceDto
{
    /// <summary>
    /// Gets or sets the silence record ID.
    /// </summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the silenced user ID.
    /// </summary>
    [JsonPropertyName("user_id")]
    public int UserId { get; set; }
}