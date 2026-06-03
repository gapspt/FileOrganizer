using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PhotoOrganizer;

static class Program
{
    static string srcDirectory = Directory.GetCurrentDirectory();
    static string dstDirectory = srcDirectory;
    static bool recursive = false;
    static int widthSamples = 16;
    static int heightSamples = 16;
    static int pixelDifference = 8;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
    static int sizeSamples;
    static ResizeOptions thumbnailResizeOptions;
    static int partitionsSize;
    static DimensionPartition<(string, Rgba32[])> imagesPartitioned;
#pragma warning restore CS8618

    static StringBuilder stringBuilder = new();

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
        Console.WriteLine($"pixelDifference={pixelDifference}");
#endif

        sizeSamples = widthSamples * heightSamples;
        thumbnailResizeOptions = new()
        {
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Bicubic,
            Compand = true, // Gamma correction
            Size = new(widthSamples, heightSamples),
        };
        partitionsSize = Math.Max(1, pixelDifference);
        imagesPartitioned = new();

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
                    case "--pixelDifference" or "-d":
                        if (!ParseKeyValueInt(ref i, out pixelDifference, 0))
                        {
                            return false;
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
        List<(string, Rgba32[])>? matchingCandidates = null;
        List<string>? similarImages = null;
        try
        {
            string path = file.FullName;

            Rgba32[] pixels = new Rgba32[sizeSamples];
            try
            {
                await ImageProcessor.GetImageResized(path, pixels, thumbnailResizeOptions);
            }
            catch (Exception)
            {
                Console.WriteLine($"Skipping non image file: {path}");
                return;
            }

            MoveFileToSubDirectoryByYear(file);
            path = file.FullName; // Update with the new path

            Rgba32 p = default;

            // Set fully transparent pixels' RGB values to zero
            for (int i = 0; i < sizeSamples; i++)
            {
                p = pixels[i];
                if (p.A == 0)
                {
                    p.Rgb = new(0, 0, 0);
                }
            }

            Rgba32 p0 = pixels[0];

            int r0 = p0.R / partitionsSize, g0 = p0.G / partitionsSize, b0 = p0.B / partitionsSize;
            int r1 = p.R / partitionsSize, g1 = p.G / partitionsSize, b1 = p.B / partitionsSize;

            matchingCandidates = SimpleObjectPool<List<(string, Rgba32[])>>.Get();
            lock (imagesPartitioned)
            {
                for (int r0i = r0 - 1; pixelDifference > 0 && r0i <= r0 + 1; r0i++) // Skip all if pixelDifference == 0
                {
                    var part0 = imagesPartitioned.GetDimensionCoordinate(r0i);
                    if (part0 == null) { continue; }
                    for (int g0i = g0 - 1; g0i <= g0 + 1; g0i++)
                    {
                        var part1 = part0.GetDimensionCoordinate(g0i);
                        if (part1 == null) { continue; }
                        for (int b0i = b0 - 1; b0i <= b0 + 1; b0i++)
                        {
                            var part2 = part1.GetDimensionCoordinate(b0i);
                            if (part2 == null) { continue; }
                            for (int r1i = r1 - 1; r1i <= r1 + 1; r1i++)
                            {
                                var part3 = part2.GetDimensionCoordinate(r1i);
                                if (part3 == null) { continue; }
                                for (int g1i = g1 - 1; g1i <= g1 + 1; g1i++)
                                {
                                    var part4 = part3.GetDimensionCoordinate(g1i);
                                    if (part4 == null) { continue; }
                                    for (int b1i = b1 - 1; b1i <= b1 + 1; b1i++)
                                    {
                                        var part5 = part4.GetDimensionCoordinate(b1i);
                                        if (part5 == null) { continue; }

                                        Debug.Assert(part5.Values != null && part5.Values.Count > 0);
                                        matchingCandidates.AddRange(part5.Values);
                                    }
                                }
                            }
                        }
                    }
                }

                var partition = imagesPartitioned
                    .GetOrAddDimensionCoordinate(r0)
                    .GetOrAddDimensionCoordinate(g0)
                    .GetOrAddDimensionCoordinate(b0)
                    .GetOrAddDimensionCoordinate(r1)
                    .GetOrAddDimensionCoordinate(g1)
                    .GetOrAddDimensionCoordinate(b1);
                if (pixelDifference == 0 && partition.Values != null) // Optimized for when pixelDifference == 0
                {
                    Debug.Assert(partition.Values.Count > 0);
                    matchingCandidates.AddRange(partition.Values);
                }
                partition.Add((path, pixels));
            } // lock (imagesPartitioned)

            if (matchingCandidates.Count == 0)
            {
                Console.WriteLine($"No similar files found (no potential matches) for {path}");
                return;
            }

            similarImages = SimpleObjectPool<List<string>>.Get();
            foreach (var (otherPath, otherPixels) in matchingCandidates)
            {
                if (ImageProcessor.ArePixelsSimilar(pixels, otherPixels, pixelDifference))
                {
                    similarImages.Add(otherPath);
                }
            }

            if (similarImages.Count == 0)
            {
                Console.WriteLine($"No similar files found ({matchingCandidates.Count} potential matches) for {path}");
                return;
            }

            lock (stringBuilder)
            {
                stringBuilder.Append(similarImages.Count);
                stringBuilder.Append(" similar files found (");
                stringBuilder.Append(matchingCandidates.Count);
                stringBuilder.Append(" potential matches) for ");
                stringBuilder.Append(path);
                foreach (string otherPath in similarImages)
                {
                    stringBuilder.Append("\n  ");
                    stringBuilder.Append(otherPath);
                }
                Console.WriteLine(stringBuilder);
                stringBuilder.Clear();
            }
        }
        catch (Exception e)
        {
            Console.Error.Write(e);
        }
        finally
        {
            matchingCandidates?.Clear();
            SimpleObjectPool<List<(string, Rgba32[])>>.ReturnIfNotNull(matchingCandidates);

            similarImages?.Clear();
            SimpleObjectPool<List<string>>.ReturnIfNotNull(similarImages);
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
            file.MoveTo(newPath, false);
            Console.WriteLine($"Moved file '{origPath}' to '{newPath}'");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error moving file '{file.FullName}': {ex.Message}");
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
