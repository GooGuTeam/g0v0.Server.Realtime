// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Realtime;

/// <summary>
/// Stores Sentry error-monitoring settings for the realtime server.
/// Managed by <see cref="RealtimeConfig"/> through the <c>"Sentry"</c> section of <c>config/realtime.json</c>.
/// All properties are read once at startup; a restart is required for changes to take effect.
/// </summary>
public class SentryOptions
{
    /// <summary>
    /// Gets a value indicating whether sentry is enabled.
    /// </summary>
    public bool Enabled => !string.IsNullOrEmpty(Dsn);

    /// <summary>
    /// Gets or sets the Sentry DSN (e.g. <c>https://&lt;key&gt;@o&lt;org&gt;.ingest.sentry.io/&lt;project&gt;</c>).
    /// Leave empty to keep Sentry disabled.
    /// </summary>
    public string Dsn { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the environment tag reported to Sentry.
    /// When empty, the value of the <c>ASPNETCORE_ENVIRONMENT</c> variable is used.
    /// </summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the performance-tracing sample rate, between 0.0 (no traces) and 1.0 (all requests).
    /// </summary>
    public double TracesSampleRate { get; set; } = 0.1;

    /// <summary>
    /// Gets or sets a value indicating whether default PII (user IDs, emails, IP addresses) is
    /// attached to events sent to Sentry.
    /// </summary>
    public bool SendDefaultPii { get; set; }
}