// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using g0v0.Server.Common.Database.Repository;

namespace g0v0.Server.Realtime.Notifications.Bot.Commands;

/// <summary>
/// Utility commands: <c>!help</c> and <c>!roll</c>.
/// </summary>
public sealed class UtilityCommands(IServiceScopeFactory scopeFactory) : BanchoBotCommandSet(scopeFactory)
{
    /// <summary>
    /// Shows available commands or usage for a specific command.
    /// </summary>
    /// <param name="senderId">
    /// The sender user ID.
    /// </param>
    /// <param name="args">
    /// The command arguments.
    /// </param>
    /// <param name="availableCommands">
    /// The registered command names.
    /// </param>
    /// <returns>
    /// The reply text.
    /// </returns>
    [BotCommand("help")]
    public Task<string?> HelpAsync(int senderId, string[] args, IReadOnlyCollection<string> availableCommands)
    {
        if (args.Length > 0)
        {
            string target = args[0].ToLowerInvariant();
            return Task.FromResult<string?>(
                availableCommands.Contains(target, StringComparer.OrdinalIgnoreCase) ? $"Usage: !{target} [args]" : $"No such command: {target}");
        }

        string[] commands = availableCommands.OrderBy(static c => c, StringComparer.Ordinal).ToArray();
        return Task.FromResult<string?>(
            commands.Length == 0 ? "No available commands" : "Available: " + string.Join(", ", commands.Select(static c => $"!{c}")));
    }

    /// <summary>
    /// Rolls a random number between 1 and the specified max (default 100).
    /// </summary>
    /// <param name="senderId">
    /// The sender user ID.
    /// </param>
    /// <param name="args">
    /// The command arguments.
    /// </param>
    /// <returns>
    /// The reply text.
    /// </returns>
    [BotCommand("roll")]
    public async Task<string?> RollAsync(int senderId, string[] args)
    {
        int max = 100;
        if (args.Length > 0 && int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
        {
            max = parsed;
        }

        int roll = Random.Shared.Next(1, max + 1);
        string? username = await GetUsernameAsync(senderId);
        return $"{username ?? "Unknown"} rolls {roll} point(s)";
    }

    private async Task<string?> GetUsernameAsync(int userId)
    {
        using IServiceScope scope = ScopeFactory.CreateScope();
        IUserRepository userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        return await userRepository.GetUsernameByIdAsync(userId);
    }
}