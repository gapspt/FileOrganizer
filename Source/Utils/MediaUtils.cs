using System.Diagnostics;
using MediaInfo;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FileOrganizer;

public static class MediaUtils
{
    #region Audio

    public static ValueTask<bool> IsAudio(string path)
    {
        try
        {
            MediaInfoWrapper media = new(path);
            return new(media.AudioStreams.Count > 0 && !media.HasVideo);
        }
        catch
        {
            return new(false);
        }
    }

    #endregion Audio

    #region Image

    public static async ValueTask<bool> IsImage(string path)
    {
        try
        {
            var format = await Image.DetectFormatAsync(path);
            return format != null;
        }
        catch
        {
            return false;
        }
    }

    public static async ValueTask GetImageResized(string path, Rgba32[] pixels, ResizeOptions resizeOptions)
    {
        using var image = await Image.LoadAsync<Rgba32>(path);
        image.Mutate(ctx => ctx
            .AutoOrient()
            .Resize(resizeOptions));
        image.CopyPixelDataTo(pixels);
    }

    public static async ValueTask CreatePngImageFile(string path, int width, int height, Rgba32[] pixels)
    {
        using var image = Image.LoadPixelData(pixels, width, height);
        await image.SaveAsPngAsync(path);
    }

    public static bool ArePixelsSimilar(Rgba32[] img1, Rgba32[] img2, int margin)
    {
        Debug.Assert(img1.Length == img2.Length);
        for (int i = 0, len = img1.Length; i < len; i++)
        {
            Rgba32 p1 = img1[i];
            Rgba32 p2 = img2[i];

            int r = p1.R - p2.R;
            int g = p1.G - p2.G;
            int b = p1.B - p2.B;
            int a = p1.A - p2.A;

            if (((r < 0 ? -r : r) + (g < 0 ? -g : g) + (b < 0 ? -b : b) + (a < 0 ? -a : a)) > margin)
            {
                return false;
            }
        }

        return true;
    }

    #endregion Image

    #region Video

    public static ValueTask<bool> IsVideo(string path)
    {
        try
        {
            MediaInfoWrapper media = new(path);
            return new(media.HasVideo);
        }
        catch
        {
            return new(false);
        }
    }

    #endregion Video
}
