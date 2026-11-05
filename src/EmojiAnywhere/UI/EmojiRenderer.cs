using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice.WIC;
using D2DPixelFormat = Vortice.DCommon.PixelFormat;
using WicPixelFormat = Vortice.WIC.PixelFormat;

namespace EmojiAnywhere.UI;

// Draws color emoji with Windows' own text engine (Direct2D + DirectWrite).
// WPF can only draw emoji in black and white, so each emoji is drawn once into a small
// bitmap and cached. Uses the emoji font already on the PC, nothing is bundled.
// UI thread only.
public static class EmojiRenderer
{
    public const int Size = 64; // pixels; WPF scales it down smoothly to the row size

    private static ID2D1Factory1? _d2d;
    private static IDWriteFactory? _dwrite;
    private static IWICImagingFactory? _wic;
    private static IDWriteTextFormat? _format;
    private static readonly Dictionary<string, BitmapSource> _cache = new();
    private static readonly LinkedList<string> _order = new();
    private const int MaxCached = 300; // about 5 MB at most

    public static ImageSource? Get(string emoji)
    {
        if (_cache.TryGetValue(emoji, out var hit)) return hit;
        try
        {
            var bmp = Draw(emoji);
            _cache[emoji] = bmp;
            _order.AddLast(emoji);
            if (_order.Count > MaxCached) { _cache.Remove(_order.First!.Value); _order.RemoveFirst(); }
            return bmp;
        }
        catch
        {
            return null; // caller falls back to plain text
        }
    }

    private static BitmapSource Draw(string emoji)
    {
        _d2d ??= D2D1.D2D1CreateFactory<ID2D1Factory1>();
        _dwrite ??= DWrite.DWriteCreateFactory<IDWriteFactory>();
        _wic ??= new IWICImagingFactory();
        if (_format == null)
        {
            _format = _dwrite.CreateTextFormat("Segoe UI Emoji", FontWeight.Normal, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, Size * 0.72f);
            _format.TextAlignment = TextAlignment.Center;
            _format.ParagraphAlignment = ParagraphAlignment.Center;
        }

        using var bitmap = _wic.CreateBitmap(Size, Size, WicPixelFormat.Format32bppPBGRA, BitmapCreateCacheOption.CacheOnLoad);
        var props = new RenderTargetProperties(new D2DPixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied));
        using var target = _d2d.CreateWicBitmapRenderTarget(bitmap, props);
        using var dc = target.QueryInterface<ID2D1DeviceContext>();
        using var brush = dc.CreateSolidColorBrush(new Color4(0, 0, 0, 1));

        dc.BeginDraw();
        dc.Clear(new Color4(0, 0, 0, 0));
        dc.DrawText(emoji, _format, new Rect(0, 0, Size, Size), brush, DrawTextOptions.EnableColorFont);
        dc.EndDraw();

        int stride = Size * 4;
        var pixels = new byte[stride * Size];
        bitmap.CopyPixels((uint)stride, pixels);
        var src = BitmapSource.Create(Size, Size, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        src.Freeze();
        return src;
    }
}
