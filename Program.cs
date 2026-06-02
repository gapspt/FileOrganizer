using System.Text.RegularExpressions;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PhotoOrganizer;

static class Program
{
    static string srcDirectory = Directory.GetCurrentDirectory();
    static string dstDirectory = srcDirectory;
    static bool recursive = false;
    static int widthSamples = 8;
    static int heightSamples = 8;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
    static int sizeSamples;
    static ObjectPool<Rgba32[]> pixelsPool;
    static ImageProcessor imageProcessor;
#pragma warning restore CS8618

    static async Task<int> Main(string[] args)
    {
        if (!ParseArguments(args))
        {
            return -1;
        }
#if DEBUG
        Console.WriteLine($"srcDirectory={srcDirectory}");
        Console.WriteLine($"dstDirectory={dstDirectory}");
        Console.WriteLine($"recursive={recursive}");
        Console.WriteLine($"widthSamples={widthSamples}");
        Console.WriteLine($"heightSamples={heightSamples}");
#endif

        sizeSamples = widthSamples * heightSamples;
        pixelsPool = new(() => new Rgba32[sizeSamples]);
        imageProcessor = new()
        {
            ThumbnailResizeOptions = new()
            {
                Mode = ResizeMode.Stretch,
                Size = new(widthSamples, heightSamples),
            }
        };

        await DirectoryUtils.ApplyToAllFilesAsync(srcDirectory, ProcessFile, recursive);

        return 0;
    }

    static bool ParseArguments(string[] args)
    {
        bool ParseKeyValueInt(ref int argIndex, out int value, int min = int.MinValue, int max = int.MaxValue)
        {
            if (++argIndex >= args.Length)
            {
                Console.Error.WriteLine($"Invalid value for {args[argIndex - 1]}: no value provided");
            }
            if (!int.TryParse(args[argIndex], out value) || value < min || value > max)
            {
                Console.Error.WriteLine($"Invalid value for {args[argIndex - 1]}: {args[argIndex]}");
                return false;
            }
            return true;
        }

        bool srcArg = false;
        bool dstArg = false;
        bool samplesArg = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg.StartsWith('-'))
            {
                switch (arg)
                {
                    case "--recursive" or "-r":
                        recursive = true;
                        continue;
                    case "--widthSamples" or "-w":
                        if (!ParseKeyValueInt(ref i, out widthSamples, 1, 64))
                        {
                            return false;
                        }
                        if (!samplesArg)
                        {
                            heightSamples = widthSamples;
                            samplesArg = true;
                        }
                        continue;
                    case "--heightSamples" or "-h":
                        if (!ParseKeyValueInt(ref i, out heightSamples, 1, 64))
                        {
                            return false;
                        }
                        if (!samplesArg)
                        {
                            widthSamples = heightSamples;
                            samplesArg = true;
                        }
                        continue;
                }

                Console.Error.WriteLine($"Unknown argument: {arg}");
                return false;
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
            return false;
        }

        return true;
    }

    static async ValueTask ProcessFile(FileInfo file)
    {
        var pixels = pixelsPool.Get();
        try
        {
            try
            {
                await imageProcessor.GetImageThumbnail(file.FullName, pixels);
            }
            catch (Exception)
            {
                Console.WriteLine($"Skipping non image file: {file.FullName}");
                return;
            }

            if (!MoveFileToSubDirectoryByYear(file))
            {
                return;
            }
        }
        finally
        {
            pixelsPool.Return(pixels);
        }
    }

    static bool MoveFileToSubDirectoryByYear(FileInfo file)
    {
        try
        {
            string origPath = file.FullName;

            string subDirName = GetPhotoYear(file) ?? "Unknown";
            string newDirectoryPath = Path.Combine(dstDirectory, subDirName);
            string newPath = Path.Combine(newDirectoryPath, file.Name);

            if (newPath == origPath)
            {
                return true;
            }

            if (Path.Exists(newPath))
            {
                Console.Error.WriteLine($"File already exists at destination: '{newPath}'");
                return false;
            }

            Directory.CreateDirectory(newDirectoryPath);
            file.MoveTo(newPath);
            Console.WriteLine($"Moved file '{origPath}' to '{newPath}'");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error processing file '{file.FullName}': {ex.Message}");
            return false;
        }
        return true;
    }

    static readonly Regex regexGooglePixelPhoto = new(@"\APXL_\d{8}_\d{9}\.jpg\z");
    static string? GetPhotoYear(FileInfo file)
    {
        string fileName = file.Name;

        if (IsMatch(regexGooglePixelPhoto, fileName))
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

    static bool IsMatch(Regex r, string s)
    {
        var m = r.Match(s);
        return m.Success && m.Length == s.Length;
    }
}
