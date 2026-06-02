using System.Text.RegularExpressions;

namespace PhotoOrganizer;

static class Program
{
    static string srcDirectory = Directory.GetCurrentDirectory();
    static string dstDirectory = srcDirectory;
    static bool recursive = false;

    static async Task Main(string[] args)
    {
        ParseArguments(args);

        await DirectoryUtils.ApplyToAllFilesAsync(srcDirectory, ProcessFile, recursive);
    }

    static void ParseArguments(string[] args)
    {
        bool srcArg = false;
        bool dstArg = false;

        foreach (var arg in args)
        {
            if (arg.StartsWith('-'))
            {
                switch (arg)
                {
                    case "--recursive" or "-r":
                        recursive = true;
                        break;
                    default:
                        Console.Error.WriteLine($"Unknown argument: {arg}");
                        return;
                }
                continue;
            }

            if (!srcArg)
            {
                srcArg = true;
                srcDirectory = dstDirectory = arg;
                continue;
            }
            if (!dstArg)
            {
                dstArg = true;
                dstDirectory = arg;
                continue;
            }

            Console.Error.WriteLine($"Invalid argument: {arg}");
            return;
        }
    }

    static ValueTask ProcessFile(FileInfo file)
    {
        MoveFileToSubDirectoryByYear(file);

        return ValueTask.CompletedTask;
    }

    static void MoveFileToSubDirectoryByYear(FileInfo file)
    {
        try
        {
            string origPath = file.FullName;

            string subDirName = GetPhotoYear(file) ?? "Unknown";
            string newDirectoryPath = Path.Combine(dstDirectory, subDirName);
            string newPath = Path.Combine(newDirectoryPath, file.Name);

            if (newPath == origPath)
            {
                return;
            }

            if (Path.Exists(newPath))
            {
                Console.Error.WriteLine($"File already exists at destination: '{newPath}'");
                return;
            }

            Directory.CreateDirectory(newDirectoryPath);
            file.MoveTo(newPath);
            Console.WriteLine($"Moved file '{origPath}' to '{newPath}'");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error processing file '{file.FullName}': {ex.Message}");
        }
    }

    static readonly Regex regexGooglePixelPhoto = new(@"\APXL_\d{8}_\d{9}\.jpg\z");
    static string? GetPhotoYear(FileInfo file)
    {
        string fileName = file.Name;

        if (IsAbsoluteMatch(regexGooglePixelPhoto, fileName))
        {
            return fileName.Substring(4, 4); // "PXL_20230123_123456789.jpg" -> "2023"
        }

        try
        {
            if (ImageMetadataUtils.TryGetDateTaken(file.FullName, out var dateTaken))
            {
                return $"{dateTaken.Year}";
            }
        }
        catch
        {
            // Ignored
        }

        return null;
    }

    static bool IsAbsoluteMatch(Regex r, string s)
    {
        var m = r.Match(s);
        return m.Success && m.Length == s.Length;
    }
}
