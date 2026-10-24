// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Realtime.Services;

/// <summary>
/// Describes a client ruleset hash that is missing, unsupported, or outdated.
/// </summary>
/// <param name="RulesetName">
/// The ruleset short name.
/// </param>
/// <param name="ServerVersion">
/// The latest server-supported version, or <c>server-not-supported</c>.
/// </param>
public sealed record RulesetHashValidationFailure(string RulesetName, string ServerVersion);