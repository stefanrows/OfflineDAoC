using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Colours and fonts for the Camlann Herald. The realm text colours are the same
/// ones the bot grid uses in MainForm; the chart fills are derived from them.
/// </summary>
internal static class HeraldPalette
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    /// <summary>Gives a control's scroll bars the Windows dark theme (Windows 10 1809+; a no-op elsewhere).</summary>
    internal static void UseDarkScrollbars(Control control)
    {
        void Apply()
        {
            try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        if (control.IsHandleCreated) Apply();
        control.HandleCreated += (_, _) => Apply();
    }

    internal static readonly Color Albion = DaocTheme.RealmTextAlbion;
    internal static readonly Color Midgard = DaocTheme.RealmTextMidgard;
    internal static readonly Color Hibernia = DaocTheme.RealmTextHibernia;
    internal static readonly Color Companion = Color.FromArgb(150, 200, 190);
    internal static readonly Font Body = new("Georgia", 8.25f);
    internal static readonly Font BodyBold = new("Georgia", 8.25f, FontStyle.Bold);
    internal static readonly Font Small = new("Georgia", 7.5f);
    internal static readonly Font Caption = new("Georgia", 7.5f, FontStyle.Bold);
    internal static readonly Font Heading = new("Georgia", 15f, FontStyle.Bold);
    internal static readonly Font Figure = new("Georgia", 13f, FontStyle.Bold);

    internal static Color Realm(int realm) => realm switch
    {
        HeraldData.Albion => Albion,
        HeraldData.Midgard => Midgard,
        HeraldData.Hibernia => Hibernia,
        _ => DaocTheme.Muted,
    };

    internal static Color RealmNamed(string realm) => realm switch
    {
        "Albion" => Albion,
        "Midgard" => Midgard,
        "Hibernia" => Hibernia,
        _ => DaocTheme.Muted,
    };

    internal static Color TypeTag(string type) => type switch
    {
        "GM" => DaocTheme.GoldLight,
        "Gamebot" => DaocTheme.Muted,
        "Companion" => Companion,
        _ => DaocTheme.Text,
    };

    /// <summary>Moves a colour toward white (amount above 0) or black (below 0).</summary>
    internal static Color Mix(Color color, double amount)
    {
        double target = amount >= 0 ? 255 : 0;
        double t = Math.Min(1, Math.Abs(amount));
        return Color.FromArgb(color.A,
            (int)Math.Round(color.R + (target - color.R) * t),
            (int)Math.Round(color.G + (target - color.G) * t),
            (int)Math.Round(color.B + (target - color.B) * t));
    }

    /// <summary>Pixel metric at 96 DPI scaled to the control's current DPI.</summary>
    internal static int Px(int value, int dpi) => (int)Math.Round(value * dpi / 96.0);
}

/// <summary>Dark, gold-ruled grid used by every Herald table.</summary>
internal sealed class HeraldGridView : DataGridView
{
    internal HeraldGridView()
    {
        HeraldPalette.UseDarkScrollbars(this);
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        ReadOnly = true;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        AutoGenerateColumns = false;
        RowHeadersVisible = false;
        MultiSelect = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        BackgroundColor = DaocTheme.StoneDark;
        BorderStyle = BorderStyle.Fixed3D;
        GridColor = Color.FromArgb(78, 70, 56);
        EnableHeadersVisualStyles = false;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        RowTemplate.Height = 25;
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(61, 54, 43),
            ForeColor = DaocTheme.GoldLight,
            SelectionBackColor = Color.FromArgb(61, 54, 43),
            SelectionForeColor = DaocTheme.GoldLight,
            Font = HeraldPalette.BodyBold,
            Padding = new Padding(2),
        };
        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(35, 31, 26),
            ForeColor = DaocTheme.Text,
            SelectionBackColor = Color.FromArgb(90, 72, 43),
            SelectionForeColor = DaocTheme.GoldLight,
            Font = HeraldPalette.Body,
            Padding = new Padding(2),
        };
        AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(42, 37, 30) };
    }

    /// <summary>
    /// Adds a text column bound to <paramref name="property"/>. Null values show as a dash.
    /// Fill columns share the spare width, so give them a minimum width.
    /// </summary>
    internal DataGridViewColumn AddColumn(string header, string property, int width,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft,
        string? format = null, bool fill = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = property,
            Width = width,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        column.DefaultCellStyle.Alignment = alignment;
        column.DefaultCellStyle.NullValue = "—";
        if (format is not null)
            column.DefaultCellStyle.Format = format;
        if (fill)
        {
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            column.FillWeight = 100;
            column.MinimumWidth = width;
        }
        Columns.Add(column);
        return column;
    }
}

