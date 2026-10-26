// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Realtime.Notifications.Bot;
using g0v0.Server.Realtime.Notifications.Bot.Commands;
using g0v0.Server.Realtime.Objects.Rooms;
using Moq;
using NUnit.Framework;
using osu.Game.Online.Chat;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Countdown;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using OsuMatchType = osu.Game.Online.Rooms.MatchType;

namespace g0v0.Server.Realtime.Tests.Multiplayer;

/// <summary>
/// Tests for the tournament management (<c>!mp</c>) commands.
/// </summary>
[TestFixture]
public class MultiplayerCommandsTests : MultiplayerTestBase
{
    private const string PermissionDenied = "You don't have the required privilege to perform this action.";

    private MultiplayerCommands _commands = null!;

    private FakeBeatmapRepository _beatmaps = null!;

    /// <inheritdoc />
    /// <remarks>
    /// The room facade runs without a beatmap repository so playlist edits are
    /// not re-validated against a beatmap record: the in-memory room repository
    /// does not round-trip playlist beatmap checksums. The commands themselves
    /// get a beatmap repository through their own scope factory below, which is
    /// what <c>!mp map</c> actually needs.
    /// </remarks>
    protected override bool EnableBeatmapValidation => false;

    /// <inheritdoc />
    protected override bool EnableRelationshipValidation => true;

    [SetUp]
    public void SetUpCommands()
    {
        _beatmaps = new FakeBeatmapRepository();
        FakeMultiplayerScopeFactory scopeFactory = new(RoomRepository, _beatmaps, RelationshipRepository, UserRepository, ChatRepository);
        _commands = new MultiplayerCommands(scopeFactory, RoomManager, EventDispatcher, RulesetManager);
        UserRepository!.AddUser(new User { Id = UserId, Username = "Host" });
        UserRepository.AddUser(new User { Id = UserId2, Username = "Guest" });
    }

    [Test]
    public async Task Help_ListsSupportedSubCommands()
    {
        string? reply = await _commands.HandleAsync(new BotCommandContext(UserId, 0, ChannelType.Public, ["help"]));

        Assert.That(reply, Does.Contain("!mp make"));
        Assert.That(reply, Does.Contain("!mp listrefs"));
        Assert.That(reply, Does.Contain("!mp aborttimer"));
    }

    [Test]
    public async Task NoArguments_ShowsUsage()
    {
        string? reply = await _commands.HandleAsync(new BotCommandContext(UserId, 0, ChannelType.Public, []));

        Assert.That(reply, Is.EqualTo("Usage: !mp <subcommand> [args]. Try !mp help."));
    }

    [Test]
    public async Task UnsupportedSubCommand_IsRejected()
    {
        string? reply = await _commands.HandleAsync(new BotCommandContext(UserId, 0, ChannelType.Public, ["makeprivate"]));

        Assert.That(reply, Is.EqualTo("!mp makeprivate is currently not supported."));
    }

    [Test]
    public async Task OutsideMultiplayerChannel_IsRejected()
    {
        IRoom room = await CreateRoomAsync();

        string? reply = await _commands.HandleAsync(new BotCommandContext(UserId, room.ChannelId, ChannelType.Public, ["name", "Nope"]));

        Assert.That(reply, Is.EqualTo("The !mp command can only be used in a multiplayer channel."));
        Assert.That(room.Settings.Name, Is.Not.EqualTo("Nope"));
    }

    [Test]
    public async Task UnknownSubCommand_IsReported()
    {
        IRoom room = await CreateRoomAsync();

        string? reply = await RunAsync(room, UserId, "frobnicate");

        Assert.That(reply, Is.EqualTo("Unknown mp subcommand. Send !mp help for usage."));
    }

    [Test]
    public async Task NonHost_CannotRunCommands()
    {
        IRoom room = await CreateRoomWithGuestAsync();

        string? reply = await RunAsync(room, UserId2, "name", "Guest", "Room");

        Assert.That(reply, Is.EqualTo(PermissionDenied));
        Assert.That(room.Settings.Name, Is.Not.EqualTo("Guest Room"));
    }

