// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration;
using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Notifications.Bot.Commands;

/// <summary>
/// FAQ command: <c>!faq [lang:]&lt;entry&gt;</c> replies with a stored tip and
/// <c>!faq list</c> lists every configured entry.
/// </summary>
/// <remarks>
/// The answers live in the optional <c>config/faq.json</c> file; see <see cref="FaqConfig"/>.
/// The configuration is read on every invocation, so a future reload trigger takes effect
/// without touching this class.
/// </remarks>
/// <param name="scopeFactory">The service scope factory.</param>
/// <param name="configManager">The configuration manager that provides <see cref="FaqConfig"/>.</param>
/// <param name="logger">The logger.</param>
public sealed class FaqCommands(
    IServiceScopeFactory scopeFactory,
    ConfigManager configManager,
    ILogger<FaqCommands> logger) : BanchoBotCommandSet(scopeFactory)
{
    /// <summary>
    /// Replies with the requested FAQ entry, the entry list or a usage hint.
    /// </summary>
    /// <param name="args">The command arguments.</param>
    /// <returns>The reply text.</returns>
    [BotCommand("faq")]
    public string? Faq(string[] args)
    {
        try
        {
            return FaqCatalog.BuildReply(configManager.TryGet<FaqConfig>(), args);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // The file is handwritten by maintainers, so a syntax error is a realistic
            // outcome. Report it without failing the chat message that carried the command.
            logger.LogWarning(exception, "Failed to load the FAQ configuration.");
            return FaqCatalog.UnavailableReply;
        }
    }
}