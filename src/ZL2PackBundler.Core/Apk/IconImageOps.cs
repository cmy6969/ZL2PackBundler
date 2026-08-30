using SkiaSharp;

namespace ZL2PackBundler.Core.Apk;

/// <summary>图标图像处理（SkiaSharp）：解码、中心裁剪、按目标尺寸缩放、PNG/WebP 编码。</summary>
public static class IconImageOps
{
    /// <summary>只读图像尺寸（不解码像素）。失败返回 (0,0)。</summary>
    public static (int Width, int Height) GetDimensions(byte[] bytes)
    {
        try
        {
            using var codec = SKCodec.Create(new MemoryStream(bytes));
            return codec == null ? (0, 0) : (codec.Info.Width, codec.Info.Height);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>验证图标可解码（支持 PNG/JPG/WebP/BMP 等常见格式）。</summary>
    public static void Validate(byte[] bytes)
    {
        using var bitmap = SKBitmap.Decode(bytes)
            ?? throw new InvalidDataException("无法解码图标图片。请使用 PNG/JPG/WebP 等常见格式。");
        if (bitmap.Width < 1 || bitmap.Height < 1)
            throw new InvalidDataException("图标图片尺寸无效。");
    }

    /// <summary>中心裁剪为正方形，缩放到目标尺寸，按目标格式编码（webp=true → WebP，否则 PNG）。</summary>
    public static byte[] ResizeSquare(byte[] srcBytes, int targetWidth, int targetHeight, bool webp)
    {
        using var src = SKBitmap.Decode(srcBytes)
            ?? throw new InvalidDataException("无法解码图标图片。请使用 PNG/JPG/WebP 等常见格式。");
        if (src.Width < 1 || src.Height < 1)
            throw new InvalidDataException("图标图片尺寸无效。");
        if (targetWidth < 1 || targetHeight < 1)
            throw new InvalidDataException("目标图标尺寸无效。");

        var side = Math.Min(src.Width, src.Height);
        var left = (src.Width - side) / 2;
        var top = (src.Height - side) / 2;

        using var cropped = new SKBitmap(side, side, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(cropped))
        {
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
            canvas.DrawBitmap(src,
                new SKRect(left, top, left + side, top + side),
                new SKRect(0, 0, side, side), paint);
        }

        using var resized = cropped.Resize(
                new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul),
                SKFilterQuality.High)
            ?? throw new InvalidDataException("图标缩放失败。");

        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(webp ? SKEncodedImageFormat.Webp : SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
