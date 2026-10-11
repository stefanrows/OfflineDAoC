using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.ComponentModel;
using System.Reflection;

namespace OfflineDaoc.Launcher;

internal static class DaocTheme
{
    internal static readonly Color Void = Color.FromArgb(18, 15, 12);
    internal static readonly Color Stone = Color.FromArgb(47, 43, 36);
    internal static readonly Color StoneDark = Color.FromArgb(25, 24, 21);
    internal static readonly Color StoneLight = Color.FromArgb(79, 71, 56);
    internal static readonly Color Iron = Color.FromArgb(102, 100, 90);
    internal static readonly Color Gold = Color.FromArgb(194, 157, 77);
    internal static readonly Color GoldLight = Color.FromArgb(232, 205, 135);
    internal static readonly Color Parchment = Color.FromArgb(215, 199, 157);
    internal static readonly Color Ink = Color.FromArgb(43, 32, 22);
    internal static readonly Color Panel = Color.FromArgb(35, 31, 26);
    internal static readonly Color PanelAlt = Color.FromArgb(42, 37, 30);
    internal static readonly Color Text = Color.FromArgb(224, 215, 190);
    internal static readonly Color Muted = Color.FromArgb(166, 154, 128);
    internal static readonly Color Success = Color.FromArgb(139, 191, 104);
    internal static readonly Color Danger = Color.FromArgb(232, 95, 83);
    internal static readonly Color Albion = Color.FromArgb(171, 64, 58);
    internal static readonly Color Midgard = Color.FromArgb(62, 94, 144);
    internal static readonly Color Hibernia = Color.FromArgb(57, 117, 66);
    internal static readonly Color Input = Color.FromArgb(59, 53, 43);
    internal static readonly Color GridHeader = Color.FromArgb(61, 54, 43);
    internal static readonly Color GridRow = Color.FromArgb(35, 31, 26);
    internal static readonly Color GridRowAlternate = Color.FromArgb(42, 37, 30);
    internal static readonly Color GridSelection = Color.FromArgb(90, 72, 43);
    internal static readonly Color GridLine = Color.FromArgb(78, 70, 56);
    // Realm names in lists are drawn in these lighter tints; the card accents above stay as they are.
    internal static readonly Color RealmTextAlbion = Color.FromArgb(225, 116, 105);
    internal static readonly Color RealmTextMidgard = Color.FromArgb(124, 161, 215);
    internal static readonly Color RealmTextHibernia = Color.FromArgb(112, 178, 112);

    /// <summary>Shared font instances. They are never disposed; controls only reference them.</summary>
    internal static class Fonts
    {
        internal static readonly Font Body = new("Georgia", 8.25f);
        internal static readonly Font Bold = new("Georgia", 8.25f, FontStyle.Bold);
        internal static readonly Font Caption = new("Georgia", 8f, FontStyle.Bold);
        internal static readonly Font Title = new("Georgia", 13f, FontStyle.Bold);
    }

    internal static Color RealmColor(string? realmName) => realmName switch
    {
        "Albion" => RealmTextAlbion,
        "Midgard" => RealmTextMidgard,
        "Hibernia" => RealmTextHibernia,
        _ => Color.White,
    };

    internal static Color RealmColor(int realm) => realm switch
    {
        1 => RealmTextAlbion,
        2 => RealmTextMidgard,
        3 => RealmTextHibernia,
        _ => Color.White,
    };

    internal static void StyleInput(Control input)
    {
        input.BackColor = Input;
        input.ForeColor = Text;
        input.Font = Fonts.Body;
        input.Margin = new Padding(0, 0, 8, 0);
        input.Height = 27;
        if (input is TextBox textBox) textBox.BorderStyle = BorderStyle.FixedSingle;
    }