/// <summary>
/// Section or filter pill: rounded, gold-ruled, filled gold when selected. Owner-drawn
/// because the stock flat button cannot show the rounded gold selected state.
/// </summary>
internal sealed class PillButton : Button
{
    private bool _selected;
    private bool _hovered;

    internal PillButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = HeraldPalette.BodyBold;
        ForeColor = DaocTheme.GoldLight;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(8, 0, 8, 0);
        Margin = new Padding(0, 0, 6, 0);
        TabStop = true;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            Invalidate();
        }
    }

    protected override Size DefaultSize => new(96, 26);

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using (GraphicsPath path = PillPath(bounds))
        {
            Color top = _selected ? DaocTheme.GoldLight
                : _hovered ? Color.FromArgb(77, 68, 52) : Color.FromArgb(58, 52, 42);
            Color bottom = _selected ? DaocTheme.Gold : DaocTheme.StoneDark;
            using (var fill = new LinearGradientBrush(bounds, top, bottom, 90f))
                graphics.FillPath(fill, path);
            using (var border = new Pen(_selected || _hovered ? DaocTheme.GoldLight : DaocTheme.Gold, 1f))
                graphics.DrawPath(border, path);
        }

        graphics.SmoothingMode = SmoothingMode.Default;
        Color text = !Enabled ? Color.FromArgb(111, 106, 94) : _selected ? DaocTheme.Ink : DaocTheme.GoldLight;
        TextRenderer.DrawText(graphics, Text, Font, bounds, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private static GraphicsPath PillPath(Rectangle bounds)
    {
        int diameter = Math.Max(2, bounds.Height);
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 90, 180);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 180);
        path.CloseFigure();
        return path;
    }
}

/// <summary>One-of-many pill row. Changed fires only when the selection moves.</summary>
internal sealed class PillSelector : FlowLayoutPanel
{
    private readonly List<PillButton> _pills = [];
    private int _selected = -1;

    internal PillSelector(params string[] items)
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = false;
        Margin = new Padding(0, 0, 10, 0);
        Padding = new Padding(0);
        BackColor = DaocTheme.Panel;
        for (int index = 0; index < items.Length; index++)
        {
            int captured = index;
            var pill = new PillButton { Text = items[index], BackColor = BackColor };
            pill.Click += (_, _) => SelectedIndex = captured;
            _pills.Add(pill);
            Controls.Add(pill);
        }

        if (items.Length > 0)
            SelectIndex(0);
    }

    internal event EventHandler? Changed;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value < 0 || value >= _pills.Count || value == _selected)
                return;
            SelectIndex(value);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal string SelectedText => _selected >= 0 ? _pills[_selected].Text : string.Empty;

    /// <summary>Pills take the row's background so each row blends into its own panel.</summary>
    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        foreach (PillButton pill in _pills)
            pill.BackColor = BackColor;
    }

    private void SelectIndex(int index)
    {
        _selected = index;
        for (int i = 0; i < _pills.Count; i++)
            _pills[i].Selected = i == index;
    }
}

internal sealed record DonutSlice(string Label, int Count);

/// <summary>
/// Anti-aliased ring of slices in shades of one colour, with the total in the centre
/// and a legend (class, count, share) beside it.
/// </summary>
internal sealed class DonutChart : Control
{
    private static readonly double[] Shades = [0.30, -0.18, 0.55, -0.32, 0.10, 0.42, -0.42, 0.20, -0.06, 0.66, -0.22, 0.00, 0.36, -0.10];
    private IReadOnlyList<DonutSlice> _slices = [];
    private Color _color = DaocTheme.Gold;
    private string _centerValue = string.Empty;
    private string _centerCaption = string.Empty;

