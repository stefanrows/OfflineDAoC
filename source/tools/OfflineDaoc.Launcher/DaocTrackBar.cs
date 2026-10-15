using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Native trackbar with a dark channel and a gold thumb. Windows paints these two parts itself,
/// so they are repainted from the control's custom-draw notifications. Keyboard, mouse dragging
/// and value handling stay with the native control.
/// </summary>
internal sealed class DaocTrackBar : TrackBar
{
    private const int OcmNotify = 0x204E;          // WM_NOTIFY reflected back to the control
    private const int NmCustomDrawCode = -12;
    private const int CdrfDoDefault = 0x00;
    private const int CdrfNotifyItemDraw = 0x20;
    private const int CdrfSkipDefault = 0x04;
    private const uint CddsPrepaint = 0x00000001;
    private const uint CddsItemPrepaint = 0x00010001;
    private const int TbcdThumb = 0x0002;
    private const int TbcdChannel = 0x0003;
    private const int TbmGetThumbRect = 0x0400 + 25;   // WM_USER + 25
    private const int TbmGetChannelRect = 0x0400 + 26; // WM_USER + 26
    private const int TrackThickness = 6;

    public DaocTrackBar()
    {
        TickStyle = TickStyle.None;
        BackColor = DaocTheme.Panel;
        Cursor = Cursors.Hand;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == OcmNotify && CustomDrawResult(message.LParam) is int result)
        {
            message.Result = (IntPtr)result;
            return;
        }
        base.WndProc(ref message);
    }

    private int? CustomDrawResult(IntPtr lParam)
    {
        var draw = Marshal.PtrToStructure<NmCustomDraw>(lParam);
        if (draw.Header.Code != NmCustomDrawCode)
            return null;
        if (draw.DrawStage == CddsPrepaint)
            return CdrfNotifyItemDraw;
        if (draw.DrawStage != CddsItemPrepaint)
            return CdrfDoDefault;
        int part = draw.ItemSpec.ToInt32();
        if (part != TbcdChannel && part != TbcdThumb)
            return CdrfDoDefault;
        PaintPart(draw.Hdc, part);
        return CdrfSkipDefault;
    }

    private void PaintPart(IntPtr hdc, int part)
    {
        using var graphics = Graphics.FromHdc(hdc);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        NativeRect thumb = ThumbRect();
        if (part == TbcdChannel)
        {
            NativeRect channel = new();
            SendMessage(Handle, TbmGetChannelRect, IntPtr.Zero, ref channel);
            int top = channel.Top + Math.Max(0, (channel.Bottom - channel.Top - TrackThickness) / 2);
            var track = new Rectangle(channel.Left, top, Math.Max(0, channel.Right - channel.Left), TrackThickness);
            if (track.Width <= 0)
                return;
            using (var groove = new SolidBrush(DaocTheme.StoneDark))
                graphics.FillRectangle(groove, track);
            int thumbCentre = (thumb.Left + thumb.Right) / 2;
            var filled = new Rectangle(track.Left, track.Top, Math.Clamp(thumbCentre - track.Left, 0, track.Width), track.Height);
            if (filled.Width > 0)
            {
                using var fill = new SolidBrush(Enabled ? DaocTheme.Gold : Color.FromArgb(86, 82, 72));
                graphics.FillRectangle(fill, filled);
            }
            using var edge = new Pen(DaocTheme.GridLine);
            graphics.DrawRectangle(edge, track.X, track.Y, track.Width - 1, track.Height - 1);
            return;
        }

        var bounds = Rectangle.FromLTRB(thumb.Left + 1, thumb.Top + 1, thumb.Right - 1, thumb.Bottom - 1);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        Color light = Enabled ? DaocTheme.GoldLight : Color.FromArgb(120, 116, 104);
        Color dark = Enabled ? DaocTheme.Gold : Color.FromArgb(86, 82, 72);
        using (var fill = new LinearGradientBrush(bounds, light, dark, 90f))
            graphics.FillRectangle(fill, bounds);
        using var outline = new Pen(DaocTheme.Void);
        graphics.DrawRectangle(outline, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    private NativeRect ThumbRect()
    {
        NativeRect thumb = new();
        SendMessage(Handle, TbmGetThumbRect, IntPtr.Zero, ref thumb);
        return thumb;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NmHeader
    {
        public IntPtr HwndFrom;
        public IntPtr IdFrom;
        public int Code;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NmCustomDraw
    {
        public NmHeader Header;
        public uint DrawStage;
        public IntPtr Hdc;
        public NativeRect Rect;
        public IntPtr ItemSpec;
        public uint ItemState;
        public IntPtr ItemParam;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref NativeRect lParam);
}
