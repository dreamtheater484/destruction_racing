using System;
using System.Collections.Generic;
using Godot;

namespace DestructionRacing.UI;

/// <summary>One row of a <see cref="MenuView"/>: an action (Enter), a choice or slider (←/→), or a heading.</summary>
public sealed class MenuItem
{
    public MenuItem(string caption) => Caption = caption;

    public string Caption { get; set; }
    /// <summary>The current value, shown beside the caption (null: an action row).</summary>
    public Func<string>? Value { get; init; }
    public Action? Activate { get; init; }
    /// <summary>←/→ (−1 / +1).</summary>
    public Action<int>? Change { get; init; }
    /// <summary>A disabled row is greyed and skipped.</summary>
    public Func<bool>? Enabled { get; init; }
    /// <summary>A hidden row takes no space.</summary>
    public Func<bool>? Visible { get; init; }
    /// <summary>The explanation shown while the row has the focus.</summary>
    public Func<string>? Hint { get; init; }
    /// <summary>A section heading (not focusable).</summary>
    public bool Heading { get; init; }
    /// <summary>Optional colour swatch beside the value (palette rows).</summary>
    public Func<Color?>? Swatch { get; init; }

    public bool IsEnabled => Enabled?.Invoke() ?? true;
    public bool IsVisible => Visible?.Invoke() ?? true;
    public bool Focusable => !Heading && IsEnabled && IsVisible;
}

/// <summary>
/// A keyboard menu (plan §13: arrows, Enter and Esc everywhere, with visible focus): ↑/↓ move the focus, ←/→ change
/// the focused value, Enter activates. Rows are labels in the broadcast style; the focused one gets a yellow bar.
/// </summary>
public partial class MenuView : VBoxContainer
{
    private readonly List<MenuItem> _items = new();
    private readonly List<(Control Row, ColorRect Bar, Label Caption, Label Value, Panel Swatch)> _rows = new();
    private int _focus = -1;

    public int CaptionWidth { get; set; } = 320;
    public int FontSize { get; set; } = 32;
    public IReadOnlyList<MenuItem> Items => _items;
    public MenuItem? Current => _focus >= 0 && _focus < _items.Count ? _items[_focus] : null;
    public int FocusIndex => _focus;
    /// <summary>The focus moved or a value changed.</summary>
    public event Action? Changed;

    public MenuView Add(MenuItem item)
    {
        _items.Add(item);
        if (IsInsideTree()) BuildRow(item);
        return this;
    }

    public MenuView Clear()
    {
        _items.Clear();
        foreach (var r in _rows) r.Row.QueueFree();
        _rows.Clear();
        _focus = -1;
        return this;
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 2);
        foreach (var item in _items) BuildRow(item);
        Refresh();
    }

    private void BuildRow(MenuItem item)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 14);
        var bar = new ColorRect { CustomMinimumSize = new Vector2(6, FontSize + 8), Color = Colors.Transparent, MouseFilter = MouseFilterEnum.Ignore };
        row.AddChild(bar);
        var caption = HudStyle.Label(item.Caption, item.Heading ? FontSize - 10 : FontSize, HudStyle.Grey, item.Heading ? "Bold" : "SemiBold", outline: 0);
        if (item.Value != null) caption.CustomMinimumSize = new Vector2(CaptionWidth, 0);
        caption.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(caption);
        var swatch = new Panel { CustomMinimumSize = new Vector2(34, 22), Visible = false, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        row.AddChild(swatch);
        var value = HudStyle.Label("", FontSize, HudStyle.White, "ExtraBold", outline: 0);
        value.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(value);
        if (item.Heading)
        {
            var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
            margin.AddThemeConstantOverride("margin_top", 14);
            row.RemoveChild(bar);
            margin.AddChild(row);
            AddChild(margin);
            _rows.Add((margin, bar, caption, value, swatch));
            return;
        }
        AddChild(row);
        _rows.Add((row, bar, caption, value, swatch));
    }

    /// <summary>Puts the focus on the first focusable row (or keeps it if it still can have it).</summary>
    public void FocusFirst()
    {
        _focus = -1;
        Move(1);
    }

    public void FocusOn(MenuItem item)
    {
        int i = _items.IndexOf(item);
        if (i >= 0 && _items[i].Focusable) _focus = i;
        Refresh();
    }

    public void Refresh()
    {
        if (_focus < 0 || _focus >= _items.Count || !_items[_focus].Focusable)
        {
            int keep = _focus;
            _focus = -1;
            for (int i = Math.Max(0, keep); i < _items.Count && _focus < 0; i++) if (_items[i].Focusable) _focus = i;
            for (int i = 0; i < _items.Count && _focus < 0; i++) if (_items[i].Focusable) _focus = i;
        }
        for (int i = 0; i < _rows.Count && i < _items.Count; i++)
        {
            var item = _items[i];
            var (row, bar, caption, value, swatch) = _rows[i];
            row.Visible = item.IsVisible;
            bool focused = i == _focus;
            bool enabled = item.IsEnabled;
            caption.Text = item.Caption;
            caption.AddThemeColorOverride("font_color", item.Heading ? HudStyle.Yellow : !enabled ? new Color(1f, 1f, 1f, 0.28f) : focused ? HudStyle.Yellow : HudStyle.Grey);
            caption.AddThemeFontOverride("font", HudStyle.Font(item.Heading ? "Bold" : focused ? "ExtraBold" : "SemiBold"));
            bar.Color = focused ? HudStyle.Yellow : Colors.Transparent;
            string v = item.Value?.Invoke() ?? "";
            value.Text = focused && item.Change != null && v.Length > 0 ? $"◀  {v}  ▶" : v;
            value.AddThemeColorOverride("font_color", !enabled ? new Color(1f, 1f, 1f, 0.28f) : HudStyle.White);
            var sw = item.Swatch?.Invoke();
            swatch.Visible = sw != null;
            if (sw is Color c)
                swatch.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                {
                    BgColor = c, BorderColor = focused ? HudStyle.Yellow : new Color(0f, 0f, 0f, 0.7f),
                    BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
                });
        }
    }

    private void Move(int d)
    {
        if (_items.Count == 0) return;
        int i = _focus;
        for (int n = 0; n < _items.Count; n++)
        {
            i = ((i + d) % _items.Count + _items.Count) % _items.Count;
            if (_items[i].Focusable) { _focus = i; break; }
        }
        Refresh();
        Changed?.Invoke();
    }

    /// <summary>Handles ↑/↓/←/→/Enter; returns false for keys the menu does not use.</summary>
    public bool HandleKey(Key key)
    {
        var item = Current;
        if (item != null && !item.IsEnabled) item = null;
        switch (key)
        {
            case Key.Up: Move(-1); return true;
            case Key.Down: Move(1); return true;
            case Key.Left or Key.Right when item?.Change != null:
                item.Change(key == Key.Right ? 1 : -1);
                Refresh();
                Changed?.Invoke();
                return true;
            case Key.Enter or Key.KpEnter when item?.Activate != null:
                item.Activate();
                Refresh();
                Changed?.Invoke();
                return true;
            case Key.Enter or Key.KpEnter when item?.Change != null:
                item.Change(1);
                Refresh();
                Changed?.Invoke();
                return true;
        }
        return false;
    }
}