    /// <summary>Secondary action inside a launcher panel: a rune button sized to its label.</summary>
    internal static RuneButton ToolButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Padding = new Padding(16, 0, 16, 0),
        MinimumSize = new Size(0, 31),
    };

    /// <summary>Flat check box with a gold outline and a dark check fill, for the launcher's switches.</summary>
    internal static void StyleCheckBox(CheckBox box)
    {
        box.FlatStyle = FlatStyle.Flat;
        box.BackColor = Panel;
        box.FlatAppearance.BorderColor = Gold;
        box.FlatAppearance.BorderSize = 1;
        box.FlatAppearance.CheckedBackColor = Input;
    }

    internal static void StyleNumeric(NumericUpDown number)
    {
        number.BackColor = Input;
        number.ForeColor = Text;
        number.Font = Fonts.Body;
        number.BorderStyle = BorderStyle.FixedSingle;
    }

    /// <summary>
    /// One grid look for every launcher list: dark (or, for the exchange, parchment) rows, a gold
    /// header drawn without visual styles, a selection colour and horizontal grid lines. Columns,
    /// data bindings and behaviour are left to the caller.
    /// </summary>
    internal static void StyleGrid(DataGridView grid, DaocGridTone tone = DaocGridTone.Stone)
    {
        bool parchment = tone == DaocGridTone.Parchment;
        Color background = parchment ? Color.FromArgb(205, 187, 144) : StoneDark;
        Color header = parchment ? Color.FromArgb(80, 59, 35) : GridHeader;
        Color row = parchment ? Color.FromArgb(215, 199, 157) : GridRow;
        Color alternate = parchment ? Color.FromArgb(201, 182, 138) : GridRowAlternate;
        Color rowText = parchment ? Ink : Text;
        Color selection = parchment ? Color.FromArgb(117, 85, 45) : GridSelection;
        Color selectionText = parchment ? Color.White : GoldLight;

        grid.EnableHeadersVisualStyles = false;
        grid.RowHeadersVisible = false;
        grid.BackgroundColor = background;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.GridColor = parchment ? Color.FromArgb(127, 103, 64) : GridLine;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = header,
            ForeColor = GoldLight,
            SelectionBackColor = header,
            SelectionForeColor = GoldLight,
            Font = Fonts.Bold,
            Padding = new Padding(2),
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = row,
            ForeColor = rowText,
            SelectionBackColor = selection,
            SelectionForeColor = selectionText,
            Font = Fonts.Body,
            Padding = new Padding(2),
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = alternate };
        grid.ColumnHeadersHeight = 28;
        grid.RowTemplate.Height = 25;
    }
}

internal enum DaocGridTone
{
    Stone,
    Parchment,
}

internal sealed class StoneSurface : Panel
{
    public StoneSurface()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Padding = new Padding(14);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var background = new LinearGradientBrush(ClientRectangle, Color.FromArgb(54, 50, 42), Color.FromArgb(25, 23, 20), 90f);
        e.Graphics.FillRectangle(background, ClientRectangle);

        // Subtle deterministic banding gives the surface the compressed stone texture
        // common to small fan launchers without shipping a large modern art asset.
        for (var y = 5; y < Height; y += 7)
        {
            var shade = 33 + ((y * 17) % 13);
            using var texture = new Pen(Color.FromArgb(65, shade + 12, shade + 8, shade));
            e.Graphics.DrawLine(texture, 2, y, Width - 3, y);
        }

        using var outer = new Pen(Color.FromArgb(12, 10, 8), 2f);
        using var middle = new Pen(DaocTheme.StoneLight);
        using var inner = new Pen(Color.FromArgb(112, 91, 61));
        e.Graphics.DrawRectangle(outer, 1, 1, Math.Max(0, Width - 3), Math.Max(0, Height - 3));
        e.Graphics.DrawRectangle(middle, 3, 3, Math.Max(0, Width - 7), Math.Max(0, Height - 7));
        e.Graphics.DrawRectangle(inner, 6, 6, Math.Max(0, Width - 13), Math.Max(0, Height - 13));
    }
}

internal sealed class InsetPanel : Panel
{
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Accent { get; init; } = DaocTheme.Gold;

    public InsetPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = DaocTheme.Panel;
        Padding = new Padding(2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var shadow = new Pen(Color.FromArgb(10, 9, 7), 2f);
        using var edge = new Pen(DaocTheme.StoneLight);
        using var accent = new Pen(Accent);
        e.Graphics.DrawRectangle(shadow, 1, 1, Math.Max(0, Width - 3), Math.Max(0, Height - 3));
        e.Graphics.DrawLine(edge, 3, 3, Width - 4, 3);
        e.Graphics.DrawLine(accent, 3, Height - 4, Width - 4, Height - 4);
    }
}

internal sealed class RuneButton : Button
{
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowOrnaments { get; set; } = true;
    private bool _hovered;
    private bool _pressed;
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Accent { get; init; } = DaocTheme.Gold;