    [Test]
    public async Task HostRenamesRoom()
    {
        IRoom room = await CreateRoomAsync();

        string? reply = await RunAsync(room, UserId, "name", "Grand", "Finals");

        Assert.That(reply, Is.EqualTo("Room name updated to \"Grand Finals\"."));
        Assert.That(room.Settings.Name, Is.EqualTo("Grand Finals"));
    }

    [Test]
    public async Task HostResizesRoom()
    {
        IRoom room = await CreateRoomAsync();

        Assert.That(await RunAsync(room, UserId, "size", "4"), Is.EqualTo("Changed room size to 4."));
        Assert.That(room.Settings.MaxParticipants, Is.EqualTo(4));

        Assert.That(await RunAsync(room, UserId, "size", "1"), Does.StartWith("Invalid room size."));
        Assert.That(await RunAsync(room, UserId, "size"), Is.EqualTo("Usage: !mp size <size>"));
    }

    [Test]
    public async Task HostSwitchesTeamMode()
    {
        IRoom room = await CreateRoomAsync();

        Assert.That(await RunAsync(room, UserId, "set", "2"), Is.EqualTo("Team mode updated to Team VS."));

        // The room implementation is replaced when the match type changes.
        Assert.That(Hub.GetRoom(RoomId)!.Settings.MatchType, Is.EqualTo(OsuMatchType.TeamVersus));
        Assert.That(await RunAsync(Hub.GetRoom(RoomId)!, UserId, "set", "1"), Does.StartWith("Unsupported teammode."));
    }

    [Test]
    public async Task HostLocksAndUnlocksSlots()
    {
        IRoom room = await CreateRoomAsync();

        Assert.That(await RunAsync(room, UserId, "lock"), Is.EqualTo("Room is now locked."));
        Assert.That(((StandardMatchRoomState)room.MatchState!).Locked, Is.True);

        Assert.That(await RunAsync(room, UserId, "unlock"), Is.EqualTo("Room is now unlocked."));
        Assert.That(((StandardMatchRoomState)room.MatchState!).Locked, Is.False);
    }

    [Test]
    public async Task HostPromotesReferee_WhoCanThenControlTheRoom()
    {
        IRoom room = await CreateRoomWithGuestAsync();

        Assert.That(await RunAsync(room, UserId, "addref", "Guest"), Is.EqualTo("Added Guest to referees."));
        Assert.That(room.Users.Single(user => user.UserID == UserId2).Role, Is.EqualTo(MultiplayerRoomUserRole.Referee));
        Receiver.Verify(client => client.UserRoleChanged(UserId2, MultiplayerRoomUserRole.Referee), Times.Once);

        Assert.That(
            await RunAsync(room, UserId2, "name", "Referee", "Room"),
            Is.EqualTo("Room name updated to \"Referee Room\"."));

        // A referee must not be able to grant or revoke referee status.
        Assert.That(await RunAsync(room, UserId2, "addref", "Host"), Is.EqualTo(PermissionDenied));

        Assert.That(await RunAsync(room, UserId, "removeref", "Guest"), Is.EqualTo("Removed Guest from referees."));
        Assert.That(room.Users.Single(user => user.UserID == UserId2).Role, Is.EqualTo(MultiplayerRoomUserRole.Player));
        Assert.That(await RunAsync(room, UserId2, "name", "Nope"), Is.EqualTo(PermissionDenied));
    }

    [Test]
    public async Task HostChangesBeatmap()
    {
        IRoom room = await CreateRoomAsync();

        string? reply = await RunAsync(room, UserId, "map", "4444");

        Assert.That(reply, Does.Contain("#4444"));
        Assert.That(room.CurrentItem.BeatmapID, Is.EqualTo(4444));
    }