/// <summary>An action of an <see cref="ActionMenu"/> with its own hotkey.</summary>
public sealed record MenuAction(string Label, Key Hotkey, Action Run);

/// <summary>
/// A short list of actions (the pause menu, the results): ←/→ or ↑/↓ move the focus, Enter runs it, and each action's
/// hotkey runs it directly (R race again, Q quit …).
/// </summary>
public partial class ActionMenu : BoxContainer
{
    private readonly List<MenuAction> _actions = new();
    private readonly List<Label> _labels = new();
    private int _focus;

    public int FontSize { get; set; } = 30;

    public void SetActions(IEnumerable<MenuAction> actions)
    {
        _actions.Clear();
        _actions.AddRange(actions);
        foreach (var l in _labels) l.QueueFree();
        _labels.Clear();
        AddThemeConstantOverride("separation", Vertical ? 4 : 34);
        foreach (var a in _actions)
        {
            var label = HudStyle.Label("", FontSize, HudStyle.Grey, "Bold", Vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left, 0);
            AddChild(label);
            _labels.Add(label);
        }
        _focus = 0;
        Refresh();
    }

    private void Refresh()
    {
        for (int i = 0; i < _labels.Count; i++)
        {
            bool on = i == _focus;
            var a = _actions[i];
            string key = a.Hotkey == Key.None ? "" : $"  {Core.Controls.KeyLabel(a.Hotkey)}";
            _labels[i].Text = on ? $"▶ {a.Label} ◀" : a.Label + key;
            _labels[i].AddThemeColorOverride("font_color", on ? HudStyle.Yellow : HudStyle.Grey);
            _labels[i].AddThemeFontOverride("font", HudStyle.Font(on ? "ExtraBold" : "SemiBold"));
        }
    }

    /// <summary>Handles the menu keys and the hotkeys; returns true when the key was used.</summary>
    public bool HandleKey(Key key)
    {
        if (_actions.Count == 0) return false;
        bool prev = Vertical ? key == Key.Up : key == Key.Left;
        bool next = Vertical ? key == Key.Down : key == Key.Right;
        if (prev || next)
        {
            _focus = (_focus + (next ? 1 : -1) + _actions.Count) % _actions.Count;
            Refresh();
            return true;
        }
        if (key is Key.Enter or Key.KpEnter)
        {
            _actions[_focus].Run();
            return true;
        }
        foreach (var a in _actions)
            if (a.Hotkey != Key.None && a.Hotkey == key)
            {
                a.Run();
                return true;
            }
        return false;
    }
}
