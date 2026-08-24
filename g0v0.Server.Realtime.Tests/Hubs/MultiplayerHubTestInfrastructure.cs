// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

// This file intentionally hosts several small test helper types.
#pragma warning disable MA0048 // File name must match type name
#pragma warning disable SA1402 // File may only contain a single type

using System.Collections.Concurrent;
using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Realtime.Hubs;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;

namespace g0v0.Server.Realtime.Tests.Hubs;

/// <summary>
/// Test infrastructure for <see cref="MultiplayerHub"/> tests: an in-memory
/// SignalR router, a recording client, fakes for hub context/group manager and
/// a scope factory which resolves the fake room repository.
/// </summary>
internal static class MultiplayerHubTestInfrastructure
{
}

/// <summary>
/// Routes client invocations to per-connection recorders.
/// </summary>
internal sealed class MultiplayerClientRouter
{
    private readonly Dictionary<string, RecordingMultiplayerClient> _connections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _groups = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> ConnectionIds => _connections.Keys;

    public void AddToGroup(string connectionId, string groupName)
    {
        EnsureConnection(connectionId);
        if (!_groups.TryGetValue(groupName, out HashSet<string>? members))
        {
            members = new HashSet<string>(StringComparer.Ordinal);
            _groups[groupName] = members;
        }

        members.Add(connectionId);
    }

    public IMultiplayerClient CreateProxy(Func<MultiplayerClientRouter, IEnumerable<string>> targetSelector)
        => new RoutedMultiplayerClient(this, targetSelector);

    public void EnsureConnection(string connectionId)
    {
        if (!_connections.ContainsKey(connectionId))
        {
            _connections[connectionId] = new RecordingMultiplayerClient();
        }
    }

    /// <summary>
    /// Aliases one connection key to another so both route to the same recorder.
    /// </summary>
    public void AliasConnection(string alias, string target)
    {
        EnsureConnection(target);
        _connections[alias] = _connections[target];
    }

    public RecordingMultiplayerClient GetRecorder(string connectionId)
    {
        EnsureConnection(connectionId);
        return _connections[connectionId];
    }

    public string[] GetGroupMembers(string groupName)
        => _groups.TryGetValue(groupName, out HashSet<string>? members) ? members.ToArray() : [];

    public void RemoveFromGroup(string connectionId, string groupName)
    {
        if (_groups.TryGetValue(groupName, out HashSet<string>? members))
        {
            members.Remove(connectionId);
        }
    }

    private sealed class RoutedMultiplayerClient(
        MultiplayerClientRouter router,
        Func<MultiplayerClientRouter, IEnumerable<string>> targetSelector) : IMultiplayerClient
    {
        public Task RoomStateChanged(MultiplayerRoomState state)
            => Task.WhenAll(Targets().Select(target => target.RoomStateChanged(state)));

        public Task UserJoined(MultiplayerRoomUser user)
            => Task.WhenAll(Targets().Select(target => target.UserJoined(user)));

        public Task UserLeft(MultiplayerRoomUser user)
            => Task.WhenAll(Targets().Select(target => target.UserLeft(user)));

        public Task UserKicked(MultiplayerRoomUser user)
            => Task.WhenAll(Targets().Select(target => target.UserKicked(user)));

        public Task Invited(int invitedBy, long roomId, string password)
            => Task.WhenAll(Targets().Select(target => target.Invited(invitedBy, roomId, password)));

        public Task HostChanged(int userId)
            => Task.WhenAll(Targets().Select(target => target.HostChanged(userId)));

        public Task SettingsChanged(MultiplayerRoomSettings newSettings)
            => Task.WhenAll(Targets().Select(target => target.SettingsChanged(newSettings)));

        public Task UserStateChanged(int userId, MultiplayerUserState state)
            => Task.WhenAll(Targets().Select(target => target.UserStateChanged(userId, state)));

        public Task MatchUserStateChanged(int userId, MatchUserState state)
            => Task.WhenAll(Targets().Select(target => target.MatchUserStateChanged(userId, state)));

        public Task MatchRoomStateChanged(MatchRoomState state)
            => Task.WhenAll(Targets().Select(target => target.MatchRoomStateChanged(state)));

        public Task MatchEvent(MatchServerEvent e)
            => Task.WhenAll(Targets().Select(target => target.MatchEvent(e)));

        public Task UserBeatmapAvailabilityChanged(int userId, BeatmapAvailability beatmapAvailability)
            => Task.WhenAll(Targets().Select(target => target.UserBeatmapAvailabilityChanged(userId, beatmapAvailability)));

        public Task UserStyleChanged(int userId, int? beatmapId, int? rulesetId)
            => Task.WhenAll(Targets().Select(target => target.UserStyleChanged(userId, beatmapId, rulesetId)));

        public Task UserModsChanged(int userId, IEnumerable<APIMod> mods)
            => Task.WhenAll(Targets().Select(target => target.UserModsChanged(userId, mods)));

        public Task LoadRequested()
            => Task.WhenAll(Targets().Select(static target => target.LoadRequested()));

        public Task GameplayStarted()
            => Task.WhenAll(Targets().Select(static target => target.GameplayStarted()));

        public Task GameplayAborted(GameplayAbortReason reason)
            => Task.WhenAll(Targets().Select(target => target.GameplayAborted(reason)));

        public Task ResultsReady()
            => Task.WhenAll(Targets().Select(static target => target.ResultsReady()));

        public Task PlaylistItemAdded(MultiplayerPlaylistItem item)
            => Task.WhenAll(Targets().Select(target => target.PlaylistItemAdded(item)));

        public Task PlaylistItemRemoved(long playlistItemId)
            => Task.WhenAll(Targets().Select(target => target.PlaylistItemRemoved(playlistItemId)));

        public Task PlaylistItemChanged(MultiplayerPlaylistItem item)
            => Task.WhenAll(Targets().Select(target => target.PlaylistItemChanged(item)));

        public Task UserVotedToSkipIntro(int userId, bool voted)
            => Task.WhenAll(Targets().Select(target => target.UserVotedToSkipIntro(userId, voted)));

        public Task VoteToSkipIntroPassed()
            => Task.WhenAll(Targets().Select(static target => target.VoteToSkipIntroPassed()));

        public Task UserRoleChanged(int userId, MultiplayerRoomUserRole newRole)
            => Task.WhenAll(Targets().Select(target => target.UserRoleChanged(userId, newRole)));

        public Task DisconnectRequested()
            => Task.WhenAll(Targets().Select(static target => target.DisconnectRequested()));

        public Task ServerShuttingDown()
            => Task.WhenAll(Targets().Select(static target => target.ServerShuttingDown()));

        private IEnumerable<RecordingMultiplayerClient> Targets()
            => targetSelector(router).Distinct(StringComparer.Ordinal).Select(router.GetRecorder);
    }
}

