using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace FrameSetup
{
    // Photo-frame logo with a green tick, drawn in code so the exe icon and the in-app header match.
    static class Logo
    {
        public static Bitmap Draw(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float s = size;
                using (var path = RoundRect(0, 0, s - 1, s - 1, s * 0.22f))
                using (var bg = new LinearGradientBrush(new PointF(0, 0), new PointF(s, s),
                    Color.FromArgb(59, 130, 246), Color.FromArgb(29, 78, 216)))
                    g.FillPath(bg, path);

                // frame outline with a mountain and a sun inside
                float pw = Math.Max(1.2f, s * 0.065f);
                var fr = new RectangleF(s * 0.18f, s * 0.22f, s * 0.64f, s * 0.48f);
                using (var pen = new Pen(Color.White, pw)) g.DrawRectangle(pen, fr.X, fr.Y, fr.Width, fr.Height);
                using (var white = new SolidBrush(Color.White))
                {
                    if (size >= 24)
                    {
                        g.FillPolygon(white, new[] {
                            new PointF(fr.Left + s * 0.05f, fr.Bottom - s * 0.04f), new PointF(fr.Left + s * 0.2f, fr.Top + s * 0.22f),
                            new PointF(fr.Left + s * 0.31f, fr.Top + s * 0.32f), new PointF(fr.Left + s * 0.39f, fr.Top + s * 0.25f),
                            new PointF(fr.Right - s * 0.05f, fr.Bottom - s * 0.04f) });
                        float sun = s * 0.1f;
                        g.FillEllipse(white, fr.Right - s * 0.2f, fr.Top + s * 0.07f, sun, sun);
                    }
                    g.FillRectangle(white, s * 0.42f, s * 0.73f, s * 0.16f, s * 0.06f);
                }

                // green tick badge, bottom right
                float br = s * 0.3f, bx = s * 0.66f, by = s * 0.66f;
                using (var ring = new SolidBrush(Color.FromArgb(29, 78, 216))) g.FillEllipse(ring, bx - br / 2 - s * 0.03f, by - br / 2 - s * 0.03f, br + s * 0.06f, br + s * 0.06f);
                using (var green = new SolidBrush(Color.FromArgb(34, 197, 94))) g.FillEllipse(green, bx - br / 2, by - br / 2, br, br);
                using (var tick = new Pen(Color.White, Math.Max(1.2f, s * 0.045f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(tick, new[] { new PointF(bx - br * 0.24f, by), new PointF(bx - br * 0.05f, by + br * 0.2f), new PointF(bx + br * 0.26f, by - br * 0.2f) });
            }
            return bmp;
        }

        public static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
            float d = r * 2;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
