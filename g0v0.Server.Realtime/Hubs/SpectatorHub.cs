// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Realtime.Extensions;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Players;
using g0v0.Server.Realtime.Services;
using Microsoft.AspNetCore.SignalR;
using osu.Game.Beatmaps;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Spectator;
using osu.Game.Scoring;
using ConfigurationManager = g0v0.Server.Common.Configuration.ConfigurationManager;

namespace g0v0.Server.Realtime.Hubs;

/// <summary>
/// Handles spectator play-session streaming and watch subscriptions for lazer clients.
/// </summary>
/// <param name="playerManager">The player manager.</param>
/// <param name="configManager">The configuration manager.</param>
/// <param name="hubContext">The typed spectator hub context.</param>
/// <param name="userRepository">The user repository.</param>
/// <param name="beatmapRepository">The beatmap repository.</param>
/// <param name="scoreBuffer">The score buffer.</param>
/// <param name="rulesetManager">The ruleset manager.</param>
/// <param name="scoreUploader">The score uploader.</param>
/// <param name="scoreProcessedNotificationService">The score processed notification service.</param>
/// <param name="logger">The logger.</param>
/// <param name="scopeFactory">The service scope factory.</param>
public class SpectatorHub(
    PlayerManager playerManager,
    ConfigurationManager configManager,
    IHubContext<SpectatorHub, ISpectatorClient> hubContext,
    IUserRepository userRepository,
    IBeatmapRepository beatmapRepository,
    ScoreBuffer scoreBuffer,
    RulesetManager rulesetManager,
    ScoreUploader scoreUploader,
    ScoreProcessedNotificationService scoreProcessedNotificationService,
    ILogger<SpectatorHub> logger,
    IServiceScopeFactory scopeFactory)
    : LazerRealtimeHub<ISpectatorClient>(playerManager), ISpectatorServer
{
    /// <inheritdoc />
    public async override Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        var hub = hubContext;
        var manager = PlayerManager;
        var scopes = scopeFactory;

        var player =
            GetOrCreatePlayer(
                new PlayerFacade(PlayerManager)
                {
                    _scoreBuffer = scoreBuffer,
                    _scoreUploader = scoreUploader,
                    _configManager = configManager,
                    _scoreProcessedNotificationService = scoreProcessedNotificationService,
                });

        await player.HubConnected(nameof(SpectatorHub));
        logger.LogInformation(
            "Spectator hub connected for player {UserId} ({Server}).",
            player.PlayerId,
            player.Server);

        player.OnUserBeganPlayingForSpectatorHub = p =>
        {
            logger.LogDebug(
                "Sending spectator begin-play event to watcher {WatcherUserId} ({WatcherServer}) for player {TargetUserId} ({TargetServer}).",
                player.PlayerId,
                player.Server,
                p.PlayerId,
                p.Server);

            return hub.Clients.User(player.PlayerId.ToString())
                .UserBeganPlaying(p.PlayerId, p.State.SpectatorState!);
        };

        player.OnUserFinishedPlayingForSpectatorHub = p =>
        {
            logger.LogDebug(
                "Sending spectator finish-play event to watcher {WatcherUserId} ({WatcherServer}) for player {TargetUserId} ({TargetServer}).",
                player.PlayerId,
                player.Server,
                p.PlayerId,
                p.Server);

            return hub.Clients.User(player.PlayerId.ToString())
                .UserFinishedPlaying(p.PlayerId, p.State.SpectatorState!);
        };

        player.OnUserSentFramesForSpectatorHub = (p, data) =>
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "Sending spectator frame data to watcher {WatcherUserId} ({WatcherServer}) for player {TargetUserId} ({TargetServer}). FrameCount: {FrameCount}.",
                    player.PlayerId,
                    player.Server,
                    p.PlayerId,
                    p.Server,
                    data.Frames.Count);
            }

            return hub.Clients.User(player.PlayerId.ToString())
                .UserSentFrames(p.PlayerId, data);
        };

        player.OnWatchedForSpectatorHub = async watcher =>
        {
            logger.LogInformation(
                "Spectator watcher {WatcherUserId} ({WatcherServer}) started watching player {TargetUserId} ({TargetServer}).",
                watcher.PlayerId,
                watcher.Server,
                player.PlayerId,
                player.Server);

            var username = await GetUsernameByIdAsync(scopes, watcher.PlayerId);
            if (username == null)
            {
                logger.LogWarning(
                    "Could not resolve username for spectator watcher {WatcherUserId} ({WatcherServer}).",
                    watcher.PlayerId,
                    watcher.Server);
            }

            string displayUsername;
            Debug.Assert(username != null, nameof(username) + " != null");

            var notFromThisSource =
                manager
                    .GetWatchingPlayers(player).Where(p => p.PlayerId == watcher.PlayerId && p.Server != watcher.Server)
                    .ToArray();
            switch (notFromThisSource.Length)
            {
                case 0:
                    // This watcher is unique. Use username directly.
                    displayUsername = username;
                    break;
                case >= 2:
                    // There are the same watcher but from different servers. And server name has beed applied.
                    displayUsername = $"{username} ({watcher.Server})";
                    break;
                default:
                    {
                        // There is only one same watcher from the different server. Remove this and add both them with server name.
                        var formerPlayer = notFromThisSource[0];
                        await hub.Clients.User(player.PlayerId.ToString())
                            .UserEndedWatching(formerPlayer.PlayerId);
                        await hub.Clients.User(player.PlayerId.ToString()).UserStartedWatching([
                            new SpectatorUser
                            {
                                OnlineID = watcher.PlayerId,
                                Username = $"{username} ({formerPlayer.Server})",
                            }
                        ]);
                        displayUsername = $"{username} ({watcher.Server})";
                        break;
                    }
            }

            await hub.Clients.User(player.PlayerId.ToString()).UserStartedWatching([
                new SpectatorUser { OnlineID = watcher.PlayerId, Username = displayUsername, }
            ]);
        };

        player.OnWatchedStoppedForSpectatorHub = async watcher =>
        {
            logger.LogInformation(
                "Spectator watcher {WatcherUserId} ({WatcherServer}) stopped watching player {TargetUserId} ({TargetServer}).",
                watcher.PlayerId,
                watcher.Server,
                player.PlayerId,
                player.Server);

            var notFromThisSource =
                manager
                    .GetWatchingPlayers(player).Where(p => p.PlayerId == watcher.PlayerId && p.Server != watcher.Server)
                    .ToArray();
            switch (notFromThisSource.Length)
            {
                case 0:
                    // No more same watcher from different server. Remove this directly.
                    await hub.Clients.User(player.PlayerId.ToString()).UserEndedWatching(watcher.PlayerId);
                    break;
                case 1:
                    {
                        // There is only one same watcher from the different server. Remove this and add the other one with server name.
                        var remainingPlayer = notFromThisSource[0];
                        var username = await GetUsernameByIdAsync(scopes, watcher.PlayerId);
                        Debug.Assert(username != null, nameof(username) + " != null");
                        await hub.Clients.User(player.PlayerId.ToString()).UserEndedWatching(watcher.PlayerId);
                        await hub.Clients.User(player.PlayerId.ToString()).UserStartedWatching([
                            new SpectatorUser
                            {
                                OnlineID = remainingPlayer.PlayerId,
                                Username = $"{username} ({remainingPlayer.Server})",
                            }
                        ]);
                        break;
                    }

                default:
                    // There are still multiple same watchers from different servers. Do nothing.
                    break;
            }
        };

        player.OnScoreProcessedForSpectatorHub = (scoreId) =>
        {
            logger.LogInformation(
                "Spectator score processed for player {TargetUserId} ({TargetServer}). ScoreId: {ScoreId}.",
                player.PlayerId,
                player.Server,
                scoreId);

            return hub.Clients.User(player.PlayerId.ToString()).UserScoreProcessed(player.PlayerId, scoreId);
        };
    }

    /// <inheritdoc />
    public async override Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);

        var player = GetPlayer();
        if (exception == null)
        {
            logger.LogInformation(
                "Spectator hub disconnected for player {UserId} ({Server}).",
                player.PlayerId,
                player.Server);
        }
        else
        {
            logger.LogWarning(
                exception,
                "Spectator hub disconnected with an error for player {UserId} ({Server}).",
                player.PlayerId,
                player.Server);
        }

        player.OnUserBeganPlayingForSpectatorHub = null;
        player.OnUserFinishedPlayingForSpectatorHub = null;
        player.OnUserSentFramesForSpectatorHub = null;
        player.OnWatchedForSpectatorHub = null;
        player.OnWatchedStoppedForSpectatorHub = null;

        await player.HubDisconnected(nameof(SpectatorHub));
    }

    /// <inheritdoc />
    public async Task BeginPlaySession(long? scoreToken, SpectatorState state)
    {
        var playerId = Context.GetUserId();
        logger.LogInformation(
            "Begin spectator play session requested by player {UserId}. ScoreToken: {ScoreToken}, RulesetId: {RulesetId}, BeatmapId: {BeatmapId}, State: {SpectatorState}.",
            playerId,
            scoreToken,
            state.RulesetID,
            state.BeatmapID,
            state.State);

        if (state.RulesetID == null)
        {
            logger.LogWarning(
                "Ignoring spectator begin-play request from player {UserId} because RulesetId is missing. ScoreToken: {ScoreToken}.",
                playerId,
                scoreToken);
            return;
        }

        if (state.BeatmapID == null)
        {
            logger.LogWarning(
                "Ignoring spectator begin-play request from player {UserId} because BeatmapId is missing. ScoreToken: {ScoreToken}.",
                playerId,
                scoreToken);
            return;
        }

        var beatmap = await beatmapRepository.GetByIdAsync(state.BeatmapID.Value);
        if (beatmap == null)
        {
            logger.LogWarning(
                "Ignoring spectator begin-play request from player {UserId} because beatmap {BeatmapId} was not found. ScoreToken: {ScoreToken}.",
                playerId,
                state.BeatmapID.Value,
                scoreToken);
            return;
        }

        var score = new Score()
        {
            ScoreInfo = new ScoreInfo
            {
                APIMods = state.Mods.ToArray(),
                User =
                    new APIUser { Id = playerId, Username = (await userRepository.GetUsernameByIdAsync(playerId))!, },
                Ruleset = rulesetManager.GetRuleset(state.RulesetID.Value).RulesetInfo,
                BeatmapInfo = new BeatmapInfo
                {
                    OnlineID = state.BeatmapID.Value,
                    MD5Hash = beatmap.Checksum!,
                    Status = configManager.Get<GameConfiguration>().EnableAllBeatmapLeaderboard
                        ? BeatmapOnlineStatus.Approved
                        : beatmap.Status,
                },
                MaximumStatistics = state.MaximumStatistics
            }
        };
        await GetPlayer().BeingPlaying(scoreToken, score, state);
        logger.LogInformation(
            "Spectator play session started for player {UserId}. ScoreToken: {ScoreToken}, BeatmapId: {BeatmapId}, RulesetId: {RulesetId}.",
            playerId,
            scoreToken,
            state.BeatmapID.Value,
            state.RulesetID.Value);
    }

    /// <inheritdoc />
    public async Task SendFrameData(FrameDataBundle data)
    {
        var player = GetPlayer();
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Spectator frame data received from player {UserId} ({Server}). ScoreToken: {ScoreToken}, FrameCount: {FrameCount}.",
                player.PlayerId,
                player.Server,
                player.State.ScoreToken,
                data.Frames.Count);
        }

        await player.SendFrames(data);
    }

    /// <inheritdoc />
    public async Task EndPlaySession(SpectatorState state)
    {
        var player = GetPlayer();
        logger.LogInformation(
            "End spectator play session requested by player {UserId} ({Server}). ScoreToken: {ScoreToken}, State: {SpectatorState}.",
            player.PlayerId,
            player.Server,
            player.State.ScoreToken,
            state.State);

        await player.FinishPlaying(state);
    }

    /// <inheritdoc />
    public async Task StartWatchingUser(int userId)
    {
        // TODO: restricted check

        // FIXME: support users from different server.
        var player = GetPlayer();
        logger.LogInformation(
            "Spectator watch request from player {WatcherUserId} ({WatcherServer}) for target {TargetUserId}.",
            player.PlayerId,
            player.Server,
            userId);

        var target = PlayerManager.GetPlayerAllInstances(userId).FirstOrDefault();

        if (target == null)
        {
            logger.LogWarning(
                "Spectator watch request from player {WatcherUserId} ({WatcherServer}) ignored because target {TargetUserId} is not online.",
                player.PlayerId,
                player.Server,
                userId);
            return;
        }

        await player.WatchPlayer(target);
    }

    /// <inheritdoc />
    public async Task EndWatchingUser(int userId)
    {
        var player = GetPlayer();
        var targets = PlayerManager.GetPlayerAllInstances(userId).ToArray();
        logger.LogInformation(
            "Spectator unwatch request from player {WatcherUserId} ({WatcherServer}) for target {TargetUserId}. TargetInstanceCount: {TargetInstanceCount}.",
            player.PlayerId,
            player.Server,
            userId,
            targets.Length);

        foreach (var target in targets)
        {
            await player.StopWatchPlayer(target);
        }
    }

    private static async Task<string?> GetUsernameByIdAsync(IServiceScopeFactory scopeFactory, long userId)
    {
        using var scope = scopeFactory.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        return await userRepository.GetUsernameByIdAsync(userId);
    }
}