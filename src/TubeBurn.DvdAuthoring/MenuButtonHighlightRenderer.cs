using TubeBurn.Domain;

namespace TubeBurn.DvdAuthoring;

/// <summary>
/// Generates a 2-bit-per-pixel highlight bitmap for DVD menu button overlays.
/// Color 0 = transparent (background), Color 1 = button border outline.
/// The DVD player uses PCI BTNI color overrides for normal/selected/activated states.
/// </summary>
public static class MenuButtonHighlightRenderer
{
    private const int BorderThickness = 3;

    /// <summary>
    /// Renders a highlight bitmap for the given buttons.
    /// </summary>
    /// <param name="buttons">Button definitions with coordinates.</param>
    /// <param name="standard">Video standard (determines frame height).</param>
    /// <returns>A byte array of width*height pixels, each 0 or 1.</returns>
    public static byte[] Render(IReadOnlyList<MenuButton> buttons, VideoStandard standard)
    {
        ArgumentNullException.ThrowIfNull(buttons);

        var width = 720;
        var height = standard == VideoStandard.Ntsc ? 480 : 576;
        var bitmap = new byte[width * height];

        // Apply PAR compensation to match SkiaMenuRenderer's pre-squeezed visual layout.
        // Menus are 16:9 anamorphic; NTSC PAR = 40:33, PAL PAR = 64:45.
        var parScale = standard == VideoStandard.Ntsc ? 33f / 40f : 45f / 64f;
        var parOffset = 720f * (1f - parScale) / 2f;

        foreach (var button in buttons)
        {
            var px = (int)(parOffset + button.X * parScale);
            var pw = (int)(button.Width * parScale);
            DrawButtonBorder(bitmap, width, height, px, button.Y, pw, button.Height);
        }

        return bitmap;
    }

    public static int GetWidth() => 720;

    public static int GetHeight(VideoStandard standard) =>
        standard == VideoStandard.Ntsc ? 480 : 576;

    private static void DrawButtonBorder(byte[] bitmap, int bitmapWidth, int bitmapHeight,
        int x, int y, int w, int h)
    {
        var x2 = Math.Min(x + w, bitmapWidth);
        var y2 = Math.Min(y + h, bitmapHeight);
        x = Math.Max(x, 0);
        y = Math.Max(y, 0);

        // Top border
        for (var row = y; row < Math.Min(y + BorderThickness, y2); row++)
            for (var col = x; col < x2; col++)
                bitmap[row * bitmapWidth + col] = 1;

        // Bottom border
        for (var row = Math.Max(y2 - BorderThickness, y); row < y2; row++)
            for (var col = x; col < x2; col++)
                bitmap[row * bitmapWidth + col] = 1;

        // Left border
        for (var row = y; row < y2; row++)
            for (var col = x; col < Math.Min(x + BorderThickness, x2); col++)
                bitmap[row * bitmapWidth + col] = 1;

        // Right border
        for (var row = y; row < y2; row++)
            for (var col = Math.Max(x2 - BorderThickness, x); col < x2; col++)
                bitmap[row * bitmapWidth + col] = 1;
    }
}
