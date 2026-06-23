// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Services;
using ConfigurationManager = g0v0.Server.Common.Configuration.ConfigurationManager;

namespace g0v0.Server.Realtime.Objects.Players;

/// <summary>
/// Provides late-bound dependencies shared by player instances across realtime hubs.
/// </summary>
public interface IPlayerFacade
{
    /// <summary>
    /// Gets or sets the player manager.
    /// </summary>
    PlayerManager _manager { get; set; }

    /// <summary>
    /// Gets or sets the configuration manager, when needed by player operations.
    /// </summary>
    ConfigurationManager? _configManager { get; set; }

    /// <summary>
    /// Gets or sets the score buffer, when spectator replay capture is available.
    /// </summary>
    ScoreBuffer? _scoreBuffer { get; set; }

    /// <summary>
    /// Gets or sets the score uploader, when spectator replay upload is available.
    /// </summary>
    ScoreUploader? _scoreUploader { get; set; }

    /// <summary>
    /// Gets or sets the score processed notification service,
    /// when score-processed IPC notifications are available.
    /// </summary>
    ScoreProcessedNotificationService? _scoreProcessedNotificationService { get; set; }

    /// <summary>
    /// Copies non-null optional dependencies from another facade.
    /// </summary>
    /// <param name="facade">The facade to copy dependencies from.</param>
    void ApplyNonNullDependenciesFrom(IPlayerFacade facade)
    {
        ArgumentNullException.ThrowIfNull(facade);

        _configManager = facade._configManager ?? _configManager;
        _scoreBuffer = facade._scoreBuffer ?? _scoreBuffer;
        _scoreUploader = facade._scoreUploader ?? _scoreUploader;
        _scoreProcessedNotificationService = facade._scoreProcessedNotificationService ?? _scoreProcessedNotificationService;
    }
}