using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace DamageTracker;

internal static class ReflectionHelpers
{
    private static readonly string[] PlayerLinks =
    {
        "Player",
        "OwnerPlayer",
        "Owner",
        "Controller",
        "Summoner",
        "SourcePlayer",
        "Creature",
        "Dealer"
    };

    private static readonly string[] IdMembers =
    {
        "NetId",
        "PlayerId",
        "LocalPlayerId",
        "Id"
    };

    private static readonly string[] NameMembers =
    {
        "DisplayName",
        "CharacterName",
        "Name",
        "LocalizedName"
    };

    private static readonly string[] CharacterLinks =
    {
        "Character",
        "CharacterModel",
        "SelectedCharacter"
    };

    private static readonly string[] CharacterIdMembers =
    {
        "Entry",
        "Id",
        "CharacterId"
    };

    private static readonly string[] CharacterTitleMembers =
    {
        "Title",
        "Name",
        "LocalizedName",
        "CharacterName"
    };

    private static readonly string[] PortraitMembers =
    {
        "CharacterSelectIcon",
        "IconTexture",
        "IconOutlineTexture",
        "Portrait",
        "Avatar"
    };

    private static readonly string[] DamageMembers =
    {
        "DamageDealt",
        "FinalDamage",
        "ActualDamage",
        "UnblockedDamage",
        "Amount",
        "Damage"
    };

