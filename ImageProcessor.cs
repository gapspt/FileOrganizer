using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PhotoOrganizer
{
    public class ImageProcessor
    {
        public ResizeOptions ThumbnailResizeOptions = new() { Mode = ResizeMode.Stretch, Size = new(8, 8) };

        public async ValueTask GetImageThumbnail(string path, Rgba32[] pixels)
        {
            using var image = await Image.LoadAsync<Rgba32>(path);
            image.Mutate(ctx => ctx
                .AutoOrient()
                .Resize(ThumbnailResizeOptions));
            image.CopyPixelDataTo(pixels);
        }

        public static async ValueTask CreateImageFile(string path, int width, int height, Rgba32[] pixels)
        {
            using var image = Image.LoadPixelData(pixels, width, height);
            await image.SaveAsPngAsync(path);
        }
    }
}
