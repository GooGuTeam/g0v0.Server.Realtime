using g0v0.Server.Common.Configuration;

namespace g0v0.Server.Realtime;

/// <summary>
/// Provides the runtime base path used to resolve configuration files in the web host.
/// </summary>
/// <param name="env">The current host environment.</param>
public class WebPathProvider(IHostEnvironment env) : IPathProvider
{
    /// <inheritdoc />
    public string GetBasePath()
    {
        return env.ContentRootPath;
    }
}