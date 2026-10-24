// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

// RoomBase deliberately groups members by domain responsibility (state machine,
// persistence, countdowns) rather than by StyleCop's member-order rules.
#pragma warning disable SA1201 // Elements should appear in the correct order
#pragma warning disable SA1202 // Elements should be ordered by access
#pragma warning disable SA1204 // Static members should appear before non-static members
#pragma warning disable SA1214 // Readonly fields should appear before non-readonly fields

using System.Collections.Concurrent;
using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Fetching;
using g0v0.Server.Realtime.Extensions;
using g0v0.Server.Realtime.Services;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Countdown;
using osu.Game.Online.Rooms;
using OsuMatchType = osu.Game.Online.Rooms.MatchType;
using Room = g0v0.Server.Common.Database.Models.Room;

namespace g0v0.Server.Realtime.Objects.Rooms;

/// <summary>
/// Provides the shared state machine, persistence and event broadcasting for
/// all multiplayer room types.
/// </summary>
/// <remarks>
/// All mutating operations must be invoked while holding the room's
/// <see cref="RoomLock"/> to guarantee serialised access from concurrent hub
/// calls. The room holds the authoritative in-memory snapshot; the database is
/// the persistence/restore source.
/// </remarks>
public abstract class RoomBase : IRoom
{
    /// <summary>
    /// The maximum number of characters in a room name.
    /// </summary>
    public const int MaxNameLength = 100;

    /// <summary>
    /// The maximum supported participant limit.
    /// </summary>
    public const int MaxParticipantsLimit = 16;

    private const int HighestOfficialRulesetId = 3;

    /// <inheritdoc />
    public long RoomId { get; }

    /// <inheritdoc />
    public MultiplayerRoomState State { get; protected set; } = MultiplayerRoomState.Open;

    /// <inheritdoc />
    public MultiplayerRoomSettings Settings { get; protected set; }

    /// <inheritdoc />
    public IList<MultiplayerRoomUser> Users { get; } = new List<MultiplayerRoomUser>();

    /// <inheritdoc />
    public MultiplayerRoomUser? Host { get; protected set; }

    /// <inheritdoc />
    public MatchRoomState? MatchState { get; protected set; }

    /// <inheritdoc />
    public IList<MultiplayerPlaylistItem> Playlist { get; } = new List<MultiplayerPlaylistItem>();

    /// <inheritdoc />
    public IList<MultiplayerCountdown> ActiveCountdowns { get; } = new List<MultiplayerCountdown>();

    /// <inheritdoc />
    public int ChannelId { get; set; }

    /// <inheritdoc />
    public bool TournamentMode { get; }

    /// <summary>
    /// Gets the date at which an empty tournament room will be destroyed, or
    /// <see langword="null"/> when no destruction is pending.
    /// </summary>
    public DateTimeOffset? EndDate { get; private set; }

    private CancellationTokenSource? _endDateCancellation;

    /// <summary>
    /// Gets the per-room lock used to serialise all mutating operations.
    /// </summary>
    public SemaphoreSlim RoomLock { get; } = new(1, 1);

    /// <summary>
    /// Gets the set of banned user IDs.
    /// </summary>
    public IReadOnlySet<int> BannedUsers => _bannedUsers;

    private readonly HashSet<int> _bannedUsers = new();

    private readonly ConcurrentDictionary<MultiplayerCountdown, CountdownInfo> _trackedCountdowns = new();

    private int _nextCountdownId;

    /// <summary>
    /// Gets the room dependency facade.
    /// </summary>
    protected IRoomFacade Facade { get; }

    /// <summary>
    /// Resolves the connected player instance for a user ID, or <see langword="null"/>.
    /// </summary>
    /// <param name="userId">
    /// The user ID.
    /// </param>
    /// <returns>
    /// The player, or <see langword="null"/> when not connected.
    /// </returns>
    protected Objects.Players.IPlayer? GetPlayer(int userId)
    {
        return Facade.PlayerManager?.GetPlayer<Objects.Players.IPlayer>(userId);
    }

    /// <summary>
    /// Gets or sets the callback invoked when the room is disbanded (no users remain).
    /// </summary>
    public Func<Task>? OnDisbanded { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the room's match type changes.
    /// </summary>
    public Func<OsuMatchType, Task>? OnMatchTypeChanged { get; set; }

    /// <summary>
    /// Executes the supplied operation with a scoped room repository.
    /// </summary>
    /// <typeparam name="T">
    /// The result type.
    /// </typeparam>
    /// <param name="operation">
    /// The operation to run with the repository.
    /// </param>
    /// <returns>
    /// The operation result.
    /// </returns>
    private async Task<T> WithRepository<T>(Func<IRoomRepository, Task<T>> operation)
    {
        using IServiceScope scope = Facade.ScopeFactory.CreateScope();
        IRoomRepository repository = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
        return await operation(repository);
    }

    /// <summary>
    /// Executes the supplied operation with a scoped room repository.
    /// </summary>
    /// <param name="operation">
    /// The operation to run with the repository.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    private async Task WithRepository(Func<IRoomRepository, Task> operation)
    {
        using IServiceScope scope = Facade.ScopeFactory.CreateScope();
        IRoomRepository repository = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
        await operation(repository);
    }

    /// <summary>
    /// Executes the supplied operation with a scoped beatmap repository.
    /// </summary>
    /// <typeparam name="T">
    /// The result type.
    /// </typeparam>
    /// <param name="operation">
    /// The operation to run with the repository.
    /// </param>
    /// <returns>
    /// The operation result, or <see langword="null"/> when no beatmap repository is registered.
    /// </returns>
    private async Task<T?> WithBeatmapRepository<T>(Func<IBeatmapRepository, Task<T>> operation)
    {
        using IServiceScope scope = Facade.ScopeFactory.CreateScope();
        IBeatmapRepository? repository = scope.ServiceProvider.GetService<IBeatmapRepository>();
        return repository == null ? default : await operation(repository);
    }

    /// <summary>
    /// Executes the supplied operation with a scoped beatmap repository and the osu! fetcher.
    /// Falls back to the plain repository lookup when no fetcher is registered.
    /// </summary>
    /// <param name="operation">
    /// The operation to run with the repository and fetcher.
    /// </param>
    /// <param name="lookup">
    /// The fallback operation used when no fetcher is registered.
    /// </param>
    /// <returns>
    /// The operation result, or <see langword="null"/> when no beatmap repository is registered.
    /// </returns>
    private async Task<Beatmap?> WithBeatmapRepositoryAndFetcher(Func<IBeatmapRepository, IFetcher, Task<Beatmap?>> operation, Func<IBeatmapRepository, Task<Beatmap?>> lookup)
    {
        using IServiceScope scope = Facade.ScopeFactory.CreateScope();
        IBeatmapRepository? repository = scope.ServiceProvider.GetService<IBeatmapRepository>();
        if (repository == null)
        {
            return null;
        }

        IFetcher? fetcher = scope.ServiceProvider.GetService<IFetcher>();
        return fetcher == null ? await lookup(repository) : await operation(repository, fetcher);
    }

    /// <summary>
    /// Resolves the star rating for a beatmap from the beatmap repository.
    /// </summary>
    /// <param name="beatmapId">
    /// The beatmap ID.
    /// </param>
    /// <returns>
    /// The star rating, or <c>0</c> when the beatmap is unknown.
    /// </returns>
    private async Task<double> GetStarRating(int beatmapId)
    {
        Beatmap? beatmap = await WithBeatmapRepositoryAndFetcher(
            async (repo, fetcher) => await repo.GetOrFetchByIdAsync(beatmapId, fetcher),
            repo => repo.GetByIdAsync(beatmapId));
        return beatmap?.DifficultyRating ?? 0;
    }

    /// <summary>
    /// The amount of time allowed for players to finish loading gameplay.
    /// </summary>
    private static readonly TimeSpan GameplayLoadTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initializes a new instance of the <see cref="RoomBase"/> class from a database room.
    /// </summary>
    /// <param name="room">
    /// The database room.
    /// </param>
    /// <param name="facade">
    /// The room dependency facade.
    /// </param>
    protected RoomBase(Room room, IRoomFacade facade)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(facade);

        this.Facade = facade;
        RoomId = room.Id;
        ChannelId = room.ChannelId;
        TournamentMode = room.TournamentMode;
        Settings = new MultiplayerRoomSettings
        {
            Name = room.Name,
            Password = room.Password ?? string.Empty,
            MatchType = room.Type,
            QueueMode = room.QueueMode,
            AutoStartDuration = TimeSpan.FromSeconds(room.AutoStartDuration),
            AutoSkip = room.AutoSkip,
            MaxParticipants = room.MaxParticipants,
        };

        foreach (Playlist playlist in room.Playlists)
        {
            Playlist.Add(playlist.ToMultiplayerPlaylistItem());
        }
    }

