// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;

namespace g0v0.Server.Realtime.Notifications.Bot.Commands;

/// <summary>
/// Parses <c>!faq</c> arguments and resolves them against the configured entries.
/// </summary>
/// <remarks>
/// Everything here is a pure function over <see cref="FaqConfig"/>, so the argument
/// parsing, the language fallback and the reply texts can be tested without a file
/// system or a chat server.
/// </remarks>
internal static class FaqCatalog
{
    /// <summary>
    /// The reply used when the command is sent without an argument.
    /// </summary>
    internal const string UsageReply = "Usage: !faq [lang:]<entry>. Try !faq list to see all entries.";

    /// <summary>
    /// The reply used by <c>!faq list</c> when no usable entry is configured.
    /// </summary>
    internal const string EmptyReply = "No FAQ entries available.";

    /// <summary>
    /// The reply used when the requested entry does not exist.
    /// </summary>
    internal const string NotFoundReply = "Entry not found!";

    /// <summary>
    /// The reply used when the configuration file exists but cannot be read.
    /// </summary>
    internal const string UnavailableReply = "The FAQ is currently unavailable. Please try again later.";

    private const string ListCommand = "list";

    private const string ListPrefix = "Available entries: ";

    private const string FallbackLanguage = "en";

    private const int MaximumLanguageTagLength = 12;

    /// <summary>
    /// Builds the reply of a <c>!faq</c> command.
    /// </summary>
    /// <param name="config">The loaded configuration, or <see langword="null"/> when the file is absent.</param>
    /// <param name="arguments">The command arguments, as split by <see cref="BanchoBot"/>.</param>
    /// <returns>The reply text, which is never <see langword="null"/>.</returns>
    internal static string BuildReply(FaqConfig? config, IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return UsageReply;
        }

        List<FaqEntry> entries = GetEntries(config);
        (string? language, string id) = ParseQuery(arguments[0]);

        // A language prefix without an entry ("!faq en:") is most likely a typo, so the
        // usage hint is more useful than "Entry not found!".
        if (string.IsNullOrWhiteSpace(id))
        {
            return UsageReply;
        }

        if (string.Equals(id, ListCommand, StringComparison.OrdinalIgnoreCase))
        {
            return entries.Count == 0
                ? EmptyReply
                : ListPrefix + string.Join(", ", entries.Select(static entry => entry.Id.Trim())) + ".";
        }

        foreach (FaqEntry entry in entries.Where(entry => string.Equals(entry.Id.Trim(), id, StringComparison.OrdinalIgnoreCase)))
        {
            return ResolveTranslation(entry, language, config?.DefaultLanguage) ?? NotFoundReply;
        }

