using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

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

        DamageTrackerOverlay.EnsureCreated();
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
    public static void BeforeCombatStartPostfix(object[] __args)
    {
        object? runState = __args.Length > 0 ? __args[0] : null;
        object? combatState = __args.Length > 1 ? __args[1] : null;

        RunDamageTrackerService.BeginRun(runState);
        RunDamageTrackerService.BeginCombat(combatState);
        DamageTrackerOverlay.EnsureCreated();
    }

    public static void AfterCombatEndPostfix(object[] __args)
    {
        RunDamageTrackerService.EndCombat();
    }

    public static void AfterPlayerTurnStartPostfix(object[] __args)
    {
        object? player = __args.Length > 2 ? __args[2] : null;
        RunDamageTrackerService.NotePlayer(player);
        DamageTrackerOverlay.EnsureCreated();
    }

    public static void AfterDamageGivenPostfix(object[] __args)
    {
        object? dealer = __args.Length > 2 ? __args[2] : null;

        // Only record damage dealt by player creatures; skip enemy/monster damage
        if (dealer != null && !ReflectionHelpers.IsPlayerCreature(dealer))
            return;

        object? result = __args.Length > 3 ? __args[3] : null;
        object? target = __args.Length > 5 ? __args[5] : null;
        object? cardSource = __args.Length > 6 ? __args[6] : null;

        RunDamageTrackerService.RecordDamage(dealer, result, target, cardSource);
    }
}