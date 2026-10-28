// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;

namespace g0v0.Server.Realtime;

/// <summary>
/// A single <c>!faq</c> entry: one language independent identifier with an answer per language.
/// </summary>
/// <remarks>
/// The identifier must not contain spaces, because chat commands are split on whitespace
/// before the arguments reach the command handler. Identifiers are matched case insensitively.
/// </remarks>
public class FaqEntry
{
    /// <summary>
    /// Gets or sets the identifier matched by <c>!faq &lt;entry&gt;</c>.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the answer text keyed by language code, for example <c>en</c> or <c>zh</c>.
    /// </summary>
    /// <remarks>
    /// Keys are matched by their primary language subtag, so <c>zh-CN</c> answers a
    /// <c>zh:</c> request. Blank values are treated as missing.
    /// </remarks>
    [SuppressMessage(
        "Maintainability",
        "MA0016:Prefer using collection abstraction instead of implementation",
        Justification = "The JSON configuration contract populates a concrete Dictionary, and the command only reads it.")]
    public Dictionary<string, string> Translations { get; set; } = [];
}