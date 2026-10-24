// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;

namespace g0v0.Server.Realtime.Notifications.Bot.Commands;

/// <summary>
/// Score commands: <c>!re</c> (most recent score, including failed) and
/// <c>!pr</c> (most recent passed score).
/// </summary>
public sealed class ScoreCommands(IServiceScopeFactory scopeFactory) : BanchoBotCommandSet(scopeFactory)
{
    /// <summary>
    /// Shows the user's most recent score (including failed attempts).
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
    [BotCommand("re")]
    public Task<string?> RecentAsync(int senderId, string[] args)
        => GetRecentScoreAsync(senderId, includeFailed: true);

    /// <summary>
    /// Shows the user's most recent passed score.
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
    [BotCommand("pr")]
    public Task<string?> PreviousAsync(int senderId, string[] args)
        => GetRecentScoreAsync(senderId, includeFailed: false);

    private async Task<string?> GetRecentScoreAsync(int senderId, bool includeFailed)
    {
        using IServiceScope scope = ScopeFactory.CreateScope();
        IUserRepository userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        IScoreRepository scoreRepository = scope.ServiceProvider.GetRequiredService<IScoreRepository>();

        Common.Database.Models.User? targetUser = await userRepository.GetByIdAsync(senderId);
        if (targetUser == null)
        {
            return "User not found.";
        }

        IReadOnlyList<Score> scores = await scoreRepository.GetRecentByUserIdAndModeAsync(senderId, 0, 1);
        Score? score = scores.FirstOrDefault(candidate => includeFailed || candidate.Passed);
        return score == null
            ? "You have no scores."
            : $"Played at {score.EndedAt:yyyy-MM-dd HH:mm}\n{score.Pp:N2}pp {score.Accuracy:P2} {score.TotalScore:N0}";
    }
}