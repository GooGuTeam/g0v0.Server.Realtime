// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Rulesets;
using g0v0.Server.Realtime.Hubs;
using g0v0.Server.Realtime.Services;
using ConfigurationManager = g0v0.Server.Common.Configuration.ConfigurationManager;

namespace g0v0.Server.Realtime.Objects.Rooms;

/// <summary>
/// Default implementation of <see cref="IRoomFacade"/>.
/// </summary>
public class RoomFacade : IRoomFacade
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomFacade"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <param name="eventDispatcher">The multiplayer event dispatcher.</param>
    /// <param name="logger">The logger.</param>
    public RoomFacade(
        IServiceScopeFactory scopeFactory,
        MultiplayerEventDispatcher eventDispatcher,
        ILogger<RoomFacade>? logger = null)
    {
        ScopeFactory = scopeFactory;
        EventDispatcher = eventDispatcher;
        Logger = logger;
    }

    /// <inheritdoc />
    public IServiceScopeFactory ScopeFactory { get; set; }

    /// <inheritdoc />
    public MultiplayerEventDispatcher EventDispatcher { get; set; }

    /// <inheritdoc />
    public ILogger? Logger { get; set; }

    /// <inheritdoc />
    public ConfigurationManager? ConfigManager { get; set; }

    /// <inheritdoc />
    public RulesetManager? RulesetManager { get; set; }

    /// <inheritdoc />
    public IRulesetHashValidator? RulesetHashValidator { get; set; }
}