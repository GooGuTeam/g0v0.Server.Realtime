// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.MySQL;
using g0v0.Server.Common.Database.PostgreSQL;
using g0v0.Server.Common.Extensions;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Realtime.Hubs;
using g0v0.Server.Realtime.Json;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Notifications;
using g0v0.Server.Realtime.Notifications.Bot;
using g0v0.Server.Realtime.Objects.Chat;
using g0v0.Server.Realtime.Objects.Rooms;
using g0v0.Server.Realtime.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using osu.Game.Online;

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
        // MVC runs on Newtonsoft.Json (via the official
        // Microsoft.AspNetCore.Mvc.NewtonsoftJson package): the DTOs carry
        // explicit snake_case property names and the camelCase resolver keeps
        // the fallback shape of the anonymous payloads.
        builder.Services.AddControllers().AddNewtonsoftJson(options =>
            options.SerializerSettings.ContractResolver = RealtimeJson.MvcContractResolver);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSingleton<IPathProvider, WebPathProvider>();

        ConfigManager configManager = new(builder.Environment.ContentRootPath);
        GeneralConfig generalConfig = configManager.Get<GeneralConfig>();
        builder.Services.AddSingleton(configManager);

        RealtimeConfig realtimeConfig = configManager.Get<RealtimeConfig>();
        if (realtimeConfig.Sentry.Enabled)
        {
            builder.WebHost.UseSentry(options =>
            {
                options.Dsn = realtimeConfig.Sentry.Dsn;
                options.Environment = string.IsNullOrWhiteSpace(realtimeConfig.Sentry.Environment)
                    ? builder.Environment.EnvironmentName
                    : realtimeConfig.Sentry.Environment;
                options.TracesSampleRate = realtimeConfig.Sentry.TracesSampleRate;
                options.SendDefaultPii = realtimeConfig.Sentry.SendDefaultPii;
            });
        }

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
        builder.Services.AddRedis(ServerIdentify.Realtime);
        builder.Services.AddCache();
        builder.Services.AddFetcher();
        builder.Services.AddStorage();
        builder.Services.AddMemoryCache();
        builder.Services.AddBackgroundTaskRunner();
        builder.Services.AddSingleton<RulesetManager>();
        builder.Services.AddSingleton<IRulesetHashValidator, RulesetHashValidator>();
        builder.Services.AddSingleton<PlayerManager>();
        builder.Services.AddSingleton<ScoreBuffer>();
        builder.Services.AddSingleton<ScoreUploader>();
        builder.Services.AddSingleton<ScoreProcessedNotificationService>();
        builder.Services.AddSingleton<MultiplayerEventDispatcher>();
        builder.Services.AddSingleton<INotificationServer, NotificationServer>();
        builder.Services.AddScoped<ChatPmStartChecker>();
        builder.Services.AddScoped<ChatChannelDtoFactory>();
        builder.Services.AddSingleton<IRoomFacade>(serviceProvider => new RoomFacade(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<MultiplayerEventDispatcher>(),
            serviceProvider.GetService<ILogger<RoomFacade>>())
        {
            RulesetManager = serviceProvider.GetRequiredService<RulesetManager>(),
            RulesetHashValidator = serviceProvider.GetRequiredService<IRulesetHashValidator>(),
            NotificationServer = serviceProvider.GetRequiredService<INotificationServer>(),
            PlayerManager = serviceProvider.GetRequiredService<PlayerManager>(),
        });
        builder.Services.AddSingleton<RoomManager>();
        builder.Services.AddSingleton<IBanchoBot>(serviceProvider =>
        {
            BanchoBot bot = new(
                serviceProvider.GetRequiredService<ILogger<BanchoBot>>(),
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                serviceProvider.GetRequiredService<INotificationServer>(),
                serviceProvider.GetRequiredService<PlayerManager>());
            bot.Initialize();
            return bot;
        });
        builder.Services.AddSingleton<LazerWebSocketListener>();
        builder.Services.AddSingleton(serviceProvider =>
        {
            ChatIpcHandler handler = new(
                serviceProvider.GetRequiredService<InterProcessCommunicationClient>(),
                serviceProvider.GetRequiredService<IServiceScopeFactory>());
            handler.Register();
            return handler;
        });

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
        // Constructing the handler registers the chat IPC notice handlers
        // ("new_notification") on the IPC client; nothing else resolves it, so
        // without this the notices would be silently dropped.
        _ = app.Services.GetRequiredService<ChatIpcHandler>();

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

        app.UseWebSockets();
        app.Map("/notification-server", async context =>
        {
            LazerWebSocketListener listener = context.RequestServices.GetRequiredService<LazerWebSocketListener>();
            await listener.HandleAsync(context);
        });

        return app;
    }
}