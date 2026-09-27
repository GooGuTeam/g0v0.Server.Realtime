// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Primitives;

namespace g0v0.Server.Realtime.Controllers;

/// <summary>
/// Reads the parameters of a chat request the way osu-web's
/// <c>get_params(request()->all())</c> does: from a JSON body, a form body or the
/// query string, with nested JSON objects flattened into dot-separated names.
/// </summary>
internal sealed class ChatRequestParameters
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>
    /// Reads the request body (JSON or form) and falls back to the query string.
    /// </summary>
    /// <param name="request">The HTTP request.</param>
    /// <returns>The parsed parameters.</returns>
    public static async Task<ChatRequestParameters> ReadAsync(HttpRequest request)
    {
        ChatRequestParameters parameters = new();

        if (request.HasJsonContentType())
        {
            try
            {
                using JsonDocument document = await JsonDocument.ParseAsync(request.Body);
                Flatten(document.RootElement, prefix: null, parameters._values);
            }
            catch (JsonException)
            {
                // A malformed body is treated as if no body had been sent.
            }
        }
        else if (request.HasFormContentType)
        {
            IFormCollection form = await request.ReadFormAsync();
            foreach (KeyValuePair<string, StringValues> field in form)
            {
                parameters._values[NormalizeKey(field.Key)] = field.Value.ToString();
            }
        }

        // Query parameters only fill the gaps, mirroring request()->all().
        foreach (KeyValuePair<string, StringValues> query in request.Query)
        {
            string key = NormalizeKey(query.Key);
            if (!parameters._values.ContainsKey(key))
            {
                parameters._values[key] = query.Value.ToString();
            }
        }

        return parameters;
    }

    /// <summary>
    /// Gets a string parameter.
    /// </summary>
    /// <param name="name">The parameter name.</param>
    /// <returns>The value, or <see langword="null"/> when absent.</returns>
    public string? GetString(string name) => _values.TryGetValue(name, out string? value) ? value : null;

    /// <summary>
    /// Gets an integer parameter.
    /// </summary>
    /// <param name="name">The parameter name.</param>
    /// <returns>The value, or <see langword="null"/> when absent or malformed.</returns>
    public int? GetInt(string name) =>
        int.TryParse(GetString(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

    /// <summary>
    /// Gets a boolean parameter.
    /// </summary>
    /// <param name="name">The parameter name.</param>
    /// <returns>The value, or <see langword="null"/> when absent or malformed.</returns>
    public bool? GetBool(string name) => bool.TryParse(GetString(name), out bool value) ? value : null;

    private static void Flatten(JsonElement element, string? prefix, Dictionary<string, string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    Flatten(property.Value, prefix == null ? property.Name : $"{prefix}.{property.Name}", values);
                }

                break;

            case JsonValueKind.Array:
                if (prefix != null)
                {
                    values[prefix] = string.Join(',', element.EnumerateArray().Select(static item => item.ToString()));
                }

                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                break;

            default:
                if (prefix != null)
                {
                    values[prefix] = element.ToString();
                }

                break;
        }
    }

    private static string NormalizeKey(string key) =>
        key.EndsWith("[]", StringComparison.Ordinal) ? key[..^2] : key;
}