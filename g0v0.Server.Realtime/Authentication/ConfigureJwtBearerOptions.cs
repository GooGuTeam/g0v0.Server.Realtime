// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Text;
using g0v0.Server.Common.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace g0v0.Server.Realtime.Authentication;

/// <summary>
/// Configures <see cref="JwtBearerOptions"/> using DI-resolved services.
/// </summary>
public class ConfigureJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly ConfigurationManager<GeneralConfiguration> _config;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseJwtTokenHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigureJwtBearerOptions"/> class.
    /// </summary>
    /// <param name="config">The configuration manager for general settings.</param>
    /// <param name="serviceProvider">The root service provider used to resolve scoped dependencies.</param>
    /// <param name="logger">The logger for token handler setup diagnostics.</param>
    public ConfigureJwtBearerOptions(
        ConfigurationManager<GeneralConfiguration> config,
        IServiceProvider serviceProvider,
        ILogger<DatabaseJwtTokenHandler> logger)
    {
        _config = config;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Configures named JWT bearer options.
    /// </summary>
    /// <param name="name">The authentication scheme name.</param>
    /// <param name="options">The options to configure.</param>
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        Configure(options);
    }

    /// <summary>
    /// Configures the default JWT bearer options.
    /// </summary>
    /// <param name="options">The options to configure.</param>
    public void Configure(JwtBearerOptions options)
    {
        var jwtConfig = _config.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfig.JwtSecretKey));

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateAudience = !string.IsNullOrEmpty(jwtConfig.JwtAudience),
            ValidAudience = jwtConfig.JwtAudience,
            ValidateIssuer = !string.IsNullOrEmpty(jwtConfig.JwtIssuer),
            ValidIssuer = jwtConfig.JwtIssuer,
            ValidateLifetime = true,
            NameClaimType = "sub",
        };

        // Replace default token handler with our database-backed handler
        options.TokenHandlers.Clear();
        options.TokenHandlers.Add(new DatabaseJwtTokenHandler(_serviceProvider, _logger));

        options.SaveToken = true;
    }
}