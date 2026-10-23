// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using g0v0.Server.Common.Rulesets;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Utils;

namespace g0v0.Server.Realtime.Extensions;

/// <summary>
/// Provides mod validation for multiplayer playlist items and user mod selections.
/// </summary>
internal static class MultiplayerModExtensions
{
    /// <summary>
    /// Ensures that a <see cref="MultiplayerPlaylistItem"/>'s required and allowed
    /// mods are compatible with each other and the item's ruleset.
    /// </summary>
    /// <param name="item">The playlist item to validate.</param>
    /// <param name="rulesetManager">The ruleset manager used to resolve the item's ruleset.</param>
    /// <exception cref="InvalidStateException">If the mods are invalid.</exception>
    public static void EnsureModsValid(this MultiplayerPlaylistItem item, RulesetManager rulesetManager)
    {
        Ruleset ruleset = GetRuleset(rulesetManager, item.RulesetID);

        // Check against the ruleset.
        if (!ModUtils.InstantiateValidModsForRuleset(ruleset, item.RequiredMods, out List<Mod>? requiredMods))
        {
            string invalidRequiredAcronyms = string.Join(
                ',',
                item.RequiredMods
                    .Where(m => requiredMods!.TrueForAll(valid => !string.Equals(valid.Acronym, m.Acronym, StringComparison.OrdinalIgnoreCase)))
                    .Select(m => m.Acronym));
            throw new InvalidStateException($"Invalid mods were selected for specified ruleset: {invalidRequiredAcronyms}");
        }

        if (!ModUtils.InstantiateValidModsForRuleset(ruleset, item.AllowedMods, out List<Mod>? allowedMods))
        {
            string invalidAllowedAcronyms = string.Join(
                ',',
                item.AllowedMods
                    .Where(m => allowedMods!.TrueForAll(valid => !string.Equals(valid.Acronym, m.Acronym, StringComparison.OrdinalIgnoreCase)))
                    .Select(m => m.Acronym));
            throw new InvalidStateException($"Invalid mods were selected for specified ruleset: {invalidAllowedAcronyms}");
        }

        if (!ModUtils.CheckCompatibleSet(requiredMods!, out List<Mod>? invalid))
        {
            throw new InvalidStateException($"Invalid combination of required mods: {string.Join(',', invalid!.Select(m => m.Acronym))}");
        }

        if (!ModUtils.CheckValidRequiredModsForMultiplayer(requiredMods!, item.Freestyle, out invalid))
        {
            throw new InvalidStateException($"Invalid required mods were selected: {string.Join(',', invalid!.Select(m => m.Acronym))}");
        }

        if (!ModUtils.CheckValidAllowedModsForMultiplayer(allowedMods!, item.Freestyle, out invalid))
        {
            throw new InvalidStateException($"Invalid free mods were selected: {string.Join(',', invalid!.Select(m => m.Acronym))}");
        }

        // Check aggregate combinations with each allowed mod individually.
        if (allowedMods!.Exists(allowedMod => !ModUtils.CheckCompatibleSet(requiredMods!.Concat([allowedMod]), out invalid)))
        {
            throw new InvalidStateException($"Invalid combination of required and allowed mods: {string.Join(',', invalid!.Select(m => m.Acronym))}");
        }
    }

    /// <summary>
    /// Checks whether the given mods are compatible with the current playlist
    /// item's mods and ruleset.
    /// </summary>
    /// <param name="item">The <see cref="MultiplayerPlaylistItem"/> to validate the user mods against.</param>
    /// <param name="user">The <see cref="MultiplayerRoomUser"/> whose mods to validate.</param>
    /// <param name="proposedMods">The proposed user mods to check against the <see cref="MultiplayerPlaylistItem"/>.</param>
    /// <param name="rulesetManager">The ruleset manager used to resolve the ruleset.</param>
    /// <param name="validMods">The set of mods which <i>are</i> valid.</param>
    /// <returns>Whether all user mods are valid for the <see cref="MultiplayerPlaylistItem"/>.</returns>
    public static bool ValidateUserMods(
        this MultiplayerPlaylistItem item,
        MultiplayerRoomUser user,
        IEnumerable<APIMod> proposedMods,
        RulesetManager rulesetManager,
        [NotNullWhen(false)] out IEnumerable<APIMod>? validMods)
    {
        Ruleset ruleset = GetRuleset(rulesetManager, user.RulesetId ?? item.RulesetID);

        bool proposedWereValid = true;
        proposedWereValid &= ModUtils.InstantiateValidModsForRuleset(ruleset, proposedMods, out List<Mod>? valid);

        // Freestyle unconditionally allows all freemods.
        if (!item.Freestyle)
        {
            // Check allowed by the room.
            foreach (Mod mod in valid!.ToList().Where(mod => item.AllowedMods.All(m => !string.Equals(m.Acronym, mod.Acronym, StringComparison.OrdinalIgnoreCase))))
            {
                valid.Remove(mod);
                proposedWereValid = false;
            }
        }

        // Check valid as a combination.
        if (!ModUtils.CheckCompatibleSet(item.RequiredMods.Select(m => m.ToMod(ruleset)).Concat(valid!), out List<Mod>? invalid))
        {
            proposedWereValid = false;
            foreach (Mod mod in invalid!)
            {
                valid!.Remove(mod);
            }
        }

        validMods = valid!.Select(m => new APIMod(m));

        return proposedWereValid;
    }

    private static Ruleset GetRuleset(RulesetManager rulesetManager, int rulesetId)
    {
        try
        {
            return rulesetManager.GetRuleset(rulesetId);
        }
        catch (ArgumentException)
        {
            throw new InvalidStateException($"Attempted to select an unsupported ruleset ({rulesetId}).");
        }
    }
}