    public static string ResolveRunToken(object? runState)
    {
        if (runState is IRunState typedRunState)
        {
            return !string.IsNullOrWhiteSpace(typedRunState.Rng.StringSeed)
                ? typedRunState.Rng.StringSeed
                : $"run-{RuntimeHelpers.GetHashCode(typedRunState)}";
        }

        if (runState == null)
        {
            return "unknown-run";
        }

        // Try direct members first
        foreach (string memberName in new[] { "Seed", "RunId", "Id" })
        {
            object? value = TryGetMemberValue(runState, memberName);
            if (value != null)
            {
                string text = value.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text) && !LooksLikeTypeName(text))
                    return text;
            }
        }

        // Navigate Rng.StringSeed / Rng.Seed (IRunState.Rng is RunRngSet)
        string? seedFromRng = TryGetSeedFromRng(runState);
        if (!string.IsNullOrEmpty(seedFromRng))
            return seedFromRng;

        return $"run-{RuntimeHelpers.GetHashCode(runState)}";
    }

    /// <summary>
    /// Resolves a stable run identifier that survives save &amp; quit.
    /// Navigates runState.Rng.StringSeed which is constant for a given run.
    /// </summary>
    public static string ResolveStableRunId(object? runState)
    {
        if (runState is IRunState typedRunState)
        {
            return typedRunState.Rng.StringSeed ?? string.Empty;
        }

        if (runState == null) return string.Empty;

        // Try direct members
        foreach (string memberName in new[] { "Seed", "RunId" })
        {
            object? value = TryGetMemberValue(runState, memberName);
            if (value != null)
            {
                string text = value.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text) && !LooksLikeTypeName(text))
                    return text;
            }
        }

        // Navigate Rng.StringSeed / Rng.Seed
        return TryGetSeedFromRng(runState) ?? string.Empty;
    }

    /// <summary>
    /// Returns true if the creature is a player character (not a monster/enemy).
    /// </summary>
    public static bool IsPlayerCreature(object? creature)
    {
        if (creature is Creature typedCreature)
        {
            return typedCreature.IsPlayer || typedCreature.Side == CombatSide.Player;
        }

        if (creature == null) return false;

        object? isPlayer = TryGetMemberValue(creature, "IsPlayer");
        if (isPlayer is bool b) return b;

        object? side = TryGetMemberValue(creature, "Side");
        if (side != null) return string.Equals(side.ToString(), "Player", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private static string? TryGetSeedFromRng(object runState)
    {
        object? rng = TryGetMemberValue(runState, "Rng");
        if (rng == null) return null;

        foreach (string seedMember in new[] { "StringSeed", "Seed" })
        {
            object? seedVal = TryGetMemberValue(rng, seedMember);
            if (seedVal != null)
            {
                string text = seedVal.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text) && !LooksLikeTypeName(text))
                    return text;
            }
        }

        return null;
    }

    public static bool TryResolvePlayerHandle(object? source, out PlayerHandle handle)
    {
        if (TryResolveTypedPlayer(source, out Player? typedPlayer) && typedPlayer != null)
        {
            ulong typedPlayerKey = typedPlayer.NetId;
            string typedDisplayName = TryGetPlatformDisplayName(typedPlayerKey)
                ?? typedPlayer.Creature.Name
                ?? $"Player {typedPlayerKey}";
            string typedCharacterName = typedPlayer.Character.Id.Entry;
            Texture2D? typedPortraitTexture = typedPlayer.Character.IconTexture;

            handle = new PlayerHandle(typedPlayerKey, typedDisplayName, typedCharacterName, typedPortraitTexture);
            return true;
        }

        if (source == null)
        {
            handle = default;
            return false;
        }

        object? playerCandidate = FindPlayerCandidate(source, 0);
        if (playerCandidate == null)
        {
            handle = default;
            return false;
        }

        ulong playerKey = TryGetUnsignedId(playerCandidate) ?? (ulong)RuntimeHelpers.GetHashCode(playerCandidate);
        string displayName = TryGetPlatformDisplayName(playerKey)
            ?? TryGetDisplayName(playerCandidate)
            ?? $"Player {playerKey}";
        string? characterName = TryGetCharacterName(playerCandidate);
        Texture2D? portraitTexture = TryGetCharacterPortrait(playerCandidate);

        handle = new PlayerHandle(playerKey, displayName, characterName, portraitTexture);
        return true;
    }

    public static bool TryResolveDamageAmount(object? value, out decimal amount)
    {
        if (value is DamageResult typedDamageResult)
        {
            amount = typedDamageResult.UnblockedDamage;
            return amount > 0;
        }

        if (TryConvertToDecimal(value, out amount))
        {
            return true;
        }

        if (value != null)
        {
            foreach (string memberName in DamageMembers)
            {
                if (TryConvertToDecimal(TryGetMemberValue(value, memberName), out amount))
                {
                    return true;
                }
            }
        }

        amount = 0m;
        return false;
    }

    private static object? FindPlayerCandidate(object source, int depth)
    {
        if (depth > 3)
        {
            return null;
        }

        Type type = source.GetType();
        if (type.Name.Contains("Player", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        foreach (string link in PlayerLinks)
        {
            object? next = TryGetMemberValue(source, link);
            if (next == null || ReferenceEquals(next, source))
            {
                continue;
            }

            object? resolved = FindPlayerCandidate(next, depth + 1);
            if (resolved != null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static bool TryResolveTypedPlayer(object? source, out Player? player)
    {
        switch (source)
        {
            case Player typedPlayer:
                player = typedPlayer;
                return true;
            case Creature creature when creature.Player != null:
                player = creature.Player;
                return true;
            case Creature creature when creature.PetOwner != null:
                player = creature.PetOwner;
                return true;
            case CardModel card when card.Owner != null:
                player = card.Owner;
                return true;
            default:
                player = null;
                return false;
        }
    }

    private static ulong? TryGetUnsignedId(object source)
    {
        foreach (string memberName in IdMembers)
        {
            object? value = TryGetMemberValue(source, memberName);
            if (value == null)
            {
                continue;
            }

            if (value is ulong unsignedLong)
            {
                return unsignedLong;
            }

            if (value is uint unsignedInt)
            {
                return unsignedInt;
            }

            if (value is long signedLong && signedLong >= 0)
            {
                return (ulong)signedLong;
            }

            if (value is int signedInt && signedInt >= 0)
            {
                return (ulong)signedInt;
            }

            if (ulong.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? TryGetDisplayName(object source)
    {
        foreach (string memberName in NameMembers)
        {
            object? value = TryGetMemberValue(source, memberName);
            string? text = value?.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static string? TryGetCharacterName(object source)
    {
        object? character = TryGetCharacter(source);
        if (character == null)
        {
            return null;
        }

        object? idObject = TryGetMemberValue(character, "Id");
        if (idObject != null)
        {
            foreach (string memberName in CharacterIdMembers)
            {
                string? idText = TryGetMemberValue(idObject, memberName)?.ToString();
                if (!string.IsNullOrWhiteSpace(idText) && !LooksLikeTypeName(idText))
                {
                    return idText;
                }
            }
        }

        foreach (string memberName in CharacterTitleMembers)
        {
            string? titleText = TryGetMemberValue(character, memberName)?.ToString();
            if (!string.IsNullOrWhiteSpace(titleText) && !LooksLikeTypeName(titleText))
            {
                return titleText;
            }
        }

        return null;
    }

    private static Texture2D? TryGetCharacterPortrait(object source)
    {
        object? character = TryGetCharacter(source);
        if (character == null)
        {
            return null;
        }

        foreach (string memberName in PortraitMembers)
        {
            if (TryGetMemberValue(character, memberName) is Texture2D texture)
            {
                return texture;
            }
        }

        return null;
    }

    private static object? TryGetCharacter(object source)
    {
        foreach (string memberName in CharacterLinks)
        {
            object? character = TryGetMemberValue(source, memberName);
            if (character != null)
            {
                return character;
            }
        }

        return null;
    }

    private static string? TryGetPlatformDisplayName(ulong playerKey)
    {
        try
        {
            PlatformType platformType = PlatformUtil.PrimaryPlatform;
            if (platformType == PlatformType.None)
            {
                return null;
            }

            string? personaName = PlatformUtil.GetPlayerName(platformType, playerKey);

            if (string.IsNullOrWhiteSpace(personaName)
                || string.Equals(personaName, "[unknown]", StringComparison.OrdinalIgnoreCase)
                || string.Equals(personaName, playerKey.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                return null;
            }

            return personaName;
        }
        catch
        {
            return null;
        }
    }

    private static object? TryGetMemberValue(object source, string memberName)
    {
        Type type = source.GetType();
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        PropertyInfo? property = type.GetProperty(memberName, flags);
        if (property != null)
        {
            try
            {
                return property.GetValue(source);
            }
            catch
            {
                return null;
            }
        }

        FieldInfo? field = type.GetField(memberName, flags);
        if (field != null)
        {
            try
            {
                return field.GetValue(source);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static bool LooksLikeTypeName(string value)
    {
        return value.Contains('.', StringComparison.Ordinal) || value.Contains('+', StringComparison.Ordinal);
    }

    private static bool TryConvertToDecimal(object? value, out decimal amount)
    {
        switch (value)
        {
            case decimal decimalValue:
                amount = decimalValue;
                return true;
            case double doubleValue:
                amount = (decimal)doubleValue;
                return true;
            case float floatValue:
                amount = (decimal)floatValue;
                return true;
            case long longValue:
                amount = longValue;
                return true;
            case int intValue:
                amount = intValue;
                return true;
            case short shortValue:
                amount = shortValue;
                return true;
            case byte byteValue:
                amount = byteValue;
                return true;
            default:
                if (value != null && decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed))
                {
                    amount = parsed;
                    return true;
                }

                amount = 0m;
                return false;
        }
    }
}