using System.Globalization;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace DamageTracker;

public static class RunDamageTrackerService
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<ulong, PlayerDamageSnapshot> Totals = new();
    private static readonly string SavePath = ProjectSettings.GlobalizePath("user://damage_tracker_state.json");

    private static string? _currentRunToken;
    private static string? _stableRunId;
    private static int _combatIndex;
    private static bool _combatActive;
    private static ulong? _activePlayerKey;
    private static int _damageCounter;

    public static event Action<OverlayState>? Changed;

    public static void BeginRun(object? runState)
    {
        string nextToken = ReflectionHelpers.ResolveRunToken(runState);
        string stableId = ReflectionHelpers.ResolveStableRunId(runState);

        lock (SyncRoot)
        {
            // Same token in memory — skip
            if (string.Equals(_currentRunToken, nextToken, StringComparison.Ordinal))
                return;

            // Same stable ID (e.g. after save & quit) — keep accumulated data
            if (!string.IsNullOrEmpty(stableId) &&
                string.Equals(_stableRunId, stableId, StringComparison.Ordinal))
            {
                _currentRunToken = nextToken;
                return;
            }

            // Try restoring from disk if stable ID matches
            if (!string.IsNullOrEmpty(stableId) && TryLoadState(stableId))
            {
                _currentRunToken = nextToken;
                _stableRunId = stableId;
                Publish();
                return;
            }

            // Truly new run — reset everything
            Totals.Clear();
            _currentRunToken = nextToken;
            _stableRunId = stableId;
            _combatIndex = 0;
            _combatActive = false;
            _activePlayerKey = null;
            _damageCounter = 0;
        }

        Publish();
    }

    public static void BeginCombat(object? combatState)
    {
        SaveState();

        lock (SyncRoot)
        {
            _combatIndex++;
            _combatActive = combatState != null;
            _activePlayerKey = null;

            foreach (PlayerDamageSnapshot snapshot in Totals.Values)
            {
                snapshot.CombatDamage = 0m;
                snapshot.IsActive = false;
            }
        }

        Publish();
    }

    public static void EndCombat()
    {
        lock (SyncRoot)
        {
            _combatActive = false;
            _activePlayerKey = null;

            foreach (PlayerDamageSnapshot snapshot in Totals.Values)
            {
                snapshot.IsActive = false;
            }
        }

        SaveState();
        Publish();
    }

    public static void NotePlayer(object? player)
    {
        if (!ReflectionHelpers.TryResolvePlayerHandle(player, out PlayerHandle handle))
        {
            return;
        }

        lock (SyncRoot)
        {
            PlayerDamageSnapshot snapshot = GetOrCreate(handle);
            ApplyHandle(snapshot, handle);
            _activePlayerKey = handle.PlayerKey;

            foreach (PlayerDamageSnapshot playerSnapshot in Totals.Values)
            {
                playerSnapshot.IsActive = playerSnapshot.PlayerKey == handle.PlayerKey;
            }
        }

        Publish();
    }

    public static void RecordDamage(object? dealer, object? result, object? target, object? cardSource)
    {
        if (!ReflectionHelpers.TryResolveDamageAmount(result, out decimal damage) || damage <= 0)
        {
            return;
        }

        if (!ReflectionHelpers.TryResolvePlayerHandle(dealer, out PlayerHandle handle))
        {
            if (!ReflectionHelpers.TryResolvePlayerHandle(cardSource, out handle))
            {
                return;
            }
        }

        lock (SyncRoot)
        {
            PlayerDamageSnapshot snapshot = GetOrCreate(handle);
            ApplyHandle(snapshot, handle);
            snapshot.TotalDamage += damage;
            snapshot.CombatDamage += damage;
            snapshot.LastDamage = damage;
            if (damage > snapshot.MaxHitDamage)
                snapshot.MaxHitDamage = damage;
            snapshot.LastUpdatedUtc = DateTime.UtcNow;
            _damageCounter++;
        }

        if (_damageCounter % 10 == 0)
            SaveState();

        Publish();
    }

    /// <summary>
    /// 处理回合开始时的状态伤害（如Poison）
    /// 在敌方回合开始时调用，检测并记录Poison伤害
    /// </summary>
    public static void ProcessStatusDamageOnTurnStart(CombatState combatState)
    {
        try
        {
            if (combatState == null)
            {
                GD.PrintErr("[DamageTracker] ProcessStatusDamageOnTurnStart: combatState is null");
                return;
            }
            
            // 改用 Enemies 属性直接获取敌方生物列表，避免 GetOpponentsOf(null) 的 NRE
            var enemies = combatState.Enemies;
            if (enemies == null || enemies.Count == 0)
            {
                GD.Print("[DamageTracker] ProcessStatusDamageOnTurnStart: no enemies found");
                return;
            }
            
            GD.Print($"[DamageTracker] ProcessStatusDamageOnTurnStart: checking {enemies.Count} enemies for Poison");
            
            foreach (var enemy in enemies)
            {
                if (enemy == null) continue;
                
                var creatureName = enemy.Monster?.Title?.ToString() ?? "Unknown";
                GD.Print($"[DamageTracker]   checking enemy: {creatureName}");
                
                if (enemy.IsDead)
                {
                    GD.Print($"[DamageTracker]   skip dead enemy: {creatureName}");
                    continue;
                }
                
                // 检查Poison和Doom Power
                ReflectionHelpers.IteratePowers(enemy, (powerType, powerAmount, applier) =>
                {
                    GD.Print($"[DamageTracker]     power: {powerType}, amount: {powerAmount}, applier: {(applier != null ? "exists" : "null")}");
                    
                    // Poison 类型伤害
                    if (powerType == "Poison" && powerAmount > 0 && applier != null)
                    {
                        GD.Print($"[DamageTracker]     recording Poison damage: {powerAmount} from {applier}");
                        RecordStatusDamage(applier, powerAmount, enemy, "Poison");
                    }
                    
                    // Doom 类型伤害 - 条件斩杀：只有当怪物HP <= Doom层数时才触发
                    if (powerType == "Doom" && powerAmount > 0 && applier != null)
                    {
                        // 获取敌人当前HP（需要在回调外获取，这里用reflection）
                        var enemyHp = enemy.CurrentHp;
                        if (enemyHp > 0 && enemyHp <= powerAmount)
                        {
                            GD.Print($"[DamageTracker]     Doom kill! enemy HP: {enemyHp}, doom amount: {powerAmount}");
                            RecordStatusDamage(applier, (decimal)enemyHp, enemy, "Doom");
                        }
                        else
                        {
                            GD.Print($"[DamageTracker]     Doom not lethal. enemy HP: {enemyHp}, doom amount: {powerAmount}");
                        }
                    }
                });
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[DamageTracker] ProcessStatusDamageOnTurnStart error: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// 记录灾厄(Doom)斩杀伤害
    /// </summary>
    public static void RecordDoomDamage(CombatState combatState, System.Collections.Generic.IReadOnlyList<Creature>? creatures)
    {
        if (creatures == null) return;
        
        try
        {
            foreach (var creature in creatures)
            {
                if (creature == null) continue;
                
                // 查找这个生物身上的Doom power，获取applier
                ReflectionHelpers.GetPowerApplier(creature, "Doom", out var applier, out var amount);
                if (amount > 0)
                {
                    // Doom斩杀伤害 = doom层数
                    decimal doomDamage = (decimal)amount;
                    RecordStatusDamage(applier, doomDamage, creature, "Doom");
                }
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[DamageTracker] RecordDoomDamage error: {ex.Message}");
        }
    }

    /// <summary>
    /// 记录状态伤害（Poison、Doom等）到指定玩家
    /// </summary>
    private static void RecordStatusDamage(object? applier, decimal damage, object? target, string damageType)
    {
        if (damage <= 0) return;
        
        PlayerHandle? handle = null;
        
        // 尝试从applier获取玩家句柄
        if (applier != null)
        {
            if (ReflectionHelpers.TryResolvePlayerHandle(applier, out var h))
            {
                handle = h;
            }
        }
        
        // 如果没有applier或applier不是玩家，尝试通过cardSource
        if (handle == null)
        {
            // 尝试通过当前活跃玩家
            lock (SyncRoot)
            {
                if (_activePlayerKey.HasValue && Totals.TryGetValue(_activePlayerKey.Value, out var snap))
                {
                    handle = new PlayerHandle(snap.PlayerKey, snap.DisplayName, snap.CharacterName, null);
                }
            }
        }
        
        if (handle == null) return;

        lock (SyncRoot)
        {
            PlayerDamageSnapshot snapshot = GetOrCreate(handle.Value);
            snapshot.TotalDamage += damage;
            snapshot.CombatDamage += damage;
            snapshot.LastDamage = damage;
            if (damage > snapshot.MaxHitDamage)
                snapshot.MaxHitDamage = damage;
            snapshot.LastUpdatedUtc = DateTime.UtcNow;
            _damageCounter++;
        }

        if (_damageCounter % 10 == 0)
            SaveState();

        Publish();
    }

    public static OverlayState BuildOverlayState()
    {
        List<PlayerDamageSnapshot> snapshots;
        string runToken;
        int combatIndex;
        bool combatActive;
        ulong? activePlayerKey;

        lock (SyncRoot)
        {
            snapshots = Totals.Values
                .OrderByDescending(snapshot => snapshot.TotalDamage)
                .ThenBy(snapshot => snapshot.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(snapshot => snapshot.Clone())
                .ToList();
            runToken = _currentRunToken ?? "unknown-run";
            combatIndex = _combatIndex;
            combatActive = _combatActive;
            activePlayerKey = _activePlayerKey;
        }

        return new OverlayState
        {
            RunToken = runToken,
            CombatIndex = combatIndex,
            CombatActive = combatActive,
            ActivePlayerKey = activePlayerKey,
            Players = snapshots
        };
    }

    private static PlayerDamageSnapshot GetOrCreate(PlayerHandle handle)
    {
        if (Totals.TryGetValue(handle.PlayerKey, out PlayerDamageSnapshot? existing))
        {
            return existing;
        }

        PlayerDamageSnapshot created = new()
        {
            PlayerKey = handle.PlayerKey,
            DisplayName = handle.DisplayName
        };
        Totals.Add(handle.PlayerKey, created);
        return created;
    }

    private static void ApplyHandle(PlayerDamageSnapshot snapshot, PlayerHandle handle)
    {
        if (!string.IsNullOrWhiteSpace(handle.DisplayName))
            snapshot.DisplayName = handle.DisplayName;
        if (!string.IsNullOrWhiteSpace(handle.CharacterName))
            snapshot.CharacterName = handle.CharacterName;
        if (handle.PortraitTexture != null)
            snapshot.PortraitTexture = handle.PortraitTexture;
    }

    public static string Format(decimal value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static void Publish()
    {
        Changed?.Invoke(BuildOverlayState());
    }

    // ── Persistence ────────────────────────────────────────────

    private static void SaveState()
    {
        try
        {
            SavedState state;
            lock (SyncRoot)
            {
                if (string.IsNullOrEmpty(_stableRunId)) return;

                state = new SavedState
                {
                    StableRunId = _stableRunId,
                    CombatIndex = _combatIndex,
                    Players = Totals.Values.Select(s => new SavedPlayer
                    {
                        PlayerKey = s.PlayerKey,
                        DisplayName = s.DisplayName,
                        CharacterName = s.CharacterName,
                        TotalDamage = s.TotalDamage,
                        MaxHitDamage = s.MaxHitDamage
                    }).ToList()
                };
            }

            string json = JsonSerializer.Serialize(state, SavedStateCtx.Default.SavedState);
            System.IO.File.WriteAllText(SavePath, json);
        }
        catch
        {
            // Silently ignore save errors
        }
    }

    private static bool TryLoadState(string stableId)
    {
        try
        {
            if (!System.IO.File.Exists(SavePath)) return false;

            string json = System.IO.File.ReadAllText(SavePath);
            SavedState? state = JsonSerializer.Deserialize(json, SavedStateCtx.Default.SavedState);
            if (state == null || !string.Equals(state.StableRunId, stableId, StringComparison.Ordinal))
                return false;

            Totals.Clear();
            _combatIndex = state.CombatIndex;
            _combatActive = false;
            _activePlayerKey = null;

            foreach (SavedPlayer sp in state.Players)
            {
                Totals[sp.PlayerKey] = new PlayerDamageSnapshot
                {
                    PlayerKey = sp.PlayerKey,
                    DisplayName = sp.DisplayName,
                    CharacterName = sp.CharacterName,
                    TotalDamage = sp.TotalDamage,
                    MaxHitDamage = sp.MaxHitDamage
                };
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class OverlayState
{
    public string RunToken { get; init; } = "unknown-run";

    public int CombatIndex { get; init; }

    public bool CombatActive { get; init; }

    public ulong? ActivePlayerKey { get; init; }

    public IReadOnlyList<PlayerDamageSnapshot> Players { get; init; } = Array.Empty<PlayerDamageSnapshot>();
}

public sealed class PlayerDamageSnapshot
{
    public ulong PlayerKey { get; set; }

    public string DisplayName { get; set; } = "Unknown Player";

    public string CharacterName { get; set; } = "Unknown Character";

    public Texture2D? PortraitTexture { get; set; }

    public bool IsActive { get; set; }

    public decimal TotalDamage { get; set; }

    public decimal CombatDamage { get; set; }

    public decimal LastDamage { get; set; }

    public decimal MaxHitDamage { get; set; }

    public DateTime LastUpdatedUtc { get; set; }

    public PlayerDamageSnapshot Clone()
    {
        return new PlayerDamageSnapshot
        {
            PlayerKey = PlayerKey,
            DisplayName = DisplayName,
            CharacterName = CharacterName,
            PortraitTexture = PortraitTexture,
            IsActive = IsActive,
            TotalDamage = TotalDamage,
            CombatDamage = CombatDamage,
            LastDamage = LastDamage,
            MaxHitDamage = MaxHitDamage,
            LastUpdatedUtc = LastUpdatedUtc
        };
    }
}

public readonly record struct PlayerHandle(ulong PlayerKey, string DisplayName, string? CharacterName, Texture2D? PortraitTexture);

// ── Persistence models ─────────────────────────────────────

public sealed class SavedState
{
    public string StableRunId { get; set; } = "";
    public int CombatIndex { get; set; }
    public List<SavedPlayer> Players { get; set; } = new();
}

public sealed class SavedPlayer
{
    public ulong PlayerKey { get; set; }
    public string DisplayName { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public decimal TotalDamage { get; set; }
    public decimal MaxHitDamage { get; set; }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(SavedState))]
internal partial class SavedStateCtx : System.Text.Json.Serialization.JsonSerializerContext { }