    /// <inheritdoc />
    public virtual async Task<RoomJoinResult> UserCanJoin(int userId)
    {
        if (Facade.RulesetHashValidator == null)
        {
            return RoomJoinResult.Allowed;
        }

        List<RulesetHashValidationFailure> failures = [];
        foreach (MultiplayerPlaylistItem item in Playlist)
        {
            RulesetHashValidationFailure? failure = await Facade.RulesetHashValidator.ValidatePlayerAsync(
                item.RulesetID,
                userId);
            if (failure != null)
            {
                failures.Add(failure);
            }
        }

        return failures.Count == 0
            ? RoomJoinResult.Allowed
            : RoomJoinResult.Rejected(
                "You have one or more outdated or invalid rulesets to join room: "
                + string.Join(", ", failures.Select(failure =>
                    $"(name: {failure.RulesetName}, server-ver: {failure.ServerVersion})")));
    }

    /// <inheritdoc />
    public abstract MultiplayerPlaylistItem CurrentItem { get; }

    /// <inheritdoc />
    public virtual async Task<RoomJoinResult> UserCanJoinWithPassword(int userId, string password)
    {
        // TODO: add user restriction check
        return BannedUsers.Contains(userId)
            ? RoomJoinResult.Rejected("You are banned from this room.")
            : EndDate != null && DateTimeOffset.UtcNow >= EndDate
                ? RoomJoinResult.Rejected("The room is no longer available.")
                : !string.IsNullOrEmpty(Settings.Password)
                  && !string.Equals(Settings.Password, password, StringComparison.Ordinal)
                    ? RoomJoinResult.Rejected(RoomJoinResult.InvalidPasswordReason)
                    : await UserCanJoin(userId);
    }

    /// <inheritdoc />
    public async Task<RoomJoinResult> JoinRoom(
        int userId,
        MultiplayerRoomUserRole role = MultiplayerRoomUserRole.Player,
        string? password = null)
    {
        if (Users.Any(u => u.UserID == userId))
        {
            return RoomJoinResult.Rejected($"User {userId} is already in the room.");
        }

        RoomJoinResult eligibility = await UserCanJoinWithPassword(userId, password ?? Settings.Password);
        if (!eligibility.Success)
        {
            return eligibility;
        }

        // A join cancels any pending end-date destruction of the room.
        DateTimeOffset? previousEndDate = EndDate;
        if (EndDate != null)
        {
            CancelEndDateDestruction();
            EndDate = null;
            await WithRepository(repo => repo.SetRoomEndDate(RoomId, endDate: null));
        }

        MultiplayerRoomUser user = new(userId)
        {
            Role = role,
            State = MultiplayerUserState.Idle,
        };

        try
        {
            // Inform clients already in the room before adding the user, so the
            // existing clients receive UserJoined before any subsequent state
            // snapshots. The joining client subscribes to the room group only
            // after JoinRoom completes, so it does not receive its own UserJoined.
            await Facade.EventDispatcher.PostUserJoinedAsync(RoomId, user);

            Users.Add(user);

            if (Host == null)
            {
                Host = user;
                await Facade.EventDispatcher.PostHostChangedAsync(RoomId, user.UserID);
            }

            await WithRepository(repo => repo.AddUserToRoom(RoomId, userId));
            if (Facade.NotificationServer != null && ChannelId > 0)
            {
                Players.IPlayer? player = GetPlayer(user.UserID);
                if (player != null)
                {
                    await Facade.NotificationServer.JoinChannel(player, ChannelId);
                }
            }

            await HandleUserJoined(user);
        }
        catch
        {
            // Full transactional rollback: reuse the standard leave flow so the
            // participant row, slots/teams and any UserJoined/HostChanged events
            // are compensated (a UserLeft is broadcast to remove the ghost
            // member), and restore the cancelled end-date destruction timer.
            if (Users.Contains(user))
            {
                await RemoveUserInternal(user);
            }
            else
            {
                // The user never reached Users (e.g. PostUserJoinedAsync failed),
                // but defensively clear any persisted participant row.
                await WithRepository(repo => repo.RemoveUserFromRoom(RoomId, userId));
            }

            // Compensate the previously broadcast UserJoined so existing clients
            // do not retain a ghost member.
            await Facade.EventDispatcher.PostUserLeftAsync(RoomId, user);

            try
            {
                if (Facade.NotificationServer != null && ChannelId > 0)
                {
                    Players.IPlayer? player = GetPlayer(user.UserID);
                    if (player != null)
                    {
                        await Facade.NotificationServer.LeaveChannel(player, ChannelId);
                    }
                }
            }
            catch (InterProcessCommunicationRemoteException)
            {
                // Ignore it because the user maybe was not in room.
            }

            // RemoveUserInternal may have scheduled a fresh end-date destruction
            // for an empty room; cancel it and restore the previous timer.
            if (previousEndDate == null)
            {
                throw;
            }

            {
                CancelEndDateDestruction();
                EndDate = null;
                await WithRepository(repo => repo.SetRoomEndDate(RoomId, endDate: null));
                await ScheduleEndDateDestruction(previousEndDate.Value);
            }

            throw;
        }

        return RoomJoinResult.Allowed;
    }

    /// <inheritdoc />
    public async Task LeaveRoom(int userId)
    {
        MultiplayerRoomUser user = Users.FirstOrDefault(u => u.UserID == userId)
                                   ?? throw new InvalidStateException("User is not in the room.");

        await RemoveUserInternal(user);
        await Facade.EventDispatcher.PostUserLeftAsync(RoomId, user);
    }

    /// <inheritdoc />
    public async Task KickUser(int userId, int kickedBy)
    {
        if (userId == kickedBy)
        {
            throw new InvalidStateException("Can't kick self.");
        }

        MultiplayerRoomUser user = Users.FirstOrDefault(u => u.UserID == userId)
                                   ?? throw new InvalidOperationException("Target user is not in the current room.");

        if (user.Role == MultiplayerRoomUserRole.Referee)
        {
            throw new InvalidStateException("Can't kick a referee.");
        }

        await RemoveUserInternal(user);
        await Facade.EventDispatcher.PostUserKickedAsync(RoomId, user);
    }

    /// <inheritdoc />
    public void BanUser(int userId)
    {
        _bannedUsers.Add(userId);
    }

    /// <inheritdoc />
    public async Task TransferHost(int source, int target)
    {
        bool isHost = Host?.UserID == source;
        bool isReferee = Users.FirstOrDefault(u => u.UserID == source)?.Role == MultiplayerRoomUserRole.Referee;

        if (!isHost && !isReferee)
        {
            throw new NotHostException();
        }

        await SetHost(target);
    }

    /// <inheritdoc />
    public async Task SetHost(int userId)
    {
        MultiplayerRoomUser newHost = Users.FirstOrDefault(u => u.UserID == userId)
                                      ?? throw new InvalidStateException("User is not in the room.");

        Host = newHost;
        await Facade.EventDispatcher.PostHostChangedAsync(RoomId, newHost.UserID);
        await WithRepository(repo => repo.UpdateRoomHost(RoomId, newHost.UserID));
    }

    /// <inheritdoc />
    public async Task ChangeRoomSettings(MultiplayerRoomSettings newSettings)
    {
        if (State != MultiplayerRoomState.Open)
        {
            throw new InvalidStateException("Attempted to change settings while a game is in progress.");
        }

        if (newSettings.Name.Length > MaxNameLength)
        {
            throw new InvalidStateException("Room name too long.");
        }

        if (newSettings.MatchType is not (OsuMatchType.HeadToHead or OsuMatchType.TeamVersus))
        {
            throw new InvalidStateException("Invalid match type selected.");
        }

        if (newSettings.MaxParticipants is < 2 or > MaxParticipantsLimit)
        {
            throw new InvalidStateException($"Max participants must be between 2 and {MaxParticipantsLimit}.");
        }

        if (newSettings.MaxParticipants < Users.Count)
        {
            throw new InvalidStateException("There are more players currently in the room than your new requested max participant limit.");
        }

        // The server is authoritative over the playlist item ID.
        newSettings.PlaylistItemId = Settings.PlaylistItemId;

        // Re-submitting identical settings is a no-op: no re-persist, no unready,
        // no countdown stop and no settings broadcast.
        if (Settings.Equals(newSettings))
        {
            return;
        }

        MultiplayerRoomSettings previousSettings = Settings;
        Settings = newSettings;
        Settings.PlaylistItemId = previousSettings.PlaylistItemId;

        try
        {
            await UpdateRoomSettings(Settings);
        }
        catch
        {
            Settings = previousSettings;
            throw;
        }

        if (previousSettings.MatchType != newSettings.MatchType && OnMatchTypeChanged != null)
        {
            await OnMatchTypeChanged(newSettings.MatchType);
        }

        // Repoint PlaylistItemId in case the settings refer to a removed item.
        await UpdateCurrentItem();

        await HandleMatchStateSettingsChanged(previousSettings);
        await HandleSettingsChanged(playlistItemChanged: false);
        await UpdateRoomStateIfRequired();
    }

