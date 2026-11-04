using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace FrameSetup
{
    enum St { Waiting, Failed, Passed }

    static class Ui
    {
        public static readonly Color Head = Color.FromArgb(15, 23, 42);
        public static readonly Color HeadSub = Color.FromArgb(148, 163, 184);
        public static readonly Color Bg = Color.FromArgb(248, 250, 252);
        public static readonly Color NavBg = Color.FromArgb(241, 245, 249);
        public static readonly Color Line = Color.FromArgb(226, 232, 240);
        public static readonly Color Text = Color.FromArgb(15, 23, 42);
        public static readonly Color Muted = Color.FromArgb(71, 85, 105);
        public static readonly Color Faint = Color.FromArgb(148, 163, 184);
        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentSoft = Color.FromArgb(239, 246, 255);
        public static readonly Color NoteText = Color.FromArgb(30, 58, 138);
        public static readonly Color Ok = Color.FromArgb(22, 163, 74);
        public static readonly Color Bad = Color.FromArgb(220, 38, 38);
        public static readonly Color BadSoft = Color.FromArgb(254, 242, 242);
        public static readonly Color BadText = Color.FromArgb(153, 27, 27);
        public static readonly Color Wait = Color.FromArgb(217, 119, 6);
        public static readonly Color WaitSoft = Color.FromArgb(255, 251, 235);

        public static float Scale = 1f;
        public static int S(float px) { return (int)Math.Round(px * Scale); }

        public static readonly Font Base = new Font("Segoe UI", 9.75f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 8.75f);
        public static readonly Font Caps = new Font("Segoe UI Semibold", 8f);
        public static readonly Font H2 = new Font("Segoe UI Semibold", 13.5f);
        public static readonly Font Mono = new Font("Consolas", 9.25f);

        // Spinner tick shared by every waiting icon.
        public static readonly Timer Spin = new Timer { Interval = 90, Enabled = true };
        public static int SpinAngle;
        static Ui() { Spin.Tick += (s, e) => SpinAngle = (SpinAngle + 30) % 360; }

        public static Button Btn(string text, bool primary, EventHandler click)
        {
            var b = new Button
            {
                Text = text, FlatStyle = FlatStyle.Flat, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(S(8), S(3), S(8), S(3)), Margin = new Padding(0, 0, S(8), 0), Cursor = Cursors.Hand,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Text, Font = Base, UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(203, 213, 225);
            if (click != null) b.Click += click;
            b.EnabledChanged += (s, e) =>
            {
                b.BackColor = !b.Enabled ? Color.FromArgb(226, 232, 240) : primary ? Accent : Color.White;
                b.FlatAppearance.BorderColor = !b.Enabled ? Color.FromArgb(226, 232, 240) : primary ? Accent : Color.FromArgb(203, 213, 225);
            };
            return b;
        }

        public static FlowLayoutPanel Row(params Control[] items)
        {
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = Padding.Empty, BackColor = Color.Transparent };
            foreach (var c in items)
            {
                if (c is Label || c is TextBox || c is ComboBox) c.Margin = new Padding(0, S(4), S(8), 0);
                row.Controls.Add(c);
            }
            return row;
        }

        public static Label Inline(string text, Color color, Font font = null)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = color, Font = font ?? Small, BackColor = Color.Transparent };
        }
    }

    // Label that wraps to whatever width the parent Stack gives it.
    class WrapLabel : Label
    {
        public WrapLabel(string text, Font font, Color color)
        {
            Text = text; Font = font; ForeColor = color; AutoSize = false; BackColor = Color.Transparent;
            UseMnemonic = false;
        }
        public override Size GetPreferredSize(Size proposed)
        {
            int w = Math.Max(proposed.Width, 20);
            var sz = TextRenderer.MeasureText(Text.Length == 0 ? " " : Text, Font, new Size(w, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            return new Size(w, sz.Height + 2);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }

    // Vertical stack: children take the full width, each as tall as it wants. Hidden children are skipped.
    class Stack : Panel
    {
        public int Gap;
        public Color Border = Color.Empty;
        public int Radius;

        public Stack(int gap = 8) { Gap = Ui.S(gap); DoubleBuffered = true; BackColor = Color.Transparent; }

        public T Add<T>(T c) where T : Control { Controls.Add(c); return c; }

        int ChildHeight(Control c, int w)
        {
            if (c is WrapLabel || c is Stack || c is CheckHeader) return c.GetPreferredSize(new Size(w, 0)).Height;
            if (c.AutoSize) return c.GetPreferredSize(new Size(w, 0)).Height;
            return c.Height;
        }

        public override Size GetPreferredSize(Size proposed)
        {
            int w = proposed.Width - Padding.Horizontal, h = Padding.Vertical, n = 0;
            foreach (Control c in Controls)
            {
                if (!IsShown(c)) continue;
                h += ChildHeight(c, w) + (n++ > 0 ? Gap : 0);
            }
            return new Size(proposed.Width, h);
        }

        // Visible is false for every child while this stack is hidden, so track the intended state ourselves.
        static readonly Dictionary<Control, bool> hidden = new Dictionary<Control, bool>();
        public static void Show(Control c, bool on) { hidden[c] = !on; c.Visible = on; if (c.Parent != null) c.Parent.PerformLayout(); }
        static bool IsShown(Control c) { bool h; return !(hidden.TryGetValue(c, out h) && h); }

        bool laying;

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (laying) return;
            laying = true;
            try
            {
                int w = ClientSize.Width - Padding.Horizontal, y = Padding.Top, n = 0;
                foreach (Control c in Controls)
                {
                    if (!IsShown(c)) continue;
                    if (n++ > 0) y += Gap;
                    // Auto-sized controls (buttons, rows, tick boxes) keep their own size; everything else fills the width.
                    if (c.AutoSize && !(c is WrapLabel || c is Stack || c is CheckHeader))
                    {
                        var ps = c.GetPreferredSize(new Size(w, 0));
                        c.SetBounds(Padding.Left, y, Math.Min(w, ps.Width), ps.Height);
                        y += ps.Height;
                    }
                    else
                    {
                        int h = ChildHeight(c, w);
                        c.SetBounds(Padding.Left, y, w, h);
                        y += h;
                    }
                }
            }
            finally { laying = false; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Border.IsEmpty) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Logo.RoundRect(0, 0, Width - 1, Height - 1, Radius))
            using (var pen = new Pen(Border)) e.Graphics.DrawPath(pen, p);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (BackColor == Color.Transparent || Radius == 0) { base.OnPaintBackground(e); return; }
            Color under = Ui.Bg;
            for (var p = Parent; p != null; p = p.Parent) if (p.BackColor.A == 255) { under = p.BackColor; break; }
            using (var b = new SolidBrush(under)) e.Graphics.FillRectangle(b, ClientRectangle);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Logo.RoundRect(0, 0, Width - 1, Height - 1, Radius))
            using (var b = new SolidBrush(BackColor)) e.Graphics.FillPath(b, p);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

        public static Stack Card(string title)
        {
            var s = new Stack(8) { BackColor = Color.White, Border = Ui.Line, Radius = Ui.S(8), Padding = new Padding(Ui.S(14), Ui.S(11), Ui.S(14), Ui.S(13)) };
            if (title != null) s.Add(new WrapLabel(title.ToUpperInvariant(), Ui.Caps, Ui.Faint));
            return s;
        }

        public static Stack Note(string text)
        {
            var s = new Stack(4) { BackColor = Ui.AccentSoft, Radius = Ui.S(6), Padding = new Padding(Ui.S(12), Ui.S(9), Ui.S(12), Ui.S(9)) };
            s.Add(new WrapLabel(text, Ui.Base, Ui.NoteText));
            return s;
        }

        public static Stack Numbered(params string[] items)
        {
            var s = new Stack(4);
            for (int i = 0; i < items.Length; i++) s.Add(new WrapLabel((i + 1) + ".  " + items[i], Ui.Base, Ui.Text));
            return s;
        }
    }

    // Round state icon plus a bold message and a muted line underneath.
    class CheckHeader : Control
    {
        public St State;
        public string Msg = "", Sub = "";

        public CheckHeader()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Ui.Spin.Tick += (s, e) => { if (State == St.Waiting && Visible) Invalidate(new Rectangle(0, 0, Ui.S(28), Ui.S(28))); };
        }

        int TextW(int w) { return Math.Max(40, w - Ui.S(38)); }

        public override Size GetPreferredSize(Size proposed)
        {
            int tw = TextW(proposed.Width);
            var f = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
            int h = TextRenderer.MeasureText(Msg.Length == 0 ? " " : Msg, Ui.Bold, new Size(tw, int.MaxValue), f).Height;
            if (Sub.Length > 0) h += Ui.S(2) + TextRenderer.MeasureText(Sub, Ui.Small, new Size(tw, int.MaxValue), f).Height;
            return new Size(proposed.Width, Math.Max(Ui.S(28), h + Ui.S(4)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int d = Ui.S(26);
            var r = new Rectangle(0, 0, d, d);
            Color c = State == St.Passed ? Ui.Ok : State == St.Failed ? Ui.Bad : Ui.Wait;
            using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
            using (var pen = new Pen(Color.White, Ui.S(2.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (State == St.Passed)
                    g.DrawLines(pen, new[] { new PointF(d * 0.28f, d * 0.52f), new PointF(d * 0.44f, d * 0.68f), new PointF(d * 0.73f, d * 0.34f) });
                else if (State == St.Failed)
                {
                    g.DrawLine(pen, d / 2f, d * 0.26f, d / 2f, d * 0.56f);
                    using (var w = new SolidBrush(Color.White)) g.FillEllipse(w, d / 2f - Ui.S(1.6f), d * 0.7f - Ui.S(1.6f), Ui.S(3.2f), Ui.S(3.2f));
                }
                else
                {
                    var arc = new RectangleF(d * 0.26f, d * 0.26f, d * 0.48f, d * 0.48f);
                    g.DrawArc(pen, arc, Ui.SpinAngle, 270);
                }
            }
            int tx = Ui.S(38), tw = TextW(Width);
            var f = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
            int mh = TextRenderer.MeasureText(Msg.Length == 0 ? " " : Msg, Ui.Bold, new Size(tw, int.MaxValue), f).Height;
            int top = Sub.Length == 0 ? Math.Max(0, (d - mh) / 2) : Ui.S(1);
            TextRenderer.DrawText(g, Msg, Ui.Bold, new Rectangle(tx, top, tw, mh), Ui.Text, f);
            if (Sub.Length > 0)
                TextRenderer.DrawText(g, Sub, Ui.Small, new Rectangle(tx, top + mh + Ui.S(2), tw, Height), Ui.Muted, f);
        }
    }

    // Live check block: state header, optional "fix it" box, optional extra controls.
    class CheckView : Stack
    {
        public readonly CheckHeader Header = new CheckHeader();
        readonly Stack fix = new Stack(4) { BackColor = Ui.BadSoft, Padding = new Padding(Ui.S(12), Ui.S(9), Ui.S(12), Ui.S(10)) };
        public readonly Stack Extra = new Stack(6);

        public CheckView() : base(8)
        {
            fix.Radius = Ui.S(6);
            Add(Header); Add(fix); Add(Extra);
            Stack.Show(fix, false); Stack.Show(Extra, false);
        }

        public St State { get { return Header.State; } }

        public void Set(St st, string msg, string sub = "", string fixTitle = null, params string[] steps)
        {
            Header.State = st; Header.Msg = msg ?? ""; Header.Sub = sub ?? "";
            fix.Controls.Clear();
            if (fixTitle != null)
            {
                fix.Add(new WrapLabel(fixTitle, Ui.Bold, Ui.BadText));
                for (int i = 0; i < steps.Length; i++) fix.Add(new WrapLabel((i + 1) + ".  " + steps[i], Ui.Base, Ui.Text));
            }
            Stack.Show(fix, fixTitle != null);
            Header.Invalidate();
            Relayout(this);
        }

        public void ShowExtra(bool on) { Stack.Show(Extra, on); Relayout(this); }

        // Walk up so every Stack recomputes its height.
        public static void Relayout(Control c)
        {
            for (var p = c; p != null; p = p.Parent)
            {
                if (p is StepPage) { ((StepPage)p).Fit(); break; }
                p.PerformLayout();
            }
        }
    }

    // One wizard page: scrolls when taller than the window.
    class StepPage : Panel
    {
        public readonly Stack Body = new Stack(12) { Padding = new Padding(Ui.S(26), Ui.S(20), Ui.S(26), Ui.S(16)) };

        public StepPage(string title, string lead)
        {
            AutoScroll = true; BackColor = Ui.Bg; Dock = DockStyle.Fill;
            Controls.Add(Body);
            var head = new Stack(4);
            head.Add(new WrapLabel(title, Ui.H2, Ui.Text));
            if (lead != null) head.Add(new WrapLabel(lead, Ui.Base, Ui.Muted));
            Body.Add(head);
        }

        bool fitting;

        public void Fit()
        {
            if (fitting) return;
            fitting = true;
            try { DoFit(); } finally { fitting = false; }
        }

        void DoFit()
        {
            int w = ClientSize.Width;
            int h = Body.GetPreferredSize(new Size(w, 0)).Height;
            if (h > ClientSize.Height) { w -= SystemInformation.VerticalScrollBarWidth; h = Body.GetPreferredSize(new Size(w, 0)).Height; }
            Body.SetBounds(0, AutoScrollPosition.Y, w, h);
            Body.PerformLayout();
            foreach (Control c in Body.Controls) c.PerformLayout();
            Body.Invalidate(true);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Fit(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) Fit(); }
    }

    // Left-hand step list with state dots.
    class StepNav : Control
    {
        public string[] Names;
        public St[] States;
        public bool[] Visited;
        public int Current;
        public event Action<int> Picked;
        int hover = -1;

        public StepNav()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.NavBg; Cursor = Cursors.Hand;
        }

        int RowH { get { return Ui.S(38); } }
        int Top0 { get { return Ui.S(14); } }

        protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.Y); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e) { int h = Hit(e.Y); if (h >= 0 && Picked != null) Picked(h); }
        int Hit(int y) { int i = (y - Top0) / RowH; return y >= Top0 && Names != null && i < Names.Length ? i : -1; }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Names == null) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            int pad = Ui.S(10);
            for (int i = 0; i < Names.Length; i++)
            {
                var row = new Rectangle(pad, Top0 + i * RowH, Width - pad * 2, RowH - Ui.S(4));
                if (i == Current || i == hover)
                {
                    using (var p = Logo.RoundRect(row.X, row.Y, row.Width, row.Height, Ui.S(6)))
                    using (var b = new SolidBrush(i == Current ? Color.White : Ui.Line)) g.FillPath(b, p);
                }
                int d = Ui.S(22);
                var dot = new Rectangle(row.X + Ui.S(10), row.Y + (row.Height - d) / 2, d, d);
                St st = States[i];
                bool live = i == Current || Visited[i] || st != St.Waiting;
                Color fill = st == St.Passed ? Ui.Ok : st == St.Failed ? Ui.Bad : live ? Ui.WaitSoft : Ui.Line;
                using (var b = new SolidBrush(fill)) g.FillEllipse(b, dot);
                if (st == St.Waiting && live) using (var p = new Pen(Ui.Wait, Ui.S(2))) g.DrawEllipse(p, dot.X + 1, dot.Y + 1, dot.Width - 2, dot.Height - 2);
                string glyph = st == St.Passed ? "✓" : st == St.Failed ? "!" : (i + 1).ToString();
                Color gc = st != St.Waiting ? Color.White : live ? Ui.Wait : Ui.Muted;
                TextRenderer.DrawText(g, glyph, Ui.Caps, dot, gc, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                var tr = new Rectangle(dot.Right + Ui.S(10), row.Y, row.Right - dot.Right - Ui.S(12), row.Height);
                TextRenderer.DrawText(g, Names[i], i == Current ? Ui.Bold : Ui.Base, tr, i == Current ? Ui.Text : Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
