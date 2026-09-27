// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Authentication;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Extensions;
using g0v0.Server.Realtime.Notifications;
using g0v0.Server.Realtime.Notifications.Bot;
using g0v0.Server.Realtime.Objects.Chat;
using g0v0.Server.Realtime.Services;
using Microsoft.AspNetCore.Mvc;
using osu.Game.Online.Chat;

namespace g0v0.Server.Realtime.Controllers;

/// <summary>
/// Chat message endpoints.
/// </summary>
[ApiController]
[Route("chat/channels/{channel}/messages")]
public class MessagesController(
    IChatRepository chatRepository,
    IUserRepository userRepository,
    INotificationServer notificationServer,
    ChatChannelDtoFactory chatChannelDtoFactory,
    ConfigManager configManager,
    IBanchoBot banchoBot) : ControllerBase
{
    /// <summary>
    /// Gets messages from a channel (returned in chronological order).
    /// </summary>
    /// <param name="channel">The channel ID or name.</param>
    /// <param name="limit">The maximum number of messages to return (1-50).</param>
    /// <param name="returnObject">Whether to return the <c>{ messages, users }</c> object.</param>
    /// <param name="since">Get messages with an ID greater than this value.</param>
    /// <param name="until">Get messages with an ID less than this value.</param>
    /// <returns>The messages in chronological order.</returns>
    [HttpGet]
    [RequireScope("chat.read")]
    public async Task<IActionResult> GetMessages(
        string channel,
        [FromQuery] int limit = 50,
        [FromQuery(Name = "return_object")] bool returnObject = false,
        [FromQuery] int since = 0,
        [FromQuery] int? until = null)
    {
        int currentUserId = HttpContext.GetUserId();
        ChatChannel? dbChannel = await chatRepository.GetChannelAsync(channel);
        if (dbChannel == null)
        {
            return ChatErrorResults.ChannelNotFound();
        }

        // ChatChannelRead requires a membership row, hidden ones included.
        if (await chatRepository.GetUserChannelAsync(dbChannel.ChannelId, currentUserId) == null)
        {
            return ChatErrorResults.NotInChannel();
        }

        ChatConfig chatConfig = configManager.TryGet<ChatConfig>() ?? new ChatConfig();
        IReadOnlyList<ChatMessage> messages = await chatRepository.GetMessagesAsync(
            dbChannel.ChannelId,
            Math.Clamp(limit, 1, 50),
            since > 0 ? since : null,
            until);

        bool isAnnouncement = dbChannel.Type == ChannelType.Announce;
        IReadOnlyList<ChatMessage> visible = ChatMessageFilter
            .Apply(messages, dbChannel, currentUserId, chatConfig.PublicBacklogLimit)
            .ToList();

        Dictionary<int, User> senders = await LoadSendersAsync(visible);

        return returnObject
            ? Ok(new
            {
                messages = visible.Select(message => ChatMessageDto.FromDb(message, isAnnouncement)).ToList(),
                users = senders.Values.Select(UserCompactDto.FromDb).ToList(),
            })
            : Ok(visible
                .Select(message => ChatMessageDto.FromDb(message, isAnnouncement, CreateSender(message.SenderId, senders)))
                .ToList());
    }

    /// <summary>
    /// Sends a message to a channel.
    /// </summary>
    /// <param name="channel">The channel ID or name.</param>
    /// <returns>The sent message.</returns>
    /// <remarks>
    /// Parameters (<c>message</c>, <c>is_action</c>, <c>uuid</c>) are read from a
    /// JSON body, a form body or the query string.
    /// </remarks>
    [HttpPost]
    [RequireScope("chat.write")]
    public async Task<IActionResult> SendMessage(string channel)
    {
        int currentUserId = HttpContext.GetUserId();
        ChatRequestParameters parameters = await ChatRequestParameters.ReadAsync(Request);
        ChatChannel? dbChannel = await chatRepository.GetChannelAsync(channel);
        if (dbChannel == null)
        {
            return ChatErrorResults.ChannelNotFound();
        }

        // ChatChannelSend = ChatChannelRead + ChatChannelCanMessage.
        if (await chatRepository.GetUserChannelAsync(dbChannel.ChannelId, currentUserId) == null)
        {
            return ChatErrorResults.NotInChannel();
        }

        string? canMessageError = await chatChannelDtoFactory.GetCanMessageErrorAsync(dbChannel, currentUserId);
        if (canMessageError != null)
        {
            return ChatErrorResults.Forbidden(canMessageError);
        }

        // The legacy per-channel silence still blocks posting.
        if (await chatRepository.IsUserSilencedAsync(dbChannel.ChannelId, currentUserId))
        {
            return ChatErrorResults.Forbidden(
                "You cannot send messages while silenced, restricted or banned.",
                "messaging_restricted");
        }

        ChatMessage message;
        try
        {
            message = await notificationServer.SendMessage(
                dbChannel.ChannelId,
                currentUserId,
                parameters.GetString("message") ?? string.Empty,
                parameters.GetBool("is_action") ?? false,
                parameters.GetString("uuid"));
        }
        catch (ChatMessageRejectedException exception)
        {
            return ChatErrorResults.FromRejection(exception);
        }

        if (ChatConventions.IsUserCommand(message.Content))
        {
            await banchoBot.TryHandleAsync(currentUserId, dbChannel.ChannelId, message.Content, dbChannel.Type);
        }

        Dictionary<int, User> senders = await LoadSendersAsync([message]);
        return Ok(ChatMessageDto.FromDb(
            message,
            dbChannel.Type == ChannelType.Announce,
            CreateSender(message.SenderId, senders)));
    }

    private static UserCompactDto? CreateSender(int senderId, Dictionary<int, User> senders)
    {
        return senders.TryGetValue(senderId, out User? sender) ? UserCompactDto.FromDb(sender) : null;
    }

    private async Task<Dictionary<int, User>> LoadSendersAsync(IReadOnlyList<ChatMessage> messages)
    {
        List<int> senderIds = messages.Select(static message => message.SenderId).Distinct().ToList();
        if (senderIds.Count == 0)
        {
            return [];
        }

        IReadOnlyList<User> senders = await userRepository.GetByIdsAsync(senderIds);
        return senders.ToDictionary(static user => user.Id);
    }
}