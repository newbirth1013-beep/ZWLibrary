using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ZWLibrary
{
    internal sealed class PreviewCanvas : Control
    {
        internal PreviewData Data;
        internal bool HighlightInsertionPoint;
        internal string Message = "항목을 선택하세요.";
        internal PreviewCanvas()
        {
            DoubleBuffered = true; ResizeRedraw = true;
            BackColor = Color.FromArgb(32, 40, 47); ForeColor = Color.Gainsboro;
            Dock = DockStyle.Fill;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var d = Data;
            if (d == null || !d.HasGeometry)
            {
                TextRenderer.DrawText(e.Graphics, Message, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            double minX = HighlightInsertionPoint ? Math.Min(0, d.MinX) : d.MinX, maxX = HighlightInsertionPoint ? Math.Max(0, d.MaxX) : d.MaxX;
            double minY = HighlightInsertionPoint ? Math.Min(0, d.MinY) : d.MinY, maxY = HighlightInsertionPoint ? Math.Max(0, d.MaxY) : d.MaxY;
            double w = Math.Max(maxX - minX, 0.001), h = Math.Max(maxY - minY, 0.001);
            double scale = Math.Min(Math.Max(1, Width - 32) / w, Math.Max(1, Height - 32) / h);
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            Func<PointF, PointF> screen = p => new PointF((float)(Width / 2.0 + (p.X - cx) * scale), (float)(Height / 2.0 - (p.Y - cy) * scale));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(125, 220, 235), 1.2f))
                foreach (var stroke in d.Lines) e.Graphics.DrawLines(pen, Array.ConvertAll(stroke.Points, p => screen(p)));
            using (var brush = new SolidBrush(ForeColor))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                foreach (var label in d.Texts)
                {
                    PointF top = screen(new PointF(label.Bounds.Left, label.Bounds.Bottom));
                    float height = (float)(label.Bounds.Height * scale);
                    if (height < 3) continue;
                    var box = new RectangleF(top.X, top.Y, Math.Max(2, (float)(label.Bounds.Width * scale)), height);
                    using (var font = new Font(Font.FontFamily, Math.Max(3, Math.Min(80, height * 0.72f)), GraphicsUnit.Pixel))
                        e.Graphics.DrawString(label.Text, font, brush, box, format);
                }
            if (HighlightInsertionPoint)
            {
                PointF origin = screen(PointF.Empty);
                using (var pen = new Pen(Color.Orange, 2))
                {
                    e.Graphics.DrawEllipse(pen, origin.X - 5, origin.Y - 5, 10, 10);
                    e.Graphics.DrawLine(pen, origin.X - 9, origin.Y, origin.X + 9, origin.Y);
                    e.Graphics.DrawLine(pen, origin.X, origin.Y - 9, origin.X, origin.Y + 9);
                }
                TextRenderer.DrawText(e.Graphics, "주황색 십자: 삽입점", Font, new Point(5, 5), Color.Orange, BackColor);
            }
        }
    }
}
