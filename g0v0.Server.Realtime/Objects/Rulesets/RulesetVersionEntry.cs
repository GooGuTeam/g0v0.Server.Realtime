// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace g0v0.Server.Realtime.Objects.Rulesets;

/// <summary>
/// Describes the hashes published for the available versions of a custom ruleset.
/// </summary>
public class RulesetVersionEntry
{
    /// <summary>
    /// Gets or sets the latest supported ruleset version.
    /// </summary>
    [JsonProperty("latest-version")]
    public string LatestVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mapping from ruleset versions to assembly hashes.
    /// </summary>
    [JsonProperty("versions")]
    [SuppressMessage(
        "Maintainability",
        "MA0016:Prefer using collection abstraction instead of implementation",
        Justification = "The IPC contract requires a concrete Dictionary value.")]
    public Dictionary<string, string> Versions { get; set; } = [];
}