// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Realtime.Notifications.Bot.Commands;

/// <summary>
/// Base class for BanchoBot command sets.
/// </summary>
public abstract class BanchoBotCommandSet
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BanchoBotCommandSet"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    protected BanchoBotCommandSet(IServiceScopeFactory scopeFactory)
    {
        ScopeFactory = scopeFactory;
    }

    /// <summary>
    /// Gets the service scope factory used to resolve repositories.
    /// </summary>
    protected IServiceScopeFactory ScopeFactory { get; }

    /// <summary>
    /// Resolves a repository from a fresh service scope.
    /// </summary>
    /// <typeparam name="T">The repository type.</typeparam>
    /// <returns>The repository.</returns>
    protected T GetRepository<T>()
        where T : notnull
    {
        return ScopeFactory.CreateScope().ServiceProvider.GetRequiredService<T>();
    }
}