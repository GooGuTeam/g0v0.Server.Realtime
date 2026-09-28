// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace g0v0.Server.Realtime.Controllers;

/// <summary>
/// The body of the mark-read request, mirroring osu-web's
/// <c>NotificationsController::markRead</c> parameters.
/// </summary>
public sealed class MarkNotificationsReadRequest
{
    /// <summary>
    /// Gets or sets the identity filters. Only used when
    /// <see cref="Notifications"/> is absent.
    /// </summary>
    [JsonPropertyName("identities")]
    public IReadOnlyList<NotificationIdentityParameter>? Identities { get; set; }

    /// <summary>
    /// Gets or sets the explicit notification entries.
    /// </summary>
    [JsonPropertyName("notifications")]
    public IReadOnlyList<NotificationIdentityParameter>? Notifications { get; set; }
}