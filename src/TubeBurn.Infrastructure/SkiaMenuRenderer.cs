using System.Diagnostics;
using SkiaSharp;
using TubeBurn.Domain;

namespace TubeBurn.Infrastructure;

/// <summary>
/// Renders DVD menu backgrounds using SkiaSharp, then encodes to MPEG-2 PS via ffmpeg.
/// Two distinct visual designs:
///   Level 1 (ChannelSelect): white background, circle avatars, dark text, thin borders
///   Level 2 (VideoSelect): banner background with blur + dark overlay, thumbnails, white text
/// </summary>
public static class SkiaMenuRenderer
{
    private const string DefaultFontFamily = "Open Sans Condensed SemiBold";
    private const string FallbackFontFamily = "Arial";
    private const string Ellipsis = "\u2026"; // …

    // Level 1 (Channel Select) colors
    private static readonly SKColor L1Background = SKColors.White;
    private static readonly SKColor L1TextColor = SKColor.Parse("#1B2442");
    private static readonly SKColor L1BorderColor = SKColor.Parse("#CCCCCC");
    private static readonly SKColor L1HeaderColor = SKColor.Parse("#333333");

    // Level 2 (Video Select) colors
    private static readonly SKColor L2FallbackBg = SKColor.Parse("#1B2442");
    private static readonly SKColor L2OverlayColor = new(0, 0, 0, 160); // semi-transparent black
    private static readonly SKColor L2TextColor = SKColors.White;

    // Avatar/thumbnail sizes
    private const int AvatarDiameter = 40;
    private const int ThumbnailWidth = 80;
    private const int ThumbnailHeight = 45;

    public static async Task<string> RenderAsync(
        string ffmpegPath,
        string outputDirectory,
        MenuPage page,
        VideoStandard standard,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        ArgumentNullException.ThrowIfNull(page);

        Directory.CreateDirectory(outputDirectory);

        var width = 720;
        var height = standard == VideoStandard.Ntsc ? 480 : 576;

        var safeName = string.Join("_", page.MenuId.Split(Path.GetInvalidFileNameChars()));
        var pngPath = Path.Combine(outputDirectory, $"menu-{safeName}-p{page.PageNumber}.png");
        var mpgPath = Path.Combine(outputDirectory, $"menu-{safeName}-p{page.PageNumber}.mpg");

        RenderToPng(page, width, height, pngPath);

        var target = standard == VideoStandard.Ntsc ? "ntsc-dvd" : "pal-dvd";
        var args = $"-loop 1 -i \"{pngPath}\" -t 1 -target {target} -aspect 16:9 -an -y \"{mpgPath}\"";
        var success = await RunFfmpegAsync(ffmpegPath, args, cancellationToken);

        if (!success)
            throw new InvalidOperationException($"ffmpeg failed to encode menu PNG to MPEG-2: {pngPath}");

        return mpgPath;
    }

    /// <summary>
    /// Renders menu to PNG bytes (for UI preview without ffmpeg encoding).
    /// Output is stretched to 16:9 display aspect ratio to match how DVD players render it.
    /// </summary>
    public static byte[] RenderPreview(MenuPage page, VideoStandard standard)
    {
        var width = 720;
        var height = standard == VideoStandard.Ntsc ? 480 : 576;

        // Render at DVD storage resolution
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        RenderToCanvas(canvas, page, width, height);

        // Stretch to 16:9 display aspect ratio (PAR 40:33 for NTSC, 16:11 for PAL)
        var displayWidth = standard == VideoStandard.Ntsc
            ? (int)(width * 40.0 / 33.0)  // 873px
            : (int)(width * 16.0 / 11.0); // 1047px
        using var stretched = new SKBitmap(displayWidth, height);
        using var stretchCanvas = new SKCanvas(stretched);
        stretchCanvas.DrawBitmap(bitmap, new SKRect(0, 0, displayWidth, height));

        using var image = SKImage.FromBitmap(stretched);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    private static void RenderToPng(MenuPage page, int width, int height, string outputPath)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        RenderToCanvas(canvas, page, width, height);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(outputPath);
        data.SaveTo(stream);
    }

    // NTSC 16:9 pixel aspect ratio = 40:33 ≈ 1.2121
    // Pre-squeeze horizontally so content looks correct after player stretches to 16:9.
    // Scale factor = 33/40 = 0.825; offset centers the squeezed content in the 720px frame.
    private const float ParScale = 33f / 40f; // 0.825
    private const float ParOffset = 720f * (1f - ParScale) / 2f; // ~63px centering offset