/// <summary>
/// Records all <see cref="IMultiplayerClient"/> invocations routed to a connection.
/// </summary>
internal sealed class RecordingMultiplayerClient : IMultiplayerClient
{
    public List<MultiplayerRoomState> RoomStates { get; } = [];

    public List<MultiplayerRoomUser> UserJoinedEvents { get; } = [];

    public List<MultiplayerRoomUser> UserLeftEvents { get; } = [];

    public List<MultiplayerRoomUser> UserKickedEvents { get; } = [];

    public List<int> HostChangedEvents { get; } = [];

    public List<int> LoadRequestedEvents { get; } = [];

    public List<int> GameplayStartedEvents { get; } = [];

    public List<int> ResultsReadyEvents { get; } = [];

    public List<MultiplayerRoomSettings> SettingsChangedEvents { get; } = [];

    public List<MultiplayerPlaylistItem> PlaylistItemAddedEvents { get; } = [];

    public List<MultiplayerPlaylistItem> PlaylistItemChangedEvents { get; } = [];

    public List<(int UserId, int? BeatmapId, int? RulesetId)> UserStyleChangedEvents { get; } = [];

    public List<string> EventLog { get; } = [];

    public int DisconnectRequestCount { get; private set; }

    public void Clear()
    {
        RoomStates.Clear();
        UserJoinedEvents.Clear();
        UserLeftEvents.Clear();
        UserKickedEvents.Clear();
        HostChangedEvents.Clear();
        LoadRequestedEvents.Clear();
        GameplayStartedEvents.Clear();
        ResultsReadyEvents.Clear();
        SettingsChangedEvents.Clear();
        PlaylistItemAddedEvents.Clear();
        PlaylistItemChangedEvents.Clear();
        UserStyleChangedEvents.Clear();
        EventLog.Clear();
        DisconnectRequestCount = 0;
    }

    public Task RoomStateChanged(MultiplayerRoomState state)
    {
        RoomStates.Add(state);
        return Task.CompletedTask;
    }

    public Task UserJoined(MultiplayerRoomUser user)
    {
        UserJoinedEvents.Add(user);
        return Task.CompletedTask;
    }

    public Task UserLeft(MultiplayerRoomUser user)
    {
        UserLeftEvents.Add(user);
        return Task.CompletedTask;
    }

    public Task UserKicked(MultiplayerRoomUser user)
    {
        UserKickedEvents.Add(user);
        return Task.CompletedTask;
    }

    public Task Invited(int invitedBy, long roomId, string password) => Task.CompletedTask;

