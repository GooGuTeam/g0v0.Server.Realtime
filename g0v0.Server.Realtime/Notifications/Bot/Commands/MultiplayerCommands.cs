// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

// The command handlers are grouped by subcommand for readability rather than by
// StyleCop's static-before-instance member ordering.
#pragma warning disable SA1204 // Static members should appear before non-static members

using System.Globalization;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using g0v0.Server.Common.Fetching;
using g0v0.Server.Realtime.Hubs;
using g0v0.Server.Realtime.Manager;
using g0v0.Server.Realtime.Objects.Rooms;
using osu.Game.Online.API;
using osu.Game.Online.Chat;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.Countdown;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using osu.Game.Online.Rooms;
using OsuMatchType = osu.Game.Online.Rooms.MatchType;

namespace g0v0.Server.Realtime.Notifications.Bot.Commands;

/// <summary>
/// Implements the tournament management (<c>!mp</c>) commands documented in the
/// osu! wiki, operating directly on the rooms owned by this server.
/// </summary>
/// <remarks>
/// <para>
/// Reference: <see href="https://osu.ppy.sh/wiki/osu%21_tournament_client/osu%21tourney/Tournament_management_commands"/>.
/// </para>
/// <para>
/// Because the rooms live in this process, the commands mutate them through the
/// same <see cref="IRoom"/> surface the multiplayer hub uses, so every command
/// runs while holding the room's <see cref="IRoom.RoomLock"/> and validates the
/// caller exactly like a hub call would. The replies are posted to the room
/// channel by <c>BanchoBot</c>.
/// </para>
/// <para>
/// Authorisation mirrors the hub: mutating commands require the room host or a
/// referee, while <c>addref</c>/<c>removeref</c> are restricted to the host so a
/// referee cannot escalate privileges. Reply texts follow the upstream
/// implementation (osu-web) as closely as this room model allows.
/// </para>
/// </remarks>
/// <param name="scopeFactory">The service scope factory used to resolve repositories.</param>
/// <param name="roomManager">The room registry.</param>
/// <param name="eventDispatcher">The room event dispatcher.</param>
public sealed class MultiplayerCommands(
    IServiceScopeFactory scopeFactory,
    RoomManager roomManager,
    MultiplayerEventDispatcher eventDispatcher) : BanchoBotCommandSet(scopeFactory)
{
    /// <summary>
    /// The smallest room size accepted by <c>!mp size</c>.
    /// </summary>
    private const int MinimumRoomSize = 2;

    /// <summary>
    /// The largest room size accepted by <c>!mp size</c>.
    /// </summary>
    private const int MaximumRoomSize = 16;

    /// <summary>
    /// The room size used by <c>!mp make</c>.
    /// </summary>
    private const int DefaultRoomSize = 8;

    /// <summary>
    /// The countdown duration used by <c>!mp timer</c> when no argument is given.
    /// </summary>
    private const int DefaultTimerSeconds = 30;

    /// <summary>
    /// The reply sent when the command is used outside a multiplayer channel.
    /// </summary>
    private const string MultiplayerChannelOnly = "The !mp command can only be used in a multiplayer channel.";

    /// <summary>
    /// The reply sent when the channel has no live room attached.
    /// </summary>
    private const string RoomNotFound = "Could not find a room for this channel.";

    /// <summary>
    /// The reply sent when the caller lacks the required privilege.
    /// </summary>
    private const string PermissionDenied = "You don't have the required privilege to perform this action.";

    /// <summary>
    /// The reply sent for an unknown subcommand.
    /// </summary>
    private const string UnknownSubCommand = "Unknown mp subcommand. Send !mp help for usage.";

    /// <summary>
    /// The help text. Kept on a single line because chat messages have their line
    /// breaks normalised away.
    /// </summary>
    private const string HelpText = "Available !mp commands:\n"
                                    + "Room: !mp make <name>, !mp close, !mp settings\n"
                                    + "Settings: !mp name <title>, !mp password [password], !mp size <size>, !mp set <0|2>\n"
                                    + "Slots: !mp lock, !mp unlock, !mp move <username> <slot>, !mp team <username> <red|blue>\n"
                                    + "Players: !mp host <username>, !mp invite <username>, !mp kick <username>, !mp ban <username>\n"
                                    + "Referees: !mp addref <username>, !mp removeref <username>, !mp listrefs\n"
                                    + "Beatmap: !mp map <beatmap_id> [ruleset_id], !mp mods [acronym ...]\n"
                                    + "Match: !mp start [seconds], !mp abort, !mp timer [seconds], !mp aborttimer\n"
                                    + "Commands taking <username> also accept #<user_id>.";

    /// <summary>
    /// Handles <c>!mp &lt;subcommand&gt; [args]</c>.
    /// </summary>
    /// <param name="context">The invocation context.</param>
    /// <returns>The reply text, or <see langword="null"/> when nothing should be sent.</returns>
    [BotCommand("mp")]
    public async Task<string?> HandleAsync(BotCommandContext context)
    {
        IReadOnlyList<string> arguments = context.Arguments;
        if (arguments.Count == 0)
        {
            return "Usage: !mp <subcommand> [args]. Try !mp help.";
        }

        string subcommand = arguments[0].ToLowerInvariant();
        string[] args = arguments.Skip(1).ToArray();

        switch (subcommand)
        {
            // Both require data this server does not model, so they are rejected
            // as upstream does rather than silently ignored.
            case "makeprivate":
            case "clearhost":
                return $"!mp {subcommand} is currently not supported.";

            case "help":
                return HelpText;

            case "make":
                return await MakeRoomAsync(context.SenderId, args);
        }

        if (context.ChannelType != ChannelType.Multiplayer)
        {
            return MultiplayerChannelOnly;
        }

        IRoom? room = FindRoom(context.ChannelId);
        if (room == null)
        {
            return RoomNotFound;
        }

        await room.RoomLock.WaitAsync();
        try
        {
            return !CanExecute(room, subcommand, context.SenderId)
                ? PermissionDenied
                : await ExecuteAsync(room, subcommand, args, context.SenderId);
        }
        catch (NotHostException)
        {
            return PermissionDenied;
        }
        catch (InvalidStateException exception)
        {
            return exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
        finally
        {
            room.RoomLock.Release();
        }
    }

    /// <summary>
    /// Finds the live room attached to a chat channel.
    /// </summary>
    /// <param name="channelId">The chat channel ID.</param>
    /// <returns>The room, or <see langword="null"/> when no room uses the channel.</returns>
    private IRoom? FindRoom(int channelId)
    {
        return channelId <= 0
            ? null
            : roomManager.GetAllRooms().FirstOrDefault(room => room.ChannelId == channelId);
    }

    /// <summary>
    /// Determines whether the caller may run the given subcommand.
    /// </summary>
    /// <param name="room">The room the command targets.</param>
    /// <param name="subcommand">The subcommand name.</param>
    /// <param name="senderId">The caller's user ID.</param>
    /// <returns><see langword="true"/> when the caller is allowed to proceed.</returns>
    private static bool CanExecute(IRoom room, string subcommand, int senderId)
    {
        MultiplayerRoomUser? sender = room.Users.FirstOrDefault(user => user.UserID == senderId);

        // Reading the room state is harmless, so any room member may do it.
        switch (subcommand)
        {
            case "settings":
            case "listrefs":
                return sender != null || room.HostUserId == senderId;
        }

        bool isHost = room.HostUserId == senderId || room.Host?.UserID == senderId;
        bool isReferee = sender?.Role == MultiplayerRoomUserRole.Referee;

        // Referees must not be able to grant or revoke referee status.
        return subcommand is "addref" or "removeref"
            ? isHost
            : isHost || isReferee;
    }

    /// <summary>
    /// Dispatches an authorised subcommand.
    /// </summary>
    /// <param name="room">The target room.</param>
    /// <param name="subcommand">The subcommand name.</param>
    /// <param name="args">The subcommand arguments.</param>
    /// <param name="senderId">The caller's user ID.</param>
    /// <returns>The reply text.</returns>
    private Task<string?> ExecuteAsync(IRoom room, string subcommand, string[] args, int senderId)
    {
        return subcommand switch
        {
            "settings" => DescribeRoomAsync(room),
            "listrefs" => ListRefereesAsync(room),
            "name" => RenameRoomAsync(room, args),
            "password" => SetPasswordAsync(room, args),
            "size" => SetRoomSizeAsync(room, args),
            "set" => SetMatchTypeAsync(room, args),
            "lock" => SetLockStateAsync(room, senderId, locked: true),
            "unlock" => SetLockStateAsync(room, senderId, locked: false),
            "host" => TransferHostAsync(room, args),
            "invite" => InviteAsync(room, args, senderId),
            "kick" => KickAsync(room, args, senderId, ban: false),
            "ban" => KickAsync(room, args, senderId, ban: true),
            "addref" => SetRefereeAsync(room, args, senderId, referee: true),
            "removeref" => SetRefereeAsync(room, args, senderId, referee: false),
            "map" => ChangeBeatmapAsync(room, args, senderId),
            "mods" => ChangeModsAsync(room, args, senderId),
            "move" => MoveUserAsync(room, args),
            "team" => ChangeTeamAsync(room, args),
            "start" => StartMatchAsync(room, args),
            "abort" => AbortMatchAsync(room),
            "timer" => StartTimerAsync(room, args),
            "aborttimer" => AbortTimersAsync(room),
            "close" => CloseRoomAsync(room, senderId),
            _ => Task.FromResult<string?>(UnknownSubCommand),
        };
    }

    /// <summary>
    /// Creates a tournament room owned by the caller.
    /// </summary>
    /// <param name="senderId">The caller's user ID.</param>
    /// <param name="args">The room name.</param>
    /// <returns>The reply text.</returns>
    private async Task<string?> MakeRoomAsync(int senderId, string[] args)
    {
        string name = string.Join(' ', args).Trim();
        if (name.Length == 0)
        {
            return "Usage: !mp make <name>";
        }

        int limit = GetConfig().MaxActiveTournamentRooms;
        int active = roomManager.GetAllRooms().Count(room => room.TournamentMode && room.HostUserId == senderId);
        if (active >= limit)
        {
            return "You've reached the maximum active tournament rooms limit.";
        }

        MultiplayerRoom room = new(0)
        {
            Settings =
            {
                Name = name,
                MatchType = OsuMatchType.HeadToHead,
                MaxParticipants = DefaultRoomSize,
            },
        };

        IRoom created = await roomManager.CreateRoom(room, senderId, tournamentMode: true);
        return $"Created room \"{name}\" (#{created.RoomId}, channel #mp_{created.RoomId}). "
               + "Join it from the multiplayer lobby to add beatmaps and invite players.";
    }

    /// <summary>
    /// Describes the room state, as <c>!mp settings</c> does upstream.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <returns>The reply text.</returns>
    private async Task<string?> DescribeRoomAsync(IRoom room)
    {
        List<MultiplayerRoomUser> users = room.Users.ToList();
        Dictionary<int, string> usernames = await GetUsernamesAsync(users.Select(user => user.UserID).Append(room.HostUserId ?? 0));

        string host = room.Host?.UserID is { } hostId && usernames.TryGetValue(hostId, out string? hostName)
            ? hostName
            : room.HostUserId is { } hostUserId && usernames.TryGetValue(hostUserId, out string? ownerName)
                ? ownerName
                : "None";

        string beatmap = room.Playlist.Count == 0
            ? "None"
            : $"#{room.CurrentItem.BeatmapID} ({GetRulesetName(room.CurrentItem.RulesetID)})";

        string participants = users.Count == 0
            ? "None"
            : string.Join("\n", users.Select(user =>
            {
                string name = usernames.GetValueOrDefault(user.UserID, $"#{user.UserID.ToString(CultureInfo.InvariantCulture)}");
                string role = user.Role == MultiplayerRoomUserRole.Referee
                    ? "referee"
                    : user.UserID == room.Host?.UserID
                        ? "host"
                        : "player";
                return $"{name} ({role}, {user.State})";
            }));

        return $"Room name: {room.Settings.Name} (ID {room.RoomId.ToString(CultureInfo.InvariantCulture)})\n"
               + $"Host: {host} | Team mode: {GetMatchTypeName(room.Settings.MatchType)}\n"
               + $"Size: {room.Settings.MaxParticipants?.ToString(CultureInfo.InvariantCulture) ?? "unlimited"} | "
               + $"Password: {(string.IsNullOrEmpty(room.Settings.Password) ? "none" : room.Settings.Password)}\n"
               + $"Beatmap: {beatmap}\n"
               + $"Players ({users.Count.ToString(CultureInfo.InvariantCulture)}): {participants}";
    }

    /// <summary>
    /// Lists the room referees, as <c>!mp listrefs</c> does upstream.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <returns>The reply text.</returns>
    private async Task<string?> ListRefereesAsync(IRoom room)
    {
        List<int> refereeIds = room.Users
            .Where(user => user.Role == MultiplayerRoomUserRole.Referee)
            .Select(user => user.UserID)
            .ToList();

        if (refereeIds.Count == 0)
        {
            return "No referees in this room.";
        }

        Dictionary<int, string> usernames = await GetUsernamesAsync(refereeIds);
        IEnumerable<string> names = refereeIds
            .Select(userId => usernames.GetValueOrDefault(userId, $"#{userId.ToString(CultureInfo.InvariantCulture)}"))
            .OrderBy(name => name, StringComparer.Ordinal);

        return $"Referees ({refereeIds.Count.ToString(CultureInfo.InvariantCulture)}): {string.Join(", ", names)}";
    }

    /// <summary>
    /// Renames the room.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="args">The new name.</param>
    /// <returns>The reply text.</returns>
    private static async Task<string?> RenameRoomAsync(IRoom room, string[] args)
    {
        string name = string.Join(' ', args).Trim();
        if (name.Length == 0)
        {
            return "Usage: !mp name <title>";
        }

        MultiplayerRoomSettings settings = CloneSettings(room.Settings);
        settings.Name = name;
        await room.ChangeRoomSettings(settings);
        return $"Room name updated to \"{name}\".";
    }

    /// <summary>
    /// Sets or clears the room password.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="args">The new password, or nothing to clear it.</param>
    /// <returns>The reply text.</returns>
    private static async Task<string?> SetPasswordAsync(IRoom room, string[] args)
    {
        string password = args.Length == 0 ? string.Empty : args[0];

        MultiplayerRoomSettings settings = CloneSettings(room.Settings);
        settings.Password = password;
        await room.ChangeRoomSettings(settings);
        return password.Length == 0 ? "Room password removed." : "Room password updated.";
    }

    /// <summary>
    /// Changes the room's participant limit.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="args">The new size.</param>
    /// <returns>The reply text.</returns>
    private static async Task<string?> SetRoomSizeAsync(IRoom room, string[] args)
    {
        if (args.Length != 1 || !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int size))
        {
            return "Usage: !mp size <size>";
        }

        if (size is < MinimumRoomSize or > MaximumRoomSize)
        {
            return $"Invalid room size. It must be between {MinimumRoomSize.ToString(CultureInfo.InvariantCulture)} and {MaximumRoomSize.ToString(CultureInfo.InvariantCulture)}.";
        }

        MultiplayerRoomSettings settings = CloneSettings(room.Settings);
        settings.MaxParticipants = (byte)size;
        await room.ChangeRoomSettings(settings);
        return $"Changed room size to {size.ToString(CultureInfo.InvariantCulture)}.";
    }

    /// <summary>
    /// Changes the room's team mode.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="args">The team mode (<c>0</c> or <c>2</c>).</param>
    /// <returns>The reply text.</returns>
    private static async Task<string?> SetMatchTypeAsync(IRoom room, string[] args)
    {
        if (args.Length != 1)
        {
            return "Only single-argument teammode is supported: !mp set <0|2>";
        }

        OsuMatchType matchType = args[0] switch
        {
            "0" => OsuMatchType.HeadToHead,
            "2" => OsuMatchType.TeamVersus,
            _ => throw new InvalidStateException(
                "Unsupported teammode. Currently supported: 0 (Head to Head), 2 (Team VS)."),
        };

        MultiplayerRoomSettings settings = CloneSettings(room.Settings);
        settings.MatchType = matchType;
        await room.ChangeRoomSettings(settings);
        return $"Team mode updated to {GetMatchTypeName(matchType)}.";
    }

    /// <summary>
    /// Locks or unlocks the room's slots.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="senderId">The caller's user ID.</param>
    /// <param name="locked">Whether the slots should be locked.</param>
    /// <returns>The reply text.</returns>
    private static async Task<string?> SetLockStateAsync(IRoom room, int senderId, bool locked)
    {
        await room.HandleUserRequest(senderId, new SetLockStateRequest { Locked = locked });
        return locked ? "Room is now locked." : "Room is now unlocked.";
    }

    /// <summary>
    /// Transfers the host role to another user.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="args">The target user.</param>
    /// <returns>The reply text.</returns>
    private async Task<string?> TransferHostAsync(IRoom room, string[] args)
    {
        if (args.Length == 0)
        {
            return "Usage: !mp host <username|#<userid>>";
        }

        User? target = await ResolveUserAsync(args);
        if (target == null)
        {
            return "User not found.";
        }

        if (room.Users.All(user => user.UserID != target.Id))
        {
            return $"{target.Username} is not in the room.";
        }

        await room.SetHost(target.Id);
        return $"Host transferred to {target.Username}.";
    }

    /// <summary>
    /// Invites a user to the room.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="args">The target user.</param>
    /// <param name="senderId">The caller's user ID.</param>
    /// <returns>The reply text.</returns>
    private async Task<string?> InviteAsync(IRoom room, string[] args, int senderId)
    {
        if (args.Length == 0)
        {
            return "Usage: !mp invite <username|#<userid>>";
        }

        User? target = await ResolveUserAsync(args);
        if (target == null)
        {
            return "Target user not found.";
        }

        RoomJoinResult eligibility = await room.UserCanJoinWithPassword(target.Id, room.Settings.Password);
        if (!eligibility.Success)
        {
            return eligibility.Reason ?? "User is not eligible to join this room.";
        }

        await eventDispatcher.PostUserInvitedAsync(room.RoomId, target.Id, senderId, room.Settings.Password);
        return $"Invitation sent to {target.Username}.";
    }

    /// <summary>
    /// Kicks (and optionally bans) a user from the room.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="args">The target user.</param>
    /// <param name="senderId">The caller's user ID.</param>
    /// <param name="ban">Whether to also ban the user.</param>
    /// <returns>The reply text.</returns>
    private async Task<string?> KickAsync(IRoom room, string[] args, int senderId, bool ban)
    {
        string verb = ban ? "ban" : "kick";
        if (args.Length == 0)
        {
            return $"Usage: !mp {verb} <username|#<userid>>";
        }

        User? target = await ResolveUserAsync(args);
        if (target == null)
        {
            return "User not found.";
        }

        if (ban && target.Id == senderId)
        {
            return "You cannot ban yourself.";
        }

        // Kick first: banning a referee whose kick is rejected would otherwise
        // leave the ban applied without the user ever leaving.
        await room.KickUser(target.Id, senderId);

        if (ban)
        {
            room.BanUser(target.Id);
        }

        return ban ? $"Banned {target.Username} from the room." : $"Kicked {target.Username} from the room.";
    }

    /// <summary>
    /// Grants or revokes the referee role.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="args">The target user.</param>
    /// <param name="senderId">The caller's user ID.</param>
    /// <param name="referee">Whether the user should become a referee.</param>
    /// <returns>The reply text.</returns>
    private async Task<string?> SetRefereeAsync(IRoom room, string[] args, int senderId, bool referee)
    {
        string verb = referee ? "add" : "remove";
        if (args.Length == 0)
        {
            return $"Usage: !mp {verb}ref <username|#<userid>>";
        }

        User? target = await ResolveUserAsync(args);
        if (target == null)
        {
            return "User not found.";
        }

        if (referee && target.Id == senderId)
        {
            return "You cannot add yourself as a referee.";
        }

        if (room.Users.All(user => user.UserID != target.Id))
        {
            return $"{target.Username} is not in the room.";
        }

        await room.ChangeUserRole(target.Id, referee ? MultiplayerRoomUserRole.Referee : MultiplayerRoomUserRole.Player);
        return referee
            ? $"Added {target.Username} to referees."
            : $"Removed {target.Username} from referees.";
    }

    /// <summary>
    /// Changes the beatmap of the current playlist item, adding one when the
    /// playlist is empty.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="args">The beatmap ID and an optional ruleset ID.</param>
    /// <param name="senderId">The caller's user ID.</param>
    /// <returns>The reply text.</returns>
    private async Task<string?> ChangeBeatmapAsync(IRoom room, string[] args, int senderId)
    {
        if (args.Length < 1 || !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int beatmapId))
        {
            return "Usage: !mp map <beatmap_id> [<ruleset_id>]";
        }

        int? requestedRuleset = null;
        if (args.Length >= 2)
        {
            if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rulesetId))
            {
                return "Usage: !mp map <beatmap_id> [<ruleset_id>]";
            }

            requestedRuleset = rulesetId;
        }

        Beatmap? beatmap = await GetBeatmapAsync(beatmapId);
        if (beatmap == null)
        {
            return "Failed to fetch beatmap.";
        }

        // Without an explicit ruleset the beatmap's own ruleset is used, which is
        // what upstream does (aligned with RefereeHub's map handling).
        int ruleset = requestedRuleset ?? beatmap.Mode;

        MultiplayerPlaylistItem item = room.Playlist.Count > 0 && !room.CurrentItem.Expired
            ? room.CurrentItem.Clone()
            : new MultiplayerPlaylistItem
            {
                OwnerID = senderId,
                WinCondition = WinCondition.Score,
                RequiredMods = [],
                AllowedMods = [],
            };

        item.BeatmapID = beatmap.Id;
        item.BeatmapChecksum = beatmap.Checksum ?? string.Empty;
        item.RulesetID = ruleset;

        if (item.ID == 0)
        {
            await room.AddPlaylistItem(senderId, item);
        }
        else
        {
            await room.EditPlaylistItem(senderId, item);
        }

        string title = beatmap.BeatmapSet == null
            ? $"#{beatmap.Id.ToString(CultureInfo.InvariantCulture)}"
            : $"{beatmap.BeatmapSet.Artist} - {beatmap.BeatmapSet.Title}";
        return $"Beatmap changed to: {title} [{beatmap.Version}] (#{beatmap.Id.ToString(CultureInfo.InvariantCulture)}, {GetRulesetName(ruleset)}).";
    }

    /// <summary>
    /// Replaces the required mods of the current playlist item.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="args">The mod acronyms; none clears them.</param>
    /// <param name="senderId">The caller's user ID.</param>
    /// <returns>The reply text.</returns>
    private static async Task<string?> ChangeModsAsync(IRoom room, string[] args, int senderId)
    {
        if (room.Playlist.Count == 0)
        {
            return "Add a beatmap to the room before setting mods.";
        }

        if (room.CurrentItem.Expired)
        {
            return "Cannot change mods of an already played playlist item.";
        }

        List<APIMod> mods = args
            .Select(acronym => new APIMod { Acronym = acronym.ToUpperInvariant() })
            .ToList();

        MultiplayerPlaylistItem item = room.CurrentItem.Clone();
        item.RequiredMods = mods;
        await room.EditPlaylistItem(senderId, item);

        return mods.Count == 0
            ? "Removed all required mods."
            : $"Changed mods to {string.Join(", ", mods.Select(mod => mod.Acronym))}.";
    }

    /// <summary>
    /// Moves a user into a specific slot.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="args">The target user and the slot number (1-based).</param>
    /// <returns>The reply text.</returns>
    private async Task<string?> MoveUserAsync(IRoom room, string[] args)
    {
        if (args.Length < 2)
        {
            return "Usage: !mp move <username|#<userid>> <slot>";
        }

        string rawSlot = args[^1];
        User? target = await ResolveUserAsync(args[..^1]);
        if (target == null)
        {
            return "User not found.";
        }

        if (!int.TryParse(rawSlot, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot) || slot < 1)
        {
            return "Invalid slot number.";
        }

        // Slots are 0-based in the room state but 1-based for tournament users,
        // matching the wording of the osu! wiki.
        int slotId = slot - 1;

        if (room.MatchState is StandardMatchRoomState { Slots: not null } matchState
            && Array.IndexOf(matchState.Slots, target.Id) == slotId)
        {
            return $"{target.Username} is already in slot #{slot.ToString(CultureInfo.InvariantCulture)}.";
        }

        await room.HandleUserRequest(target.Id, new ChangeSlotRequest { SlotID = (byte)slotId });
        return $"Moved {target.Username} into slot #{slot.ToString(CultureInfo.InvariantCulture)}.";
    }

    /// <summary>
    /// Moves a user into a team.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="args">The target user and the team colour.</param>
    /// <returns>The reply text.</returns>
    private async Task<string?> ChangeTeamAsync(IRoom room, string[] args)
    {
        if (args.Length < 2)
        {
            return "Usage: !mp team <username|#<userid>> <red|blue>";
        }

        if (room.MatchState is not TeamVersusRoomState)
        {
            return "!mp team is not supported in Head to Head mode.";
        }

        string colour = args[^1].ToLowerInvariant();
        User? target = await ResolveUserAsync(args[..^1]);
        if (target == null)
        {
            return "User not found.";
        }

        int teamId = colour switch
        {
            "red" => 0,
            "blue" => 1,
            _ => throw new InvalidStateException("Unsupported team colour. Use 'red' or 'blue'."),
        };

        await room.HandleUserRequest(target.Id, new ChangeTeamRequest { TeamID = teamId });
        return $"Moved {target.Username} to team {colour}.";
    }

    /// <summary>
    /// Starts the match, optionally after a countdown.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="args">The optional delay in seconds.</param>
    /// <returns>The reply text.</returns>
    private static async Task<string?> StartMatchAsync(IRoom room, string[] args)
    {
        int delay = 0;
        if (args.Length > 1
            || (args.Length == 1 && (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out delay) || delay < 0)))
        {
            return "Usage: !mp start [<time_in_seconds>]";
        }

        if (room.Users.All(user => user.State != MultiplayerUserState.Ready))
        {
            return "Can't start match when no users are ready.";
        }

        if (delay == 0)
        {
            await room.StartMatch();
            return "Started the match.";
        }

        await room.StartMatchCountdown(TimeSpan.FromSeconds(delay));
        return $"Match starts in {FormatTime(delay)}.";
    }

    /// <summary>
    /// Aborts the ongoing match.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <returns>The reply text.</returns>
    private static async Task<string?> AbortMatchAsync(IRoom room)
    {
        await room.AbortMatch();
        return "Aborted the match.";
    }

    /// <summary>
    /// Starts a reminder countdown.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="args">The optional duration in seconds.</param>
    /// <returns>The reply text.</returns>
    private static async Task<string?> StartTimerAsync(IRoom room, string[] args)
    {
        int seconds = DefaultTimerSeconds;
        if (args.Length > 1
            || (args.Length == 1 && (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds) || seconds < 1)))
        {
            return "Usage: !mp timer [<time_in_seconds>]";
        }

        await room.StartCountdown(new ReminderCountdown { TimeRemaining = TimeSpan.FromSeconds(seconds) });
        return $"Countdown ends in {FormatTime(seconds)}.";
    }

    /// <summary>
    /// Stops every countdown currently running in the room.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <returns>The reply text.</returns>
    private static async Task<string?> AbortTimersAsync(IRoom room)
    {
        await room.StopAllCountdowns();
        return "All timers aborted.";
    }

    /// <summary>
    /// Closes the room by removing every user from it.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="senderId">The caller's user ID.</param>
    /// <returns>The reply text.</returns>
    private static async Task<string?> CloseRoomAsync(IRoom room, int senderId)
    {
        // Referees cannot be kicked, so demote them first.
        foreach (MultiplayerRoomUser user in room.Users.ToArray().Where(user => user.Role == MultiplayerRoomUserRole.Referee))
        {
            await room.ChangeUserRole(user.UserID, MultiplayerRoomUserRole.Player);
        }

        // Removals are driven one user at a time so each client receives its
        // kicked event before the room disappears.
        foreach (MultiplayerRoomUser user in room.Users.ToArray().Where(user => user.UserID != senderId))
        {
            await room.KickUser(user.UserID, senderId);
        }

        // The caller cannot kick themselves, so they leave last.
        if (room.Users.Any(user => user.UserID == senderId))
        {
            await room.LeaveRoom(senderId);
        }

        // Tournament rooms are only scheduled for destruction when their last
        // user leaves, so close them explicitly.
        if (room.Users.Count > 0 || room.State != MultiplayerRoomState.Closed)
        {
            await room.Disband();
        }

        return "This room is going to be closed.";
    }

    /// <summary>
    /// Resolves a user from a username or <c>#&lt;user_id&gt;</c>, joining
    /// multi-word usernames.
    /// </summary>
    /// <param name="args">The raw arguments naming the user.</param>
    /// <returns>The resolved user, or <see langword="null"/>.</returns>
    private async Task<User?> ResolveUserAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        using IServiceScope scope = ScopeFactory.CreateScope();
        IUserRepository userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        string raw = string.Join(' ', args).Trim();

        if (raw.Length > 1
            && raw[0] == '#'
            && int.TryParse(raw[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int userId))
        {
            return await userRepository.GetByIdAsync(userId);
        }

        // osu! usernames may contain spaces, which the chat command separates, so
        // the underscores used by the client are accepted as well.
        return await userRepository.GetByUsernameAsync(raw)
               ?? await userRepository.GetByUsernameAsync(raw.Replace('_', ' '));
    }

    /// <summary>
    /// Resolves usernames for the given user IDs.
    /// </summary>
    /// <param name="userIds">The user IDs.</param>
    /// <returns>The usernames by user ID.</returns>
    private async Task<Dictionary<int, string>> GetUsernamesAsync(IEnumerable<int> userIds)
    {
        List<int> ids = userIds.Where(static id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        using IServiceScope scope = ScopeFactory.CreateScope();
        IUserRepository userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        IReadOnlyList<User> users = await userRepository.GetByIdsAsync(ids);
        return users.ToDictionary(static user => user.Id, static user => user.Username);
    }

    /// <summary>
    /// Loads a beatmap from the repository, fetching it from the osu! API when
    /// it is not cached locally.
    /// </summary>
    /// <param name="beatmapId">The beatmap ID.</param>
    /// <returns>The beatmap, or <see langword="null"/> when it cannot be loaded.</returns>
    private async Task<Beatmap?> GetBeatmapAsync(int beatmapId)
    {
        using IServiceScope scope = ScopeFactory.CreateScope();
        IBeatmapRepository? beatmapRepository = scope.ServiceProvider.GetService<IBeatmapRepository>();
        if (beatmapRepository == null)
        {
            return null;
        }

        IFetcher? fetcher = scope.ServiceProvider.GetService<IFetcher>();

        try
        {
            return fetcher == null
                ? await beatmapRepository.GetByIdAsync(beatmapId)
                : await beatmapRepository.GetOrFetchByIdAsync(beatmapId, fetcher);
        }
        catch (Exception)
        {
            // A missing beatmap surfaces as an exception from the fetching path.
            return null;
        }
    }

    /// <summary>
    /// Copies the room settings so a single field can be changed without
    /// mutating the room's current instance.
    /// </summary>
    /// <param name="settings">The settings to copy.</param>
    /// <returns>The copied settings.</returns>
    private static MultiplayerRoomSettings CloneSettings(MultiplayerRoomSettings settings)
    {
        return new MultiplayerRoomSettings
        {
            Name = settings.Name,
            Password = settings.Password,
            MatchType = settings.MatchType,
            QueueMode = settings.QueueMode,
            AutoSkip = settings.AutoSkip,
            AutoStartDuration = settings.AutoStartDuration,
            MaxParticipants = settings.MaxParticipants,
            PlaylistItemId = settings.PlaylistItemId,
        };
    }

    /// <summary>
    /// Loads the realtime configuration.
    /// </summary>
    /// <returns>The configuration, or the defaults when it is not registered.</returns>
    private RealtimeConfig GetConfig()
    {
        using IServiceScope scope = ScopeFactory.CreateScope();
        return scope.ServiceProvider.GetService<ConfigManager>()?.Get<RealtimeConfig>() ?? new RealtimeConfig();
    }

    /// <summary>
    /// Formats a duration in seconds as a human-readable string.
    /// </summary>
    /// <param name="seconds">The duration in seconds.</param>
    /// <returns>The formatted duration.</returns>
    private static string FormatTime(int seconds)
    {
        if (seconds <= 0)
        {
            return "0 seconds";
        }

        int minutes = seconds / 60;
        int remainingSeconds = seconds % 60;

        List<string> parts = [];
        if (minutes > 0)
        {
            parts.Add(minutes == 1 ? "1 minute" : $"{minutes.ToString(CultureInfo.InvariantCulture)} minutes");
        }

        if (remainingSeconds > 0)
        {
            parts.Add(remainingSeconds == 1 ? "1 second" : $"{remainingSeconds.ToString(CultureInfo.InvariantCulture)} seconds");
        }

        return string.Join(" and ", parts);
    }

    /// <summary>
    /// Gets the display name of a match type.
    /// </summary>
    /// <param name="matchType">The match type.</param>
    /// <returns>The display name.</returns>
    private static string GetMatchTypeName(OsuMatchType matchType)
    {
        return matchType switch
        {
            OsuMatchType.HeadToHead => "Head to Head",
            OsuMatchType.TeamVersus => "Team VS",
            _ => matchType.ToString(),
        };
    }

    /// <summary>
    /// Gets the display name of a ruleset.
    /// </summary>
    /// <param name="rulesetId">The ruleset ID.</param>
    /// <returns>The display name.</returns>
    private static string GetRulesetName(int rulesetId)
    {
        return rulesetId switch
        {
            0 => "osu!",
            1 => "osu!taiko",
            2 => "osu!catch",
            3 => "osu!mania",
            _ => $"ruleset {rulesetId.ToString(CultureInfo.InvariantCulture)}",
        };
    }
}