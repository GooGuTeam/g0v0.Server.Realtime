// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Realtime.Objects.Rooms;

/// <summary>
/// Describes whether a user may join a multiplayer room and why a join was rejected.
/// </summary>
/// <param name="Success">
/// Whether the join is allowed.
/// </param>
/// <param name="Reason">
/// The rejection reason, or <see langword="null"/> when the join is allowed.
/// </param>
public sealed record RoomJoinResult(bool Success, string? Reason)
{
    /// <summary>
    /// The rejection reason used when the supplied room password is invalid.
    /// </summary>
    public const string InvalidPasswordReason = "The room password is invalid.";

    /// <summary>
    /// Gets a successful join result.
    /// </summary>
    public static RoomJoinResult Allowed { get; } = new(Success: true, Reason: null);

    /// <summary>
    /// Creates a rejected join result.
    /// </summary>
    /// <param name="reason">
    /// The reason the join was rejected.
    /// </param>
    /// <returns>
    /// The rejected result.
    /// </returns>
    public static RoomJoinResult Rejected(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new RoomJoinResult(Success: false, Reason: reason);
    }
}