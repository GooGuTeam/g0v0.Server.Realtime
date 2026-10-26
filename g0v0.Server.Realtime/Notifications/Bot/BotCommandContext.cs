// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using osu.Game.Online.Chat;

namespace g0v0.Server.Realtime.Notifications.Bot;

/// <summary>
/// Describes the chat message a <see cref="IBanchoBot"/> command was invoked from.
/// </summary>
/// <remarks>
/// Command methods registered with <see cref="Commands.BotCommandAttribute"/> may
/// take a parameter of this type to inspect the originating channel; an
/// <see cref="int"/> parameter still receives only the sender's user ID.
/// </remarks>
/// <param name="SenderId">The user ID of the sender.</param>
/// <param name="ChannelId">The ID of the channel the message was sent in.</param>
/// <param name="ChannelType">The type of the channel the message was sent in.</param>
/// <param name="Arguments">The command arguments, excluding the command name.</param>
public sealed record BotCommandContext(
    int SenderId,
    int ChannelId,
    ChannelType ChannelType,
    IReadOnlyList<string> Arguments);