// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Realtime.Notifications.Bot.Commands;

/// <summary>
/// Marks a method as a BanchoBot chat command.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class BotCommandAttribute(string name) : Attribute
{
    /// <summary>
    /// Gets the command name (without the <c>!</c> prefix).
    /// </summary>
    public string Name { get; } = name;
}