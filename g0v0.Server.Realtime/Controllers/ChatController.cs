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

namespace g0v0.Server.Realtime.Controllers;

/// <summary>
/// Chat keepalive, presence and new-conversation endpoints.
/// </summary>
[ApiController]
[Route("chat")]
public class ChatController(
    IChatRepository chatRepository,
    IUserRepository userRepository,
    INotificationServer notificationServer,
    ChatChannelDtoFactory chatChannelDtoFactory,
    ChatPmStartChecker pmStartChecker,
    PlayerManager playerManager) : ControllerBase
{
    /// <summary>
    /// Keeps the chat connection alive and returns recent silences.
    /// </summary>
    /// <returns>The keepalive response.</returns>
    /// <remarks>
    /// Parameters (<c>history_since</c>, <c>since</c>) are read from a JSON body,
    /// a form body or the query string, as in osu-web.
    /// </remarks>
    [HttpPost("ack")]
    [RequireScope("chat.read")]
    public async Task<IActionResult> KeepAlive()
    {
        int currentUserId = HttpContext.GetUserId();
        ChatRequestParameters parameters = await ChatRequestParameters.ReadAsync(Request);
        int? historySince = parameters.GetInt("history_since");
        int? since = parameters.GetInt("since");

        // Refreshes the activity markers of the public channels the user is in.
        await notificationServer.Ack(LazerPlayer.GetOrCreate(playerManager, currentUserId));

        return Ok(new { silences = await GetSilencesAsync(historySince, since) });
    }

    /// <summary>
    /// Starts a new private message conversation with another user.
    /// </summary>
    /// <returns>The channel, the sent message and the new channel ID.</returns>
    /// <remarks>
    /// Parameters (<c>target_id</c>, <c>message</c>, <c>is_action</c>,
    /// <c>uuid</c>) are read from a JSON body, a form body or the query string.
    /// </remarks>
    [HttpPost("new")]
    [RequireScope("chat.write")]
    public async Task<IActionResult> StartPrivateConversation()
    {
        int currentUserId = HttpContext.GetUserId();
        ChatRequestParameters parameters = await ChatRequestParameters.ReadAsync(Request);
        int? targetId = parameters.GetInt("target_id");

        if (targetId == null)
        {
            return ChatErrorResults.Unprocessable("target user not found");
        }

        User? target = await userRepository.GetByIdAsync(targetId.Value);
        if (target == null)
        {
            return ChatErrorResults.TargetUserNotFound();
        }

        if (target.Id == currentUserId)
        {
            return ChatErrorResults.Unprocessable("can't send message to same user");
        }

        string? pmError = await pmStartChecker.CheckAsync(currentUserId, target.Id);
        if (pmError != null)
        {
            return ChatErrorResults.Forbidden(pmError);
        }

        ChatChannel? channel = await chatRepository.GetPmChannelAsync(currentUserId, target.Id);
        if (channel == null)
        {
            // Channel::createPM adds both users to the conversation.
            channel = await chatRepository.CreatePmChannelAsync(currentUserId, target.Id);
            await chatRepository.AddUserChannelAsync(channel.ChannelId, currentUserId);
            await chatRepository.AddUserChannelAsync(channel.ChannelId, target.Id);
        }

        string? canMessageError = await chatChannelDtoFactory.GetCanMessageErrorAsync(channel, currentUserId);
        if (canMessageError != null)
        {
            return ChatErrorResults.Forbidden(canMessageError);
        }

        ChatMessage message;
        try
        {
            message = await notificationServer.SendMessage(
                channel.ChannelId,
                currentUserId,
                parameters.GetString("message") ?? string.Empty,
                parameters.GetBool("is_action") ?? false,
                parameters.GetString("uuid"));
        }
        catch (ChatMessageRejectedException exception)
        {
            return ChatErrorResults.FromRejection(exception);
        }

        ChatUserChannel? membership = await chatRepository.GetUserChannelAsync(channel.ChannelId, currentUserId);
        IReadOnlyList<User> senders = await userRepository.GetByIdsAsync([currentUserId]);

        return Ok(new
        {
            channel = await chatChannelDtoFactory.CreateChannelAsync(channel, currentUserId, membership),
            message = ChatMessageDto.FromDb(
                message,
                isAnnouncement: false,
                senders.Count > 0 ? UserCompactDto.FromDb(senders[0]) : null),
            new_channel_id = channel.ChannelId,
        });
    }

    /// <summary>
    /// Gets the current user's channel presence.
    /// </summary>
    /// <returns>The user's channels.</returns>
    [HttpGet("presence")]
    [RequireScope("chat.read")]
    public async Task<IActionResult> GetPresence()
    {
        int currentUserId = HttpContext.GetUserId();
        return Ok(await chatChannelDtoFactory.CreateChannelListAsync(currentUserId, includeRecentMessages: true));
    }

    /// <summary>
    /// Gets the current user's channel presence and silences.
    /// </summary>
    /// <param name="historySince">Get silence records after this silence ID.</param>
    /// <param name="since">Get silence records after this message ID.</param>
    /// <param name="includes">The types of updates to include.</param>
    /// <returns>The presence and silence updates.</returns>
    [HttpGet("updates")]
    [RequireScope("chat.read")]
    public async Task<IActionResult> GetUpdates(
        [FromQuery] int? historySince = null,
        [FromQuery] int? since = null,
        [FromQuery(Name = "includes[]")] string[]? includes = null)
    {
        includes ??= ["presence", "silences"];
        int currentUserId = HttpContext.GetUserId();

        bool includePresence = includes.Contains("presence", StringComparer.Ordinal);
        bool includeSilences = includes.Contains("silences", StringComparer.Ordinal);

        object? presence = includePresence ? await chatChannelDtoFactory.CreateChannelListAsync(currentUserId) : null;
        object? silences = includeSilences ? await GetSilencesAsync(historySince, since) : null;

        return Ok(new
        {
            presence,
            messages = Array.Empty<object>(),
            silences,
        });
    }

    private async Task<IEnumerable<UserSilenceDto>> GetSilencesAsync(int? historySince, int? since)
    {
        DateTimeOffset? bannedSince = since.HasValue ? await GetMessageTimestampAsync(since.Value) : null;
        IReadOnlyList<SilenceUser> silences = await chatRepository.GetSilencesAsync(historySince, bannedSince);
        return silences.Select(static silence => new UserSilenceDto { Id = silence.Id, UserId = silence.UserId });
    }

    private async Task<DateTimeOffset?> GetMessageTimestampAsync(int messageId)
    {
        ChatMessage? message = await chatRepository.GetMessageAsync(messageId);
        return message?.Timestamp;
    }
}