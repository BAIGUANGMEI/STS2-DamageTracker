using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace DamageTracker;

[ModInitializer("Initialize")]
public static class ModEntry
{
    private static Harmony? _harmony;

    public static void Initialize()
    {
        if (_harmony != null)
        {
            return;
        }

        _harmony = new Harmony("com.example.sts2.damage_tracker");
        PatchHook(nameof(Hook.BeforeCombatStart), nameof(HookPatches.BeforeCombatStartPostfix));
        PatchHook(nameof(Hook.AfterCombatEnd), nameof(HookPatches.AfterCombatEndPostfix));
        PatchHook(nameof(Hook.AfterPlayerTurnStart), nameof(HookPatches.AfterPlayerTurnStartPostfix));
        PatchHook(nameof(Hook.AfterDamageGiven), nameof(HookPatches.AfterDamageGivenPostfix));
        PatchHook(nameof(Hook.AfterSideTurnStart), nameof(HookPatches.AfterSideTurnStartPostfix));
        PatchHook(nameof(Hook.AfterDiedToDoom), nameof(HookPatches.AfterDiedToDoomPostfix));

        // 手动加载PCK文件到res://
        string modDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        string pckPath = System.IO.Path.Combine(modDir, "DamageTracker.pck");
        GD.Print($"[DamageTracker] PCK path: {pckPath}, exists: {System.IO.File.Exists(pckPath)}");
        if (System.IO.File.Exists(pckPath))
        {
            bool success = ProjectSettings.LoadResourcePack(pckPath);
            GD.Print($"[DamageTracker] LoadResourcePack result: {success}");
        }
        else
        {
            GD.PrintErr($"[DamageTracker] PCK file not found: {pckPath}");
        }

        // Write to log file for debugging
        string logDir = OS.GetUserDataDir();
        string logPath = Path.Combine(logDir, "..", "DamageTracker_debug.log");
        using (var log = Godot.FileAccess.Open(logPath, Godot.FileAccess.ModeFlags.WriteRead))
        {
            if (log != null)
            {
                log.StoreLine($"[{DateTime.Now:HH:mm:ss}] DamageTracker Init - hooks: {_harmony?.GetPatchedMethods().Count() ?? 0}");
            }
        }
        GD.Print("[DamageTracker] Log path: " + logPath);
        Log.Info("DamageTracker initialized");
    }

    private static void PatchHook(string hookName, string postfixName)
    {
        MethodInfo original = AccessTools.Method(typeof(Hook), hookName)
            ?? throw new MissingMethodException(typeof(Hook).FullName, hookName);
        MethodInfo postfix = AccessTools.Method(typeof(HookPatches), postfixName)
            ?? throw new MissingMethodException(typeof(HookPatches).FullName, postfixName);

        _harmony!.Patch(original, postfix: new HarmonyMethod(postfix));
    }
}

internal static class HookPatches
{
    private static bool _overlayScheduled;
    
    // Hook signature:
    // BeforeCombatStart(IRunState runState, CombatState? combatState)
    public static void BeforeCombatStartPostfix(IRunState? runState, CombatState? combatState)
    {
        RunDamageTrackerService.BeginRun(runState);
        RunDamageTrackerService.BeginCombat(combatState);
        
        // Create overlay when first combat starts (game loop is guaranteed to be ready by now)
        if (!_overlayScheduled)
        {
            _overlayScheduled = true;
            DamageTrackerOverlay.EnsureCreated();
        }
    }

    // Hook signature:
    // AfterCombatEnd(IRunState runState, CombatState? combatState, CombatRoom room)
    public static void AfterCombatEndPostfix(IRunState? runState, CombatState? combatState)
    {
        RunDamageTrackerService.EndCombat();
    }

    // Hook signature:
    // AfterPlayerTurnStart(CombatState combatState, PlayerChoiceContext choiceContext, Player player)
    public static void AfterPlayerTurnStartPostfix(CombatState combatState, PlayerChoiceContext? choiceContext, Player player)
    {
        RunDamageTrackerService.NotePlayer(player);
    }

    // Hook signature:
    // AfterDamageGiven(PlayerChoiceContext choiceContext, CombatState combatState, 
    //                   Creature? dealer, DamageResult results, ValueProp props, 
    //                   Creature target, CardModel? cardSource)
    public static void AfterDamageGivenPostfix(
        PlayerChoiceContext? choiceContext,
        CombatState? combatState,
        Creature? dealer,
        DamageResult? results,
        ValueProp props,
        Creature? target,
        CardModel? cardSource)
    {
        // Only record damage dealt by player creatures; skip enemy/monster damage
        if (dealer != null && !ReflectionHelpers.IsPlayerCreature(dealer))
            return;

        RunDamageTrackerService.RecordDamage(dealer, results, target, cardSource);
    }

    // Hook signature:
    // AfterSideTurnStart(CombatState combatState, CombatSide side)
    public static void AfterSideTurnStartPostfix(CombatState combatState, CombatSide side)
    {
        // 敌方回合开始时，检测Poison等状态伤害
        if (side == CombatSide.Enemy)
        {
            RunDamageTrackerService.ProcessStatusDamageOnTurnStart(combatState);
        }
    }

    // Hook signature:
    // AfterDiedToDoom(CombatState combatState, IReadOnlyList<Creature> creatures)
    public static void AfterDiedToDoomPostfix(CombatState combatState, System.Collections.Generic.IReadOnlyList<Creature>? creatures)
    {
        // 灾厄斩杀伤害归属
        if (creatures != null)
        {
            RunDamageTrackerService.RecordDoomDamage(combatState, creatures);
        }
    }
}
