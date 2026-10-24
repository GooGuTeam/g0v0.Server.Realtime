// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Objects.Chat;
using Microsoft.AspNetCore.Mvc;

namespace g0v0.Server.Realtime.Controllers;

/// <summary>
/// Maps chat failures to the JSON error shape of the reference API:
/// <c>{ "error": ..., "msg_key": ... }</c>, mirroring the Python
/// <c>RequestError</c> exception handler.
/// </summary>
internal static class ChatErrorResults
{
    /// <summary>
    /// Builds the <c>404</c> response for a missing channel.
    /// </summary>
    /// <returns>
    /// The response.
    /// </returns>
    internal static IActionResult ChannelNotFound()
    {
        return NotFound("channel_not_found", "Channel not found.");
    }

    /// <summary>
    /// Builds the <c>404</c> response for a channel the user takes no part in.
    /// </summary>
    /// <returns>
    /// The response.
    /// </returns>
    internal static IActionResult NotInChannel()
    {
        return NotFound("you_are_not_in_channel", "You are not in this channel.");
    }

    /// <summary>
    /// Builds the <c>404</c> response for a missing message target user.
    /// </summary>
    /// <returns>
    /// The response.
    /// </returns>
    internal static IActionResult TargetUserNotFound()
    {
        return NotFound("target_user_not_found", "Target user not found.");
    }

    /// <summary>
    /// Builds the <c>403</c> response of an authorization failure.
    /// </summary>
    /// <param name="message">
    /// The error message.
    /// </param>
    /// <param name="msgKey">
    /// The localization key of the error message.
    /// </param>
    /// <returns>
    /// The response.
    /// </returns>
    internal static IActionResult Forbidden(string message, string msgKey = "forbidden")
    {
        return new ObjectResult(new { error = message, msg_key = msgKey })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }

    /// <summary>
    /// Builds the <c>422</c> response used by the chat endpoints.
    /// </summary>
    /// <param name="message">
    /// The error message.
    /// </param>
    /// <param name="msgKey">
    /// The localization key of the error message.
    /// </param>
    /// <returns>
    /// The response.
    /// </returns>
    internal static IActionResult Unprocessable(string message, string msgKey = "invalid_request")
    {
        return new UnprocessableEntityObjectResult(new { error = message, msg_key = msgKey });
    }

    /// <summary>
    /// Builds the response of a rejected message: <c>429</c> when the sender is
    /// throttled, <c>422</c> when the content itself was rejected.
    /// </summary>
    /// <param name="exception">
    /// The rejection.
    /// </param>
    /// <returns>
    /// The response.
    /// </returns>
    internal static IActionResult FromRejection(ChatMessageRejectedException exception)
    {
        return exception.Reason switch
        {
            ChatMessageRejectedException.RejectionReason.Throttled =>
                new ObjectResult(new
                {
                    error = exception.Message,
                    msg_key = "limit_exceeded",
                })
                {
                    StatusCode = StatusCodes.Status429TooManyRequests,
                },
            ChatMessageRejectedException.RejectionReason.TooLong =>
                new UnprocessableEntityObjectResult(new
                {
                    error = exception.Message,
                    msg_key = "message_too_long",
                }),
            _ =>
                new UnprocessableEntityObjectResult(new
                {
                    error = exception.Message,
                    msg_key = "message_empty",
                }),
        };
    }

    private static ObjectResult NotFound(string msgKey, string message)
    {
        return new ObjectResult(new { error = message, msg_key = msgKey })
        {
            StatusCode = StatusCodes.Status404NotFound,
        };
    }
}