namespace FileOrganizer
{
    public enum FileCategory
    {
        Unknown = 0,
        Audio,
        Image,
        Video,
    }

    public static class FileTypeDetector
    {
        public static FileCategory DetectCategoryFromExtension(string path)
        {
            int index = path.LastIndexOf('.');
            if (index < 0)
            {
                return FileCategory.Unknown;
            }
            ReadOnlySpan<char> ext = path.AsSpan(index + 1);

            return ext switch
            {
                /* // NOT SUPPORTED YET
                "mp3" or
                "wav" or
                "flac" or
                "aac" or
                "m4a" or
                "ogg" or
                "wma"
                => FileCategory.Audio,
                */

                "bmp" or "dib" or
                "cur" or
                "exr" or
                "gif" or
                "ico" or
                "jpg" or "jpeg" or "jpe" or "jfif" or "jif" or
                "pbm" or "pgm" or "ppm" or "pnm" or
                "png" or
                "qoi" or
                "tga" or "tpic" or
                "tif" or "tiff" or
                "webp"
                => FileCategory.Image,

                /* // NOT SUPPORTED YET
                "mp4" or
                "mov" or
                "mkv" or
                "avi" or
                "webm" or
                "wmv" or
                "flv"
                => FileCategory.Video,
                */

                _ => FileCategory.Unknown,
            };
        }

        public static async ValueTask<FileCategory> DetectCategoryFromContent(string path)
        {
            var category = DetectCategoryFromExtension(path);
            if (category != FileCategory.Unknown && await VerifyCategoryFromContent(path, category))
            {
                return category;
            }

            foreach (var c in Enum.GetValues<FileCategory>())
            {
                if (c != FileCategory.Unknown && c != category && await VerifyCategoryFromContent(path, c))
                {
                    return c;
                }
            }

            return FileCategory.Unknown;
        }

        public static ValueTask<bool> VerifyCategoryFromContent(string path, FileCategory category)
        {
            try
            {
                return category switch
                {
                    FileCategory.Image => ImageProcessor.IsImage(path),
                    _ => new(false),
                };
            }
            catch
            {
                return new(false);
            }
        }
    }
}
