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
        ImageDocument,
        ImageMeme,
        ImagePhoto,
        ImageScreenshot,
        ImageWallpaper,
        TextHistory,
        VideoCamera,
        VideoScreenRecording,
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
        public string? prefix;
        public string? suffix;

        public readonly bool HasYear => timeComponents >= TimeComponents.Year;
        public readonly bool HasDay => timeComponents >= TimeComponents.Day;
        public readonly bool HasMonth => timeComponents >= TimeComponents.Month;
        public readonly bool HasHour => timeComponents >= TimeComponents.Hour;
        public readonly bool HasMinute => timeComponents >= TimeComponents.Minute;
        public readonly bool HasSecond => timeComponents >= TimeComponents.Second;
        public readonly bool HasMillisecond => timeComponents >= TimeComponents.Millisecond;
    }

    public const string DirAudioRecordings = "AudioRecordings";
    public const string DirCamera = "Camera";
    public const string DirDocuments = "Documents";
    public const string DirHistory = "History";
    public const string DirMemes = "Memes";
    public const string DirMusic = "Music";
    public const string DirRingtones = "Ringtones";
    public const string DirScreenCaptures = "ScreenCaptures";
    public const string DirWallpapers = "Wallpapers";

    public const string DirUnknown = "Unknown";

    static readonly Regex regexYear = new(@"\A(19|20)\d{2}\z");
    static readonly Regex regexMonth = new(@"\A((0?[1-9])|(1[0-2]))\z");
    static readonly Regex regexDay = new(@"\A((0?[1-9])|([12][0-9])|(3[01]))\z");
    static readonly Regex regexDateUndTimeSuffExt = new(@"\A" +
        @"(19|20)\d{2}((0[1-9])|(1[0-2]))(([0-2][0-9])|(3[01]))_" +
        @"(([01][0-9])|(2[0-3]))[0-5][0-9][0-5][0-9](\d{3})?" +
        @"([_.~-][a-zA-Z0-9_.-]+)?" +
        @"\.[a-zA-Z0-9]+" +
        @"\z");
    static readonly Regex regexPrefixUndDateUndTimeSuffExt = new(@"\A" +
        @"[a-zA-Z][a-zA-Z0-9]*_" +
        @"(19|20)\d{2}((0[1-9])|(1[0-2]))(([0-2][0-9])|(3[01]))_" +
        @"(([01][0-9])|(2[0-3]))[0-5][0-9][0-5][0-9](\d{3})?" +
        @"([_.~-][a-zA-Z0-9_.-]+)?" +
        @"\.[a-zA-Z0-9]+" +
        @"\z");
    static readonly Regex regexPrefixHyphDateTimeSuffExt = new(@"\A" +
        @"[a-zA-Z][a-zA-Z0-9]*-" +
        @"(19|20)\d{2}((0[1-9])|(1[0-2]))(([0-2][0-9])|(3[01]))" +
        @"(([01][0-9])|(2[0-3]))[0-5][0-9][0-5][0-9](\d{3})?" +
        @"([_.~-][a-zA-Z0-9_.-]+)?" +
        @"\.[a-zA-Z0-9]+" +
        @"\z");
    static readonly Regex regexPrefixUnd4DigitsSuffExt = new(@"\A" +
        @"[a-zA-Z][a-zA-Z0-9]*_\d{4}" +
        @"([_.~-][a-zA-Z0-9_.-]+)?" +
        @"\.[a-zA-Z0-9]+\z");

    readonly string srcDirPath;
    readonly string dstDirPath;
    readonly bool dryRun;
    readonly int recursionLevels;

    public OrganizeCommand(string srcDirPath, string dstDirPath, bool dryRun, int recursionLevels)
    {
        Debug.Assert(Path.IsPathFullyQualified(srcDirPath));
        Debug.Assert(Path.IsPathFullyQualified(dstDirPath));

        this.srcDirPath = srcDirPath;
        this.dstDirPath = dstDirPath;
        this.dryRun = dryRun;
        this.recursionLevels = recursionLevels;
    }

    public async Task<int> Run()
    {
        if (!Directory.Exists(dstDirPath))
        {
            Console.Error.WriteLine($"Destination does not exist or is not a directory: '{dstDirPath}'");
            return -1;
        }

        await FileUtils.ApplyToAllFilesAsync(srcDirPath, ProcessFile, recursionLevels);

        // Remove empty subdirectories from the source directory
        foreach (var d in Directory.EnumerateDirectories(srcDirPath))
        {
            bool alsoDeleteSubdir = true;
            if (srcDirPath == dstDirPath)
            {
                ReadOnlySpan<char> dirName = Path.GetFileName(srcDirPath.AsSpan());
                switch (dirName)
                {
                    case DirAudioRecordings:
                    case DirCamera:
                    case DirDocuments:
                    case DirHistory:
                    case DirMemes:
                    case DirMusic:
                    case DirRingtones:
                    case DirScreenCaptures:
                    case DirWallpapers:
                        // When using the same source directory as the destination directory,
                        // keep these subdirectories even if they are empty
                        alsoDeleteSubdir = false;
                        break;
                }
            }
            if (!dryRun)
            {
                FileUtils.DeleteAllEmptySubDirectories(d, alsoDeleteSubdir);
            }
        }

        return 0;
    }

    ValueTask ProcessFile(string path, string[] relativePathComponents)
    {
        Debug.Assert(Path.IsPathFullyQualified(path));
        if (!path.StartsWith(srcDirPath))
        {
            Debug.Assert(false);
            throw new InvalidProgramException("Mismatch between file path and base directory path");
        }
        Debug.Assert(relativePathComponents.Length >= 1);

        if (DeleteIfUnnecessaryFile(path, relativePathComponents))
        {
            return ValueTask.CompletedTask;
        }

        var trimmedPathComponents = IgnoreTopDirectories(relativePathComponents, [DirUnknown]);

        // If it is inside a directory, it must be a category directory
        if (trimmedPathComponents.Length >= 2)
        {
            return trimmedPathComponents[0] switch
            {
                DirCamera => ProcessCameraFile(path, relativePathComponents),
                DirHistory => ProcessHistoryFile(path, relativePathComponents),

                // Not implemented yet:
                DirAudioRecordings or
                DirDocuments or
                DirMemes or
                DirMusic or
                DirRingtones or
                DirScreenCaptures or
                DirWallpapers or
                _ => ProcessUnknownFile(path), // Not in a recognized category directory
            };
        }

        // The file is not inside a directory, so we try to detect the category
        GetFileNameAndExtension(relativePathComponents, out string fileName, out ReadOnlySpan<char> fileExtension);
        OrganizationDetails details = GetOrganizationDetails(path, fileName, fileExtension);
        return details.category switch
        {
            OrganizationCategory.ImagePhoto or OrganizationCategory.VideoCamera
                => ProcessCameraFile(path, relativePathComponents, details),
            OrganizationCategory.TextHistory
                => ProcessHistoryFile(path, relativePathComponents, details),

            // Not implemented yet:
            OrganizationCategory.AudioMusic or
            OrganizationCategory.AudioRecording or
            OrganizationCategory.AudioRingtone or
            OrganizationCategory.ImageDocument or
            OrganizationCategory.ImageMeme or
            OrganizationCategory.ImageScreenshot or OrganizationCategory.VideoScreenRecording or
            OrganizationCategory.ImageWallpaper or
            _ => ProcessUnknownFile(path), // Not a recognized category
        };
    }

    bool DeleteIfUnnecessaryFile(string path, string[] relativePathComponents)
    {
        bool shouldDelete = false;
        try
        {
            switch (relativePathComponents[relativePathComponents.Length - 1])
            {
                case ".nomedia":
                    shouldDelete = new FileInfo(path).Length == 0;
                    break;
            }
        }
        catch { }

        if (!shouldDelete)
        {
            return false;
        }

        try
        {
            if (!dryRun)
            {
                File.Delete(path);
            }
            Console.WriteLine($"Deleted unnecessary file '{path}'");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error deleting unnecessary file '{path}': {ex.Message}");
        }
        return true;
    }

    async ValueTask ProcessCameraFile(
        string path, string[] relativePathComponents, OrganizationDetails? organizationDetails = null)
    {
        try
        {
            GetFileNameAndExtension(relativePathComponents, out string fileName, out ReadOnlySpan<char> fileExtension);

            if (fileExtension.Length < 1)
            {
                Program.WriteLineDebug($"Skipping unknown file type: '{path}'");
                return;
            }

            var trimmedPathComponents = IgnoreTopDirectories(relativePathComponents, [DirCamera, DirUnknown]);

            int len = trimmedPathComponents.Length;
            bool shouldProcess = len <= 4 &&
                (len <= 1 || IsMatch(regexYear, trimmedPathComponents[0])) &&
                (len <= 2 || trimmedPathComponents[1] == DirUnknown || IsMatch(regexMonth, trimmedPathComponents[1])) &&
                (len <= 3 || trimmedPathComponents[2] == DirUnknown || IsMatch(regexDay, trimmedPathComponents[2]));

            if (!shouldProcess)
            {
                Program.WriteLineDebug($"Skipping file in custom location: '{path}'");
                return;
            }

            var details = organizationDetails ?? GetOrganizationDetails(path, fileName, fileExtension);

            if (details.category != OrganizationCategory.ImagePhoto &&
                details.category != OrganizationCategory.VideoCamera)
            {
                Program.WriteLineDebug($"Skipping unknown file type: '{path}'");
                return;
            }

            if (details.platform != null || details.author != null)
            {
                // TODO: Choose what to do with files from Facebook, WhatsApp, Google Drive, etc.
                Console.WriteLine($"Skipping file from {details.platform}/{details.author}: '{path}'");
                return;
            }

            Debug.Assert(details.HasSecond);
            string prefix = details.category == OrganizationCategory.ImagePhoto ? "IMG_" : "VID_";
            Span<char> fileExtensionLower = stackalloc char[fileExtension.Length];
            fileExtension.ToLowerInvariant(fileExtensionLower);
            string newFileName = $"{prefix}{details.year:D4}{details.month:D2}{details.day:D2}_" +
                $"{details.hour:D2}{details.minute:D2}{details.second:D2}{details.millisecond:D3}" +
                $"{details.suffix}{fileExtensionLower}";

            // Camera/2020/IMG_20201231_235959999.jpg
            await MoveFileToDestinationDir(path, [DirCamera, GetDirectoryNameFromYear(details), newFileName]);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error organizing file '{path}': {ex.Message}");
        }
    }

    async ValueTask ProcessHistoryFile(
        string path, string[] relativePathComponents, OrganizationDetails? organizationDetails = null)
    {
        try
        {
            GetFileNameAndExtension(relativePathComponents, out string fileName, out ReadOnlySpan<char> fileExtension);

            if (fileExtension.Length < 1)
            {
                Program.WriteLineDebug($"Skipping unknown file type: '{path}'");
                return;
            }

            var trimmedPathComponents = IgnoreTopDirectories(relativePathComponents, [DirHistory, DirUnknown]);

            var details = organizationDetails ?? GetOrganizationDetails(path, fileName, fileExtension);

            if (details.category != OrganizationCategory.TextHistory)
            {
                Program.WriteLineDebug($"Skipping unknown file type: '{path}'");
                return;
            }

            if (details.platform != null || details.author != null)
            {
                Console.WriteLine($"Skipping file from {details.platform}/{details.author}: '{path}'");
                return;
            }

            bool customLocation = false;
            switch (details.prefix)
            {
                case "calls" or "sms":
                    if (trimmedPathComponents.Length > 2 ||
                        (trimmedPathComponents.Length == 2 && trimmedPathComponents[0] != "SMSBackupRestore"))
                    {
                        customLocation = true;
                        break;
                    }

                    if (details.prefix == "calls")
                    {
                        // TODO: Define the destination path
                        FileMerger.MergeCallLogs(path, );
                    }
                    else
                    {
                        // TODO: Define the destination path
                        FileMerger.MergeSmsLogs(path, );
                    }
                    break;
                default:
                    Program.WriteLineDebug($"Skipping unknown file type: '{path}'");
                    break;
            }
            if (customLocation)
            {
                Program.WriteLineDebug($"Skipping file in custom location: '{path}'");
                return;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error organizing file '{path}': {ex.Message}");
        }
    }

    ValueTask ProcessUnknownFile(string path)
    {
        Program.WriteLineDebug($"Skipping unknown file category: '{path}'");
        return ValueTask.CompletedTask;
    }

    void GetFileNameAndExtension(string[] pathComponents, out string fileName, out ReadOnlySpan<char> fileExtension)
    {
        fileName = pathComponents[pathComponents.Length - 1];
        fileExtension = Path.GetExtension(fileName.AsSpan());
    }

    ReadOnlySpan<string> IgnoreTopDirectories(ReadOnlySpan<string> pathComponents, ReadOnlySpan<string> ignoreValues)
    {
        int ignoreValuesLen = ignoreValues.Length;
        Debug.Assert(ignoreValuesLen > 0);

        // Iterate over the path components, skipping the last (file name)
        int i = 0;
        for (int limit = pathComponents.Length - 1; i < limit; i++)
        {
            string component = pathComponents[i];
            Debug.Assert(!string.IsNullOrEmpty(component));

            bool found = false;
            for (int j = 0; j < ignoreValuesLen; j++)
            {
                if (component.Equals(ignoreValues[j], StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                break;
            }
        }

        Debug.Assert(pathComponents.Slice(i).Length > 0);
        return pathComponents.Slice(i);
    }

    OrganizationDetails GetOrganizationDetails(
        string path, ReadOnlySpan<char> fileName, ReadOnlySpan<char> fileExtension)
    {
        OrganizationDetails result = new();

        int indexExt = fileName.Length - fileExtension.Length;
        ReadOnlySpan<char> ext = fileExtension.Length > 0 ? fileExtension.Slice(1) : fileExtension;

        // TODO: Detect files that are from Facebook, WhatsApp, Google Drive, etc., and fill in the plarform and author
        result.platform = result.author = null;

        bool isDateUndTimeSuffExt = IsMatch(regexDateUndTimeSuffExt, fileName);
        if (isDateUndTimeSuffExt || IsMatch(regexPrefixUndDateUndTimeSuffExt, fileName))
        {
            Debug.Assert(indexExt > 0 && ext.Length > 0);

            int prefixLen = isDateUndTimeSuffExt ? 0 : fileName.IndexOf('_') + 1;
            ReadOnlySpan<char> prefix = prefixLen == 0 ? [] : fileName.Slice(0, prefixLen);

            ReadOnlySpan<char> dateStr = fileName.Slice(prefixLen, 8);
            Debug.Assert(fileName[prefixLen + 8] == '_');
            bool hasMillisecond = char.IsAsciiDigit(fileName[prefixLen + 15]);
            ReadOnlySpan<char> timeStr = fileName.Slice(prefixLen + 9, hasMillisecond ? 9 : 6);

            int sufStart = prefixLen + 9 + timeStr.Length;
            ReadOnlySpan<char> suffix = sufStart < indexExt ? fileName.Slice(sufStart, indexExt - sufStart) : [];
            Debug.Assert(suffix.IsEmpty || !char.IsAsciiLetterOrDigit(suffix[0]));

            switch (ext)
            {
                case "jpg":
                    if (prefix.IsEmpty || prefix.SequenceEqual("IMG_") || prefix.SequenceEqual("PIC_") ||
                        prefix.SequenceEqual("PXL_"))
                    {
                        result.category = OrganizationCategory.ImagePhoto;
                    }
                    break;
                case "mp4":
                    if (prefix.IsEmpty || prefix.SequenceEqual("IMG_") || prefix.SequenceEqual("PXL_") ||
                        prefix.SequenceEqual("VID_"))
                    {
                        result.category = OrganizationCategory.VideoCamera;
                    }
                    break;
            }

            if (result.category != OrganizationCategory.Unknown)
            {
                GetDateTimeFromDigits(dateStr, timeStr, ref result);
                result.prefix = prefix.IsEmpty ? null : new(prefix);
                result.suffix = suffix.IsEmpty ? null : new(suffix);
            }
        }
        else if (IsMatch(regexPrefixUnd4DigitsSuffExt, fileName))
        {
            Debug.Assert(indexExt > 0 && ext.Length > 0);

            int prefixLen = fileName.IndexOf('_') + 1;
            ReadOnlySpan<char> prefix = fileName.Slice(0, prefixLen);

            Debug.Assert(!char.IsAsciiDigit(fileName[prefixLen + 4]));
            ReadOnlySpan<char> digitsStr = fileName.Slice(prefixLen, 4);

            int sufStart = prefixLen + digitsStr.Length;
            ReadOnlySpan<char> suffix = sufStart < indexExt ? fileName.Slice(sufStart, indexExt - sufStart) : [];
            Debug.Assert(suffix.IsEmpty || !char.IsAsciiLetterOrDigit(suffix[0]));

            switch (ext)
            {
                case "JPG":
                    if (prefix.SequenceEqual("DSC_"))
                    {
                        result.category = OrganizationCategory.ImagePhoto;
                    }
                    break;
            }

            if (result.category != OrganizationCategory.Unknown)
            {
                TryGetDateTimeFromMetadata(path, ref result);
                result.prefix = prefix.IsEmpty ? null : new(prefix);
                result.suffix = suffix.IsEmpty ? null : new(suffix);
            }
        }
        else if (IsMatch(regexPrefixHyphDateTimeSuffExt, fileName))
        {
            Debug.Assert(indexExt > 0 && ext.Length > 0);

            int prefixLen = fileName.IndexOf('-') + 1;
            ReadOnlySpan<char> prefix = prefixLen == 0 ? [] : fileName.Slice(0, prefixLen);

            ReadOnlySpan<char> dateStr = fileName.Slice(prefixLen, 8);
            bool hasMillisecond = char.IsAsciiDigit(fileName[prefixLen + 14]);
            ReadOnlySpan<char> timeStr = fileName.Slice(prefixLen + 8, hasMillisecond ? 9 : 6);

            int sufStart = prefixLen + 8 + timeStr.Length;
            ReadOnlySpan<char> suffix = sufStart < indexExt ? fileName.Slice(sufStart, indexExt - sufStart) : [];
            Debug.Assert(suffix.IsEmpty || !char.IsAsciiLetterOrDigit(suffix[0]));

            switch (ext)
            {
                case "xml":
                    if ((prefix.SequenceEqual("calls-") || prefix.SequenceEqual("sms-")) && suffix.IsEmpty)
                    {
                        result.category = OrganizationCategory.TextHistory;
                    }
                    break;
            }

            if (result.category != OrganizationCategory.Unknown)
            {
                GetDateTimeFromDigits(dateStr, timeStr, ref result);
                result.prefix = prefix.IsEmpty ? null : new(prefix);
                result.suffix = suffix.IsEmpty ? null : new(suffix);
            }
        }

        // Use the suffix as milliseconds if appropriate
        if (result.HasSecond && !result.HasMillisecond && result.suffix?.Length == 2 && result.suffix[0] == '_')
        {
            char suffixDigit = result.suffix[1];
            if (suffixDigit > '0' && suffixDigit <= '9') // Excluding '0'
            {
                result.millisecond = (ushort)(suffixDigit - '0');
                result.timeComponents = TimeComponents.Millisecond;
                result.suffix = null;
            }
        }

        return result;
    }

    static void GetDateTimeFromDigits(ReadOnlySpan<char> date, ReadOnlySpan<char> time, ref OrganizationDetails details)
    {
        Debug.Assert(date.Length == 8);
        Debug.Assert(time.Length == 6 || time.Length == 9);

        bool hasMillis = time.Length == 9;
        details.year = short.Parse(date[0..4]);                                 // 4 digits
        details.month = byte.Parse(date[4..6]);                                 // 2 digits
        details.day = byte.Parse(date[6..8]);                                   // 2 digits
        details.hour = byte.Parse(time[0..2]);                                  // 2 digits
        details.minute = byte.Parse(time[2..4]);                                // 2 digits
        details.second = byte.Parse(time[4..6]);                                // 2 digits
        details.millisecond = hasMillis ? ushort.Parse(time[6..9]) : (ushort)0; // 3 digits
        details.timeComponents = hasMillis ? TimeComponents.Millisecond : TimeComponents.Second;
    }

    static bool TryGetDateTimeFromMetadata(string path, ref OrganizationDetails details)
    {
        if (!MetadataUtils.TryGetDateTaken(path, out var dt))
        {
            return false;
        }

        details.year = (short)dt.Year;
        details.month = (byte)dt.Month;
        details.day = (byte)dt.Day;
        details.hour = (byte)dt.Hour;
        details.minute = (byte)dt.Minute;
        details.second = (byte)dt.Second;
        details.millisecond = (ushort)dt.Millisecond;
        details.timeComponents =
            details.millisecond != 0 ? TimeComponents.Millisecond :
            details.second != 0 || details.minute != 0 || details.hour != 0 ? TimeComponents.Second :
            TimeComponents.Day;
        return true;
    }

    static string GetDirectoryNameFromYear(in OrganizationDetails details)
    {
        return details.HasYear ? $"{details.year:D4}" : DirUnknown;
    }

    ValueTask MoveFileToDestinationDir(string srcPath, ReadOnlySpan<string> relativePathComponents)
    {
        string dstPath = Path.Join([dstDirPath, .. relativePathComponents]);
        return MoveFile(srcPath, dstPath);
    }

    async ValueTask MoveFile(string srcPath, string dstPath)
    {
        Debug.Assert(Path.IsPathFullyQualified(srcPath));

        dstPath = Path.GetFullPath(dstPath);
        Debug.Assert(Path.IsPathFullyQualified(dstPath));

        if (FileUtils.PathsReferToSameLocation(srcPath, dstPath))
        {
            Program.WriteLineDebug($"Skipping file '{srcPath}': already in the correct location");
            return;
        }

        if (Path.Exists(dstPath))
        {
            if (!await FileUtils.EqualFileContentsAsync(srcPath, dstPath))
            {
                Console.Error.WriteLine(
                    $"Unable to move file '{srcPath}': another file already exists at destination '{dstPath}'");
                return;
            }

            // Delete duplicate file that would be moved to the same destination of an existing similar file
            bool deleted = dryRun ?
                true :
                FileUtils.SafeDeleteIfNotSameFile(srcPath, dstPath, checkPaths: false);
            if (deleted)
            {
                Console.WriteLine($"Deleted duplicate file '{srcPath}': the same file already exists at '{dstPath}'");
            }
            else
            {
                Console.Error.WriteLine(
                    $"Unable to move file '{srcPath}': another file already exists at destination '{dstPath}'");
            }
            return;
        }

        string? dstDir = Path.GetDirectoryName(dstPath);
        Debug.Assert(dstDir != null);
        if (!dryRun)
        {
            Directory.CreateDirectory(dstDir);
            File.Move(srcPath, dstPath, false);
        }
        Console.WriteLine($"Moved file '{srcPath}' to '{dstPath}'");
    }

    static bool IsMatch(Regex r, ReadOnlySpan<char> s)
    {
        return r.IsMatch(s, 0);
    }
}