    private static void RenderToCanvas(SKCanvas canvas, MenuPage page, int width, int height)
    {
        // Draw full-frame background before PAR transform
        if (page.Type == MenuPageType.ChannelSelect)
        {
            canvas.Clear(L1Background);
        }
        else
        {
            var bannerBitmap = LoadImage(page.BackgroundImagePath);
            if (bannerBitmap is not null)
            {
                DrawBlurredBackground(canvas, bannerBitmap, width, height);
                bannerBitmap.Dispose();
            }
            else
            {
                canvas.Clear(L2FallbackBg);
            }
            using var overlayPaint = new SKPaint { Color = L2OverlayColor };
            canvas.DrawRect(0, 0, width, height, overlayPaint);
        }

        // Apply PAR compensation: squeeze horizontally, center in frame
        canvas.Save();
        canvas.Translate(ParOffset, 0);
        canvas.Scale(ParScale, 1f);

        if (page.Type == MenuPageType.ChannelSelect)
            RenderChannelSelectContent(canvas, page, width, height);
        else
            RenderVideoSelectContent(canvas, page, width, height);

        canvas.Restore();
    }

    // ── Level 1: Channel Select ──────────────────────────────────────

    private static void RenderChannelSelectContent(SKCanvas canvas, MenuPage page, int width, int height)
    {
        var typeface = ResolveTypeface();

        // Header
        using var headerFont = new SKFont(typeface, 32f);
        using var headerPaint = new SKPaint { Color = L1HeaderColor, IsAntialias = true };
        canvas.DrawText(page.MenuId, 60, 50, SKTextAlign.Left, headerFont, headerPaint);

        // Button rows
        using var labelFont = new SKFont(typeface, 22f);
        using var labelPaint = new SKPaint { Color = L1TextColor, IsAntialias = true };
        using var borderPaint = new SKPaint
        {
            Color = L1BorderColor,
            IsAntialias = true,
            IsStroke = true,
            StrokeWidth = 1,
        };

        foreach (var button in page.Buttons)
        {
            var rowRect = new SKRect(button.X, button.Y, button.X + button.Width, button.Y + button.Height);
            var rowRRect = new SKRoundRect(rowRect, 4, 4);

            // Banner background inside the row (scale-to-fit) with adaptive text color
            var hasBanner = false;
            var isDarkBanner = false;
            var bannerBitmap = LoadImage(button.BannerImagePath);
            if (bannerBitmap is not null)
            {
                hasBanner = true;
                isDarkBanner = IsDarkImage(bannerBitmap);

                canvas.Save();
                canvas.ClipRoundRect(rowRRect);
                DrawScaleToFit(canvas, bannerBitmap, rowRect);
                bannerBitmap.Dispose();

                // Semi-transparent overlay to soften the banner
                var overlayAlpha = (byte)120;
                var overlayColor = isDarkBanner
                    ? new SKColor(0, 0, 0, overlayAlpha)
                    : new SKColor(255, 255, 255, overlayAlpha);
                using var dimPaint = new SKPaint { Color = overlayColor };
                canvas.DrawRect(rowRect, dimPaint);
                canvas.Restore();
            }

            // Row border
            canvas.DrawRoundRect(rowRRect, borderPaint);

            // Circle avatar
            var avatarCx = button.X + 12 + AvatarDiameter / 2f;
            var avatarCy = button.Y + button.Height / 2f;

            var avatarBitmap = LoadImage(button.ThumbnailPath);
            if (avatarBitmap is not null)
            {
                DrawCircleImage(canvas, avatarBitmap, avatarCx, avatarCy, AvatarDiameter / 2f);
                avatarBitmap.Dispose();
            }
            else
            {
                DrawLetterCircle(canvas, button.Label, avatarCx, avatarCy, AvatarDiameter / 2f, typeface);
            }

            // Channel name — adaptive color based on banner brightness
            var textColor = hasBanner && isDarkBanner ? SKColors.White : L1TextColor;
            var shadowColor = hasBanner && isDarkBanner
                ? new SKColor(0, 0, 0, 160)
                : new SKColor(255, 255, 255, 160);

            var textX = button.X + 12 + AvatarDiameter + 12;
            var textY = button.Y + (button.Height + 22f) / 2f;
            var maxTextWidth = button.Width - (12 + AvatarDiameter + 12 + 8); // right padding
            var label = FitText(button.Label, labelFont, maxTextWidth);

            // Drop shadow (inverse of text color)
            using var shadowPaint = new SKPaint { Color = shadowColor, IsAntialias = true };
            canvas.DrawText(label, textX + 1, textY + 1, SKTextAlign.Left, labelFont, shadowPaint);

            using var textPaint = new SKPaint { Color = textColor, IsAntialias = true };
            canvas.DrawText(label, textX, textY, SKTextAlign.Left, labelFont, textPaint);
        }
    }

