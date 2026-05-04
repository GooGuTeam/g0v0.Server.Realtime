// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Users;

namespace g0v0.Server.Realtime.Objects.States.Activity;

/// <summary>
/// Represents a user searching for a multiplayer lobby.
/// </summary>
public class SearchingForLobbyActivity : IUserActivity
{
    /// <inheritdoc />
    public UserActivityType Type => UserActivityType.SearchingForLobby;

    /// <inheritdoc />
    public string GetDisplayText()
    {
        return "Looking for a lobby";
    }

    /// <inheritdoc />
    public UserActivity? ToLazer()
    {
        return new UserActivity.SearchingForLobby();
    }
}