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

    enum TimeComponents : byte
    {
        None = 0,
        Year = 1,
        Month = 2,
        Day = 3,
        Hour = 4,
        Minute = 5,
        Second = 6,
        Millisecond = 7,
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
        public TimeComponents timeComponents;
        public string? platform;
        public string? author;
        public string? suffix;

        public readonly bool HasYear => timeComponents >= TimeComponents.Year;
        public readonly bool HasDay => timeComponents >= TimeComponents.Day;
        public readonly bool HasMonth => timeComponents >= TimeComponents.Month;
        public readonly bool HasHour => timeComponents >= TimeComponents.Hour;
        public readonly bool HasMinute => timeComponents >= TimeComponents.Minute;
        public readonly bool HasSecond => timeComponents >= TimeComponents.Second;
        public readonly bool HasMillisecond => timeComponents >= TimeComponents.Millisecond;
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
            return Path.Join(parentDir, details.HasYear ? $"{details.year:D4}" : $"Unknown");
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
                    fileName = $"{prefix}{details.year:D4}{details.month:D2}{details.day:D2}_" +
                        $"{details.hour:D2}{details.minute:D2}{details.second:D2}{details.millisecond:D3}" +
                        $"{details.suffix}{origExtension}";
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
                Console.WriteLine($"Skipping file '{origPath}': already in the correct location");
#endif
                return;
            }

            if (Path.Exists(newPath))
            {
                if (!await FileUtils.EqualFileContentsAsync(origPath, newPath))
                {
                    Console.Error.WriteLine(
                        $"Unable to move file '{origPath}': another file already exists at destination '{newPath}'");
                    return;
                }

                // Delete duplicate file that would be moved to the same destination of an existing similar file
                bool deleted = dryRun ?
                    true :
                    FileUtils.SafeDeleteIfNotSameFile(origPath, newPath, checkPaths: false);
                if (deleted)
                {
                    Console.WriteLine(
                        $"Deleted duplicate file '{origPath}': the same file already exists at '{newPath}'");
                }
                else
                {
#if DEBUG
                    Console.WriteLine($"Skipping file '{origPath}': already in the correct location");
#endif
                }
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

    static readonly Regex regexPrefixUndDateUndTimeSuffExt =
        new(@"\A[a-zA-Z0-9]+_\d{8}_\d{6}(\d{3})?([_.~-][a-zA-Z0-9_.-]+)?\.[a-zA-Z0-9]+\z");

    async ValueTask<OrganizationDetails> GetOrganizationDetails(string origPath, string fileName)
    {
        OrganizationDetails result = new();

        int indexExt = fileName.LastIndexOf('.');
        ReadOnlySpan<char> ext = indexExt >= 0 ? fileName.AsSpan(indexExt + 1) : [];

        // TODO: Detect files that are from Facebook, WhatsApp, Google Drive, etc., and fill in the plarform and author
        result.platform = result.author = null;

        if (IsMatch(regexPrefixUndDateUndTimeSuffExt, fileName))
        {
            Debug.Assert(indexExt > 0 && ext.Length > 0);

            int indexUnd = fileName.IndexOf('_');
            Debug.Assert(indexUnd > 0);
            ReadOnlySpan<char> prefix = fileName.AsSpan(0, indexUnd);

            bool hasMillisecond = char.IsAsciiDigit(fileName[indexUnd + 16]);
            ReadOnlySpan<char> dateStr = fileName.AsSpan(indexUnd + 1, 8);
            ReadOnlySpan<char> timeStr = fileName.AsSpan(indexUnd + 10, hasMillisecond ? 9 : 6);

            int sufStart = indexUnd + 10 + timeStr.Length;
            ReadOnlySpan<char> suffix = sufStart < indexExt ? fileName.AsSpan(sufStart, indexExt - sufStart) : [];

            switch (ext)
            {
                case "jpg":
                    if (prefix.SequenceEqual("IMG") || prefix.SequenceEqual("PIC") || prefix.SequenceEqual("PXL"))
                    {
                        result.category = OrganizationCategory.ImagePhoto;
                    }
                    break;
                case "mp4":
                    if (prefix.SequenceEqual("IMG") || prefix.SequenceEqual("PXL") || prefix.SequenceEqual("VID"))
                    {
                        result.category = OrganizationCategory.VideoCamera;
                    }
                    break;
            }

            if (result.category != OrganizationCategory.Unknown)
            {
                result.year = short.Parse(dateStr[0..4]);                                       // 4 digits
                result.month = byte.Parse(dateStr[4..6]);                                       // 2 digits
                result.day = byte.Parse(dateStr[6..8]);                                         // 2 digits
                result.hour = byte.Parse(timeStr[0..2]);                                        // 2 digits
                result.minute = byte.Parse(timeStr[2..4]);                                      // 2 digits
                result.second = byte.Parse(timeStr[4..6]);                                      // 2 digits
                result.millisecond = hasMillisecond ? ushort.Parse(timeStr[6..9]) : (ushort)0;  // 3 digits
                result.timeComponents = hasMillisecond ? TimeComponents.Millisecond : TimeComponents.Second;

                result.suffix = suffix.Length > 0 ? new(suffix) : null;
            }
        }

        var fileCategory = await FileTypeDetector.DetectCategoryFromContent(origPath);
        if (result.category == OrganizationCategory.Unknown || !CategoriesMatch(result.category, fileCategory))
        {
            result.category = fileCategory switch
            {
                FileCategory.Audio => OrganizationCategory.AudioUnknown,
                FileCategory.Image => OrganizationCategory.ImageUnknown,
                FileCategory.Video => OrganizationCategory.VideoUnknown,
                _ => default,
            };
        }

        if (!result.HasYear && result.category != OrganizationCategory.Unknown &&
            MetadataUtils.TryGetDateTaken(origPath, out var dt))
        {
            result.year = (short)dt.Year;
            result.month = (byte)dt.Month;
            result.day = (byte)dt.Day;
            result.hour = (byte)dt.Hour;
            result.minute = (byte)dt.Minute;
            result.second = (byte)dt.Second;
            result.millisecond = (ushort)dt.Millisecond;
            result.timeComponents =
                result.millisecond != 0 ? TimeComponents.Millisecond :
                result.second != 0 || result.minute != 0 || result.hour != 0 ? TimeComponents.Second :
                TimeComponents.Day;
        }

        return result;
    }

    static bool CategoriesMatch(OrganizationCategory category, FileCategory fileCategory)
    {
        switch (category)
        {
            case OrganizationCategory.AudioMusic:
            case OrganizationCategory.AudioRecording:
            case OrganizationCategory.AudioRingtone:
            case OrganizationCategory.AudioUnknown:
                return fileCategory == FileCategory.Audio;
            case OrganizationCategory.ImageDocument:
            case OrganizationCategory.ImageMeme:
            case OrganizationCategory.ImagePhoto:
            case OrganizationCategory.ImageScreenshot:
            case OrganizationCategory.ImageUnknown:
            case OrganizationCategory.ImageWallpaper:
                return fileCategory == FileCategory.Image;
            case OrganizationCategory.VideoCamera:
            case OrganizationCategory.VideoScreenRecording:
            case OrganizationCategory.VideoUnknown:
                return fileCategory == FileCategory.Video;
            case OrganizationCategory.Unknown:
                return fileCategory == FileCategory.Unknown;
            default:
                //This should never happen, all the possible enum values must be be handled above
                throw new InvalidOperationException("Program error");
        }
    }

    static bool IsMatch(Regex r, string s)
    {
        var m = r.Match(s);
        return m.Success && m.Length == s.Length;
    }
}
