// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Authentication;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Extensions;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Notifications;
using g0v0.Server.Realtime.Objects.Chat;
using g0v0.Server.Realtime.Objects.Players;
using g0v0.Server.Realtime.Services;
using Microsoft.AspNetCore.Mvc;
using osu.Game.Online.Chat;

namespace g0v0.Server.Realtime.Controllers;

/// <summary>
/// Chat channel management endpoints.
/// </summary>
[ApiController]
[RequireUserId]
[Route("chat/channels")]
public class ChannelsController(
    IChatRepository chatRepository,
    IUserRepository userRepository,
    INotificationServer notificationServer,
    ChatChannelDtoFactory chatChannelDtoFactory,
    ChatPmStartChecker pmStartChecker,
    PlayerManager playerManager) : ControllerBase
{
    /// <summary>
    /// Lists every joinable public channel.
    /// </summary>
    /// <returns>
    /// The public channels.
    /// </returns>
    /// <remarks>
    /// The channels the user is in are exposed through <c>GET /chat/presence</c>
    /// and <c>GET /chat/updates</c> instead.
    /// </remarks>
    [HttpGet]
    [RequireScope("chat.read")]
    public async Task<IActionResult> ListChannels()
    {
        int currentUserId = HttpContext.GetUserId();
        return Ok(await chatChannelDtoFactory.CreatePublicChannelListAsync(currentUserId));
    }

    /// <summary>
    /// Gets a channel by its ID, together with the users of a PM channel.
    /// </summary>
    /// <param name="channelId">
    /// The channel ID.
    /// </param>
    /// <returns>
    /// The channel and its visible users.
    /// </returns>
    [HttpGet("{channelId:int}")]
    [RequireScope("chat.read")]
    public async Task<IActionResult> GetChannel(int channelId)
    {
        int currentUserId = HttpContext.GetUserId();
        ChatChannel? channel = await chatRepository.GetChannelAsync(channelId);
        if (channel == null)
        {
            return ChatErrorResults.ChannelNotFound();
        }

        // Channel::hasUser counts hidden membership rows as well.
        ChatUserChannel? membership = await chatRepository.GetUserChannelAsync(channelId, currentUserId);
        if (membership == null)
        {
            return ChatErrorResults.NotInChannel();
        }

        ChatChannelDto dto = await chatChannelDtoFactory.CreateChannelAsync(channel, currentUserId, membership);
        return Ok(new
        {
            channel = dto,
            users = await GetVisibleUsersAsync(channel),
        });
    }

    /// <summary>
    /// Creates a new PM or announcement channel, rejoining an existing PM
    /// conversation.
    /// </summary>
    /// <returns>
    /// The created or rejoined channel.
    /// </returns>
    /// <remarks>
    /// Parameters are read from a JSON body, a form body or the query string.
    /// </remarks>
    [HttpPost]
    [RequireScope("chat.write_manage")]
    public async Task<IActionResult> CreateChannel()
    {
        int currentUserId = HttpContext.GetUserId();
        ChatRequestParameters parameters = await ChatRequestParameters.ReadAsync(Request);
        string? type = parameters.GetString("type");
        int? targetId = parameters.GetInt("target_id");

        if (string.Equals(type, "PM", StringComparison.Ordinal))
        {
            if (targetId == null)
            {
                return ChatErrorResults.Unprocessable("missing target_id parameter");
            }

            User? target = await userRepository.GetByIdAsync(targetId.Value);
            if (target == null)
            {
                return ChatErrorResults.TargetUserNotFound();
            }

            string? error = await pmStartChecker.CheckAsync(currentUserId, target.Id);
            if (error != null)
            {
                return ChatErrorResults.Forbidden(error);
            }

            ChatChannel? channel = await chatRepository.GetPmChannelAsync(currentUserId, target.Id);
            if (channel == null)
            {
                // osu-web answers with an empty channel object for a conversation
                // that does not exist yet; it is created by the first message sent
                // through chat/new.
                return Ok(new ChatChannelDto());
            }

            await notificationServer.JoinChannel(LazerPlayer.GetOrCreate(playerManager, currentUserId), channel.ChannelId);
            ChatUserChannel? membership = await chatRepository.GetUserChannelAsync(channel.ChannelId, currentUserId);

            return Ok(await chatChannelDtoFactory.CreateChannelAsync(
                channel,
                currentUserId,
                membership,
                includeRecentMessages: true));
        }

        if (string.Equals(type, "ANNOUNCE", StringComparison.Ordinal))
        {
            // Creating announcements requires the ChatAnnounce privilege, which
            // this server cannot grant yet.
            return ChatErrorResults.Forbidden("You do not have permission to post announcement.");
        }

        return ChatErrorResults.Unprocessable("unknown or missing type parameter");
    }

    /// <summary>
    /// Joins a public channel.
    /// </summary>
    /// <param name="channel">
    /// The channel ID or name.
    /// </param>
    /// <param name="userId">
    /// The user ID.
    /// </param>
    /// <returns>
    /// The joined channel.
    /// </returns>
    [HttpPut("{channel}/users/{userId:int}")]
    [RequireScope("chat.write_manage")]
    public async Task<IActionResult> JoinChannel(string channel, int userId)
    {
        int currentUserId = HttpContext.GetUserId();
        ChatChannel? dbChannel = await chatRepository.GetChannelAsync(channel);
        if (dbChannel == null)
        {
            return ChatErrorResults.ChannelNotFound();
        }

        // ChatChannelJoin only allows public channels; multiplayer room channels
        // are joined through the room endpoints.
        if (dbChannel.Type != ChannelType.Public)
        {
            return ChatErrorResults.Forbidden("You do not have access to that channel.");
        }

        if (currentUserId != userId)
        {
            return Forbid();
        }

        await notificationServer.JoinChannel(LazerPlayer.GetOrCreate(playerManager, currentUserId), dbChannel.ChannelId);
        ChatUserChannel? membership = await chatRepository.GetUserChannelAsync(dbChannel.ChannelId, currentUserId);

        return Ok(await chatChannelDtoFactory.CreateChannelAsync(dbChannel, currentUserId, membership));
    }

    /// <summary>
    /// Leaves a channel.
    /// </summary>
    /// <param name="channel">
    /// The channel ID or name.
    /// </param>
    /// <param name="userId">
    /// The user ID.
    /// </param>
    /// <returns>
    /// No content.
    /// </returns>
    [HttpDelete("{channel}/users/{userId:int}")]
    [RequireScope("chat.write_manage")]
    public async Task<IActionResult> LeaveChannel(string channel, int userId)
    {
        int currentUserId = HttpContext.GetUserId();
        ChatChannel? dbChannel = await chatRepository.GetChannelAsync(channel);
        if (dbChannel == null)
        {
            return ChatErrorResults.ChannelNotFound();
        }

        // ChatChannelPart refuses team and private channels.
        if (dbChannel.Type is ChannelType.Team or ChannelType.Private)
        {
            return ChatErrorResults.Forbidden("Unauthorized.");
        }

        if (currentUserId != userId)
        {
            return Forbid();
        }

        await notificationServer.LeaveChannel(LazerPlayer.GetOrCreate(playerManager, currentUserId), dbChannel.ChannelId);
        return NoContent();
    }

    /// <summary>
    /// Marks a message as read in a channel. The read marker never moves backwards.
    /// </summary>
    /// <param name="channel">
    /// The channel ID or name.
    /// </param>
    /// <param name="messageId">
    /// The message ID to mark as read.
    /// </param>
    /// <returns>
    /// No content.
    /// </returns>
    [HttpPut("{channel}/mark-as-read/{messageId:int}")]
    [RequireScope("chat.read")]
    public async Task<IActionResult> MarkChannelAsRead(string channel, int messageId)
    {
        int currentUserId = HttpContext.GetUserId();
        ChatChannel? dbChannel = await chatRepository.GetChannelAsync(channel);
        if (dbChannel == null)
        {
            return ChatErrorResults.ChannelNotFound();
        }

        // osu-web's UserChannel::firstOrFail: without a membership row there is
        // nothing to mark as read.
        bool updated = await chatRepository.MarkUserChannelAsReadAsync(dbChannel.ChannelId, currentUserId, messageId);
        return updated ? NoContent() : ChatErrorResults.NotInChannel();
    }

    private async Task<IReadOnlyList<UserCompactDto>> GetVisibleUsersAsync(ChatChannel channel)
    {
        if (channel.Type != ChannelType.PM)
        {
            return [];
        }

        IReadOnlyList<int> participantIds = ChatConventions.TryGetPmUserIds(channel.Name, out int[] userIds)
            ? userIds
            : await chatRepository.GetChannelUserIdsAsync(channel.ChannelId);

        if (participantIds.Count == 0)
        {
            return [];
        }

        IReadOnlyList<User> users = await userRepository.GetByIdsAsync(participantIds);
        return users.Select(UserCompactDto.FromDb).ToList();
    }
}