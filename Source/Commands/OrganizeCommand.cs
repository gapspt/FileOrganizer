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
    public const string DirMemes = "Memes";
    public const string DirMusic = "Music";
    public const string DirRingtones = "Ringtones";
    public const string DirScreenCaptures = "ScreenCaptures";
    public const string DirWallpapers = "Wallpapers";

    public const string DirUnknown = "Unknown";

    static readonly Regex regexYear1900To2099 = new(@"\A(19|20)\d{2}\z");
    static readonly Regex regexMonth = new(@"\A((0?[1-9])|(1[0-2]))\z");
    static readonly Regex regexDay = new(@"\A((0?[1-9])|([12][0-9])|(3[01]))\z");
    static readonly Regex regexPrefixUndDateUndTimeSuffExt =
        new(@"\A[a-zA-Z0-9]+_\d{8}_\d{6}(\d{3})?([_.~-][a-zA-Z0-9_.-]+)?\.[a-zA-Z0-9]+\z");

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

        var trimmedPathComponents = IgnoreTopDirectories(relativePathComponents, [DirUnknown]);

        // If it is inside a directory, it must be a category directory
        if (trimmedPathComponents.Length >= 2)
        {
            return trimmedPathComponents[0] switch
            {
                DirCamera => ProcessCameraFile(path, relativePathComponents),

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
                (len <= 1 || IsMatch(regexYear1900To2099, trimmedPathComponents[0])) &&
                (len <= 2 || trimmedPathComponents[1] == DirUnknown || IsMatch(regexMonth, trimmedPathComponents[1])) &&
                (len <= 3 || trimmedPathComponents[2] == DirUnknown || IsMatch(regexDay, trimmedPathComponents[2]));

            if (!shouldProcess)
            {
                Program.WriteLineDebug($"Skipping file in custom location: '{path}'");
                return;
            }

            var details = organizationDetails ?? GetOrganizationDetails(path, fileName, fileExtension);

            if (details.platform != null || details.author != null)
            {
                // TODO: Choose what to do with files from Facebook, WhatsApp, Google Drive, etc.
                Console.WriteLine($"Skipping file from {details.platform}/{details.author}: '{path}'");
                return;
            }

            if (details.category != OrganizationCategory.ImagePhoto &&
                details.category != OrganizationCategory.VideoCamera)
            {
                Program.WriteLineDebug($"Skipping unknown file type: '{path}'");
                return;
            }

            string prefix = details.category == OrganizationCategory.ImagePhoto ? "IMG_" : "VID_";
            string newFileName = $"{prefix}{details.year:D4}{details.month:D2}{details.day:D2}_" +
                $"{details.hour:D2}{details.minute:D2}{details.second:D2}{details.millisecond:D3}" +
                $"{details.suffix}{fileExtension}";

            // Camera/2020/IMG_20201231_235959999.jpg
            await MoveFileToDestinationDir(path, [DirCamera, GetDirectoryNameFromYear(details), newFileName]);
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

        if (IsMatch(regexPrefixUndDateUndTimeSuffExt, fileName))
        {
            Debug.Assert(indexExt > 0 && ext.Length > 0);

            int indexUnd = fileName.IndexOf('_');
            Debug.Assert(indexUnd > 0);
            ReadOnlySpan<char> prefix = fileName.Slice(0, indexUnd);

            bool hasMillisecond = char.IsAsciiDigit(fileName[indexUnd + 16]);
            ReadOnlySpan<char> dateStr = fileName.Slice(indexUnd + 1, 8);
            ReadOnlySpan<char> timeStr = fileName.Slice(indexUnd + 10, hasMillisecond ? 9 : 6);

            int sufStart = indexUnd + 10 + timeStr.Length;
            ReadOnlySpan<char> suffix = sufStart < indexExt ? fileName.Slice(sufStart, indexExt - sufStart) : [];

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

        if (!result.HasYear && result.category != OrganizationCategory.Unknown &&
            MetadataUtils.TryGetDateTaken(path, out var dt))
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

        if (!dryRun)
        {
            string? dstDir = Path.GetDirectoryName(dstPath);
            Debug.Assert(dstDir != null);
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