        return NotFoundReply;
    }

    /// <summary>
    /// Returns the entries that can actually answer a query, in configuration order.
    /// </summary>
    /// <param name="config">The loaded configuration, or <see langword="null"/> when the file is absent.</param>
    /// <returns>The usable entries.</returns>
    /// <remarks>
    /// Entries without an identifier or without a single non-blank translation are
    /// skipped, so an entry that <c>!faq list</c> reports can always be resolved.
    /// Duplicate identifiers (case insensitive) keep the first occurrence.
    /// </remarks>
    internal static List<FaqEntry> GetEntries(FaqConfig? config)
    {
        List<FaqEntry> entries = [];
        List<FaqEntry>? configured = config?.Entries;

        if (configured == null)
        {
            return entries;
        }

        HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);

        entries.AddRange(configured.Where(entry => !string.IsNullOrWhiteSpace(entry.Id) && HasTranslation(entry) && seenIds.Add(entry.Id.Trim())));

        return entries;
    }

    /// <summary>
    /// Splits a <c>[lang:]&lt;entry&gt;</c> argument into its language and entry parts.
    /// </summary>
    /// <param name="query">The raw command argument.</param>
    /// <returns>
    /// The requested two letter language code (or <see langword="null"/> when the argument
    /// carries no language prefix) and the remaining entry identifier.
    /// </returns>
    /// <remarks>
    /// The prefix is only treated as a language when it looks like a language tag; anything
    /// else (including an identifier that merely contains a colon) is kept in the identifier,
    /// which then simply fails to match. A leading colon is a prefix the sender left empty.
    /// </remarks>
    internal static (string? Language, string Id) ParseQuery(string query)
    {
        string trimmed = query.Trim();
        int separator = trimmed.IndexOf(':');

        if (separator > 0 && IsLanguageTag(trimmed.AsSpan(0, separator)))
        {
            return (trimmed[..2].ToLowerInvariant(), trimmed[(separator + 1)..].Trim());
        }

        return (null, separator == 0 ? trimmed[1..].Trim() : trimmed);
    }

    /// <summary>
    /// Picks the text of an entry: the requested language, then the default language, then
    /// any translation so an entry listed by <c>!faq list</c> can always answer.
    /// </summary>
    /// <param name="entry">The entry to read.</param>
    /// <param name="requestedLanguage">The language requested by the sender, if any.</param>
    /// <param name="defaultLanguage">The configured fallback language, if any.</param>
    /// <returns>The answer text, or <see langword="null"/> when the entry has no usable text.</returns>
    internal static string? ResolveTranslation(FaqEntry entry, string? requestedLanguage, string? defaultLanguage)
    {
        Dictionary<string, string>? translations = entry.Translations;

        if (translations.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(requestedLanguage)
            && TryGetTranslation(translations, requestedLanguage, out string? requested))
        {
            return requested;
        }

        string fallbackLanguage = string.IsNullOrWhiteSpace(defaultLanguage) ? FallbackLanguage : defaultLanguage;

        if (TryGetTranslation(translations, fallbackLanguage, out string? fallback))
        {
            return fallback;
        }

        // Ordering by language code keeps the last resort deterministic instead of
        // depending on the dictionary's enumeration order.
        foreach (string language in translations.Keys.OrderBy(static key => key, StringComparer.Ordinal))
        {
            if (TryGetTranslation(translations, language, out string? any))
            {
                return any;
            }
        }

        return null;
    }

    private static bool HasTranslation(FaqEntry entry)
        => entry.Translations.Values.Any(static value => !string.IsNullOrWhiteSpace(value));

    private static bool TryGetTranslation(
        Dictionary<string, string> translations,
        string language,
        [NotNullWhen(true)] out string? text)
    {
        string? primaryLanguage = NormalizeLanguage(language);

        if (primaryLanguage != null)
        {
            foreach ((string key, string value) in translations)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!string.Equals(NormalizeLanguage(key), primaryLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                text = value.Trim();
                return true;
            }
        }

        text = null;
        return false;
    }

    /// <summary>
    /// Reduces a language code to its primary, lower case two letter subtag: <c>zh-CN</c>
    /// and <c>ZH</c> both become <c>zh</c>.
    /// </summary>
    /// <param name="language">The language code.</param>
    /// <returns>The primary subtag, or <see langword="null"/> when it is not a language code.</returns>
    private static string? NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        ReadOnlySpan<char> trimmed = language.AsSpan().Trim();

        return trimmed.Length >= 2 && char.IsAsciiLetter(trimmed[0]) && char.IsAsciiLetter(trimmed[1])
            ? trimmed[..2].ToString().ToLowerInvariant()
            : null;
    }

    /// <summary>
    /// Determines whether the text looks like a language tag: two ASCII letters followed by
    /// optional dash separated subtags, for example <c>en</c>, <c>zh-CN</c> or <c>zh-Hant-TW</c>.
    /// </summary>
    /// <param name="tag">The candidate tag.</param>
    /// <returns><see langword="true"/> when the text is shaped like a language tag.</returns>
    private static bool IsLanguageTag(ReadOnlySpan<char> tag)
    {
        if (tag.Length < 2
            || tag.Length > MaximumLanguageTagLength
            || !char.IsAsciiLetter(tag[0])
            || !char.IsAsciiLetter(tag[1]))
        {
            return false;
        }

        if (tag.Length == 2)
        {
            return true;
        }

        if (tag[2] != '-')
        {
            return false;
        }

        for (int i = 3; i < tag.Length; i++)
        {
            if (!char.IsAsciiLetterOrDigit(tag[i]) && tag[i] != '-')
            {
                return false;
            }
        }

        return tag.Length > 3;
    }
}