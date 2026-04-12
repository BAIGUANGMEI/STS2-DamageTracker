using System.Text.Json;
using Godot;

namespace DamageTracker;

/// <summary>
/// Flat, table-style damage overlay. Simple colors, clear columns, no overlap.
/// </summary>
public sealed partial class DamageTrackerOverlay : CanvasLayer
{
    private const float ExpandedWidth = 440f;
    private const float CompactWidth = 248f;
    private const float SideTabWidth = 86f;
    private const float SideTabHeight = 32f;
    private const float ViewportMargin = 16f;
    private const float MinPanelHeight = 72f;

    // ── Localization ───────────────────────────────────────────

    private static Dictionary<string, string>? _locStrings;

    private static Dictionary<string, string> LocStrings => _locStrings ??= LoadLocStrings();

    private static Dictionary<string, string> LoadLocStrings()
    {
        string lang = ResolveGameLanguage();
        
        // Debug: list files in localization folder
        string[] files = Godot.DirAccess.GetFilesAt($"res://assets/localization/{lang}");
        GD.Print($"[DamageTracker] {lang} folder files: {string.Join(", ", files)}");
        
        // Use res:// path - requires .pck file to be loaded
        string resPath = $"res://assets/localization/{lang}/damage_tracker.json";
        GD.Print($"[DamageTracker] LoadLocStrings: trying res:// path = {resPath}");
        
        if (!Godot.FileAccess.FileExists(resPath))
        {
            GD.Print($"[DamageTracker] LoadLocStrings: res:// path not found, trying eng fallback");
            if (lang != "eng")
                resPath = $"res://assets/localization/eng/damage_tracker.json";
            else
            {
                GD.Print("[DamageTracker] LoadLocStrings: localization file not found!");
                return new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            }
        }
        
        using Godot.FileAccess file = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read);
        string json = file?.GetAsText() ?? "{}";
        GD.Print($"[DamageTracker] LoadLocStrings: loaded json from {resPath}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveGameLanguage()
    {
        try
        {
            System.Type? locMgr = System.Type.GetType(
                "MegaCrit.Sts2.Core.Localization.LocManager, sts2", throwOnError: false);
            object? inst = locMgr?.GetProperty("Instance",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
            string? lang = inst?.GetType().GetProperty("Language")?.GetValue(inst) as string;
            if (!string.IsNullOrEmpty(lang))
            {
                GD.Print($"[DamageTracker] ResolveGameLanguage: LocManager.Language = {lang}");
                return lang;
            }
        }
        catch (System.Exception ex) { GD.Print($"[DamageTracker] ResolveGameLanguage: LocManager error {ex.Message}"); }

        // Fallback: map OS locale to STS2 three-letter code
        string locale = OS.GetLocaleLanguage().ToLowerInvariant();
        GD.Print($"[DamageTracker] ResolveGameLanguage: OS locale = {locale}");
        if (locale.StartsWith("zh")) return "zhs";
        if (locale.StartsWith("ja")) return "jpn";
        if (locale.StartsWith("ko")) return "kor";
        if (locale.StartsWith("de")) return "deu";
        if (locale.StartsWith("fr")) return "fra";
        if (locale.StartsWith("it")) return "ita";
        if (locale.StartsWith("pt")) return "ptb";
        if (locale.StartsWith("ru")) return "rus";
        if (locale.StartsWith("pl")) return "pol";
        if (locale.StartsWith("th")) return "tha";
        if (locale.StartsWith("tr")) return "tur";
        if (locale.StartsWith("es")) return "esp";
        return "eng";
    }

    private static string L(string key) => LocStrings.TryGetValue(key, out string? v) ? v : key;

    // ── Palette (keep it minimal) ──────────────────────────────
    private static readonly Color White = new("FFFFFF");
    private static readonly Color Gray = new("A0A8B4");
    private static readonly Color DimGray = new("687480");
    private static readonly Color Green = new("4ADE80");
    private static readonly Color Red = new("F87171");
    private static readonly Color Yellow = new("FACC15");
    private static readonly Color Cyan = new("22D3EE");
    private static readonly Color BgDark = new("000000B0");
    private static readonly Color BgRow = new("FFFFFF10");
    private static readonly Color BgActiveRow = new("4ADE8018");
    private static readonly Color Border = new("3A3A5C");
    private static readonly Color BorderActive = new("4ADE80");

    // Character theme colors based on their visual identity
    private static readonly Dictionary<string, Color> CharTheme = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["ironclad"]    = new Color("E05050"),  // red
        ["silent"]      = new Color("5DB85D"),  // green
        ["defect"]      = new Color("4AA8D8"),  // blue
        ["necrobinder"] = new Color("B060D0"),  // purple
        ["regent"]      = new Color("D8A030"),  // gold/orange
    };

    private static readonly Dictionary<string, Texture2D?> IconCache = new(System.StringComparer.OrdinalIgnoreCase);

    private static DamageTrackerOverlay? _instance;

    private Control? _root;
    private MarginContainer? _contentPad;
    private ScrollContainer? _bodyScroll;
    private VBoxContainer? _bodyContent;
    private VBoxContainer? _rows;
    private Label? _emptyLabel;
    private Control? _columnHeadings;
    private Control? _separator;
    private Button? _toggleBtn;
    private Button? _hideBtn;
    private Button? _sideTab;
    private bool _isDragging;
    private Vector2 _dragOffset;
    private bool _expanded = true;
    private bool _hiddenToSide;
    private HiddenDockSide _hiddenSide = HiddenDockSide.Right;
    private bool _layoutRefreshQueued;
    private bool _resetScrollPending;
    private string? _lastAppliedRunToken;
    private int _lastAppliedCombatIndex = -1;
    private OverlayState? _lastState;
    private static bool _pendingCreate;

    /// <summary>
    /// Schedule overlay creation on next frame (safe to call from mod init before game loop is ready).
    /// </summary>
    public static void ScheduleCreate()
    {
        _pendingCreate = true;
    }



    public override void _EnterTree()
    {
        Layer = 100;
        Name = nameof(DamageTrackerOverlay);
        RunDamageTrackerService.Changed += OnChanged;
    }

    public override void _ExitTree()
    {
        RunDamageTrackerService.Changed -= OnChanged;
        if (ReferenceEquals(_instance, this)) _instance = null;
    }

    public override void _Ready()
    {
        _root = new PanelContainer
        {
            Name = "Root",
            MouseFilter = Control.MouseFilterEnum.Stop,
            Position = new Vector2(16, 16),
            CustomMinimumSize = new Vector2(ExpandedWidth, 0),
            Size = new Vector2(ExpandedWidth, 0)
        };

        // Background panel (the root itself)
        PanelContainer bg = (PanelContainer)_root;
        bg.GuiInput += OnGuiInput;
        bg.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = BgDark,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6
        });

