// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Realtime.Services;

/// <summary>
/// Validates the client hash for a multiplayer playlist item's ruleset.
/// </summary>
public interface IRulesetHashValidator
{
    /// <summary>
    /// Validates that the client has the latest supported version of a ruleset.
    /// </summary>
    /// <param name="rulesetId">The online ruleset ID used by the playlist item.</param>
    /// <param name="clientHashes">The client ruleset hashes keyed by ruleset short name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A validation failure, or <see langword="null"/> when the ruleset is compatible.</returns>
    Task<RulesetHashValidationFailure?> ValidateAsync(
        int rulesetId,
        IReadOnlyDictionary<string, string> clientHashes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the ruleset hash stored in a connected player's state.
    /// </summary>
    /// <param name="rulesetId">The online ruleset ID used by the playlist item.</param>
    /// <param name="playerId">The player whose ruleset hashes should be validated.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A validation failure, or <see langword="null"/> when the ruleset is compatible.</returns>
    Task<RulesetHashValidationFailure?> ValidatePlayerAsync(
        int rulesetId,
        int playerId,
        CancellationToken cancellationToken = default);
}