    [Test]
    public async Task UnknownBeatmap_IsReported()
    {
        IRoom room = await CreateRoomAsync();
        _beatmaps.MissingBeatmapIds.Add(9999);

        Assert.That(await RunAsync(room, UserId, "map", "9999"), Is.EqualTo("Failed to fetch beatmap."));
    }

    [Test]
    public async Task BeatmapRulesetName_IsTakenFromTheRulesetManager()
    {
        IRoom room = await CreateRoomAsync();

        string? reply = await RunAsync(room, UserId, "map", "100", "1");

        // The name comes from the ruleset metadata ("taiko") rather than a local
        // table, so custom rulesets are named by the ruleset itself.
        Assert.That(reply, Does.EndWith(", taiko)."));
        Assert.That(room.CurrentItem.RulesetID, Is.EqualTo(1));
    }

    [Test]
    public async Task UnknownRuleset_IsRejected()
    {
        IRoom room = await CreateRoomAsync();

        Assert.That(await RunAsync(room, UserId, "map", "4444", "7"), Is.EqualTo("Unsupported ruleset: 7."));
    }

    [Test]
    public async Task HostSetsAndClearsMods()
    {
        IRoom room = await CreateRoomAsync();

        Assert.That(await RunAsync(room, UserId, "mods", "hd", "dt"), Is.EqualTo("Changed mods to HD, DT."));
        Assert.That(
            string.Join(",", room.CurrentItem.RequiredMods.Select(mod => mod.Acronym)),
            Is.EqualTo("HD,DT"));

        Assert.That(await RunAsync(room, UserId, "mods"), Is.EqualTo("Removed all required mods."));
        Assert.That(room.CurrentItem.RequiredMods, Is.Empty);
    }

    [Test]
    public async Task HostMovesUserIntoSlot()
    {
        IRoom room = await CreateRoomWithGuestAsync();
        await RunAsync(room, UserId, "size", "4");

        Assert.That(await RunAsync(room, UserId, "move", "Guest", "3"), Is.EqualTo("Moved Guest into slot #3."));

        // Slots are exposed 0-based, while the command accepts 1-based numbers.
        StandardMatchRoomState state = (StandardMatchRoomState)room.MatchState!;
        Assert.That(state.Slots![2], Is.EqualTo(UserId2));

        Assert.That(await RunAsync(room, UserId, "move", "Guest", "3"), Is.EqualTo("Guest is already in slot #3."));

        Assert.That(await RunAsync(room, UserId, "move", "Guest", "0"), Is.EqualTo("Invalid slot number."));
    }

    [Test]
    public async Task TeamCommand_RequiresTeamVersusRoom()
    {
        IRoom room = await CreateRoomWithGuestAsync();

        Assert.That(
            await RunAsync(room, UserId, "team", "Guest", "red"),
            Is.EqualTo("!mp team is not supported in Head to Head mode."));

        await Hub.ChangeSettings(new MultiplayerRoomSettings { MatchType = OsuMatchType.TeamVersus });
        IRoom teamRoom = Hub.GetRoom(RoomId)!;

        Assert.That(await RunAsync(teamRoom, UserId, "team", "Guest", "red"), Is.EqualTo("Moved Guest to team red."));
        Assert.That(
            ((TeamVersusUserState)teamRoom.Users.Single(user => user.UserID == UserId2).MatchState!).TeamID,
            Is.EqualTo(0));

        Assert.That(await RunAsync(teamRoom, UserId, "team", "Guest", "green"), Does.StartWith("Unsupported team colour."));
    }

    [Test]
    public async Task Start_RequiresAReadyUser()
    {
        IRoom room = await CreateRoomAsync();

        Assert.That(await RunAsync(room, UserId, "start"), Is.EqualTo("Can't start match when no users are ready."));
    }

    [Test]
    public async Task Start_StartsTheMatch()
    {
        IRoom room = await CreateRoomAsync();
        await MarkCurrentUserReadyAndAvailable();

        Assert.That(await RunAsync(room, UserId, "start"), Is.EqualTo("Started the match."));
        Assert.That(room.State, Is.EqualTo(MultiplayerRoomState.WaitingForLoad));
    }

