// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Realtime.Hubs;
using g0v0.Server.Realtime.Notifications;
using g0v0.Server.Realtime.Services;

namespace g0v0.Server.Realtime.Objects.Rooms;

/// <summary>
/// Provides late-bound dependencies shared by room instances.
/// </summary>
public interface IRoomFacade
{
    /// <summary>
    /// Gets or sets the scope factory used to resolve scoped repositories per operation.
    /// </summary>
    IServiceScopeFactory ScopeFactory { get; set; }

    /// <summary>
    /// Gets or sets the event dispatcher used to broadcast room events to connected clients.
    /// </summary>
    MultiplayerEventDispatcher EventDispatcher { get; set; }

    /// <summary>
    /// Gets or sets the logger used for room lifecycle events.
    /// </summary>
    ILogger? Logger { get; set; }

    /// <summary>
    /// Gets or sets the configuration manager, when needed by room operations.
    /// </summary>
    ConfigManager? ConfigManager { get; set; }

    /// <summary>
    /// Gets or sets the ruleset manager used to validate mod selections.
    /// </summary>
    RulesetManager? RulesetManager { get; set; }

    /// <summary>
    /// Gets or sets the validator used to check multiplayer client ruleset hashes.
    /// </summary>
    IRulesetHashValidator? RulesetHashValidator { get; set; }

    /// <summary>
    /// Gets or sets the notification server used to manage chat channel membership.
    /// </summary>
    INotificationServer? NotificationServer { get; set; }

    /// <summary>
    /// Gets or sets the player manager used to resolve connected players.
    /// </summary>
    Manager.PlayerManager? PlayerManager { get; set; }
}