    // ── Level 2: Video Select ────────────────────────────────────────

    private static void RenderVideoSelectContent(SKCanvas canvas, MenuPage page, int width, int height)
    {
        var typeface = ResolveTypeface();

        // Header: channel name + avatar
        var headerY = 18;
        var avatarBitmap = LoadImage(page.AvatarImagePath);
        var headerTextX = 60f;

        if (avatarBitmap is not null)
        {
            var avatarCx = 38f;
            var avatarCy = headerY + 20f;
            DrawCircleImage(canvas, avatarBitmap, avatarCx, avatarCy, 18f);
            avatarBitmap.Dispose();
            headerTextX = 66f;
        }

        using var headerFont = new SKFont(typeface, 30f);
        using var headerPaint = new SKPaint { Color = L2TextColor, IsAntialias = true };
        canvas.DrawText(page.MenuId, headerTextX, headerY + 30f, SKTextAlign.Left, headerFont, headerPaint);

        // Separator line
        using var linePaint = new SKPaint { Color = new SKColor(255, 255, 255, 80), StrokeWidth = 1, IsAntialias = true };
        canvas.DrawLine(20, 55, width - 20, 55, linePaint);

        // Button rows
        using var labelFont = new SKFont(typeface, 22f);
        using var labelPaint = new SKPaint { Color = L2TextColor, IsAntialias = true };
        using var rowBgPaint = new SKPaint { Color = new SKColor(0, 0, 0, 60), IsAntialias = true };

        var contentLabels = page.Buttons.Select(b => b.Label).Where(l => l.Length > 15).ToList();
        var commonSuffix = FindCommonSuffix(contentLabels);

        foreach (var button in page.Buttons)
        {
            var rowRect = new SKRect(button.X, button.Y, button.X + button.Width, button.Y + button.Height);

            // Semi-transparent row background for contrast
            canvas.DrawRoundRect(rowRect, 3, 3, rowBgPaint);

            var label = button.Label;
            if (commonSuffix.Length > 0 && label.EndsWith(commonSuffix, StringComparison.Ordinal))
                label = label[..^commonSuffix.Length].TrimEnd();

            // Thumbnail (for video buttons, not nav buttons)
            var textX = button.X + 8f;
            var thumbBitmap = LoadImage(button.ThumbnailPath);
            if (thumbBitmap is not null)
            {
                var thumbY = button.Y + (button.Height - ThumbnailHeight) / 2f;
                var thumbRect = new SKRect(button.X + 4, thumbY,
                    button.X + 4 + ThumbnailWidth, thumbY + ThumbnailHeight);
                using var thumbPaint = new SKPaint { IsAntialias = true };
                canvas.DrawBitmap(thumbBitmap, thumbRect, thumbPaint);
                thumbBitmap.Dispose();
                textX = button.X + 4 + ThumbnailWidth + 10;
            }

            // Label text — fit to remaining width
            var maxTextWidth = (button.X + button.Width - 8) - textX;
            label = FitText(label, labelFont, maxTextWidth);
            var textY = button.Y + (button.Height + 22f) / 2f;
            canvas.DrawText(label, textX, textY, SKTextAlign.Left, labelFont, labelPaint);
        }
    }

    // ── Image helpers ────────────────────────────────────────────────

