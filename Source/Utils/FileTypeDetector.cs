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
    static readonly HashSet<string> extensionsAudio = [
        "aac",
        "ac3", "eac3", "ec3",
        "aiff", "aif", "aifc",
        "amr",
        "flac",
        "m4a",
        "mp1", "mp2", "mp3" ,
        "ogg",
        "opus",
        "wav",
        "wma",
    ];
    static readonly HashSet<string> extensionsImage = [
        "bmp", "dib",
        "cur",
        "exr",
        "gif",
        "ico",
        "jpg", "jpeg", "jpe", "jfif", "jif",
        "pbm", "pgm", "ppm", "pnm",
        "png",
        "qoi",
        "tga", "tpic",
        "tif", "tiff",
        "webp",
    ];
    static readonly HashSet<string> extensionsVideo = [
        "3gp", "3g2",
        "asf",
        "avi",
        "flv", "f4v",
        "h264", "264", "avc",
        "h265", "265", "hevc",
        "mp4", "mpg", "mpeg", "m1v", "m2v", "m4v", "mpv",
        "mov", "qt",
        "mkv",
        "ogv", "ogm",
        "ts",
        "vob",
        "webm",
        "wmv",
    ];

    static readonly int extentionsMinLength = extensionsAudio.Concat(extensionsImage).Concat(extensionsVideo)
        .Min(s => s.Length);
    static readonly int extentionsMaxLength = extensionsAudio.Concat(extensionsImage).Concat(extensionsVideo)
        .Max(s => s.Length);

    public static FileCategory DetectCategoryFromExtension(string path)
    {
        int index = path.LastIndexOf('.');
        if (index < 0)
        {
            return FileCategory.Unknown;
        }
        ReadOnlySpan<char> extSpan = path.AsSpan(index + 1);
        if (extSpan.Length < extentionsMinLength || extSpan.Length > extentionsMaxLength)
        {
            return FileCategory.Unknown;
        }

        Span<char> extSpanLower = stackalloc char[extSpan.Length];
        extSpan.ToLowerInvariant(extSpanLower);
        string extLower = new(extSpan);
        return
            extensionsAudio.Contains(extLower) ? FileCategory.Audio :
            extensionsImage.Contains(extLower) ? FileCategory.Image :
            extensionsVideo.Contains(extLower) ? FileCategory.Video :
            FileCategory.Unknown;
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