    public Task HostChanged(int userId)
    {
        HostChangedEvents.Add(userId);
        return Task.CompletedTask;
    }

    public Task SettingsChanged(MultiplayerRoomSettings newSettings)
    {
        SettingsChangedEvents.Add(newSettings);
        EventLog.Add(nameof(SettingsChanged));
        return Task.CompletedTask;
    }

    public Task UserStateChanged(int userId, MultiplayerUserState state) => Task.CompletedTask;

    public Task MatchUserStateChanged(int userId, MatchUserState state) => Task.CompletedTask;

    public Task MatchRoomStateChanged(MatchRoomState state) => Task.CompletedTask;

    public Task MatchEvent(MatchServerEvent e) => Task.CompletedTask;

    public Task UserBeatmapAvailabilityChanged(int userId, BeatmapAvailability beatmapAvailability) => Task.CompletedTask;

    public Task UserStyleChanged(int userId, int? beatmapId, int? rulesetId)
    {
        UserStyleChangedEvents.Add((userId, beatmapId, rulesetId));
        return Task.CompletedTask;
    }

    public Task UserModsChanged(int userId, IEnumerable<APIMod> mods) => Task.CompletedTask;

    public Task LoadRequested()
    {
        LoadRequestedEvents.Add(1);
        return Task.CompletedTask;
    }

    public Task GameplayStarted()
    {
        GameplayStartedEvents.Add(1);
        return Task.CompletedTask;
    }

    public Task GameplayAborted(GameplayAbortReason reason) => Task.CompletedTask;

    public Task ResultsReady()
    {
        ResultsReadyEvents.Add(1);
        return Task.CompletedTask;
    }

    public Task PlaylistItemAdded(MultiplayerPlaylistItem item)
    {
        PlaylistItemAddedEvents.Add(item);
        return Task.CompletedTask;
    }

    public Task PlaylistItemRemoved(long playlistItemId)
    {
        EventLog.Add(nameof(PlaylistItemRemoved));
        return Task.CompletedTask;
    }

    public Task PlaylistItemChanged(MultiplayerPlaylistItem item)
    {
        PlaylistItemChangedEvents.Add(item);
        EventLog.Add(nameof(PlaylistItemChanged));
        return Task.CompletedTask;
    }

    public Task UserVotedToSkipIntro(int userId, bool voted) => Task.CompletedTask;

    public Task VoteToSkipIntroPassed() => Task.CompletedTask;

    public Task UserRoleChanged(int userId, MultiplayerRoomUserRole newRole) => Task.CompletedTask;

    public Task DisconnectRequested()
    {
        DisconnectRequestCount++;
        return Task.CompletedTask;
    }

    public Task ServerShuttingDown() => Task.CompletedTask;
}

/// <summary>
/// Fake hub context resolving clients/groups through the router.
/// </summary>
internal sealed class FakeHubContext(MultiplayerClientRouter router, FakeGroupManager? groupManager = null)
    : IHubContext<MultiplayerHub, IMultiplayerClient>
{
    public IHubClients<IMultiplayerClient> Clients { get; } = new FakeHubClients(router);

    public IGroupManager Groups { get; } = groupManager ?? new FakeGroupManager(router);
}

internal sealed class FakeHubCallerClients(MultiplayerClientRouter router, string callerConnectionId)
    : IHubCallerClients<IMultiplayerClient>
{
    public IMultiplayerClient All => router.CreateProxy(static state => state.ConnectionIds);

    public IMultiplayerClient Caller => router.CreateProxy(_ => [callerConnectionId]);

    public IMultiplayerClient Others => router.CreateProxy(state => state.ConnectionIds.Where(id => !string.Equals(id, callerConnectionId, StringComparison.Ordinal)));

    public IMultiplayerClient OthersInGroup(string groupName) =>
        router.CreateProxy(state => state.GetGroupMembers(groupName).Where(id => !string.Equals(id, callerConnectionId, StringComparison.Ordinal)));

    public IMultiplayerClient AllExcept(IReadOnlyList<string> excludedConnectionIds) =>
        router.CreateProxy(state => state.ConnectionIds.Except(excludedConnectionIds, StringComparer.Ordinal));

    public IMultiplayerClient Client(string connectionId) => router.CreateProxy(_ => [connectionId]);

    public IMultiplayerClient Clients(IReadOnlyList<string> connectionIds) => router.CreateProxy(_ => connectionIds);

    public IMultiplayerClient Group(string groupName) => router.CreateProxy(state => state.GetGroupMembers(groupName));

    public IMultiplayerClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
        router.CreateProxy(state => state.GetGroupMembers(groupName).Except(excludedConnectionIds, StringComparer.Ordinal));

    public IMultiplayerClient Groups(IReadOnlyList<string> groupNames) => router.CreateProxy(_ => []);

    public IMultiplayerClient User(string userId) => router.CreateProxy(_ => [userId]);

    public IMultiplayerClient Users(IReadOnlyList<string> userIds) => router.CreateProxy(_ => userIds);
}