    /// <summary>
    /// Handles match-state specific settings changes (e.g. slot reallocation when
    /// the participant limit changes). Derived rooms override this.
    /// </summary>
    /// <param name="previousSettings">
    /// The settings before the change.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    protected virtual Task HandleMatchStateSettingsChanged(MultiplayerRoomSettings previousSettings) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task ChangeUserState(int userId, MultiplayerUserState newState)
    {
        MultiplayerRoomUser user = Users.FirstOrDefault(u => u.UserID == userId)
                                   ?? throw new InvalidStateException("User is not in the room.");

        if (user.State == newState)
        {
            return;
        }

        // Silently ignore messages racing with server state transitions.
        switch (newState)
        {
            case MultiplayerUserState.Idle when user.State.IsGameplayState():
            case MultiplayerUserState.Loaded when !user.State.IsGameplayState():
            case MultiplayerUserState.ReadyForGameplay when !user.State.IsGameplayState():
                return;
        }

        EnsureValidStateSwitch(user.Role, user.State, newState);

        // Players cannot ready up while the current playlist item has already
        // been played.
        if (newState == MultiplayerUserState.Ready && CurrentItem.Expired)
        {
            throw new InvalidStateException("Cannot ready up while all items have been played.");
        }

        await ChangeAndBroadcastUserState(user, newState);

        // Signal newly-spectating users to load gameplay if currently in the middle of play.
        if (newState == MultiplayerUserState.Spectating
            && State is MultiplayerRoomState.WaitingForLoad or MultiplayerRoomState.Playing)
        {
            await Facade.EventDispatcher.PostLoadRequestedAsync(user.UserID);
        }

        await UpdateRoomStateIfRequired();
    }

    /// <inheritdoc />
    public async Task ChangeUserBeatmapAvailability(int userId, BeatmapAvailability newBeatmapAvailability)
    {
        MultiplayerRoomUser user = Users.FirstOrDefault(u => u.UserID == userId)
                                   ?? throw new InvalidStateException("User is not in the room.");

        if (user.BeatmapAvailability.Equals(newBeatmapAvailability))
        {
            return;
        }

        user.BeatmapAvailability = newBeatmapAvailability;
        await Facade.EventDispatcher.PostUserBeatmapAvailabilityChangedAsync(RoomId, user.UserID, user.BeatmapAvailability);
    }

    /// <inheritdoc />
    public async Task ChangeUserStyle(int userId, int? beatmapId, int? rulesetId)
    {
        MultiplayerRoomUser user = Users.FirstOrDefault(u => u.UserID == userId)
                                   ?? throw new InvalidStateException("User is not in the room.");

        if (user.Role == MultiplayerRoomUserRole.Referee)
        {
            throw new InvalidStateException("Referees do not participate in the match and cannot set their own style.");
        }

        if (rulesetId > HighestOfficialRulesetId)
        {
            await EnsureAllUsersSupportRulesetAsync(rulesetId.Value);
        }

        if (rulesetId != null)
        {
            int[] availableIds = Facade.RulesetManager == null
                ? [0, 1, 2, 3]
                : Facade.RulesetManager.GetAllRulesets().Select(r => r.RulesetInfo.OnlineID)
                    .Where(id => id >= 0)
                    .ToArray();
            if (!availableIds.Contains(rulesetId.Value))
            {
                throw new InvalidStateException("Attempted to select an unsupported ruleset.");
            }

            int itemRulesetId = CurrentItem.RulesetID;

            // A beatmap whose native ruleset is osu!standard may be played with
            // any ruleset; otherwise the selected ruleset must match the beatmap.
            if (itemRulesetId != 0 && rulesetId != itemRulesetId)
            {
                throw new InvalidStateException("Selected ruleset is not supported for the given beatmap.");
            }
        }

        await ValidateUserStyleBeatmap(beatmapId, rulesetId);

        await ChangeUserStyleInternal(user, beatmapId, rulesetId);
    }

    /// <summary>
    /// Validates a user's freestyle beatmap/ruleset selection against the beatmap
    /// table: the
    /// selected beatmap must exist and belong to the current item's beatmap set,
    /// and the ruleset must be supported by the selected beatmap.
    /// </summary>
    /// <param name="beatmapId">
    /// The selected beatmap ID.
    /// </param>
    /// <param name="rulesetId">
    /// The selected ruleset ID.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    /// <exception cref="InvalidStateException">
    /// The selection is not valid for the current item.
    /// </exception>
    private async Task ValidateUserStyleBeatmap(int? beatmapId, int? rulesetId)
    {
        if (beatmapId == null && rulesetId == null)
        {
            return;
        }

        // Any non-null style selection requires a freestyle item (aligned with
        // the reference changeUserStyle: either beatmapId or rulesetId forces the
        // freestyle check).
        if (!CurrentItem.Freestyle)
        {
            throw new InvalidStateException("Current item does not allow free user styles.");
        }

        // Ruleset-only selections need no beatmap table validation beyond the
        // freestyle check above.
        if (beatmapId == null)
        {
            return;
        }

        bool repositoryRegistered = false;
        Beatmap? itemBeatmap = await WithBeatmapRepositoryAndFetcher(
            async (repo, fetcher) =>
            {
                repositoryRegistered = true;
                return await repo.GetOrFetchByIdAsync(CurrentItem.BeatmapID, fetcher);
            },
            repo =>
            {
                repositoryRegistered = true;
                return repo.GetByIdAsync(CurrentItem.BeatmapID);
            });

        // Validation is skipped when no beatmap repository is registered.
        if (!repositoryRegistered)
        {
            return;
        }

        Beatmap? userBeatmap = await WithBeatmapRepositoryAndFetcher(
            async (repo, fetcher) => await repo.GetOrFetchByIdAsync(beatmapId.Value, fetcher),
            repo => repo.GetByIdAsync(beatmapId.Value)) ?? throw new InvalidStateException("Invalid beatmap selected.");
        if (userBeatmap.BeatmapSetId != itemBeatmap?.BeatmapSetId)
        {
            throw new InvalidStateException("Selected beatmap is not from the same beatmap set.");
        }

        if (rulesetId != null && userBeatmap.Mode != 0 && rulesetId != userBeatmap.Mode)
        {
            throw new InvalidStateException("Selected ruleset is not supported for the given beatmap.");
        }
    }

    /// <summary>
    /// Changes the style of a user and broadcasts the change.
    /// </summary>
    /// <param name="user">
    /// The user whose style is changing.
    /// </param>
    /// <param name="beatmapId">
    /// The new beatmap ID.
    /// </param>
    /// <param name="rulesetId">
    /// The new ruleset ID.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    private async Task ChangeUserStyleInternal(MultiplayerRoomUser user, int? beatmapId, int? rulesetId)
    {
        if (user.BeatmapId == beatmapId && user.RulesetId == rulesetId)
        {
            return;
        }

        user.BeatmapId = beatmapId;
        user.RulesetId = rulesetId;
        await Facade.EventDispatcher.PostUserStyleChangedAsync(RoomId, user.UserID, beatmapId, rulesetId);

        // Trim user mods which are no longer valid for the new style's playlist
        // item.
        if (Facade.RulesetManager != null && user.Mods.Any())
        {
            MultiplayerPlaylistItem currentItem = CurrentItem;
            if (!currentItem.ValidateUserMods(user, user.Mods, Facade.RulesetManager, out IEnumerable<APIMod>? validMods))
            {
                APIMod[] trimmed = validMods.ToArray();
                user.Mods = trimmed;
                await Facade.EventDispatcher.PostUserModsChangedAsync(RoomId, user.UserID, trimmed);
            }
        }
    }

    /// <summary>
    /// Determines whether the given user is the room host or a referee.
    /// </summary>
    /// <param name="user">
    /// The user to check.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the user is the host or a referee.
    /// </returns>
    private bool IsHostOrReferee(MultiplayerRoomUser user)
        => Host?.UserID == user.UserID || user.Role == MultiplayerRoomUserRole.Referee;

