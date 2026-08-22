// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Players;
using g0v0.Server.Realtime.Objects.Rulesets;
using Microsoft.Extensions.Caching.Memory;
using osu.Game.Rulesets;

namespace g0v0.Server.Realtime.Services;

/// <summary>
/// Validates multiplayer client ruleset hashes against metadata supplied by the lazer server.
/// </summary>
public sealed class RulesetHashValidator(
    RulesetManager rulesetManager,
    InterProcessCommunicationClient ipcClient,
    PlayerManager playerManager,
    IMemoryCache memoryCache,
    ILogger<RulesetHashValidator> logger) : IRulesetHashValidator
{
    private const string GetRulesetHashesRequest = "get_ruleset_hashes";
    private const string ServerNotSupported = "server-not-supported";
    private const string RulesetHashesCacheKey = "ruleset-hashes";

    private static readonly TimeSpan RulesetHashesCacheDuration = TimeSpan.FromHours(24);
    private static readonly Dictionary<string, string> EmptyRulesetHashes = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<RulesetHashValidationFailure?> ValidateAsync(
        int rulesetId,
        IReadOnlyDictionary<string, string> clientHashes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clientHashes);

        Ruleset ruleset;
        try
        {
            ruleset = rulesetManager.GetRuleset(rulesetId);
        }
        catch (ArgumentException)
        {
            return new RulesetHashValidationFailure(
                rulesetId.ToString(CultureInfo.InvariantCulture),
                ServerNotSupported);
        }

        if (IsOfficial(ruleset.ShortName))
        {
            return null;
        }

        clientHashes.TryGetValue(ruleset.ShortName, out string? clientHash);
        return await ValidateAsync(ruleset.ShortName, clientHash ?? string.Empty, cancellationToken);
    }

    /// <inheritdoc />
    public Task<RulesetHashValidationFailure?> ValidatePlayerAsync(
        int rulesetId,
        int playerId,
        CancellationToken cancellationToken = default)
    {
        LazerPlayer? player = playerManager.GetPlayer<LazerPlayer>(playerId, "Lazer");
        return ValidateAsync(
            rulesetId,
            player?.State.RulesetHashes ?? EmptyRulesetHashes,
            cancellationToken);
    }

    internal async Task<RulesetHashValidationFailure?> ValidateAsync(
        string rulesetName,
        string clientHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesetName);
        ArgumentNullException.ThrowIfNull(clientHash);

        if (IsOfficial(rulesetName))
        {
            return null;
        }

        IReadOnlyDictionary<string, RulesetVersionEntry> rulesetHashes =
            await GetRulesetHashesAsync(cancellationToken);

        if (!rulesetHashes.TryGetValue(rulesetName, out RulesetVersionEntry? entry))
        {
            logger.LogWarning("No hash metadata was found for ruleset {RulesetName}.", rulesetName);
            return new RulesetHashValidationFailure(rulesetName, ServerNotSupported);
        }

        if (string.IsNullOrWhiteSpace(entry.LatestVersion)
            || entry.Versions == null
            || !entry.Versions.TryGetValue(entry.LatestVersion, out string? currentHash)
            || string.IsNullOrWhiteSpace(currentHash))
        {
            logger.LogWarning("Hash metadata for ruleset {RulesetName} does not contain its latest version.", rulesetName);
            return new RulesetHashValidationFailure(rulesetName, ServerNotSupported);
        }

        if (string.Equals(currentHash, clientHash, StringComparison.Ordinal))
        {
            return null;
        }

        logger.LogWarning(
            "Hash mismatch for ruleset {RulesetName}: server hash {ServerHash}, client hash {ClientHash}.",
            rulesetName,
            currentHash,
            clientHash);
        return new RulesetHashValidationFailure(rulesetName, entry.LatestVersion);
    }

    private static bool IsOfficial(string rulesetName)
        => rulesetName is "osu" or "taiko" or "fruits" or "catch" or "mania";

    private async Task<IReadOnlyDictionary<string, RulesetVersionEntry>> GetRulesetHashesAsync(
        CancellationToken cancellationToken)
    {
        Dictionary<string, RulesetVersionEntry>? rulesetHashes =
            await memoryCache.GetOrCreateAsync(
                RulesetHashesCacheKey,
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = RulesetHashesCacheDuration;
                    return await ipcClient.RequestAsync<Dictionary<string, RulesetVersionEntry>>(
                               ServerIdentify.Lazer,
                               GetRulesetHashesRequest,
                               cancellationToken: cancellationToken)
                           ?? throw new InvalidOperationException(
                               "The lazer server returned no ruleset hash metadata.");
                });

        return rulesetHashes
               ?? throw new InvalidOperationException("The ruleset hash metadata cache returned no value.");
    }
}