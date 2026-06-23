// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Manager;
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
    public PlayerManager _manager { get; set; } = manager;

    /// <inheritdoc />
    public ConfigurationManager? _configManager { get; set; }

    /// <inheritdoc />
    public ScoreBuffer? _scoreBuffer { get; set; }

    /// <inheritdoc />
    public ScoreUploader? _scoreUploader { get; set; }

    /// <inheritdoc />
    public ScoreProcessedNotificationService? _scoreProcessedNotificationService { get; set; }
}