    /// <summary>
    /// Ensures every user's style (beatmap/ruleset selection) is valid for the
    /// current playlist item, resetting selections which no longer apply.
    /// </summary>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    private async Task EnsureAllUsersValidStyle()
    {
        MultiplayerPlaylistItem currentItem = CurrentItem;

        if (!currentItem.Freestyle)
        {
            // Reset entire style when freestyle is disabled.
            foreach (MultiplayerRoomUser user in Users)
            {
                await ChangeUserStyleInternal(user, beatmapId: null, rulesetId: null);
            }
        }
        else
        {
            Beatmap? itemBeatmap = await WithBeatmapRepositoryAndFetcher(
                async (repo, fetcher) => await repo.GetOrFetchByIdAsync(currentItem.BeatmapID, fetcher),
                repo => repo.GetByIdAsync(currentItem.BeatmapID));
            IReadOnlyList<Beatmap>? validDifficulties = itemBeatmap == null
                ? null
                : await WithBeatmapRepository(repo => repo.GetByBeatmapSetIdAsync(itemBeatmap.BeatmapSetId));

            foreach (MultiplayerRoomUser user in Users)
            {
                int? userBeatmapId = user.BeatmapId;
                int? userRulesetId = user.RulesetId;

                Beatmap? foundBeatmap = validDifficulties?.SingleOrDefault(b => b.Id == userBeatmapId);

                // Reset beatmap style if it's not a valid difficulty for the current beatmap set.
                if (userBeatmapId != null && validDifficulties != null && foundBeatmap == null)
                {
                    userBeatmapId = null;
                }

                int beatmapRuleset = foundBeatmap?.Mode ?? itemBeatmap?.Mode ?? 0;

                // Reset ruleset style when it's no longer valid for the resolved beatmap.
                if (userRulesetId != null && beatmapRuleset > 0 && userRulesetId != beatmapRuleset)
                {
                    userRulesetId = null;
                }

                await ChangeUserStyleInternal(user, userBeatmapId, userRulesetId);
            }
        }
    }

    /// <inheritdoc />
    public async Task ChangeUserMods(int userId, IEnumerable<APIMod> newMods)
    {
        MultiplayerRoomUser user = Users.FirstOrDefault(u => u.UserID == userId)
                                   ?? throw new InvalidStateException("User is not in the room.");

        if (user.Role == MultiplayerRoomUserRole.Referee)
        {
            throw new InvalidStateException("Referees do not participate in the match and cannot set their own mods.");
        }

        List<APIMod> newModList = newMods.ToList();

        if (Facade.RulesetManager != null)
        {
            MultiplayerPlaylistItem currentItem = CurrentItem;
            if (!currentItem.ValidateUserMods(user, newModList, Facade.RulesetManager, out IEnumerable<APIMod>? validMods))
            {
                throw new InvalidStateException($"Incompatible mods were selected: {string.Join(',', newModList.Except(validMods).Select(m => m.Acronym))}");
            }
        }

        if (user.Mods.SequenceEqual(newModList))
        {
            return;
        }

        user.Mods = newModList;
        await Facade.EventDispatcher.PostUserModsChangedAsync(RoomId, user.UserID, newModList);
    }

    /// <inheritdoc />
    public async Task StartMatchCountdown(TimeSpan duration)
    {
        if (State != MultiplayerRoomState.Open)
        {
            throw new InvalidStateException("Cannot start a countdown during ongoing play.");
        }

        if (Settings.AutoStartEnabled)
        {
            throw new InvalidStateException("Cannot start manual countdown if auto-start is enabled.");
        }

        await StartCountdown(new MatchStartCountdown { TimeRemaining = duration }, StartMatch);
    }

    /// <inheritdoc />
    public async Task StopCountdown(int countdownId)
    {
        MultiplayerCountdown? countdown = FindCountdownById(countdownId);
        if (countdown == null)
        {
            return;
        }

        switch (countdown)
        {
            case MatchStartCountdown when Settings.AutoStartEnabled:
            case ForceGameplayStartCountdown:
                throw new InvalidStateException("Cannot stop the requested countdown.");
        }

        await StopCountdown(countdown);
    }

    /// <inheritdoc />
    public async Task StartMatch()
    {
        if (State != MultiplayerRoomState.Open)
        {
            throw new InvalidStateException("Can't start match when already in a running state.");
        }

        if (CurrentItem.Expired)
        {
            throw new InvalidStateException("Cannot start an expired playlist item.");
        }

        if (Users.All(u => u.State != MultiplayerUserState.Ready))
        {
            await HandleGameplayCompleted();
            return;
        }

        foreach (MultiplayerRoomUser user in Users)
        {
            await ChangeUserVoteToSkipIntro(user, voted: false);
        }

        MultiplayerRoomUser[] readyUsers = Users.Where(u => u.IsReadyForGameplay()).ToArray();
        foreach (MultiplayerRoomUser? u in readyUsers)
        {
            await ChangeAndBroadcastUserState(u, MultiplayerUserState.WaitingForLoad);
        }

        await ChangeRoomState(MultiplayerRoomState.WaitingForLoad);

        await Facade.EventDispatcher.PostLoadRequestedToGroupAsync(RoomId);

        await StartCountdown(new ForceGameplayStartCountdown { TimeRemaining = GameplayLoadTimeout }, StartOrStopGameplay);
    }

    /// <inheritdoc />
    public async Task AbortMatch()
    {
        if (State is not (MultiplayerRoomState.WaitingForLoad or MultiplayerRoomState.Playing))
        {
            throw new InvalidStateException("Cannot abort a match that hasn't started.");
        }

        // Move every user back to idle, not just those in gameplay states.
        foreach (MultiplayerRoomUser user in Users.ToArray().Where(user => user.State != MultiplayerUserState.Idle))
        {
            await ChangeAndBroadcastUserState(user, MultiplayerUserState.Idle);
        }

        // Notify the whole room that the match was aborted.
        await Facade.EventDispatcher.PostGameplayAbortedToGroupAsync(RoomId, GameplayAbortReason.HostAbortedTheMatch);

        await UpdateRoomStateIfRequired();
    }

    /// <inheritdoc />
    public async Task AbortGameplay(int userId)
    {
        MultiplayerRoomUser user = Users.FirstOrDefault(u => u.UserID == userId)
                                   ?? throw new InvalidStateException("User is not in the room.");

        if (!user.State.IsGameplayState())
        {
            throw new InvalidStateException("Cannot abort gameplay while not in gameplay.");
        }

        await ChangeAndBroadcastUserState(user, MultiplayerUserState.Idle);
        await UpdateRoomStateIfRequired();
    }

    /// <inheritdoc />
    public async Task VoteToSkipIntro(int userId)
    {
        MultiplayerRoomUser user = Users.FirstOrDefault(u => u.UserID == userId)
                                   ?? throw new InvalidStateException("User is not in the room.");

        if (user.Role == MultiplayerRoomUserRole.Referee)
        {
            throw new InvalidStateException("Referees do not participate in the match and cannot vote to skip.");
        }

        if (!user.State.IsGameplayState())
        {
            throw new InvalidStateException("Cannot skip while not in gameplay.");
        }

        await ChangeUserVoteToSkipIntro(user, voted: true);
        await CheckVotesToSkipPassed();
    }

    /// <inheritdoc />
    public async Task AddPlaylistItem(int userId, MultiplayerPlaylistItem item)
    {
        MultiplayerRoomUser user = Users.FirstOrDefault(u => u.UserID == userId)
                                   ?? throw new InvalidStateException("User is not in the room.");

        bool isHostOrReferee = IsHostOrReferee(user);

        // In host-only mode only the host (or a referee) may add items.
        if (Settings.QueueMode == QueueMode.HostOnly && !isHostOrReferee)
        {
            throw new NotHostException();
        }

        int limit = isHostOrReferee
            ? Facade.ConfigManager?.Get<RealtimeConfig>().HostPlaylistLimit ?? RealtimeConfig.DefaultHostPlaylistLimit
            : Facade.ConfigManager?.Get<RealtimeConfig>().GuestPlaylistLimit ?? RealtimeConfig.DefaultGuestPlaylistLimit;

        if (Playlist.Count(i => i.OwnerID == user.UserID && !i.Expired) >= limit)
        {
            throw new InvalidStateException($"Can't enqueue more than {limit} items at once.");
        }

        if (item.Freestyle && item.AllowedMods.Any())
        {
            throw new InvalidStateException("Cannot enqueue freestyle item with mods.");
        }

        if (item.RulesetID > HighestOfficialRulesetId)
        {
            await EnsureAllUsersSupportRulesetAsync(item.RulesetID);
        }

        if (Facade.RulesetManager != null)
        {
            item.EnsureModsValid(Facade.RulesetManager);
        }

        await ValidatePlaylistItemBeatmap(item);

        await AddPlaylistItemInternal(item, user.UserID);
        await UpdateRoomStateIfRequired();
    }

