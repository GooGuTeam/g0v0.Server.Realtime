// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Notifications;
using g0v0.Server.Realtime.Services;
using ConfigurationManager = g0v0.Server.Common.Configuration.ConfigurationManager;

namespace g0v0.Server.Realtime.Objects.Players;

/// <summary>
/// Default implementation of <see cref="IPlayerFacade"/>.
/// </summary>
/// <param name="manager">The player manager.</param>
public class PlayerFacade(PlayerManager manager) : IPlayerFacade
{
    /// <inheritdoc />
    public PlayerManager Manager { get; set; } = manager;

    /// <inheritdoc />
    public RoomManager? RoomManager { get; set; }

    /// <inheritdoc />
    public ConfigurationManager? ConfigManager { get; set; }

    /// <inheritdoc />
    public ScoreBuffer? ScoreBuffer { get; set; }

    /// <inheritdoc />
    public ScoreUploader? ScoreUploader { get; set; }

    /// <inheritdoc />
    public ScoreProcessedNotificationService? ScoreProcessedNotificationService { get; set; }

    /// <inheritdoc />
    public INotificationServer? NotificationServer { get; set; }

    /// <inheritdoc />
    public ILogger? Logger { get; set; }
}