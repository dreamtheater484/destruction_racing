using DestructionRacing.UI;
using Godot;

namespace DestructionRacing.Menus;

/// <summary>
/// One screen of the front end (a full-screen control on the <see cref="FrontEnd"/>'s page stack). The host draws the
/// heading, the hint and the key line from the page's properties; the page draws its own panels.
/// </summary>
public abstract partial class Page : Control
{
    protected Page()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public FrontEnd Host { get; set; } = null!;
    /// <summary>The big title top left.</summary>
    public virtual string Heading => "";
    /// <summary>The line under the title.</summary>
    public virtual string Subheading => "";
    /// <summary>The explanation line above the keys (usually the focused row's hint).</summary>
    public virtual string Hint => "";
    public virtual string KeysHint => FrontEnd.MenuKeys;
    /// <summary>Hide the heading (the title screen draws its own).</summary>
    public virtual bool ShowChrome => true;

    /// <summary>Called each time the page becomes the top of the stack.</summary>
    public virtual void Shown() { }
    public virtual bool HandleKey(InputEventKey key) => false;
    /// <summary>Esc: return true to close the page.</summary>
    public virtual bool OnBack() => true;
    public virtual void Tick(float dt) { }

    protected static PanelContainer LeftPanel(Control parent, float width = 860f, float top = 250f)
    {
        var panel = HudStyle.Place(new PanelContainer { MouseFilter = MouseFilterEnum.Ignore }, 0f, 0f, new Vector2(70, top), new Vector2(width, 0));
        panel.AddThemeStyleboxOverride("panel", HudStyle.Panel(HudStyle.Yellow, 0.04f));
        parent.AddChild(panel);
        return panel;
    }
}

/// <summary>A page that is one <see cref="MenuView"/> in a panel on the left.</summary>
public abstract partial class MenuPage : Page
{
    protected readonly MenuView Menu = new();
    protected PanelContainer Panel = null!;
    protected VBoxContainer Box = null!;
    protected virtual float PanelWidth => 860f;
    protected virtual float PanelTop => 250f;

    public override void _Ready()
    {
        Panel = LeftPanel(this, PanelWidth, PanelTop);
        Box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        Box.AddThemeConstantOverride("separation", 8);
        Panel.AddChild(Box);
        Box.AddChild(Menu);
        Menu.Changed += () => Host.RefreshChrome();
        Build();
        Menu.FocusFirst();
    }

    /// <summary>Adds the rows (and any extra controls to <see cref="Box"/>).</summary>
    protected abstract void Build();

    public override string Hint => Menu.Current?.Hint?.Invoke() ?? "";

    public override void Shown() => Menu.Refresh();

    public override bool HandleKey(InputEventKey key) => Menu.HandleKey(key.PhysicalKeycode);
}
