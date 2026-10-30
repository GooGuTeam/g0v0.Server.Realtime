// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using g0v0.Server.Common.Configuration.Attributes;

namespace g0v0.Server.Realtime;

/// <summary>
/// Stores the chat <c>!faq</c> entries read from <c>config/faq.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// The file is optional: when it is absent, <c>!faq</c> reports that no entries are
/// available instead of failing the request that carried the command.
/// </para>
/// <para>
/// Edits require a restart for now: nothing in this server calls
/// <c>ConfigManager.Reload&lt;FaqConfig&gt;()</c>, so the <see cref="ReloadableAttribute"/>
/// markers only take effect once a reload trigger exists.
/// </para>
/// </remarks>
[ConfigFile("faq")]
public class FaqConfig
{
    /// <summary>
    /// Gets or sets the language code whose text is returned when an entry has no
    /// translation for the requested language.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>en</c>, and falls back to <c>en</c> as well when the configured
    /// value is not a two-letter code.
    /// </remarks>
    [Reloadable]
    public string DefaultLanguage { get; set; } = "en";

    /// <summary>
    /// Gets or sets the entries, in the order <c>!faq list</c> reports them.
    /// </summary>
    [Reloadable]
    [SuppressMessage(
        "Maintainability",
        "MA0016:Prefer using collection abstraction instead of implementation",
        Justification = "The JSON configuration contract populates a concrete List, and the command only reads it.")]
    public List<FaqEntry> Entries { get; set; } = [];
}