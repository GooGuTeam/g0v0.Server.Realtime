// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Reflection;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Notifications.Bot.Commands;
using g0v0.Server.Realtime.Objects.Players;
using osu.Game.Online.Chat;

namespace g0v0.Server.Realtime.Notifications.Bot;

/// <summary>
/// Handles chat bot commands prefixed with <c>!</c>, mirroring the lazer API
/// <c>BanchoBot</c> implementation.
/// </summary>
/// <remarks>
/// Commands are registered declaratively with the <see cref="BotCommandAttribute"/>
/// on methods of <see cref="BanchoBotCommandSet"/> subclasses (see
/// <see cref="UtilityCommands"/>, <see cref="ScoreCommands"/> and
/// <see cref="MultiplayerCommands"/>).
/// Only replies to <see cref="ChannelType.Public"/> channels are delivered
/// through a PM channel with the bot; replies in every other channel type are
/// delivered in the channel the command was sent in, as in the reference
/// implementation's <c>send_reply</c>.
/// </remarks>
public class BanchoBot(
    ILogger<BanchoBot> logger,
    IServiceScopeFactory scopeFactory,
    INotificationServer notificationServer,
    PlayerManager playerManager) : IBanchoBot
{
    private readonly ConcurrentDictionary<string, CommandHandler> _handlers = new(StringComparer.Ordinal);

    /// <summary>
    /// Determines whether replies to a channel type should be delivered through
    /// a PM channel. Only public-channel commands are replied via PM, matching
    /// the reference implementation's <c>send_reply</c> behaviour; every other
    /// channel type (private conversations included) receives the reply in the
    /// channel the command was sent in.
    /// </summary>
    /// <param name="channelType">The channel type.</param>
    /// <returns><see langword="true"/> when the reply should be sent through PM.</returns>
    public static bool UsesPmReply(ChannelType channelType)
    {
        return channelType == ChannelType.Public;
    }

    /// <summary>
    /// Parses a message for a command.
    /// </summary>
    /// <param name="content">The message content.</param>
    /// <returns>The command name and arguments, or <see langword="null"/> when the message is not a command.</returns>
    public static (string? Command, List<string> Arguments) Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || !content.StartsWith('!'))
        {
            return (null, []);
        }

        string[] parts = content[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0
            ? (null, [])
            : (parts[0].ToLowerInvariant(), parts.Skip(1).ToList());
    }

    /// <summary>
    /// Discovers and registers all commands from the given command sets.
    /// </summary>
    /// <param name="commandSets">The command sets to register.</param>
    public void Initialize(IEnumerable<BanchoBotCommandSet> commandSets)
    {
        foreach (BanchoBotCommandSet commandSet in commandSets)
        {
            RegisterCommandSet(commandSet);
        }
    }

    /// <summary>
    /// Registers all <see cref="BotCommandAttribute"/>-marked methods of a command set.
    /// </summary>
    /// <param name="commandSet">The command set instance.</param>
    public void RegisterCommandSet(BanchoBotCommandSet commandSet)
    {
        foreach (MethodInfo method in commandSet.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            BotCommandAttribute? attribute = method.GetCustomAttribute<BotCommandAttribute>();
            if (attribute == null)
            {
                continue;
            }

            _handlers[attribute.Name] = new CommandHandler(commandSet, method);
        }
    }

    /// <inheritdoc />
    public async Task TryHandleAsync(int senderId, int channelId, string content, ChannelType channelType)
    {
        (string? command, List<string> args) = Parse(content);
        if (command == null)
        {
            return;
        }

        if (!_handlers.TryGetValue(command, out CommandHandler? handler))
        {
            return;
        }

        BotCommandContext context = new(senderId, channelId, channelType, args);

        string? reply;
        try
        {
            reply = await handler.InvokeAsync(context, _handlers.Keys.ToArray());
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "BanchoBot command !{Command} failed for user {UserId} in channel {ChannelId}.",
                command,
                senderId,
                channelId);
            reply = "Unknown error occured.";
        }

        if (reply == null)
        {
            return;
        }

        await SendReplyAsync(senderId, channelId, reply, UsesPmReply(channelType));
    }

    private async Task SendReplyAsync(int senderId, int channelId, string content, bool isPublicChannel)
    {
        if (!isPublicChannel)
        {
            await SendToChannelAsync(channelId, content);
            return;
        }

        using IServiceScope scope = scopeFactory.CreateScope();
        IChatRepository chatRepository = scope.ServiceProvider.GetRequiredService<IChatRepository>();
        ChatChannel? pmChannel = await chatRepository.GetPmChannelAsync(senderId, IBanchoBot.BanchoBotId)
            ?? await chatRepository.CreatePmChannelAsync(senderId, IBanchoBot.BanchoBotId);

        // osu-web's Channel::addUser: the command sender joins the bot PM
        // channel (with a chat.channel.join announcement) so the reply is
        // visible to them.
        await notificationServer.JoinChannel(
            LazerPlayer.GetOrCreate(playerManager, senderId),
            pmChannel.ChannelId);

        await SendToChannelAsync(pmChannel.ChannelId, content);
    }

    private Task<ChatMessage> SendToChannelAsync(int channelId, string content)
    {
        return notificationServer.SendMessage(channelId, IBanchoBot.BanchoBotId, content, isAction: false);
    }

    private sealed class CommandHandler(BanchoBotCommandSet commandSet, MethodInfo method)
    {
        public async Task<string?> InvokeAsync(BotCommandContext context, IReadOnlyCollection<string> availableCommands)
        {
            ParameterInfo[] parameters = method.GetParameters();
            object?[] arguments = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                arguments[i] = parameters[i].ParameterType switch
                {
                    _ when parameters[i].ParameterType == typeof(BotCommandContext) => context,
                    _ when parameters[i].ParameterType == typeof(int) => context.SenderId,
                    _ when parameters[i].ParameterType == typeof(string[]) => context.Arguments.ToArray(),
                    _ when parameters[i].ParameterType == typeof(IReadOnlyCollection<string>) => availableCommands,
                    _ => null,
                };
            }

            object? result = method.Invoke(commandSet, arguments);
            return result is Task<string?> task ? await task : result as string;
        }
    }
}