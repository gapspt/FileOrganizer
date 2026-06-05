using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FileOrganizer;

class OrganizeCommand
{
    enum OrganizationCategory : byte
    {
        Unknown = 0,
        AudioMusic,
        AudioRecording,
        AudioRingtone,
        AudioUnknown,
        ImageDocument,
        ImageMeme,
        ImagePhoto,
        ImageScreenshot,
        ImageWallpaper,
        ImageUnknown,
        VideoCamera,
        VideoScreenRecording,
        VideoUnknown,
    }

    struct OrganizationDetails
    {
        public OrganizationCategory category;
        public short year;
        public byte month;
        public byte day;
        public byte hour;
        public byte minute;
        public byte second;
        public ushort millisecond;
        public string? platform;
        public string? author;

        public readonly bool HasDate => year != 0;
    }

    readonly string srcDirPath;
    readonly string dstDirPath;
    readonly bool recursive;
    readonly bool dryRun;

    public OrganizeCommand(string srcDirPath, string dstDirPath, bool recursive, bool dryRun)
    {
        this.srcDirPath = srcDirPath;
        this.dstDirPath = dstDirPath;
        this.recursive = recursive;
        this.dryRun = dryRun;
    }

    public async Task<int> Run()
    {
        if (!Directory.Exists(dstDirPath))
        {
            Console.Error.WriteLine($"Destination does not exist or is not a directory: '{dstDirPath}'");
            return -1;
        }

        await FileUtils.ApplyToAllFilesAsync(srcDirPath, ProcessFile, recursive);

        return 0;
    }

    async ValueTask ProcessFile(FileInfo file)
    {
        static string GetDirectoryWithYear(in OrganizationDetails details, string? parentDir = null)
        {
            return Path.Join(parentDir, details.HasDate ? $"{details.year:D4}" : $"Unknown");
        }

        string origPath = file.FullName;

        try
        {
            string origFileName = file.Name;

            string fileName = origFileName;
            string subDirPath;
            bool preserveDirStructure = true;

            var details = await GetOrganizationDetails(origPath, fileName);

            if (details.platform != null || details.author != null)
            {
                // TODO: Choose what to do with files from Facebook, WhatsApp, Google Drive, etc.
                Console.WriteLine($"Skipping file from {details.platform}/{details.author}: '{origPath}'");
                return;
            }

            ReadOnlySpan<char> origExtension = Path.GetExtension(origFileName.AsSpan());

            switch (details.category)
            {
                case OrganizationCategory.AudioMusic:
                    subDirPath = "Music";
                    break;
                case OrganizationCategory.AudioRecording:
                    subDirPath = GetDirectoryWithYear(details, "AudioRecordings");
                    preserveDirStructure = false;
                    // TODO: Change the fileName
                    break;
                case OrganizationCategory.AudioRingtone:
                    subDirPath = "Ringtones";
                    break;
                case OrganizationCategory.ImageDocument:
                    subDirPath = "Documents";
                    break;
                case OrganizationCategory.ImageMeme:
                    subDirPath = "Memes";
                    break;
                case OrganizationCategory.ImagePhoto or OrganizationCategory.VideoCamera:
                    subDirPath = GetDirectoryWithYear(details, "Camera");
                    preserveDirStructure = false;
                    string prefix = details.category == OrganizationCategory.ImagePhoto ? "IMG_" : "VID_";
                    fileName =
                        $"{prefix}{details.year:D4}{details.month:D2}{details.day:D2}_{details.hour:D2}{details.minute:D2}{details.second:D2}{details.millisecond:D3}{origExtension}";
                    break;
                case OrganizationCategory.ImageScreenshot or OrganizationCategory.VideoScreenRecording:
                    subDirPath = GetDirectoryWithYear(details, "ScreenCaptures");
                    // TODO: Change the fileName
                    break;
                case OrganizationCategory.ImageWallpaper:
                    subDirPath = "Wallpapers";
                    break;
                case OrganizationCategory.AudioUnknown:
                    subDirPath = "AudioOther";
                    break;
                case OrganizationCategory.ImageUnknown:
                    subDirPath = "ImagesOther";
                    break;
                case OrganizationCategory.VideoUnknown:
                    subDirPath = "VideosOther";
                    break;
                default:
#if DEBUG
                    Console.WriteLine($"Skipping unknown file type: '{origPath}'");
#endif
                    return;
            }

            if (origExtension.Length < 1 && fileName != origFileName)
            {
                Console.Error.WriteLine(
                    $"Illegal attempt to change the name of a file from '{origFileName}' to '{fileName}' (file names without extensions must not change): '{origPath}'");
                Debug.Assert(false);
                return;
            }

            if (preserveDirStructure)
            {
                string origDirPath = Path.GetDirectoryName(origPath) ?? srcDirPath;
                string originalSubDirPath = Path.GetRelativePath(srcDirPath, origDirPath);
                subDirPath = originalSubDirPath.StartsWith(subDirPath) ?
                    originalSubDirPath :
                    Path.Join(subDirPath, originalSubDirPath);
            }

            string newDirPath = Path.Join(dstDirPath, subDirPath);
            string newPath = Path.Join(newDirPath, fileName);

            if (FileUtils.PathsReferToSameLocation(newPath, origPath))
            {
#if DEBUG
                Console.WriteLine($"Skipping file already in correct location: '{origPath}'");
#endif
                return;
            }

            if (Path.Exists(newPath))
            {
                Console.Error.WriteLine($"Another file already exists at destination: '{newPath}'");
                return;
            }

            if (!dryRun)
            {
                Directory.CreateDirectory(newDirPath);
                file.MoveTo(newPath, false);
            }
            Console.WriteLine($"Moved file '{origPath}' to '{newPath}'");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error organizing file '{origPath}': {ex.Message}");
            return;
        }
    }