    private async Task EnsureAllUsersSupportRulesetAsync(int rulesetId)
    {
        if (Facade.RulesetHashValidator == null)
        {
            return;
        }

        foreach (MultiplayerRoomUser roomUser in Users)
        {
            RulesetHashValidationFailure? failure = await Facade.RulesetHashValidator.ValidatePlayerAsync(
                rulesetId,
                roomUser.UserID);
            if (failure == null)
            {
                continue;
            }

            using IServiceScope scope = Facade.ScopeFactory.CreateScope();
            IUserRepository? userRepository = scope.ServiceProvider.GetService<IUserRepository>();
            string? username = userRepository == null
                ? null
                : await userRepository.GetUsernameByIdAsync(roomUser.UserID);
            throw new InvalidStateException(
                $"User {username ?? "unknown"} has non-existent or outdated rulesets: "
                + $"(name: {failure.RulesetName}, server-ver: {failure.ServerVersion})");
        }
    }

    /// <summary>
    /// Validates the beatmap referenced by a playlist item against the beatmap
    /// table: the beatmap must exist online, its
    /// checksum must match, and the ruleset must be compatible with the
    /// beatmap's play mode.
    /// </summary>
    /// <param name="item">
    /// The playlist item to validate.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    /// <exception cref="InvalidStateException">
    /// The beatmap is unknown, modified or incompatible with the ruleset.
    /// </exception>
    private async Task ValidatePlaylistItemBeatmap(MultiplayerPlaylistItem item)
    {
        bool repositoryRegistered = false;
        Beatmap? beatmap = await WithBeatmapRepositoryAndFetcher(
            async (repo, fetcher) =>
            {
                repositoryRegistered = true;
                return await repo.GetOrFetchByIdAsync(item.BeatmapID, fetcher);
            },
            repo =>
            {
                repositoryRegistered = true;
                return repo.GetByIdAsync(item.BeatmapID);
            });

        // Validation is skipped when no beatmap repository is registered.
        if (!repositoryRegistered)
        {
            return;
        }

        if (beatmap == null)
        {
            throw new InvalidStateException("Attempted to add a beatmap which does not exist online.");
        }

        // The checksum must always match the beatmap record (aligned with the
        // reference StandardMatchController).
        if (!string.Equals(item.BeatmapChecksum, beatmap.Checksum, StringComparison.Ordinal))
        {
            throw new InvalidStateException("Attempted to add a beatmap which has been modified.");
        }

        int[] availableIds = Facade.RulesetManager != null
            ? Facade.RulesetManager.GetAllRulesets().Select(r => r.RulesetInfo.OnlineID)
                .Where(id => id >= 0)
                .ToArray()
            : [0, 1, 2, 3];
        if (!availableIds.Contains(item.RulesetID))
        {
            throw new InvalidStateException("Attempted to select an unsupported ruleset.");
        }

        if (beatmap.Mode != 0 && item.RulesetID != beatmap.Mode)
        {
            throw new InvalidStateException("Attempted to select an invalid beatmap and ruleset combination.");
        }

        // The star rating is derived from the beatmap record.
        item.StarRating = beatmap.DifficultyRating;
    }

    /// <inheritdoc />
    public async Task EditPlaylistItem(int userId, MultiplayerPlaylistItem item)
    {
        MultiplayerRoomUser user = Users.FirstOrDefault(u => u.UserID == userId)
                                   ?? throw new InvalidStateException("User is not in the room.");

        MultiplayerPlaylistItem? existing = Playlist.FirstOrDefault(p => p.ID == item.ID) ?? throw new InvalidStateException("Attempted to change an item that doesn't exist.");
        if (existing.Expired)
        {
            throw new InvalidStateException("Attempted to change an item which has already been played.");
        }

        if (existing.OwnerID != user.UserID && !IsHostOrReferee(user))
        {
            throw new InvalidStateException("Attempted to change an item which is not owned by the user.");
        }

        bool isCurrentItem = existing.ID == Settings.PlaylistItemId;
        if (isCurrentItem && State != MultiplayerRoomState.Open)
        {
            throw new InvalidStateException("The current item in the room cannot be edited when currently being played.");
        }

        if (item.Freestyle && item.AllowedMods.Any())
        {
            throw new InvalidStateException("Cannot enqueue freestyle item with mods.");
        }

        if (item.RulesetID > HighestOfficialRulesetId)
        {
            await EnsureAllUsersSupportRulesetAsync(item.RulesetID);
        }

        if (Facade.RulesetManager != null)
        {
            item.EnsureModsValid(Facade.RulesetManager);
        }

        await ValidatePlaylistItemBeatmap(item);

        // Editing an item transfers its ownership to the editing user (aligned
        // with StandardMatchController.EditPlaylistItem).
        item.OwnerID = user.UserID;

        // Ensure the playlist order doesn't change.
        item.PlaylistOrder = existing.PlaylistOrder;

        Playlist dbItem = await WithRepository(repo => repo.EditItemToPlaylist(item, RoomId, user.UserID));
        MultiplayerPlaylistItem updated = dbItem.ToMultiplayerPlaylistItem();

        // The edited item's star rating is derived from its beatmap.
        updated.StarRating = await GetStarRating(updated.BeatmapID);

        int index = Playlist.IndexOf(existing);
        Playlist[index] = updated;

        await HandlePlaylistItemChanged(updated, !string.Equals(existing.BeatmapChecksum, updated.BeatmapChecksum, StringComparison.Ordinal));
    }

    /// <summary>
    /// Handles a change to a playlist item, re-validating user styles and
    /// unreadying users when the currently-selected item changed.
    /// </summary>
    /// <param name="item">
    /// The changed item.
    /// </param>
    /// <param name="beatmapChanged">
    /// Whether the beatmap changed.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    private async Task HandlePlaylistItemChanged(MultiplayerPlaylistItem item, bool beatmapChanged)
    {
        if (item.ID == Settings.PlaylistItemId)
        {
            await EnsureAllUsersValidStyle();
            await TrimAllUsersMods();
            await UnreadyAllUsers(beatmapChanged);
        }

        await Facade.EventDispatcher.PostPlaylistItemChangedAsync(RoomId, item);
    }

    /// <summary>
    /// Trims every user's mod selection to the set which is valid for the
    /// current playlist item, broadcasting the trimmed selection.
    /// </summary>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    private async Task TrimAllUsersMods()
    {
        if (Facade.RulesetManager == null)
        {
            return;
        }

        MultiplayerPlaylistItem currentItem = CurrentItem;

        foreach (MultiplayerRoomUser? user in Users.Where(u => u.Mods.Any()).ToArray())
        {
            if (currentItem.ValidateUserMods(user, user.Mods, Facade.RulesetManager, out IEnumerable<APIMod>? validMods))
            {
                continue;
            }

            APIMod[] trimmed = validMods.ToArray();
            user.Mods = trimmed;
            await Facade.EventDispatcher.PostUserModsChangedAsync(RoomId, user.UserID, trimmed);
        }
    }

    /// <inheritdoc />
    public async Task RemovePlaylistItem(int userId, long playlistItemId)
    {
        MultiplayerRoomUser user = Users.FirstOrDefault(u => u.UserID == userId)
                                   ?? throw new InvalidStateException("User is not in the room.");

        MultiplayerPlaylistItem? item = Playlist.FirstOrDefault(p => p.ID == playlistItemId) ?? throw new InvalidStateException("Item does not exist in the room.");
        bool isCurrentItem = item.ID == Settings.PlaylistItemId;

        if (isCurrentItem)
        {
            // The current item check is only an optimisation for this condition.
            // It is guaranteed for the single item in the room to be the current item.
            if (UpcomingItems.Count() == 1)
            {
                throw new InvalidStateException("The only item in the room cannot be removed.");
            }

            if (State != MultiplayerRoomState.Open)
            {
                throw new InvalidStateException("The current item in the room cannot be removed when currently being played.");
            }
        }

        if (item.OwnerID != user.UserID && !IsHostOrReferee(user))
        {
            throw new InvalidStateException("Attempted to remove an item which is not owned by the user.");
        }

        if (item.Expired)
        {
            throw new InvalidStateException("Attempted to remove an item which has already been played.");
        }

        // Protect playlist items which already have associated score tokens.
        if (await WithRepository(repo => repo.AnyScoreTokenExistsFor(playlistItemId)))
        {
            throw new InvalidStateException("Attempted to remove an item which has already been played.");
        }

        await WithRepository(repo => repo.RemoveItemToPlaylist(playlistItemId, RoomId, user.UserID));

        Playlist.Remove(item);

        await UpdatePlaylistOrder();

        // Repoint PlaylistItemId before notifying clients of the removal so that
        // the client's CurrentPlaylistItem (Single()) always resolves a valid item.
        await UpdateCurrentItem();

        // It's important for clients to be notified of the removal AFTER settings
        // are changed so that PlaylistItemId always points to a valid item.
        await Facade.EventDispatcher.PostPlaylistItemRemovedAsync(RoomId, playlistItemId);
        await UpdateRoomStateIfRequired();
    }