internal sealed class FakeHubClients(MultiplayerClientRouter router) : IHubClients<IMultiplayerClient>
{
    public IMultiplayerClient All => router.CreateProxy(static state => state.ConnectionIds);

    public IMultiplayerClient AllExcept(IReadOnlyList<string> excludedConnectionIds) =>
        router.CreateProxy(state => state.ConnectionIds.Except(excludedConnectionIds, StringComparer.Ordinal));

    public IMultiplayerClient Client(string connectionId) => router.CreateProxy(_ => [connectionId]);

    public IMultiplayerClient Clients(IReadOnlyList<string> connectionIds) => router.CreateProxy(_ => connectionIds);

    public IMultiplayerClient Group(string groupName) => router.CreateProxy(state => state.GetGroupMembers(groupName));

    public IMultiplayerClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
        router.CreateProxy(state => state.GetGroupMembers(groupName).Except(excludedConnectionIds, StringComparer.Ordinal));

    public IMultiplayerClient Groups(IReadOnlyList<string> groupNames) => router.CreateProxy(_ => []);

    public IMultiplayerClient User(string userId) => router.CreateProxy(_ => [userId]);

    public IMultiplayerClient Users(IReadOnlyList<string> userIds) => router.CreateProxy(_ => userIds);
}

internal sealed class FakeGroupManager(MultiplayerClientRouter router) : IGroupManager
{
    public List<(string ConnectionId, string GroupName)> AddedGroups { get; } = [];

    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        AddedGroups.Add((connectionId, groupName));
        router.AddToGroup(connectionId, groupName);
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        router.RemoveFromGroup(connectionId, groupName);
        return Task.CompletedTask;
    }

    public void Reset()
    {
        AddedGroups.Clear();
    }
}

/// <summary>
/// Scope factory which always resolves the same fake room repository instance.
/// </summary>
internal sealed class FakeScopeFactory(
    FakeRoomRepository roomRepository,
    IBeatmapRepository? beatmapRepository = null,
    IRelationshipRepository? relationshipRepository = null,
    IUserRepository? userRepository = null) : IServiceScopeFactory
{
    public IServiceScope CreateScope() => new FakeScope(roomRepository, beatmapRepository, relationshipRepository, userRepository);

    private sealed class FakeScope(
        FakeRoomRepository roomRepository,
        IBeatmapRepository? beatmapRepository,
        IRelationshipRepository? relationshipRepository,
        IUserRepository? userRepository) : IServiceScope
    {
        public IServiceProvider ServiceProvider => new FakeServiceProvider(roomRepository, beatmapRepository, relationshipRepository, userRepository);

        public void Dispose()
        {
        }
    }

    private sealed class FakeServiceProvider(
        FakeRoomRepository roomRepository,
        IBeatmapRepository? beatmapRepository,
        IRelationshipRepository? relationshipRepository,
        IUserRepository? userRepository) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(IRoomRepository)
                ? roomRepository
                : serviceType == typeof(IBeatmapRepository)
                ? beatmapRepository
                : serviceType == typeof(IRelationshipRepository)
                ? relationshipRepository
                : serviceType == typeof(IUserRepository) ? userRepository : (object?)null;
        }
    }
}

internal sealed class TestHubCallerContext(string connectionId, string userIdentifier) : HubCallerContext
{
    private readonly FeatureCollection _features = new();
    private readonly Dictionary<object, object?> _items = [];

    public override string ConnectionId => connectionId;

    public override string UserIdentifier => userIdentifier;

    public override System.Security.Claims.ClaimsPrincipal? User => null;

    public override IDictionary<object, object?> Items => _items;

    public override IFeatureCollection Features => _features;

    public override CancellationToken ConnectionAborted => CancellationToken.None;

    public override void Abort()
    {
    }
}

internal sealed class RecordingTransport : IInterProcessCommunicationTransport
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<Func<string, Task>>> _subscriptions = new(StringComparer.Ordinal);

    public Task PublishAsync(string channel, string payload)
        => !_subscriptions.TryGetValue(channel, out ConcurrentBag<Func<string, Task>>? handlers)
            ? Task.CompletedTask
            : Task.WhenAll(handlers.Select(handler => handler(payload)));

    public void Subscribe(string channel, Func<string, Task> handler)
    {
        _subscriptions.GetOrAdd(channel, _ => []).Add(handler);
    }
}