// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Realtime.Objects.Notifications;

/// <summary>
/// Base type of the per-notification <c>details</c> payload.
/// </summary>
/// <remarks>
/// The details of every notification kind are stored as a JSON object and the
/// <c>new</c> event carries them as-is; lazer reads them (for example the medal
/// overlay needs the user achievement unlock details).
/// </remarks>
public abstract class NotificationDetails
{
}