    public RuneButton()
    {
        DoubleBuffered = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = DaocTheme.Fonts.Bold;
        ForeColor = DaocTheme.GoldLight;
        Width = 104;
        Height = 31;
        Margin = new Padding(4, 0, 0, 0);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        var top = _pressed ? Color.FromArgb(30, 27, 23) : _hovered ? Color.FromArgb(77, 68, 52) : Color.FromArgb(58, 52, 42);
        var bottom = _pressed ? Color.FromArgb(51, 45, 36) : Color.FromArgb(27, 25, 22);
        using var fill = new LinearGradientBrush(bounds, top, bottom, 90f);
        e.Graphics.FillRectangle(fill, bounds);
        using var outer = new Pen(Color.FromArgb(12, 10, 8));
        using var inner = new Pen(Enabled ? Accent : Color.FromArgb(86, 82, 72));
        using var shine = new Pen(Color.FromArgb(112, 102, 81));
        e.Graphics.DrawRectangle(outer, bounds);
        e.Graphics.DrawRectangle(inner, 2, 2, Width - 5, Height - 5);
        e.Graphics.DrawLine(shine, 3, 3, Width - 4, 3);

        using var ornament = new SolidBrush(Enabled ? Accent : Color.FromArgb(86, 82, 72));
        Point[] leftDiamond = [new(8, Height / 2), new(11, Height / 2 - 3), new(14, Height / 2), new(11, Height / 2 + 3)];
        Point[] rightDiamond = [new(Width - 9, Height / 2), new(Width - 12, Height / 2 - 3), new(Width - 15, Height / 2), new(Width - 12, Height / 2 + 3)];
        if (ShowOrnaments)
        {
            e.Graphics.FillPolygon(ornament, leftDiamond);
            e.Graphics.FillPolygon(ornament, rightDiamond);
        }

        var offset = _pressed ? 1 : 0;
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(offset, offset, Width, Height),
            Enabled ? ForeColor : Color.FromArgb(111, 106, 94), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class FantasyBanner : Panel
{
    private static readonly Lazy<Image?> Artwork = new(() =>
    {
        using Stream? stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("OfflineDaoc.Launcher.Assets.offline-daoc-header.png");
        if (stream == null)
            return null;
        using var loaded = new Bitmap(stream);
        return new Bitmap(loaded);
    });

    public FantasyBanner()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Padding = new Padding(4);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.FromArgb(18, 19, 18));
        Image? artwork = Artwork.Value;
        if (artwork != null && Width > 0 && Height > 0)
        {
            float scale = Math.Max(Width / (float)artwork.Width, Height / (float)artwork.Height);
            float sourceWidth = Width / scale;
            float sourceHeight = Height / scale;
            float sourceX = Math.Max(0, (artwork.Width - sourceWidth) / 2f);
            float sourceY = Math.Max(0, (artwork.Height - sourceHeight) / 2f);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(artwork, ClientRectangle,
                sourceX, sourceY, sourceWidth, sourceHeight, GraphicsUnit.Pixel);
        }

        using var shade = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(190, 10, 9, 8), Color.FromArgb(80, 10, 9, 8), 0f);
        e.Graphics.FillRectangle(shade, ClientRectangle);
        using var lowerShade = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(5, 0, 0, 0), Color.FromArgb(105, 0, 0, 0), 90f);
        e.Graphics.FillRectangle(lowerShade, ClientRectangle);

        using var outer = new Pen(Color.FromArgb(8, 7, 5), 2f);
        using var gold = new Pen(Color.FromArgb(113, 83, 43));
        e.Graphics.DrawRectangle(outer, 1, 1, Math.Max(0, Width - 3), Math.Max(0, Height - 3));
        e.Graphics.DrawRectangle(gold, 3, 3, Math.Max(0, Width - 7), Math.Max(0, Height - 7));
    }
}

internal sealed class RealmShieldPicture : PictureBox
{
    private static readonly Lazy<Image?> Shield = new(() =>
    {
        using Stream? stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("OfflineDaoc.Launcher.Assets.offline-daoc-realm-shield.png");
        if (stream == null)
            return null;
        using var loaded = new Bitmap(stream);
        return new Bitmap(loaded);
    });

    public RealmShieldPicture()
    {
        BackColor = Color.Transparent;
        Image = Shield.Value;
        SizeMode = PictureBoxSizeMode.Zoom;
        TabStop = false;
    }
}

