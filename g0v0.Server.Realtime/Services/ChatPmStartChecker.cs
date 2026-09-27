// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;

namespace g0v0.Server.Realtime.Services;

/// <summary>
/// Implements osu-web's <c>ChatPmStart</c> authorization check: whether a user
/// may start (or keep using) a private conversation with another user.
/// </summary>
/// <remarks>
/// The server has no moderator or account-restriction model yet, so only the
/// bot exemption and the block/friends-only rules of
/// <c>OsuAuthorize::checkChatPmStart</c> are implemented.
/// </remarks>
/// <param name="userRepository">The user repository.</param>
/// <param name="relationshipRepository">The relationship repository backing the block and friend checks.</param>
public sealed class ChatPmStartChecker(IUserRepository userRepository, IRelationshipRepository relationshipRepository)
{
    /// <summary>
    /// The error reported when either user blocked the other.
    /// </summary>
    public const string BlockedMessage = "Cannot message a user that is blocking you or that you have blocked.";

    /// <summary>
    /// The error reported when the target only accepts messages from friends.
    /// </summary>
    public const string FriendsOnlyMessage = "User is blocking messages from people not on their friends list.";

    /// <summary>
    /// The error reported when the sender only accepts messages from friends.
    /// </summary>
    public const string ReceiveFriendsOnlyMessage = "The user may not be able to reply because you are only accepting messages from people on your friends list.";

    /// <summary>
    /// Applies the authorization rules to already loaded relationship data.
    /// </summary>
    /// <param name="sender">The user starting the conversation.</param>
    /// <param name="target">The user the conversation is started with.</param>
    /// <param name="senderFollowsTarget">Whether the sender has the target on their friends list.</param>
    /// <param name="targetFollowsSender">Whether the target has the sender on their friends list.</param>
    /// <param name="anyBlock">Whether either user blocked the other.</param>
    /// <returns><see langword="null"/> when allowed; otherwise the error message.</returns>
    public static string? Evaluate(
        User sender,
        User target,
        bool senderFollowsTarget,
        bool targetFollowsSender,
        bool anyBlock)
    {
        string? message = null;

        if (sender.PmFriendsOnly && !senderFollowsTarget)
        {
            message = ReceiveFriendsOnlyMessage;
        }
        else if (!sender.IsBot)
        {
            if (anyBlock)
            {
                message = BlockedMessage;
            }
            else if (target.PmFriendsOnly && !targetFollowsSender)
            {
                message = FriendsOnlyMessage;
            }
        }

        return message;
    }

    /// <summary>
    /// Checks whether <paramref name="senderId"/> may start a private conversation
    /// with <paramref name="targetId"/>.
    /// </summary>
    /// <param name="senderId">The user starting the conversation.</param>
    /// <param name="targetId">The user the conversation is started with.</param>
    /// <returns><see langword="null"/> when allowed; otherwise the error message.</returns>
    public async Task<string?> CheckAsync(int senderId, int targetId)
    {
        User? sender = await userRepository.GetByIdAsync(senderId);
        User? target = await userRepository.GetByIdAsync(targetId);

        if (sender == null || target == null)
        {
            return FriendsOnlyMessage;
        }

        bool senderFollowsTarget = await relationshipRepository.IsFollowingAsync(senderId, targetId);
        bool targetFollowsSender = await relationshipRepository.IsFollowingAsync(targetId, senderId);
        bool anyBlock = await relationshipRepository.IsBlockedAsync(senderId, targetId)
            || await relationshipRepository.IsBlockedAsync(targetId, senderId);

        return Evaluate(sender, target, senderFollowsTarget, targetFollowsSender, anyBlock);
    }
}