    private static SKBitmap? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            return SKBitmap.Decode(path);
        }
        catch
        {
            return null;
        }
    }

    private static void DrawCircleImage(SKCanvas canvas, SKBitmap image, float cx, float cy, float radius)
    {
        canvas.Save();
        using var clipPath = new SKPath();
        clipPath.AddCircle(cx, cy, radius);
        canvas.ClipPath(clipPath);

        var destRect = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawBitmap(image, destRect, paint);
        canvas.Restore();

        // Circle border
        using var borderPaint = new SKPaint
        {
            Color = new SKColor(200, 200, 200, 180),
            IsAntialias = true,
            IsStroke = true,
            StrokeWidth = 1.5f,
        };
        canvas.DrawCircle(cx, cy, radius, borderPaint);
    }

    private static void DrawLetterCircle(SKCanvas canvas, string label, float cx, float cy, float radius, SKTypeface typeface)
    {
        // Filled circle with first letter
        var letter = label.Length > 0 ? label[..1].ToUpperInvariant() : "?";

        using var circlePaint = new SKPaint { Color = SKColor.Parse("#4A90D9"), IsAntialias = true };
        canvas.DrawCircle(cx, cy, radius, circlePaint);

        using var letterFont = new SKFont(typeface, radius * 1.2f);
        using var letterPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText(letter, cx, cy + radius * 0.4f, SKTextAlign.Center, letterFont, letterPaint);
    }

    /// <summary>
    /// Draws the source bitmap scaled to fill the dest rect (no stretching),
    /// cropping the excess dimension to center the image.
    /// </summary>
    private static void DrawScaleToFit(SKCanvas canvas, SKBitmap source, SKRect dest)
    {
        var destW = dest.Width;
        var destH = dest.Height;
        var scaleX = destW / source.Width;
        var scaleY = destH / source.Height;
        var scale = Math.Max(scaleX, scaleY); // fill (cover), crop excess

        var srcW = destW / scale;
        var srcH = destH / scale;
        var srcX = (source.Width - srcW) / 2f;
        var srcY = (source.Height - srcH) / 2f;
        var srcRect = new SKRect(srcX, srcY, srcX + srcW, srcY + srcH);

        canvas.DrawBitmap(source, srcRect, dest);
    }

    /// <summary>
    /// Samples the image to determine if it's predominantly dark (average luminance &lt; 128).
    /// </summary>
    private static bool IsDarkImage(SKBitmap bitmap)
    {
        // Sample every 16th pixel for speed
        long totalLuminance = 0;
        var sampleCount = 0;
        for (var y = 0; y < bitmap.Height; y += 16)
        {
            for (var x = 0; x < bitmap.Width; x += 16)
            {
                var pixel = bitmap.GetPixel(x, y);
                // Perceived luminance: 0.299R + 0.587G + 0.114B
                totalLuminance += (int)(pixel.Red * 0.299 + pixel.Green * 0.587 + pixel.Blue * 0.114);
                sampleCount++;
            }
        }

        return sampleCount > 0 && totalLuminance / sampleCount < 128;
    }

    private static void DrawBlurredBackground(SKCanvas canvas, SKBitmap source, int width, int height)
    {
        var destRect = new SKRect(0, 0, width, height);

        // Draw with gaussian blur filter — sigma 6 gives a gentle blur, banner still recognizable
        using var blurFilter = SKImageFilter.CreateBlur(6f, 6f);
        using var paint = new SKPaint { IsAntialias = true, ImageFilter = blurFilter };
        canvas.DrawBitmap(source, destRect, paint);
    }

    // ── Typeface resolution ──────────────────────────────────────────

    private static SKTypeface ResolveTypeface(string? fontFamily = null)
    {
        var family = fontFamily ?? DefaultFontFamily;
        var typeface = SKTypeface.FromFamilyName(family);
        if (typeface is null || (family == DefaultFontFamily &&
            typeface.FamilyName.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase)))
        {
            typeface = SKTypeface.FromFamilyName(FallbackFontFamily) ?? SKTypeface.Default;
        }
        return typeface;
    }

    // ── Text helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Truncates text with ellipsis to fit within maxWidthPx, measured using the actual font metrics.
    /// </summary>
    private static string FitText(string text, SKFont font, float maxWidthPx)
    {
        if (maxWidthPx <= 0)
            return Ellipsis;

        if (font.MeasureText(text) <= maxWidthPx)
            return text;

        var ellipsisWidth = font.MeasureText(Ellipsis);

        // Binary search for the longest prefix that fits with ellipsis
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            var candidate = string.Concat(text.AsSpan(0, mid), Ellipsis);
            if (font.MeasureText(candidate) <= maxWidthPx)
                lo = mid;
            else
                hi = mid - 1;
        }

        return lo > 0 ? string.Concat(text.AsSpan(0, lo).TrimEnd(), Ellipsis) : Ellipsis;
    }

    private static string FindCommonSuffix(IReadOnlyList<string> labels)
    {
        if (labels.Count < 2)
            return "";

        var shortest = labels.Min(l => l.Length);
        var commonLen = 0;

        for (var i = 1; i <= shortest; i++)
        {
            var ch = labels[0][^i];
            if (labels.All(l => l[^i] == ch))
                commonLen = i;
            else
                break;
        }

        if (commonLen < 4)
            return "";

        var suffix = labels[0][^commonLen..];

        if (!char.IsWhiteSpace(suffix[0]) && suffix[0] != '-' && suffix[0] != '(')
        {
            var spaceIdx = suffix.IndexOf(' ');
            if (spaceIdx < 0)
                return "";
            suffix = suffix[spaceIdx..];
        }

        return suffix.Length >= 4 ? suffix : "";
    }

    // ── ffmpeg encoding ──────────────────────────────────────────────

    private static async Task<bool> RunFfmpegAsync(string ffmpegPath, string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            return false;

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(stderrTask, stdoutTask);

        return process.ExitCode == 0;
    }
}