    [Test]
    public async Task Timer_StartsAndAbortsReminderCountdown()
    {
        IRoom room = await CreateRoomAsync();

        Assert.That(await RunAsync(room, UserId, "timer", "90"), Is.EqualTo("Countdown ends in 1 minute and 30 seconds."));
        Assert.That(room.ActiveCountdowns.OfType<ReminderCountdown>().Count(), Is.EqualTo(1));

        Assert.That(await RunAsync(room, UserId, "aborttimer"), Is.EqualTo("All timers aborted."));
        Assert.That(room.ActiveCountdowns.OfType<ReminderCountdown>(), Is.Empty);
    }

    [Test]
    public async Task HostBansUser()
    {
        IRoom room = await CreateRoomWithGuestAsync();

        Assert.That(await RunAsync(room, UserId, "ban", "Guest"), Is.EqualTo("Banned Guest from the room."));
        Assert.That(room.Users.Any(user => user.UserID == UserId2), Is.False);
        Assert.That(room.BannedUsers, Does.Contain(UserId2));
    }

    [Test]
    public async Task HostCanResolveUserById()
    {
        IRoom room = await CreateRoomWithGuestAsync();

        Assert.That(await RunAsync(room, UserId, "host", $"#{UserId2}"), Is.EqualTo("Host transferred to Guest."));
        Assert.That(room.Host?.UserID, Is.EqualTo(UserId2));
    }

    [Test]
    public async Task HostClosesRoom()
    {
        IRoom room = await CreateRoomWithGuestAsync();

        Assert.That(await RunAsync(room, UserId, "close"), Is.EqualTo("This room is going to be closed."));
        Assert.That(Hub.GetRoom(RoomId), Is.Null);
    }

    [Test]
    public async Task Make_CreatesTournamentRoom()
    {
        string? reply = await _commands.HandleAsync(new BotCommandContext(UserId, 0, ChannelType.Public, ["make", "Grand", "Finals"]));

        Assert.That(reply, Does.Contain("Created room"));
        Assert.That(reply, Does.Contain("Grand Finals"));

        IRoom created = RoomManager.GetAllRooms().Single(room => room.TournamentMode);
        Assert.That(created.Settings.Name, Is.EqualTo("Grand Finals"));
        Assert.That(created.HostUserId, Is.EqualTo(UserId));
        Assert.That(created.ChannelId, Is.GreaterThan(0));
    }

    /// <summary>
    /// Creates the test room and joins <see cref="MultiplayerTestBase.UserId"/> as its host.
    /// </summary>
    /// <returns>The created room.</returns>
    private async Task<IRoom> CreateRoomAsync()
    {
        await Hub.JoinRoom(RoomId);
        return Hub.GetRoom(RoomId)!;
    }

    /// <summary>
    /// Creates the test room and joins both users, restoring the host's context.
    /// </summary>
    /// <returns>The created room.</returns>
    private async Task<IRoom> CreateRoomWithGuestAsync()
    {
        IRoom room = await CreateRoomAsync();
        SetUserContext(ContextUser2);
        await Hub.JoinRoomExplicit(RoomId);
        SetUserContext(ContextUser);
        return room;
    }

    /// <summary>
    /// Runs a subcommand from the room's multiplayer channel.
    /// </summary>
    /// <param name="room">The target room.</param>
    /// <param name="senderId">The caller's user ID.</param>
    /// <param name="args">The subcommand and its arguments.</param>
    /// <returns>The reply text.</returns>
    private Task<string?> RunAsync(IRoom room, int senderId, params string[] args)
    {
        Assert.That(room.ChannelId, Is.GreaterThan(0), "the room must have a chat channel");
        return _commands.HandleAsync(new BotCommandContext(senderId, room.ChannelId, ChannelType.Multiplayer, args));
    }
}