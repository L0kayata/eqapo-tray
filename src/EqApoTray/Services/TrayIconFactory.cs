using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EqApoTray.Services;

public static partial class TrayIconFactory
{
    public static System.Drawing.Icon Create()
    {
        const int size = 32;

        // 1. Render the icon as a WPF visual.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new SolidColorBrush(Color.FromRgb(30, 144, 255));
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(size / 2.0, size / 2.0), size / 2.0 - 1, size / 2.0 - 1);

            var typeface = new Typeface(new FontFamily("Segoe UI"),
                FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var ft = new FormattedText(
                "dB",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                14,
                Brushes.White,
                pixelsPerDip: 1.0);
            dc.DrawText(ft, new Point((size - ft.Width) / 2.0, (size - ft.Height) / 2.0));
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        // 2. Encode to PNG so we can hand the pixels to GDI+.
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;

        // 3. Build a System.Drawing.Icon. GetHicon() allocates an unmanaged HICON
        //    that Icon.FromHandle does NOT own; Clone() copies it into a managed
        //    Icon, then we destroy the original handle to avoid a leak.
        using var gdiBmp = new System.Drawing.Bitmap(ms);
        var hicon = gdiBmp.GetHicon();
        try
        {
            using var unowned = System.Drawing.Icon.FromHandle(hicon);
            return (System.Drawing.Icon)unowned.Clone();
        }
        finally
        {
            DestroyIcon(hicon);
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr hIcon);
}