        // Outer margin
        _contentPad = new MarginContainer();
        _contentPad.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (string side in new[] { "margin_left", "margin_right" })
            _contentPad.AddThemeConstantOverride(side, 10);
        foreach (string side in new[] { "margin_top", "margin_bottom" })
            _contentPad.AddThemeConstantOverride(side, 8);

        VBoxContainer col = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 4);

        // ── Header row: title + toggle ──
        col.AddChild(BuildHeader());

        // ── Column headings ──
        _columnHeadings = BuildColumnHeadings();
        col.AddChild(_columnHeadings);

        // ── Thin separator ──
        _separator = HLine();
        col.AddChild(_separator);

        // ── Scrollable body ──
        _bodyScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };

        _bodyContent = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _bodyContent.AddThemeConstantOverride("separation", 4);

        // ── Player rows ──
        _rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 2);
        _bodyContent.AddChild(_rows);

        // ── Empty hint ──
        _emptyLabel = MakeLabel(L("EMPTY"), 12, DimGray);
        _emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _bodyContent.AddChild(_emptyLabel);

        _bodyScroll.AddChild(_bodyContent);
        col.AddChild(_bodyScroll);

        _contentPad.AddChild(col);
        bg.AddChild(_contentPad);
        AddChild(_root);

        _sideTab = BuildSideTab();
        AddChild(_sideTab);

        RefreshChrome();
        ApplyState(RunDamageTrackerService.BuildOverlayState());
    }

    // ── Header ─────────────────────────────────────────────────

    private Control BuildHeader()
    {
        HBoxContainer h = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        h.AddThemeConstantOverride("separation", 6);

        Label title = MakeLabel(L("TITLE"), 15, White);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        _toggleBtn = new Button
        {
            Text = "\u25bc",
            CustomMinimumSize = new Vector2(24, 24),
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.None
        };
        _toggleBtn.AddThemeFontSizeOverride("font_size", 12);
        _toggleBtn.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color("FFFFFF10"), CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 });
        _toggleBtn.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = new Color("FFFFFF20"), CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 });
        _toggleBtn.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = new Color("FFFFFF30"), CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 });
        _toggleBtn.AddThemeColorOverride("font_color", Gray);
        _toggleBtn.Pressed += OnToggle;

        _hideBtn = new Button
        {
            Text = "\u00bb",
            CustomMinimumSize = new Vector2(24, 24),
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Hide to side"
        };
        _hideBtn.AddThemeFontSizeOverride("font_size", 12);
        _hideBtn.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color("FFFFFF10"), CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 });
        _hideBtn.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = new Color("FFFFFF20"), CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 });
        _hideBtn.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = new Color("FFFFFF30"), CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 });
        _hideBtn.AddThemeColorOverride("font_color", Gray);
        _hideBtn.Pressed += OnHideToSide;

        h.AddChild(title);
        h.AddChild(_toggleBtn);
        h.AddChild(_hideBtn);
        return h;
    }

    private Button BuildSideTab()
    {
        Button tab = new()
        {
            Name = "SideTab",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.None,
            Text = L("TITLE"),
            CustomMinimumSize = new Vector2(SideTabWidth, SideTabHeight),
            Size = new Vector2(SideTabWidth, SideTabHeight)
        };
        tab.AddThemeFontSizeOverride("font_size", 12);
        tab.AddThemeColorOverride("font_color", White);
        tab.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color("121821EE"),
            BorderColor = BorderActive,
            BorderWidthLeft = 2,
            BorderWidthTop = 1,
            BorderWidthRight = 2,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6
        });
        tab.AddThemeStyleboxOverride("hover", new StyleBoxFlat
        {
            BgColor = new Color("1A2431EE"),
            BorderColor = BorderActive,
            BorderWidthLeft = 2,
            BorderWidthTop = 1,
            BorderWidthRight = 2,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6
        });
        tab.Pressed += OnRestoreFromSide;
        return tab;
    }

    // ── Column headings ────────────────────────────────────────

    private static Control BuildColumnHeadings()
    {
        HBoxContainer h = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        h.AddThemeConstantOverride("separation", 0);

        // Icon spacer (40px to match avatar)
        h.AddChild(Spacer(40));
        h.AddChild(HeadLabel(L("PLAYER"), true));
        h.AddChild(HeadLabel(L("PCT"), false, 38));
        h.AddChild(HeadLabel(L("TOTAL"), false, 62));
        h.AddChild(HeadLabel(L("COMBAT"), false, 62));
        h.AddChild(HeadLabel(L("LAST"), false, 52));
        h.AddChild(HeadLabel(L("MAX"), false, 52));
        return h;
    }

    // ── Player row ─────────────────────────────────────────────

    private Control CreateRow(PlayerDamageSnapshot snap, float ratio)
    {
        bool active = snap.IsActive;
        Color theme = GetCharTheme(snap.CharacterName);

        PanelContainer card = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 40)
        };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(theme, active ? 0.18f : 0.08f),
            BorderColor = active ? new Color(theme, 0.9f) : new Color(theme, 0.3f),
            BorderWidthLeft = 3,
            BorderWidthTop = 0, BorderWidthRight = 0, BorderWidthBottom = 0,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });

        MarginContainer mp = new();
        mp.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        mp.AddThemeConstantOverride("margin_left", 6);
        mp.AddThemeConstantOverride("margin_right", 6);
        mp.AddThemeConstantOverride("margin_top", 4);
        mp.AddThemeConstantOverride("margin_bottom", 4);

        HBoxContainer row = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 0);

        // Avatar 32x32
        row.AddChild(BuildAvatar(snap, 32));
        row.AddChild(Spacer(8));

        // Name + character stacked
        VBoxContainer nameCol = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        nameCol.AddThemeConstantOverride("separation", 0);

        Label nameLabel = MakeLabel(snap.DisplayName, 13, active ? theme.Lightened(0.3f) : White);
        nameLabel.ClipText = true;
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        Label charLabel = MakeLabel(snap.CharacterName, 10, new Color(theme, 0.7f));
        charLabel.ClipText = true;

        nameCol.AddChild(nameLabel);
        nameCol.AddChild(charLabel);
        row.AddChild(nameCol);

        // Percentage label
        int pct = (int)(ratio * 100f);
        row.AddChild(StatCell($"{pct}%", new Color(theme, 0.9f), 38));

        // Stat columns — right-aligned, fixed width
        row.AddChild(StatCell(RunDamageTrackerService.Format(snap.TotalDamage), Yellow, 62));
        row.AddChild(StatCell(RunDamageTrackerService.Format(snap.CombatDamage), Cyan, 62));
        row.AddChild(StatCell(RunDamageTrackerService.Format(snap.LastDamage), Red, 52));
        row.AddChild(StatCell(RunDamageTrackerService.Format(snap.MaxHitDamage), new Color("FF79C6"), 52));

        mp.AddChild(row);

        // ── Damage share bar ──
        Control barBg = new()
        {
            CustomMinimumSize = new Vector2(0, 3),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        ColorRect barTrack = new()
        {
            Color = new Color("FFFFFF0A"),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        barTrack.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        ColorRect barFill = new()
        {
            Color = new Color(theme, 0.6f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0, AnchorTop = 0,
            AnchorRight = Mathf.Clamp(ratio, 0f, 1f),
            AnchorBottom = 1,
            OffsetLeft = 0, OffsetTop = 0, OffsetRight = 0, OffsetBottom = 0
        };

        barBg.AddChild(barTrack);
        barBg.AddChild(barFill);

        VBoxContainer cardContent = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cardContent.AddThemeConstantOverride("separation", 0);
        cardContent.AddChild(mp);
        cardContent.AddChild(barBg);

        card.AddChild(cardContent);

        // Flash animation on update
        card.Modulate = new Color(1, 1, 1, 0.3f);
        card.TreeEntered += () =>
        {
            Tween? tw = card.CreateTween();
            tw?.TweenProperty(card, "modulate", new Color(1, 1, 1, 1), 0.35f)
               .SetTrans(Tween.TransitionType.Cubic)
               .SetEase(Tween.EaseType.Out);
        };

        return card;
    }

    // ── Avatar ─────────────────────────────────────────────────

    private static Control BuildAvatar(PlayerDamageSnapshot snap, int size)
    {
        Texture2D? tex = LoadIcon(snap.CharacterName) ?? snap.PortraitTexture;

        if (tex != null)
        {
            TextureRect img = new()
            {
                Texture = tex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(size, size),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
            };
            return img;
        }

        // Colored square fallback with initials
        PanelContainer frame = new()
        {
            CustomMinimumSize = new Vector2(size, size),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        };
        frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Color.FromHsv((snap.PlayerKey % 360) / 360f, 0.4f, 0.5f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });

        CenterContainer cc = new();
        cc.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Label ini = MakeLabel(Initials(snap), 12, White);
        cc.AddChild(ini);
        frame.AddChild(cc);
        return frame;
    }

    // ── Toggle expand / collapse ───────────────────────────────

    private void OnToggle()
    {
        _expanded = !_expanded;
        RefreshChrome();

        if (_lastState != null)
            ApplyState(_lastState);
        else
            QueueLayoutRefresh();
    }

    private void OnHideToSide()
    {
        if (_root == null)
            return;

        _hiddenSide = ResolveHiddenSide();
        _hiddenToSide = true;
        RefreshChrome();
        UpdateSideTabPosition();
    }

    private void OnRestoreFromSide()
    {
        _hiddenToSide = false;
        RefreshChrome();
        SnapRootIntoViewFromSide();
        if (_lastState != null)
            ApplyState(_lastState);
        else
            QueueLayoutRefresh();
    }

    // ── Compact row (icon + name + total only) ────────────────

    private Control CreateCompactRow(PlayerDamageSnapshot snap, float ratio)
    {
        Color theme = GetCharTheme(snap.CharacterName);
        bool active = snap.IsActive;

        PanelContainer card = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 28)
        };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(theme, active ? 0.18f : 0.08f),
            BorderColor = active ? new Color(theme, 0.9f) : new Color(theme, 0.3f),
            BorderWidthLeft = 3,
            BorderWidthTop = 0, BorderWidthRight = 0, BorderWidthBottom = 0,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });

        MarginContainer mp = new();
        mp.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        mp.AddThemeConstantOverride("margin_left", 6);
        mp.AddThemeConstantOverride("margin_right", 6);
        mp.AddThemeConstantOverride("margin_top", 2);
        mp.AddThemeConstantOverride("margin_bottom", 2);

        HBoxContainer row = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 0);

        // Avatar 24x24
        row.AddChild(BuildAvatar(snap, 24));
        row.AddChild(Spacer(6));

        // Name only (no character subtitle)
        Label nameLabel = MakeLabel(snap.DisplayName, 12, active ? theme.Lightened(0.3f) : White);
        nameLabel.ClipText = true;
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(nameLabel);

        // Total damage
        row.AddChild(StatCell(RunDamageTrackerService.Format(snap.TotalDamage), Yellow, 62));

        mp.AddChild(row);
        card.AddChild(mp);

        card.Modulate = new Color(1, 1, 1, 0.3f);
        card.TreeEntered += () =>
        {
            Tween? tw = card.CreateTween();
            tw?.TweenProperty(card, "modulate", new Color(1, 1, 1, 1), 0.35f)
               .SetTrans(Tween.TransitionType.Cubic)
               .SetEase(Tween.EaseType.Out);
        };

        return card;
    }

    // ── State update ───────────────────────────────────────────

    private void OnChanged(OverlayState s)
    {
        if (!IsInsideTree()) return;
        Callable.From(() => ApplyState(s)).CallDeferred();
    }

    private void ApplyState(OverlayState s)
    {
        if (_rows == null || _emptyLabel == null) return;

        bool resetScroll = !string.Equals(_lastAppliedRunToken, s.RunToken, System.StringComparison.Ordinal)
            || _lastAppliedCombatIndex != s.CombatIndex;
        _lastAppliedRunToken = s.RunToken;
        _lastAppliedCombatIndex = s.CombatIndex;
        _resetScrollPending |= resetScroll;

        _lastState = s;

        // Hide column headings and separator in compact mode
        if (_columnHeadings != null) _columnHeadings.Visible = _expanded;
        if (_separator != null) _separator.Visible = _expanded;

        foreach (Node c in _rows.GetChildren())
        {
            _rows.RemoveChild(c);
            c.QueueFree();
        }

        _emptyLabel.Visible = s.Players.Count == 0;

        decimal teamTotal = 0;
        for (int i = 0; i < s.Players.Count; i++)
            teamTotal += s.Players[i].TotalDamage;

        for (int i = 0; i < s.Players.Count; i++)
        {
            float ratio = teamTotal > 0 ? (float)(s.Players[i].TotalDamage / teamTotal) : 0f;
            _rows.AddChild(_expanded ? CreateRow(s.Players[i], ratio) : CreateCompactRow(s.Players[i], ratio));
        }

        RefreshChrome();
        QueueLayoutRefresh();
    }

    // ── Drag ───────────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_isDragging && @event is InputEventMouseButton mb && !mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            _isDragging = false;
    }

    private void OnGuiInput(InputEvent @event)
    {
        if (_root == null) return;
        switch (@event)
        {
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left:
                if (mb.Pressed)
                {
                    _isDragging = true;
                    _dragOffset = GetViewport().GetMousePosition() - _root.Position;
                    GetViewport().SetInputAsHandled();
                }
                else _isDragging = false;
                break;
            case InputEventMouseMotion when _isDragging:
                ClampPos(GetViewport().GetMousePosition() - _dragOffset);
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void ClampPos(Vector2 p)
    {
        if (_root == null) return;
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        Vector2 sz = GetCurrentRootSize();
        _root.Position = new Vector2(
            Mathf.Clamp(p.X, 0, Mathf.Max(0, vp.X - sz.X)),
            Mathf.Clamp(p.Y, 0, Mathf.Max(0, vp.Y - sz.Y)));
        UpdateSideTabPosition();
    }

    private void RefreshChrome()
    {
        if (_root == null)
            return;

        if (_toggleBtn != null)
            _toggleBtn.Text = _expanded ? "\u25bc" : "\u25b6";

        if (_hideBtn != null)
            _hideBtn.Text = _hiddenSide == HiddenDockSide.Left ? "\u00ab" : "\u00bb";

        _root.Visible = !_hiddenToSide;
        if (_sideTab != null)
        {
            _sideTab.Visible = _hiddenToSide;
            _sideTab.Text = _hiddenSide == HiddenDockSide.Left ? $"\u00bb {L("TITLE")}" : $"{L("TITLE")} \u00ab";
        }

        float width = _expanded ? ExpandedWidth : CompactWidth;
        _root.CustomMinimumSize = new Vector2(width, _root.CustomMinimumSize.Y);

        if (_contentPad != null)
            _contentPad.Visible = !_hiddenToSide;

        UpdateSideTabPosition();
        if (!_hiddenToSide)
            QueueLayoutRefresh();
    }

    private HiddenDockSide ResolveHiddenSide()
    {
        if (_root == null)
            return HiddenDockSide.Right;

        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float centerX = _root.Position.X + GetCurrentRootSize().X * 0.5f;
        return centerX < viewportSize.X * 0.5f ? HiddenDockSide.Left : HiddenDockSide.Right;
    }

    private void SnapRootIntoViewFromSide()
    {
        if (_root == null)
            return;

        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float width = GetCurrentRootSize().X;
        float margin = 12f;
        float x = _hiddenSide == HiddenDockSide.Left ? margin : viewportSize.X - width - margin;
        ClampPos(new Vector2(x, _root.Position.Y));
    }

    private void UpdateSideTabPosition()
    {
        if (_sideTab == null || _root == null)
            return;

        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float y = Mathf.Clamp(_root.Position.Y + 10f, 0f, Mathf.Max(0f, viewportSize.Y - SideTabHeight));
        float x = _hiddenSide == HiddenDockSide.Left ? 0f : Mathf.Max(0f, viewportSize.X - SideTabWidth);
        _sideTab.Position = new Vector2(x, y);
        _sideTab.Size = new Vector2(SideTabWidth, SideTabHeight);
    }

    private Vector2 GetCurrentRootSize()
    {
        if (_root == null)
            return Vector2.Zero;

        float width = _expanded ? ExpandedWidth : CompactWidth;
        float height = _root.Size.Y > 0 ? _root.Size.Y : _root.GetCombinedMinimumSize().Y;
        return new Vector2(width, height);
    }

    private void QueueLayoutRefresh()
    {
        if (_layoutRefreshQueued)
            return;

        _layoutRefreshQueued = true;
        Callable.From(RefreshLayoutMetrics).CallDeferred();
    }

    private void RefreshLayoutMetrics()
    {
        _layoutRefreshQueued = false;

        if (_hiddenToSide || _root == null || _bodyScroll == null || _bodyContent == null)
            return;

        float width = _expanded ? ExpandedWidth : CompactWidth;
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float maxPanelHeight = Mathf.Max(MinPanelHeight, viewportSize.Y - ViewportMargin * 2f);
        float bodyContentHeight = _bodyContent.GetCombinedMinimumSize().Y;
        float scrollMinHeight = _bodyScroll.GetCombinedMinimumSize().Y;
        float fixedHeight = Mathf.Max(0f, _root.GetCombinedMinimumSize().Y - scrollMinHeight);
        float availableBodyHeight = Mathf.Max(0f, maxPanelHeight - fixedHeight);
        float bodyHeight = Mathf.Min(bodyContentHeight, availableBodyHeight);
        float panelHeight = fixedHeight + bodyHeight;

        _bodyScroll.CustomMinimumSize = new Vector2(0, bodyHeight);
        _root.CustomMinimumSize = new Vector2(width, panelHeight);
        _root.Size = new Vector2(width, panelHeight);

        int maxScroll = Mathf.Max(0, Mathf.CeilToInt(bodyContentHeight - bodyHeight));
        if (_resetScrollPending)
        {
            _bodyScroll.ScrollVertical = 0;
            _resetScrollPending = false;
        }
        else if (_bodyScroll.ScrollVertical > maxScroll)
        {
            _bodyScroll.ScrollVertical = maxScroll;
        }

        ClampPos(_root.Position);
    }

    // ── Icon loader ────────────────────────────────────────────

    private static Texture2D? LoadIcon(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string key = name.Trim().ToLowerInvariant().Replace(' ', '_');
        if (IconCache.TryGetValue(key, out Texture2D? t)) return t;
        string path = $"res://images/ui/top_panel/character_icon_{key}.png";
        t = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        IconCache[key] = t;
        return t;
    }

    // ── Tiny helpers ───────────────────────────────────────────

    private static Label MakeLabel(string text, int size, Color color)
    {
        Label l = new()
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center
        };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    private static Control HeadLabel(string text, bool expand, int width = 0)
    {
        Label l = MakeLabel(text, 10, DimGray);
        if (expand)
        {
            l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            l.ClipText = true;
        }
        else
        {
            l.CustomMinimumSize = new Vector2(width, 0);
            l.HorizontalAlignment = HorizontalAlignment.Right;
        }
        return l;
    }

    private static Control StatCell(string val, Color color, int width)
    {
        Label l = MakeLabel(val, 13, color);
        l.CustomMinimumSize = new Vector2(width, 0);
        l.HorizontalAlignment = HorizontalAlignment.Right;
        return l;
    }

    private static Control Spacer(int w)
    {
        Control c = new() { CustomMinimumSize = new Vector2(w, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        return c;
    }

    private static Control HLine()
    {
        ColorRect r = new()
        {
            Color = Border,
            CustomMinimumSize = new Vector2(0, 1),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        return r;
    }

    private static Color GetCharTheme(string? charName)
    {
        if (!string.IsNullOrWhiteSpace(charName))
        {
            string key = charName.Trim().ToLowerInvariant().Replace(' ', '_');
            if (CharTheme.TryGetValue(key, out Color c)) return c;
        }
        return Gray;
    }

    private static string Initials(PlayerDamageSnapshot s)
    {
        string src = !string.IsNullOrWhiteSpace(s.CharacterName) ? s.CharacterName : s.DisplayName;
        string[] p = src.Split(new[] { ' ', '-', '_' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 0) return "?";
        if (p.Length == 1) return p[0].Length >= 2 ? p[0][..2].ToUpperInvariant() : p[0].ToUpperInvariant();
        return string.Concat(p[0][0], p[1][0]).ToUpperInvariant();
    }

    public override void _Process(double _)
    {
        if (!_pendingCreate) return;
        _pendingCreate = false;
        GD.Print("[DamageTracker] _Process: calling EnsureCreated...");
        EnsureCreated();
    }

    public static void EnsureCreated()
    {
        if (IsInstanceValid(_instance))
        {
            GD.Print("[DamageTracker] EnsureCreated: already exists, skipping");
            return;
        }
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            GD.Print("[DamageTracker] EnsureCreated: game loop not ready, re-scheduling");
            _pendingCreate = true;
            return;
        }
        GD.Print("[DamageTracker] EnsureCreated: creating overlay now!");
        _instance = new DamageTrackerOverlay();
        tree.Root.CallDeferred(Node.MethodName.AddChild, _instance);
    }

    private enum HiddenDockSide
    {
        Left,
        Right
    }
}