    /// <inheritdoc />
    public async Task HandleUserRequest(int userId, MatchUserRequest request)
    {
        MultiplayerRoomUser user = Users.FirstOrDefault(u => u.UserID == userId)
                                   ?? throw new InvalidStateException("User is not in the room.");

        switch (request)
        {
            case StartMatchCountdownRequest startMatchCountdownRequest:
                await StartMatchCountdown(startMatchCountdownRequest.Duration);
                break;

            case StopCountdownRequest stopCountdownRequest:
                await StopCountdown(stopCountdownRequest.ID);
                break;

            default:
                await HandleMatchUserRequest(user, request);
                break;
        }
    }

    /// <summary>
    /// Handles a match-type specific user request. Derived rooms override this.
    /// </summary>
    /// <param name="user">
    /// The requesting user.
    /// </param>
    /// <param name="request">
    /// The request.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    protected virtual Task HandleMatchUserRequest(MultiplayerRoomUser user, MatchUserRequest request)
    {
        // Unknown requests are silently ignored (aligned with the reference
        // StandardMatchController, which has no default case).
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles a user joining, hooking the room-specific match state.
    /// </summary>
    /// <param name="user">
    /// The joining user.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    protected virtual Task HandleUserJoined(MultiplayerRoomUser user) => Task.CompletedTask;

    /// <summary>
    /// Handles a user leaving, cleaning up the room-specific match state.
    /// </summary>
    /// <param name="user">
    /// The leaving user.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    protected virtual Task HandleUserLeft(MultiplayerRoomUser user) => Task.CompletedTask;

    /// <summary>
    /// Handles a user state change, updating the room-specific match state.
    /// </summary>
    /// <param name="user">
    /// The user whose state changed.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    protected virtual Task HandleUserStateChanged(MultiplayerRoomUser user) => Task.CompletedTask;

    /// <summary>
    /// Handles gameplay completion, resetting the room-specific match state and
    /// advancing the playlist queue (expiring the played item).
    /// </summary>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    protected virtual async Task HandleGameplayCompleted()
    {
        MultiplayerPlaylistItem currentItem = CurrentItem;

        // Expire the played item and persist, then let clients know it has finished.
        currentItem.Expired = true;
        currentItem.PlayedAt = DateTimeOffset.UtcNow;
        await WithRepository(repo => repo.EditItemToPlaylist(currentItem, RoomId, currentItem.OwnerID));
        await HandlePlaylistItemChanged(item: currentItem, beatmapChanged: true);

        await UpdatePlaylistOrder();

        // In host-only mode, duplicate the playlist item for the next round if no other non-expired items exist.
        if (Settings.QueueMode == QueueMode.HostOnly && Playlist.All(item => item.Expired))
        {
            MultiplayerPlaylistItem duplicate = currentItem.Clone();
            duplicate.ID = 0;
            duplicate.Expired = false;
            duplicate.PlayedAt = null;
            duplicate.PlaylistOrder = ushort.MaxValue;
            await AddPlaylistItemInternal(duplicate, currentItem.OwnerID);
        }

        await UpdateCurrentItem();
    }

    /// <summary>
    /// Updates <see cref="MultiplayerRoomSettings.PlaylistItemId"/> to the next
    /// non-expired playlist item (or the most-recently-played item when
    /// everything is expired), broadcasting the settings change when the
    /// selection moves.
    /// </summary>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    protected async Task UpdateCurrentItem()
    {
        if (Playlist.Count == 0)
        {
            Settings.PlaylistItemId = 0;
            return;
        }

        long lastItemId = Settings.PlaylistItemId;
        MultiplayerPlaylistItem nextItem = UpcomingItems.FirstOrDefault() ?? Playlist.OrderByDescending(i => i.PlayedAt).First();
        Settings.PlaylistItemId = nextItem.ID;

        if (nextItem.ID != lastItemId)
        {
            await HandleSettingsChanged(playlistItemChanged: true);
        }
    }

    /// <summary>
    /// Gets the upcoming (non-expired) playlist items in playlist order.
    /// </summary>
    private IEnumerable<MultiplayerPlaylistItem> UpcomingItems
        => Playlist.Where(i => !i.Expired).OrderBy(i => i.PlaylistOrder);

    /// <summary>
    /// Recomputes and persists the <see cref="MultiplayerPlaylistItem.PlaylistOrder"/>
    /// of all non-expired items according to the room's queueing mode.
    /// </summary>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    protected async Task UpdatePlaylistOrder()
    {
        List<MultiplayerPlaylistItem> orderedActiveItems;

        switch (Settings.QueueMode)
        {
            default:
                orderedActiveItems = Playlist.Where(item => !item.Expired).OrderBy(item => item.ID).ToList();
                break;

            case QueueMode.AllPlayersRoundRobin:
                orderedActiveItems = new List<MultiplayerPlaylistItem>();

                bool isFirstSet = true;
                Dictionary<int, int> firstSetOrderByUserId = new();

                // Group each user's items in order of addition.
                IEnumerable<IGrouping<int, MultiplayerPlaylistItem>> userItemGroups = Playlist.Where(item => !item.Expired).OrderBy(item => item.ID).GroupBy(item => item.OwnerID);

                foreach (IEnumerable<MultiplayerPlaylistItem> set in userItemGroups.Interleave())
                {
                    // Do some post processing on the set of items to ensure that the order is consistent.
                    if (isFirstSet)
                    {
                        // For the first set, preserve the existing order of items and break ties based on the order in which items were added to the queue.
                        orderedActiveItems.AddRange(set.OrderBy(item => item.PlaylistOrder).ThenBy(item => item.ID));

                        // Store the order of items to be used for all future sets.
                        firstSetOrderByUserId = orderedActiveItems.Select((item, index) => (item, index)).ToDictionary(i => i.item.OwnerID, i => i.index);
                    }
                    else
                    {
                        // For the non-first set, preserve the same ordering of users as in the first set.
                        orderedActiveItems.AddRange(set.OrderBy(i => firstSetOrderByUserId[i.OwnerID]));
                    }

                    isFirstSet = false;
                }

                break;
        }

        for (int i = 0; i < orderedActiveItems.Count; i++)
        {
            MultiplayerPlaylistItem item = orderedActiveItems[i];

            if (item.PlaylistOrder == i)
            {
                continue;
            }

            item.PlaylistOrder = (ushort)i;
            await WithRepository(repo => repo.EditItemToPlaylist(item, RoomId, item.OwnerID));
            await HandlePlaylistItemChanged(item: item, beatmapChanged: false);
        }
    }

    /// <summary>
    /// Initialises the match-type specific state for this room instance.
    /// Called by the room manager after construction or after a match-type change.
    /// </summary>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    internal virtual Task InitializeMatchState() => Task.CompletedTask;

    /// <summary>
    /// Persists the current settings (including match type) to the database
    /// without recreating the record. Used by settings changes and by the room
    /// manager on match-type changes.
    /// </summary>
    /// <param name="settings">
    /// The settings to persist.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    internal async Task UpdateRoomSettings(MultiplayerRoomSettings settings)
    {
        await WithRepository(repo => repo.UpdateRoomSettings(RoomId, settings));
    }

    /// <summary>
    /// Copies the live in-memory state from another room instance of the same room.
    /// Used by the room manager when swapping the room's implementation type.
    /// </summary>
    /// <param name="other">
    /// The room instance to copy state from.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    internal async Task CopyStateFrom(RoomBase other)
    {
        Users.Clear();
        foreach (MultiplayerRoomUser user in other.Users)
        {
            Users.Add(user);
        }

        Host = other.Host;
        State = other.State;
        Settings = other.Settings;
        ChannelId = other.ChannelId;

        Playlist.Clear();
        foreach (MultiplayerPlaylistItem item in other.Playlist)
        {
            Playlist.Add(item);
        }

        foreach (int user in other.BannedUsers)
        {
            BanUser(user);
        }

        await InitializeMatchState();
    }

    /// <summary>
    /// Updates the room state based on the current user states.
    /// </summary>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    public async Task UpdateRoomStateIfRequired()
    {
        switch (State)
        {
            case MultiplayerRoomState.Open:
                if (Settings.AutoStartEnabled)
                {
                    bool shouldHaveCountdown = !CurrentItem.Expired && Users.Any(u => u.State == MultiplayerUserState.Ready);
                    if (shouldHaveCountdown && !ActiveCountdowns.Any(c => c is MatchStartCountdown))
                    {
                        await StartCountdown(new MatchStartCountdown { TimeRemaining = Settings.AutoStartDuration }, StartMatch);
                    }
                }

                break;

            case MultiplayerRoomState.WaitingForLoad:
                int countGameplayUsers = Users.Count(u => u.State.IsGameplayState());
                int countReadyUsers = Users.Count(u => u.State == MultiplayerUserState.ReadyForGameplay);

                if (countReadyUsers == countGameplayUsers)
                {
                    await StartOrStopGameplay();
                }

                break;

            case MultiplayerRoomState.Playing:
                if (Users.All(u => u.State != MultiplayerUserState.Playing))
                {
                    bool anyUserFinishedPlay = false;

                    foreach (MultiplayerRoomUser? u in Users.Where(u => u.State == MultiplayerUserState.FinishedPlay).ToArray())
                    {
                        anyUserFinishedPlay = true;
                        await ChangeAndBroadcastUserState(u, MultiplayerUserState.Results);
                    }

                    await ChangeRoomState(MultiplayerRoomState.Open);

                    if (anyUserFinishedPlay)
                    {
                        await Facade.EventDispatcher.PostResultsReadyToGroupAsync(RoomId);
                    }

                    await HandleGameplayCompleted();
                }

                break;
        }
    }

    /// <summary>
    /// Starts gameplay for all loaded users and aborts it for those still loading.
    /// </summary>
    private async Task StartOrStopGameplay()
    {
        await StopAllCountdowns<ForceGameplayStartCountdown>();

        bool anyUserPlaying = false;

        foreach (MultiplayerRoomUser? user in Users.ToArray())
        {
            if (user.CanStartGameplay())
            {
                await ChangeAndBroadcastUserState(user, MultiplayerUserState.Playing);
                await Facade.EventDispatcher.PostGameplayStartedAsync(user.UserID);
                anyUserPlaying = true;
            }
            else if (user.State == MultiplayerUserState.WaitingForLoad)
            {
                await ChangeAndBroadcastUserState(user, MultiplayerUserState.Idle);
                await Facade.EventDispatcher.PostGameplayAbortedAsync(user.UserID, GameplayAbortReason.LoadTookTooLong);
            }
        }

        if (anyUserPlaying)
        {
            await ChangeRoomState(MultiplayerRoomState.Playing);
        }
        else
        {
            await ChangeRoomState(MultiplayerRoomState.Open);
            await HandleGameplayCompleted();
        }
    }

    /// <summary>
    /// Changes and broadcasts a user state, then notifies the match controller.
    /// </summary>
    /// <param name="user">
    /// The user whose state changed.
    /// </param>
    /// <param name="state">
    /// The new state.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    protected async Task ChangeAndBroadcastUserState(MultiplayerRoomUser user, MultiplayerUserState state)
    {
        user.State = state;
        await Facade.EventDispatcher.PostUserStateChangedAsync(RoomId, user.UserID, user.State);
        await HandleUserStateChanged(user);
    }

    private async Task ChangeRoomState(MultiplayerRoomState newState)
    {
        State = newState;
        await WithRepository(repo => repo.UpdateRoomStatus(RoomId, newState));
        await Facade.EventDispatcher.PostRoomStateChangedAsync(RoomId, newState);
    }

    private async Task RemoveUserInternal(MultiplayerRoomUser user)
    {
        Users.Remove(user);
        await WithRepository(repo => repo.RemoveUserFromRoom(RoomId, user.UserID));
        if (Facade.NotificationServer != null && ChannelId > 0)
        {
            Players.IPlayer? player = GetPlayer(user.UserID);
            if (player != null)
            {
                await Facade.NotificationServer.LeaveChannel(player, ChannelId);
            }
        }

        await HandleUserLeft(user);

        if (Host?.UserID == user.UserID)
        {
            MultiplayerRoomUser? newHost = Users.FirstOrDefault();
            if (newHost != null)
            {
                await SetHost(newHost.UserID);
            }
            else
            {
                Host = null;
            }
        }

        if (Users.Count == 0)
        {
            // Tournament rooms are retained for a grace period after the last
            // participant leaves so the match can be resumed; regular rooms
            // are disbanded immediately.
            if (TournamentMode)
            {
                TimeSpan retentionPeriod = Facade.ConfigManager?.Get<RealtimeConfig>().TournamentRoomRetentionPeriod
                                           ?? TimeSpan.FromMinutes(30);
                await ScheduleEndDateDestruction(DateTimeOffset.UtcNow + retentionPeriod);
            }
            else
            {
                await Disband();
            }

            return;
        }

        // A leaving user's skip vote may cause the remaining users' vote to pass.
        await CheckVotesToSkipPassed();

        await UpdateRoomStateIfRequired();
    }

    /// <summary>
    /// Disbands the room: marks it as ended in the database and notifies the
    /// room manager so the in-memory instance is destroyed.
    /// </summary>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    public async Task Disband()
    {
        CancelEndDateDestruction();
        await WithRepository(repo => repo.SetRoomEndDate(RoomId, DateTimeOffset.UtcNow));
        State = MultiplayerRoomState.Closed;

        if (OnDisbanded != null)
        {
            await OnDisbanded();
        }
    }

    /// <summary>
    /// Schedules destruction of the in-memory room once <paramref name="endDate"/>
    /// is reached, persisting the end date and starting a timer which disbands
    /// the room (removing it from the room manager) at that point.
    /// </summary>
    /// <param name="endDate">
    /// The date at which the room should be destroyed.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    private async Task ScheduleEndDateDestruction(DateTimeOffset endDate)
    {
        EndDate = endDate;
        await WithRepository(repo => repo.SetRoomEndDate(RoomId, endDate));

        CancelEndDateDestruction();

        CancellationTokenSource cancellationSource = new();
        _endDateCancellation = cancellationSource;

        _ = Task.Run(
            async () =>
            {
                try
                {
                    // Wait until the end date is reached, re-checking the deadline
                    // after any wake-up so an early wake cannot destroy the room
                    // before its end date.
                    while (DateTimeOffset.UtcNow < endDate)
                    {
                        TimeSpan delay = endDate - DateTimeOffset.UtcNow;
                        if (delay <= TimeSpan.Zero)
                        {
                            break;
                        }

                        await Task.Delay(delay, cancellationSource.Token).ConfigureAwait(false);
                    }

                    await RoomLock.WaitAsync(cancellationSource.Token).ConfigureAwait(false);
                    try
                    {
                        // Double-check inside the lock: a join may have cancelled
                        // the timer and cleared the end date in the meantime.
                        if (EndDate == null || DateTimeOffset.UtcNow < EndDate)
                        {
                            return;
                        }

                        await Disband();
                    }
                    finally
                    {
                        RoomLock.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    // A join cancelled the destruction.
                }
                catch (Exception ex)
                {
                    Facade.Logger?.LogError(ex, "Failed to destroy room {RoomId} at its end date.", RoomId);
                }
            },
            cancellationSource.Token);
    }

    /// <summary>
    /// Cancels any pending end-date destruction of the room.
    /// </summary>
    private void CancelEndDateDestruction()
    {
        _endDateCancellation?.Cancel();
        _endDateCancellation = null;
    }

    /// <summary>
    /// Sets the room's end date without starting the destruction timer, for
    /// tests which exercise the expired-room state (the timer would otherwise
    /// destroy the room immediately).
    /// </summary>
    /// <param name="endDate">
    /// The end date to set.
    /// </param>
    internal void SetEndDateForTesting(DateTimeOffset? endDate)
    {
        EndDate = endDate;
    }

    private async Task HandleSettingsChanged(bool playlistItemChanged)
    {
        await UnreadyAllUsers(playlistItemChanged);
        await Facade.EventDispatcher.PostRoomSettingsChangedAsync(RoomId, Settings);
    }

    /// <summary>
    /// Unreadies all users and resets their beatmap availability when the
    /// currently-selected playlist item changed.
    /// </summary>
    /// <param name="resetBeatmapAvailability">
    /// Whether to reset users' beatmap availability.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    private async Task UnreadyAllUsers(bool resetBeatmapAvailability)
    {
        foreach (MultiplayerRoomUser? u in Users.Where(u => u.State == MultiplayerUserState.Ready).ToArray())
        {
            await ChangeAndBroadcastUserState(u, MultiplayerUserState.Idle);
        }

        if (resetBeatmapAvailability)
        {
            foreach (MultiplayerRoomUser user in Users)
            {
                await ChangeUserBeatmapAvailability(user.UserID, new BeatmapAvailability(DownloadState.Unknown));
            }
        }

        // Assume some destructive operation took place to warrant unreadying all
        // users, and pre-emptively stop any match start countdown.
        await StopAllCountdowns<MatchStartCountdown>();
    }

    /// <summary>
    /// Adds a playlist item to the in-memory queue and persists it.
    /// </summary>
    /// <param name="item">
    /// The item to add.
    /// </param>
    /// <param name="ownerId">
    /// The owning user ID.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    protected async Task AddPlaylistItemInternal(MultiplayerPlaylistItem item, int ownerId)
    {
        // New items are appended to the queue; the queue reorder assigns the
        // real playlist order afterwards.
        item.PlaylistOrder = ushort.MaxValue;
        item.Expired = false;
        item.PlayedAt = null;

        Playlist dbItem = await WithRepository(repo => repo.AddItemToPlaylist(item, RoomId, ownerId));
        MultiplayerPlaylistItem converted = dbItem.ToMultiplayerPlaylistItem();

        // The added item's star rating is derived from its beatmap.
        converted.StarRating = await GetStarRating(converted.BeatmapID);

        Playlist.Add(converted);
        await Facade.EventDispatcher.PostPlaylistItemAddedAsync(RoomId, converted);

        // Reorder the queue: in
        // round-robin mode this interleaves the new item into the queue.
        if (State == MultiplayerRoomState.Open)
        {
            await UpdatePlaylistOrder();

            // Repoint the current item to the first non-expired item (aligned
            // with StandardMatchController.addItem -> updateCurrentItem), which
            // selects a newly-added item when the whole playlist is expired.
            await UpdateCurrentItem();
        }
        else
        {
            // First item added: make sure the client can resolve CurrentPlaylistItem.
            if (Settings.PlaylistItemId == 0)
            {
                await UpdateCurrentItem();
            }
        }
    }

    private async Task ChangeUserVoteToSkipIntro(MultiplayerRoomUser user, bool voted)
    {
        if (user.VotedToSkipIntro == voted)
        {
            return;
        }

        user.VotedToSkipIntro = voted;
        await Facade.EventDispatcher.PostUserVotedToSkipIntroAsync(RoomId, user.UserID, voted);
    }

    private async Task CheckVotesToSkipPassed()
    {
        int countVotedUsers = Users.Count(u => u is { State: MultiplayerUserState.Playing, VotedToSkipIntro: true });
        int countGameplayUsers = Users.Count(u => u.State == MultiplayerUserState.Playing);

        if (countVotedUsers >= (countGameplayUsers / 2) + 1)
        {
            await Facade.EventDispatcher.PostVoteToSkipIntroPassedAsync(RoomId);
        }
    }

    private static void EnsureValidStateSwitch(MultiplayerRoomUserRole userRole, MultiplayerUserState oldState, MultiplayerUserState newState)
    {
        switch (newState)
        {
            case MultiplayerUserState.Idle:
                if (oldState.IsGameplayState())
                {
                    throw new InvalidStateException("Cannot return to idle without aborting gameplay.");
                }

                break;

            case MultiplayerUserState.Ready:
                if (oldState != MultiplayerUserState.Idle)
                {
                    throw new InvalidStateChangeException(oldState, newState);
                }

                break;

            case MultiplayerUserState.WaitingForLoad:
            case MultiplayerUserState.Playing:
            case MultiplayerUserState.Results:
                // State is managed by the server.
                throw new InvalidStateChangeException(oldState, newState);

            case MultiplayerUserState.Loaded:
                if (oldState != MultiplayerUserState.WaitingForLoad)
                {
                    throw new InvalidStateChangeException(oldState, newState);
                }

                break;

            case MultiplayerUserState.ReadyForGameplay:
                if (oldState != MultiplayerUserState.Loaded)
                {
                    throw new InvalidStateChangeException(oldState, newState);
                }

                break;

            case MultiplayerUserState.FinishedPlay:
                if (oldState != MultiplayerUserState.Playing)
                {
                    throw new InvalidStateChangeException(oldState, newState);
                }

                break;

            case MultiplayerUserState.Spectating:
                if (oldState != MultiplayerUserState.Idle && oldState != MultiplayerUserState.Ready)
                {
                    throw new InvalidStateChangeException(oldState, newState);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(newState), newState, message: null);
        }

        if (userRole == MultiplayerRoomUserRole.Referee
            && newState != MultiplayerUserState.Idle
            && newState != MultiplayerUserState.Spectating)
        {
            throw new InvalidStateException("Referees cannot participate in the match.");
        }
    }

    #region Countdowns

    /// <summary>
    /// The amount of time allowed for players to finish loading gameplay.
    /// </summary>
    /// <summary>
    /// Starts a countdown which invokes <paramref name="onComplete"/> when it finishes.
    /// </summary>
    /// <typeparam name="T">
    /// The countdown type.
    /// </typeparam>
    /// <param name="countdown">
    /// The countdown to start.
    /// </param>
    /// <param name="onComplete">
    /// The callback invoked when the countdown completes.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    public async Task StartCountdown<T>(T countdown, Func<Task>? onComplete = null)
        where T : MultiplayerCountdown
    {
        if (countdown.IsExclusive)
        {
            await StopAllCountdowns<T>();
        }

        countdown.ID = Interlocked.Increment(ref _nextCountdownId);

        CountdownInfo countdownInfo = new(countdown);
        _trackedCountdowns[countdown] = countdownInfo;
        ActiveCountdowns.Add(countdown);

        await Facade.EventDispatcher.PostCountdownStartedAsync(RoomId, countdown);

        countdownInfo.Task = RunCountdown(countdown, countdownInfo, onComplete);
    }

    private async Task RunCountdown(MultiplayerCountdown countdown, CountdownInfo countdownInfo, Func<Task>? onComplete)
    {
        try
        {
            using CancellationTokenSource cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                countdownInfo.StopSource.Token,
                countdownInfo.SkipSource.Token);
            await Task.Delay(countdownInfo.Duration, cancellationSource.Token);
        }
        catch (OperationCanceledException)
        {
            // Clients need to be notified of cancellations in the following code.
        }

        try
        {
            if (countdownInfo.StopSource.IsCancellationRequested)
            {
                return;
            }

            await StopCountdown(countdown);

            if (onComplete != null)
            {
                await onComplete();
            }
        }
        finally
        {
            countdownInfo.Dispose();
        }
    }

    /// <summary>
    /// Stops all countdowns of the given type.
    /// </summary>
    private async Task StopAllCountdowns<T>()
        where T : MultiplayerCountdown
    {
        foreach (T? countdown in ActiveCountdowns.OfType<T>().ToArray())
        {
            await StopCountdown(countdown);
        }
    }

    /// <summary>
    /// Stops the given countdown, preventing its callback from running.
    /// </summary>
    /// <param name="countdown">
    /// The countdown to stop.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    public async Task StopCountdown(MultiplayerCountdown? countdown)
    {
        if (countdown == null || !_trackedCountdowns.TryRemove(countdown, out CountdownInfo? countdownInfo))
        {
            return;
        }

        await countdownInfo.StopSource.CancelAsync();

        ActiveCountdowns.Remove(countdown);
        await Facade.EventDispatcher.PostCountdownStoppedAsync(RoomId, countdown);
    }

    /// <summary>
    /// Finds an active countdown by ID.
    /// </summary>
    /// <param name="countdownId">
    /// The countdown ID.
    /// </param>
    /// <returns>
    /// The countdown, or <see langword="null"/> if not found.
    /// </returns>
    public MultiplayerCountdown? FindCountdownById(int countdownId)
        => ActiveCountdowns.SingleOrDefault(c => c.ID == countdownId);

    private sealed class CountdownInfo : IDisposable
    {
        public MultiplayerCountdown Countdown { get; }

        public CancellationTokenSource StopSource { get; } = new();

        public CancellationTokenSource SkipSource { get; } = new();

        public TimeSpan Duration { get; }

        public Task Task { get; set; } = Task.CompletedTask;

        public CountdownInfo(MultiplayerCountdown countdown)
        {
            Countdown = countdown;
            Duration = countdown.TimeRemaining;
        }

        public void Dispose()
        {
            StopSource.Dispose();
            SkipSource.Dispose();
        }
    }

    #endregion
}