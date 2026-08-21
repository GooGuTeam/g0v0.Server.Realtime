// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.MySQL;
using g0v0.Server.Common.Database.PostgreSQL;
using g0v0.Server.Common.Extensions;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Realtime.Hubs;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Rooms;
using g0v0.Server.Realtime.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using osu.Game.Online;
using ConfigurationManager = g0v0.Server.Common.Configuration.ConfigurationManager;

namespace g0v0.Server.Realtime;

/// <summary>
/// Application entry point for the g0v0 realtime API server.
/// </summary>
public static class Program
{
    private static readonly Action<HubOptions> ConfigureClientHubOptions = options =>
    {
        // JSON hub protocol is enabled by default, but we use MessagePack.
        // Some models are not compatible with the JSON protocol, so we should never negotiate it.
        options.SupportedProtocols?.Remove("json");
    };

    /// <summary>
    /// Configures and runs the web host.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        ConfigureServices(builder);
        ConfigureApp(builder.Build()).Run();
    }

    /// <summary>
    /// Registers all services required by the realtime server.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSingleton<IPathProvider, WebPathProvider>();

        ConfigurationManager configManager = new(builder.Environment.ContentRootPath);
        GeneralConfiguration generalConfig = configManager.Get<GeneralConfiguration>();
        builder.Services.AddSingleton(configManager);

        builder.Services.AddRepositories(generalConfig.UseLegacyDatabase);
        if (generalConfig.UseLegacyDatabase)
        {
            builder.Services.AddDbContext<MysqlDbContext>(options =>
                options.UseMySql(
                    generalConfig.MySqlConnectionString,
                    ServerVersion.AutoDetect(generalConfig.MySqlConnectionString)));
        }
        else
        {
            builder.Services.AddDbContext<PostgreSqlDbContext>(options =>
                options.UseNpgsql(generalConfig.PostgresqlConnectionString));
        }

        builder.Services.AddOAuthAuthentication();
        builder.Services.AddRedis("realtime");
        builder.Services.AddStorage();
        builder.Services.AddMemoryCache();
        builder.Services.AddBackgroundTaskRunner();
        builder.Services.AddSingleton<RulesetManager>();
        builder.Services.AddSingleton<PlayerManager>();
        builder.Services.AddSingleton<ScoreBuffer>();
        builder.Services.AddSingleton<ScoreUploader>();
        builder.Services.AddSingleton<ScoreProcessedNotificationService>();
        builder.Services.AddSingleton<MultiplayerEventDispatcher>();
        builder.Services.AddSingleton<IRoomFacade>(serviceProvider => new RoomFacade(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<MultiplayerEventDispatcher>(),
            serviceProvider.GetService<ILogger<RoomFacade>>())
        {
            RulesetManager = serviceProvider.GetRequiredService<RulesetManager>(),
        });
        builder.Services.AddSingleton<RoomManager>();

        // Copy from osu-server-spectator
        builder.Services.AddSignalR()
            .AddMessagePackProtocol(options =>
            {
                // This is required for match type states/events, which are regularly sent as derived implementations where that type is not conveyed in the invocation signature itself.
                //
                // Some references:
                // https://github.com/neuecc/MessagePack-CSharp/issues/1171 ("it's not messagepack's issue")
                // https://github.com/dotnet/aspnetcore/issues/30096 ("it's definitely broken")
                // https://github.com/dotnet/aspnetcore/issues/7298 (current tracking issue, though weirdly described as a javascript client issue)
                options.SerializerOptions = SignalRUnionWorkaroundResolver.OPTIONS;
            })
            .AddHubOptions<MetadataHub>(ConfigureClientHubOptions)
            .AddHubOptions<SpectatorHub>(ConfigureClientHubOptions)
            .AddHubOptions<MultiplayerHub>(ConfigureClientHubOptions);
        builder.Services.AddSingleton<IUserIdProvider, JwtUserIdProvider>();
    }

    /// <summary>
    /// Configures the web application pipeline.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The configured application.</returns>
    public static WebApplication ConfigureApp(WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHub<MetadataHub>("/signalr/metadata");
        app.MapHub<SpectatorHub>("/signalr/spectator");
        app.MapHub<MultiplayerHub>("/signalr/multiplayer");

        return app;
    }
}