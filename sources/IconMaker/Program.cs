using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconMaker;

/// <summary>
/// The glyph on an icon's badge.
/// </summary>
public enum Badge
{
    Code,
    Lock,
    Globe,
}

/// <summary>
/// Draws GistDrive's folder icons: a folder with a round badge, and the badge tells them apart
/// (code for GistDrive, a lock for Secret, a globe for Public). Writes .ico files with the sizes
/// the shell asks for, a 256 px .png for the notification app ID, and a preview sheet.
/// Run from the repository root: dotnet run --project sources/IconMaker -- sources/GistDrive/icons
/// </summary>
public static class Program
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 256];

    [STAThread]
    public static void Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "icons");
        Directory.CreateDirectory(output);
        (string Name, Badge Badge)[] icons = [("gistdrive", Badge.Code), ("secret", Badge.Lock), ("public", Badge.Globe)];
        foreach (var (name, badge) in icons)
        {
            File.WriteAllBytes(Path.Combine(output, $"{name}.ico"), Ico([.. Sizes.Select(size => Render(size, badge))]));
        }

        File.WriteAllBytes(Path.Combine(output, "gistdrive.png"), Png(Render(256, Badge.Code)));
        File.WriteAllBytes(Path.Combine(output, "preview.png"), Png(Preview([.. icons.Select(i => i.Badge)])));
        Console.WriteLine($"icons written to {output}");
    }

    private static BitmapSource Render(int size, Badge badge)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));
            Draw(dc, badge, small: size <= 24);
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // Everything is drawn in a 256 px design space and scaled down.
    private static void Draw(DrawingContext dc, Badge badge, bool small)
    {
        dc.DrawGeometry(Brush("#E0A631"), null, Geometry.Parse("M 16,62 Q 16,40 38,40 L 92,40 Q 104,40 112,50 L 122,62 L 218,62 Q 240,62 240,84 L 240,196 Q 240,216 220,216 L 36,216 Q 16,216 16,196 Z"));
        dc.DrawGeometry(Brush("#FFC83D"), null, Geometry.Parse("M 16,98 Q 16,84 30,84 L 226,84 Q 240,84 240,98 L 240,196 Q 240,216 220,216 L 36,216 Q 16,216 16,196 Z"));

        // The badge grows on the small sizes, where its glyph would blur into a dot.
        var center = small ? new Point(172, 168) : new Point(184, 176);
        var radius = small ? 84.0 : 62.0;
        var scale = radius / 62.0;
        var color = badge switch
        {
            Badge.Code => "#1F6FEB",
            Badge.Lock => "#24292F",
            _ => "#1A7F37",
        };
        dc.DrawEllipse(Brush(color), new Pen(Brushes.White, 8 * scale), center, radius, radius);

        // The glyphs are drawn around (184, 176) and moved onto the badge.
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, center.X - (184 * scale), center.Y - (176 * scale)));
        var stroke = new Pen(Brushes.White, small ? 14 : 11) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        switch (badge)
        {
            case Badge.Code:
                dc.DrawGeometry(null, stroke, Geometry.Parse("M 164,154 L 144,176 L 164,198 M 204,154 L 224,176 L 204,198 M 192,148 L 176,204"));
                break;
            case Badge.Lock:
                dc.DrawGeometry(null, stroke, Geometry.Parse("M 168,176 L 168,160 A 16,16 0 0 1 200,160 L 200,176"));
                dc.DrawRoundedRectangle(Brushes.White, null, new Rect(156, 170, 56, 40), 7, 7);
                break;
            case Badge.Globe:
                var thin = new Pen(Brushes.White, small ? 11 : 8);
                dc.DrawEllipse(null, thin, new Point(184, 176), 34, 34);
                dc.DrawEllipse(null, thin, new Point(184, 176), 14, 34);
                dc.DrawLine(thin, new Point(150, 176), new Point(218, 176));
                break;
        }

        dc.Pop();
    }

    /// <summary>
    /// Every size of every icon at actual size on light and dark, then 16, 24 and 32 px blown up
    /// four times, so the small sizes can be judged pixel by pixel.
    /// </summary>
    private static RenderTargetBitmap Preview(Badge[] badges)
    {
        const int gap = 12;
        int[] magnified = [16, 24, 32];
        var width = gap + Sizes.Sum(s => s + gap) + magnified.Sum(s => (s * 4) + gap);
        var rowHeight = 256 + (2 * gap);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            for (var row = 0; row < badges.Length * 2; row++)
            {
                var badge = badges[row / 2];
                var top = row * rowHeight;
                dc.DrawRectangle(Brush(row % 2 == 0 ? "#FFFFFF" : "#202020"), null, new Rect(0, top, width, rowHeight));
                double x = gap;
                foreach (var size in Sizes)
                {
                    dc.DrawImage(Render(size, badge), new Rect(x, top + gap, size, size));
                    x += size + gap;
                }

                foreach (var size in magnified)
                {
                    dc.DrawImage(Render(size, badge), new Rect(x, top + gap, size * 4, size * 4));
                    x += (size * 4) + gap;
                }
            }
        }

        var bitmap = new RenderTargetBitmap(width, rowHeight * badges.Length * 2, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static byte[] Png(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    // An .ico holds a PNG for 256 px and classic 32-bit DIBs below, which every part of the shell reads.
    private static byte[] Ico(IReadOnlyList<BitmapSource> images)
    {
        var payloads = images.Select(i => i.PixelWidth >= 256 ? Png(i) : Dib(i)).ToList();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);
        var offset = 6 + (16 * images.Count);
        for (var i = 0; i < images.Count; i++)
        {
            var size = images[i].PixelWidth;
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(payloads[i].Length);
            writer.Write(offset);
            offset += payloads[i].Length;
        }

        foreach (var payload in payloads) writer.Write(payload);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] Dib(BitmapSource image)
    {
        var size = image.PixelWidth;
        var stride = size * 4;
        var pixels = new byte[stride * size];
        new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, stride, 0);
        var maskStride = (size + 31) / 32 * 4;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(40);
        writer.Write(size);
        writer.Write(size * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write((stride * size) + (maskStride * size));
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        for (var row = size - 1; row >= 0; row--) writer.Write(pixels, row * stride, stride);
        writer.Write(new byte[maskStride * size]);
        writer.Flush();
        return stream.ToArray();
    }
}