/// <summary>
/// Tab strip sized to its text: every tab stays on one row at the 960 px minimum width, with no
/// scroll arrows. Title-case labels and a font step that narrows only on small windows keep the
/// nine launcher tabs inside the strip. The selected tab has a gold underline; hover is highlighted.
/// </summary>
internal sealed class DaocTabControl : TabControl
{
    private const int StripHeight = 29;
    private const int MinimumTabPadding = 6;
    private const int MaximumTabPadding = 14;
    private const TextFormatFlags TextFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
    // Widest step first; the first one that leaves the minimum padding in the strip is used.
    private static readonly Font[] TabFonts = new float[] { 8.5f, 8f, 7.5f }
        .Select(size => new Font("Georgia", size, FontStyle.Bold)).ToArray();
    private Color _stripColor = DaocTheme.StoneDark;
    private int _hoverIndex = -1;

    public DaocTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Normal;
        ItemSize = new Size(0, StripHeight);
        Font = TabFonts[0];
        Padding = new Point(MinimumTabPadding, 0);
        BackColor = _stripColor;
    }

    /// <summary>Colour of the strip behind and beyond the tabs.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color StripColor
    {
        get => _stripColor;
        set
        {
            _stripColor = value;
            BackColor = value;
            Invalidate();
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        FitTabs();
        base.OnLayout(levent);
    }

    // Resize is not raised for a size set before the handle exists, so the fit is refreshed here too.
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        FitTabs();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        FitTabs();
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        FitTabs();
    }

    protected override void OnControlRemoved(ControlEventArgs e)
    {
        base.OnControlRemoved(e);
        FitTabs();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        bool selected = e.Index == SelectedIndex;
        bool hovered = !selected && e.Index == _hoverIndex;
        Rectangle rectangle = GetTabRect(e.Index);
        rectangle.Inflate(-1, 0);
        Color top = selected ? Color.FromArgb(79, 65, 42) : hovered ? Color.FromArgb(70, 62, 48) : Color.FromArgb(47, 43, 36);
        Color bottom = selected ? Color.FromArgb(39, 32, 23) : hovered ? Color.FromArgb(36, 33, 28) : Color.FromArgb(25, 24, 21);
        using (var fill = new LinearGradientBrush(rectangle, top, bottom, 90f))
            e.Graphics.FillRectangle(fill, rectangle);
        using (var border = new Pen(selected ? DaocTheme.Gold : hovered ? DaocTheme.StoneLight : DaocTheme.Iron))
            e.Graphics.DrawRectangle(border, rectangle.X, rectangle.Y, rectangle.Width - 1, rectangle.Height - 1);
        if (selected)
        {
            using var underline = new SolidBrush(DaocTheme.GoldLight);
            e.Graphics.FillRectangle(underline, rectangle.X, rectangle.Bottom - 3, rectangle.Width, 3);
        }
        Color text = selected ? DaocTheme.GoldLight : hovered ? DaocTheme.Parchment : DaocTheme.Muted;
        TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, Font, rectangle, text, TextFlags);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = TabIndexAt(e.Location);
        if (hover == _hoverIndex) return;
        _hoverIndex = hover;
        InvalidateStrip();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex < 0) return;
        _hoverIndex = -1;
        InvalidateStrip();
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        const int WmPaint = 0x000F;
        const int WmPrint = 0x0317;
        const int WmPrintClient = 0x0318;
        if (message.Msg is not (WmPaint or WmPrint or WmPrintClient) || !IsHandleCreated || TabCount == 0) return;

        // Printing (DrawToBitmap, screenshots) passes its device context; painting uses the window.
        using var graphics = message.Msg == WmPaint ? Graphics.FromHwnd(Handle) : Graphics.FromHdc(message.WParam);
        var lastTab = GetTabRect(TabCount - 1);
        var headerHeight = Math.Max(lastTab.Bottom + 2, DisplayRectangle.Top);
        using var fill = new SolidBrush(_stripColor);
        graphics.FillRectangle(fill, lastTab.Right, 0, Math.Max(0, Width - lastTab.Right), headerHeight);
        using var edge = new Pen(Color.FromArgb(116, 88, 48));
        graphics.DrawLine(edge, lastTab.Right, headerHeight - 1, Width, headerHeight - 1);
    }

    private void FitTabs()
    {
        int count = TabPages.Count;
        if (count == 0 || Width <= 0) return;
        // The strip starts 2 px in; keep a small margin before the right edge.
        int available = Width - 6;
        Font chosen = TabFonts[^1];
        int textWidth = 0;
        foreach (Font candidate in TabFonts)
        {
            textWidth = 0;
            foreach (TabPage page in TabPages)
                // Measured without EndEllipsis: that flag needs a bounded width and would under-measure.
                textWidth += TextRenderer.MeasureText(page.Text, candidate, Size.Empty, TextFormatFlags.NoPadding).Width + 1;
            chosen = candidate;
            if (textWidth + 2 * MinimumTabPadding * count <= available) break;
        }
        int padding = Math.Clamp((available - textWidth) / (2 * count), MinimumTabPadding, MaximumTabPadding);
        if (!ReferenceEquals(Font, chosen)) Font = chosen;
        if (Padding.X != padding) Padding = new Point(padding, 0);
    }

    private int TabIndexAt(Point point)
    {
        for (int index = 0; index < TabCount; index++)
            if (GetTabRect(index).Contains(point)) return index;
        return -1;
    }

    private void InvalidateStrip() =>
        Invalidate(new Rectangle(0, 0, Width, Math.Max(StripHeight + 2, DisplayRectangle.Top)));
}

