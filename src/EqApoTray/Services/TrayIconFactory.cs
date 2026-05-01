using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EqApoTray.Services;

public static class TrayIconFactory
{
    public static ImageSource Create()
    {
        const int size = 32;
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

        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }
}
