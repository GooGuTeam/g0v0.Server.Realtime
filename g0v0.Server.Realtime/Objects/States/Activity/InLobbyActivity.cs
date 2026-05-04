// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Users;

namespace g0v0.Server.Realtime.Objects.States.Activity;

/// <summary>
/// Represents a user inside a multiplayer lobby.
/// </summary>
public class InLobbyActivity : IUserActivity
{
    /// <inheritdoc />
    public UserActivityType Type => UserActivityType.InLobby;

    /// <summary>
    /// Gets or sets the room ID.
    /// </summary>
    public long RoomID { get; set; }

    /// <summary>
    /// Gets or sets the room name.
    /// </summary>
    public string RoomName { get; set; } = string.Empty;

    /// <inheritdoc />
    public string GetDisplayText()
    {
        return $"In {RoomName}";
    }

    /// <inheritdoc />
    public UserActivity? ToLazer()
    {
        return new UserActivity.InLobby()
        {
            RoomID = RoomID,
            RoomName = RoomName,
        };
    }
}