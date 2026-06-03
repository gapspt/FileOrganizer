using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PhotoOrganizer
{
    public static class ImageProcessor
    {
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
    }
}
