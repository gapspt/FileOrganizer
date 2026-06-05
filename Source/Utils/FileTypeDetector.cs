namespace FileOrganizer;

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
            "aac" or
            "ac3" or "eac3" or "ec3" or
            "aiff" or "aif" or "aifc" or
            "amr" or
            "flac" or
            "m4a" or
            "mp1" or "mp2" or "mp3" or
            "ogg" or
            "opus" or
            "wav" or
            "wma"
            => FileCategory.Audio,

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

            "3gp" or "3g2" or
            "asf" or
            "avi" or
            "flv" or "f4v" or
            "h264" or "264" or "avc" or
            "h265" or "265" or "hevc" or
            "mp4" or "mpg" or "mpeg" or "m1v" or "m2v" or "m4v" or "mpv" or
            "mov" or "qt" or
            "mkv" or
            "ogv" or "ogm" or
            "ts" or
            "vob" or
            "webm" or
            "wmv"
            => FileCategory.Video,

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
                FileCategory.Audio => MediaUtils.IsAudio(path),
                FileCategory.Image => MediaUtils.IsImage(path),
                FileCategory.Video => MediaUtils.IsVideo(path),
                _ => new(false),
            };
        }
        catch
        {
            return new(false);
        }
    }
}
