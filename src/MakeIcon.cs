using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

class MakeIcon
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr CreateIconIndirect([In] ref ICONINFO piconinfo);
    [StructLayout(LayoutKind.Sequential)]
    struct ICONINFO { public bool fIcon; public IntPtr xHotspot; public IntPtr yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr hIcon);

    static void Main()
    {
        int[] sizes = { 256, 48, 32, 16 };
        using (var fs = new FileStream(@"C:\Users\qzwlz\Doubao\chats\2026-11-05\new-chat\缩略图引擎选择器\src\app.ico", FileMode.Create, FileAccess.Write))
        using (var w = new BinaryWriter(fs))
        {
            // ICONDIR
            w.Write((ushort)0);
            w.Write((ushort)1);
            w.Write((ushort)sizes.Length);
            // 先占位目录，后面回填
            long dirPos = fs.Position;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)0); // width 0 means 256
                w.Write((byte)0); // height 0 means 256
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((ushort)1);
                w.Write((ushort)32);
                w.Write((uint)0);
                w.Write((uint)0);
            }
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                byte[] png = DrawPng(s);
                long offset = fs.Position;
                w.Write(png);
                // 回填目录项
                long back = fs.Position;
                fs.Seek(dirPos + i * 16, SeekOrigin.Begin);
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((ushort)1);
                w.Write((ushort)32);
                w.Write((uint)png.Length);
                w.Write((uint)offset);
                fs.Seek(back, SeekOrigin.Begin);
            }
        }
        Console.WriteLine("ico written");
    }

    static byte[] DrawPng(int s)
    {
        using (var bmp = new Bitmap(s, s))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            // 圆角背景：蓝青渐变
            float r = s * 0.22f;
            var rect = new Rectangle(0, 0, s - 1, s - 1);
            using (var path = Rounded(rect, r))
            using (var brush = new LinearGradientBrush(rect, Color.FromArgb(37, 99, 235), Color.FromArgb(14, 165, 233), 45f))
            {
                g.FillPath(brush, path);
            }
            // 白色"图片"图标：山+太阳+相框
            int pad = (int)(s * 0.2);
            var inner = new Rectangle(pad, pad, s - pad * 2, (int)(s - pad * 2.2f));
            // 相框白底
            g.FillRectangle(Brushes.White, inner);
            // 山
            int baseY = inner.Bottom;
            int midX = inner.Left + inner.Width / 2;
            var mountain = new Point[]
            {
                new Point(inner.Left, baseY),
                new Point(inner.Left + inner.Width / 4, inner.Top + inner.Height / 2),
                new Point(inner.Left + inner.Width / 2, baseY),
            };
            using (var mb = new SolidBrush(Color.FromArgb(37, 99, 235))) g.FillPolygon(mb, mountain);
            var mountain2 = new Point[]
            {
                new Point(inner.Left + inner.Width / 3, baseY),
                new Point((int)(inner.Left + inner.Width * 0.7f), (int)(inner.Top + inner.Height / 3f)),
                new Point(inner.Right, baseY),
            };
            using (var mb2 = new SolidBrush(Color.FromArgb(14, 165, 233))) g.FillPolygon(mb2, mountain2);
            // 太阳
            int sunR = (int)(s * 0.05);
            g.FillEllipse(Brushes.OrangeRed, inner.Right - sunR * 3, inner.Top + sunR, sunR * 2, sunR * 2);
            // 底部一条小条表示"缩略图列表"
            int barH = (int)(s * 0.05);
            g.FillRectangle(new SolidBrush(Color.FromArgb(37, 99, 235)), inner.Left, inner.Bottom - barH, inner.Width, barH);
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }

    static GraphicsPath Rounded(Rectangle r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
