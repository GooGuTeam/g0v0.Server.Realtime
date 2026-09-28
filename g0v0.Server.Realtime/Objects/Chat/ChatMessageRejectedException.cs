// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Realtime.Objects.Chat;

/// <summary>
/// Thrown when a chat message cannot be sent.
/// </summary>
public class ChatMessageRejectedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChatMessageRejectedException"/> class.
    /// </summary>
    public ChatMessageRejectedException()
        : this(RejectionReason.Empty, "The message could not be sent.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatMessageRejectedException"/> class.
    /// </summary>
    /// <param name="message">The error message reported to the client.</param>
    public ChatMessageRejectedException(string message)
        : this(RejectionReason.Empty, message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatMessageRejectedException"/> class.
    /// </summary>
    /// <param name="message">The error message reported to the client.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public ChatMessageRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = RejectionReason.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatMessageRejectedException"/> class.
    /// </summary>
    /// <param name="reason">The reason the message was rejected.</param>
    /// <param name="message">The error message reported to the client.</param>
    public ChatMessageRejectedException(RejectionReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    /// <summary>
    /// Describes why a chat message was rejected.
    /// </summary>
    public enum RejectionReason
    {
        /// <summary>
        /// The message was empty, reported as <c>422</c>.
        /// </summary>
        Empty,

        /// <summary>
        /// The message exceeded the channel's length limit, reported as <c>422</c>.
        /// </summary>
        TooLong,

        /// <summary>
        /// The sender exceeded the message rate limit, reported as <c>429</c>.
        /// </summary>
        Throttled,
    }

    /// <summary>
    /// Gets the reason the message was rejected.
    /// </summary>
    public RejectionReason Reason { get; }
}