    static readonly Regex regexPrefixUndDateUndTimeMillisExt = new(@"\A[a-zA-Z0-9]+_\d{8}_\d{9}\.[a-zA-Z0-9]+\z");

    async ValueTask<OrganizationDetails> GetOrganizationDetails(string origPath, string fileName)
    {
        OrganizationDetails result = new();

        // TODO: Detect files that are from Facebook, WhatsApp, Google Drive, etc., and fill in the plarform and author
        result.platform = result.author = null;

        if (IsMatch(regexPrefixUndDateUndTimeMillisExt, fileName))
        {
            int indexUnd = fileName.IndexOf('_');
            int indexExt = fileName.LastIndexOf('.');
            Debug.Assert(indexUnd > 0);
            Debug.Assert(indexExt > 0);
            ReadOnlySpan<char> prefix = fileName.AsSpan(0, indexUnd);
            ReadOnlySpan<char> ext = fileName.AsSpan(indexExt + 1);

            result.category = ext switch
            {
                "jpg" => prefix switch
                {
                    "PXL" => OrganizationCategory.ImagePhoto, // Photo taken with Google Pixel
                    _ => default,
                },

                _ => default,
            };

            if (result.category != default)
            {
                ReadOnlySpan<char> dateTimeMillisStr = fileName.AsSpan(indexUnd + 1);

                result.year = short.Parse(dateTimeMillisStr[0..4]);             // 4 digits
                result.month = byte.Parse(dateTimeMillisStr[4..6]);             // 2 digits
                result.day = byte.Parse(dateTimeMillisStr[6..8]);               // 2 digits
                // Underscore                                                   // 1 character
                result.hour = byte.Parse(dateTimeMillisStr[9..11]);             // 2 digits
                result.minute = byte.Parse(dateTimeMillisStr[11..13]);          // 2 digits
                result.second = byte.Parse(dateTimeMillisStr[13..15]);          // 2 digits
                result.millisecond = ushort.Parse(dateTimeMillisStr[15..18]);   // 3 digits
            }
        }

        if (result.category == default)
        {
            var fileCategory = await FileTypeDetector.DetectCategoryFromContent(origPath);
            result.category = fileCategory switch
            {
                FileCategory.Audio => OrganizationCategory.AudioUnknown,
                FileCategory.Image => OrganizationCategory.ImageUnknown,
                FileCategory.Video => OrganizationCategory.VideoUnknown,
                _ => default,
            };
        }

        if (!result.HasDate && result.category != default && MetadataUtils.TryGetDateTaken(origPath, out var dt))
        {
            result.year = (short)dt.Year;
            result.month = (byte)dt.Month;
            result.day = (byte)dt.Day;
            result.hour = (byte)dt.Hour;
            result.minute = (byte)dt.Minute;
            result.second = (byte)dt.Second;
            result.millisecond = (ushort)dt.Millisecond;
        }

        return result;
    }

    static bool IsMatch(Regex r, string s)
    {
        var m = r.Match(s);
        return m.Success && m.Length == s.Length;
    }
}