    internal DonutChart()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = DaocTheme.Panel;
        ForeColor = DaocTheme.Text;
        // Table cells size a control from its preferred size only when AutoSize is on.
        AutoSize = true;
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
    }

    internal void SetData(IReadOnlyList<DonutSlice> slices, Color color, string centerValue, string centerCaption)
    {
        _slices = slices;
        _color = color;
        _centerValue = centerValue;
        _centerCaption = centerCaption;
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(Math.Max(HeraldPalette.Px(200, DeviceDpi), proposedSize.Width), HeraldPalette.Px(196, DeviceDpi));

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(BackColor);
        int dpi = DeviceDpi;
        int pad = HeraldPalette.Px(8, dpi);
        var rows = new List<(int Index, DonutSlice Slice)>();
        for (int i = 0; i < _slices.Count; i++)
        {
            if (_slices[i].Count > 0)
                rows.Add((i, _slices[i]));
        }

        int total = rows.Sum(row => row.Slice.Count);
        int diameter = Math.Max(HeraldPalette.Px(70, dpi), Math.Min(Height - pad * 2, (int)(Width * 0.40)));
        var ring = new Rectangle(pad, (Height - diameter) / 2, diameter, diameter);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (total <= 0)
        {
            using var empty = new SolidBrush(DaocTheme.StoneLight);
            graphics.FillEllipse(empty, ring);
        }
        else
        {
            float start = -90f;
            foreach ((int index, DonutSlice slice) in rows)
            {
                float sweep = 360f * slice.Count / total;
                using (var brush = new SolidBrush(HeraldPalette.Mix(_color, Shades[index % Shades.Length])))
                    graphics.FillPie(brush, ring, start, sweep);
                using (var edge = new Pen(DaocTheme.StoneDark, 1f))
                    graphics.DrawPie(edge, ring, start, sweep);
                start += sweep;
            }
        }

        int hole = (int)(diameter * 0.58);
        var holeRect = new Rectangle(ring.X + (diameter - hole) / 2, ring.Y + (diameter - hole) / 2, hole, hole);
        using (var centre = new SolidBrush(BackColor))
            graphics.FillEllipse(centre, holeRect);
        graphics.SmoothingMode = SmoothingMode.Default;
        var valueRect = new Rectangle(holeRect.X, holeRect.Y + holeRect.Height / 5, holeRect.Width, holeRect.Height * 2 / 5);
        var captionRect = new Rectangle(holeRect.X, valueRect.Bottom, holeRect.Width, holeRect.Height / 5);
        TextRenderer.DrawText(graphics, _centerValue, HeraldPalette.Figure, valueRect, DaocTheme.GoldLight,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(graphics, _centerCaption, HeraldPalette.Small, captionRect, DaocTheme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        int legendLeft = ring.Right + pad * 2;
        int legendWidth = Width - legendLeft - pad;
        if (rows.Count == 0 || legendWidth < HeraldPalette.Px(90, dpi))
            return;

        int rowHeight = HeraldPalette.Px(17, dpi);
        int fit = Math.Max(1, (Height - pad * 2) / rowHeight);
        int shown = rows.Count <= fit ? rows.Count : fit - 1;
        int shownRows = shown + (rows.Count > shown ? 1 : 0);
        int top = (Height - shownRows * rowHeight) / 2;
        int swatch = HeraldPalette.Px(9, dpi);
        int countWidth = HeraldPalette.Px(36, dpi);
        int shareWidth = HeraldPalette.Px(56, dpi);
        int gap = HeraldPalette.Px(8, dpi);
        int nameWidth = Math.Max(HeraldPalette.Px(40, dpi), legendWidth - swatch - countWidth - shareWidth - gap - HeraldPalette.Px(8, dpi));
        for (int row = 0; row < shown; row++)
        {
            (int index, DonutSlice slice) = rows[row];
            int y = top + row * rowHeight;
            using (var brush = new SolidBrush(HeraldPalette.Mix(_color, Shades[index % Shades.Length])))
                graphics.FillRectangle(brush, legendLeft, y + (rowHeight - swatch) / 2, swatch, swatch);
            var nameRect = new Rectangle(legendLeft + swatch + HeraldPalette.Px(6, dpi), y, nameWidth, rowHeight);
            var countRect = new Rectangle(nameRect.Right, y, countWidth, rowHeight);
            var shareRect = new Rectangle(countRect.Right + gap, y, shareWidth, rowHeight);
            string share = (slice.Count * 100.0 / total).ToString("0.0", CultureInfo.CurrentCulture) + "%";
            TextRenderer.DrawText(graphics, slice.Label, HeraldPalette.Body, nameRect, DaocTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(graphics, slice.Count.ToString("N0", CultureInfo.CurrentCulture), HeraldPalette.Body, countRect, DaocTheme.GoldLight,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(graphics, share, HeraldPalette.Body, shareRect, DaocTheme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        if (shown < rows.Count)
        {
            int remaining = rows.Count - shown;
            var moreRect = new Rectangle(legendLeft, top + shown * rowHeight, legendWidth, rowHeight);
            TextRenderer.DrawText(graphics, $"+{remaining:N0} more", HeraldPalette.Small, moreRect, DaocTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }
}

internal sealed record BarItem(string Label, long Value, string ValueText, Color Color);

/// <summary>Horizontal bars with a label, a value and optional share text per row.</summary>
internal sealed class BarList : Control
{
    private const int RowHeightAt96 = 22;
    private IReadOnlyList<BarItem> _items = [];
    private long _maximum;

    internal BarList()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = DaocTheme.Panel;
        ForeColor = DaocTheme.Text;
        AutoSize = true;
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
    }

    /// <summary>Bars scale to <paramref name="maximum"/>, or to the largest value when 0.</summary>
    internal void SetItems(IReadOnlyList<BarItem> items, long maximum = 0)
    {
        _items = items;
        _maximum = maximum;
        Invalidate();
        Parent?.PerformLayout();
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(Math.Max(HeraldPalette.Px(200, DeviceDpi), proposedSize.Width),
            Math.Max(1, _items.Count) * HeraldPalette.Px(RowHeightAt96, DeviceDpi) + HeraldPalette.Px(8, DeviceDpi));

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(BackColor);
        if (_items.Count == 0)
            return;

        int dpi = DeviceDpi;
        int pad = HeraldPalette.Px(4, dpi);
        int rowHeight = Math.Max(HeraldPalette.Px(12, dpi), Math.Min(HeraldPalette.Px(RowHeightAt96, dpi), (Height - pad * 2) / _items.Count));
        int labelWidth = Math.Min(Width / 2, _items.Max(item => TextRenderer.MeasureText(item.Label, HeraldPalette.Body).Width) + HeraldPalette.Px(12, dpi));
        int valueWidth = Math.Min(Width / 2, _items.Max(item => TextRenderer.MeasureText(item.ValueText, HeraldPalette.Body).Width) + HeraldPalette.Px(10, dpi));
        int trackLeft = pad + labelWidth;
        int trackRight = Width - pad - valueWidth;
        int trackWidth = Math.Max(HeraldPalette.Px(20, dpi), trackRight - trackLeft);
        long maximum = _maximum > 0 ? _maximum : Math.Max(1, _items.Max(item => item.Value));
        int barHeight = Math.Max(HeraldPalette.Px(6, dpi), rowHeight * 2 / 3);

        for (int i = 0; i < _items.Count; i++)
        {
            BarItem item = _items[i];
            int top = pad + i * rowHeight;
            var labelRect = new Rectangle(pad, top, labelWidth - HeraldPalette.Px(6, dpi), rowHeight);
            TextRenderer.DrawText(graphics, item.Label, HeraldPalette.Body, labelRect, DaocTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            var track = new Rectangle(trackLeft, top + (rowHeight - barHeight) / 2, trackWidth, barHeight);
            using (var trackBrush = new SolidBrush(DaocTheme.StoneDark))
                graphics.FillRectangle(trackBrush, track);
            int length = item.Value <= 0 ? 0 : Math.Max(HeraldPalette.Px(2, dpi), (int)Math.Round(trackWidth * (double)item.Value / maximum));
            if (length > 0)
            {
                using var bar = new LinearGradientBrush(new Rectangle(track.X, track.Y, length, track.Height),
                    HeraldPalette.Mix(item.Color, -0.35), item.Color, 0f);
                graphics.FillRectangle(bar, track.X, track.Y, length, track.Height);
            }

            var valueRect = new Rectangle(trackRight + HeraldPalette.Px(6, dpi), top, valueWidth - HeraldPalette.Px(6, dpi), rowHeight);
            TextRenderer.DrawText(graphics, item.ValueText, HeraldPalette.Body, valueRect, DaocTheme.Text,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }
}