/// <summary>Dark colour table for the launcher's context menus.</summary>
internal sealed class DaocMenuColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => DaocTheme.StoneDark;
    public override Color ImageMarginGradientBegin => DaocTheme.Panel;
    public override Color ImageMarginGradientMiddle => DaocTheme.Panel;
    public override Color ImageMarginGradientEnd => DaocTheme.Panel;
    public override Color MenuBorder => DaocTheme.StoneLight;
    public override Color MenuItemBorder => DaocTheme.Gold;
    public override Color MenuItemSelected => DaocTheme.GridSelection;
    public override Color MenuItemSelectedGradientBegin => DaocTheme.GridSelection;
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(70, 58, 36);
    public override Color MenuItemPressedGradientBegin => DaocTheme.GridHeader;
    public override Color MenuItemPressedGradientEnd => DaocTheme.GridHeader;
    public override Color SeparatorDark => DaocTheme.StoneLight;
    public override Color SeparatorLight => DaocTheme.StoneDark;
    public override Color CheckBackground => DaocTheme.Input;
    public override Color CheckSelectedBackground => DaocTheme.Input;
    public override Color CheckPressedBackground => DaocTheme.Input;
}

internal static class DaocMenu
{
    private static readonly ToolStripRenderer Renderer = new ToolStripProfessionalRenderer(new DaocMenuColors()) { RoundedEdges = false };

    /// <summary>Applies the dark renderer and gold-tinted text to a strip and its items.</summary>
    internal static void Style(ToolStrip strip)
    {
        strip.Renderer = Renderer;
        strip.BackColor = DaocTheme.StoneDark;
        strip.ForeColor = DaocTheme.Text;
        foreach (ToolStripItem item in strip.Items)
            item.ForeColor = DaocTheme.Text;
    }
}

/// <summary>
/// Window icon built at runtime from the embedded realm shield: a multi-size .ico (16, 32, 48, 256)
/// held in memory. The caller owns the returned Icon and disposes it after its window is destroyed.
/// </summary>
internal static class LauncherIcon
{
    private const string ShieldResource = "OfflineDaoc.Launcher.Assets.offline-daoc-realm-shield.png";
    private static readonly int[] FrameSizes = [16, 32, 48, 256];

    internal static Icon? Create()
    {
        using Stream? source = Assembly.GetExecutingAssembly().GetManifestResourceStream(ShieldResource);
        if (source is null)
            return null;
        using var shield = new Bitmap(source);
        var frames = FrameSizes.Select(size => RenderFrame(shield, size)).ToArray();

        using var icon = new MemoryStream();
        using (var writer = new BinaryWriter(icon, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)frames.Length);
            uint offset = (uint)(6 + 16 * frames.Length);
            for (int index = 0; index < frames.Length; index++)
            {
                byte dimension = (byte)(FrameSizes[index] >= 256 ? 0 : FrameSizes[index]);
                writer.Write(dimension);
                writer.Write(dimension);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)frames[index].Length);
                writer.Write(offset);
                offset += (uint)frames[index].Length;
            }
            foreach (byte[] frame in frames)
                writer.Write(frame);
        }
        icon.Position = 0;
        return new Icon(icon);
    }

    private static byte[] RenderFrame(Bitmap shield, int size)
    {
        using var canvas = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(canvas))
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            int width = (int)Math.Round(size * shield.Width / (double)shield.Height);
            graphics.DrawImage(shield, (size - width) / 2f, 0f, width, size);
        }
        using var png = new MemoryStream();
        canvas.Save(png, ImageFormat.Png);
        return png.ToArray();
    }
}
