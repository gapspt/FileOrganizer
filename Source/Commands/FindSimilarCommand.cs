using System.Diagnostics;
using System.Text;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FileOrganizer;

class FindSimilarCommand
{
    readonly string srcDirPath;
    readonly string? dstDirPath;
    readonly bool recursive;
    readonly int pixelDifference;

    readonly int sizeSamples;
    readonly ResizeOptions thumbnailResizeOptions;

    readonly int partitionsSize;
    readonly DimensionPartition<(string, Rgba32[])> imagesPartitioned = new();

    readonly HashSet<string> allSimilarImages = new();

    readonly StringBuilder stringBuilder = new();

    public FindSimilarCommand(
        string srcDirPath, string? dstDirPath, bool recursive, int widthSamples, int heightSamples, int pixelDifference)
    {
        this.srcDirPath = srcDirPath;
        this.dstDirPath = dstDirPath;
        this.recursive = recursive;
        this.pixelDifference = pixelDifference;

        sizeSamples = widthSamples * heightSamples;
        thumbnailResizeOptions = new()
        {
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Bicubic,
            Compand = true, // Gamma correction
            Size = new(widthSamples, heightSamples),
        };

        partitionsSize = Math.Max(1, pixelDifference);
    }

    public async Task<int> Run()
    {
        if (dstDirPath != null && !Directory.Exists(dstDirPath))
        {
            Console.Error.WriteLine($"Destination does not exist or is not a directory: '{dstDirPath}'");
            return -1;
        }

        await DirectoryUtils.ApplyToAllFilesAsync(srcDirPath, ProcessFile, recursive);

        if (dstDirPath != null && allSimilarImages.Count > 0)
        {
            MoveAllToDirectory(allSimilarImages, dstDirPath);
        }

        return 0;
    }

    async ValueTask ProcessFile(FileInfo file)
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
#if DEBUG
                Console.WriteLine($"Skipping non image file: {path}");
#endif
                return;
            }

            // Set fully transparent pixels' RGB values to zero
            for (int i = 0; i < sizeSamples; i++)
            {
                if (pixels[i].A == 0)
                {
                    pixels[i] = new(0, 0, 0, 0);
                }
            }

            Rgba32 p0 = pixels[0];
            Rgba32 p1 = pixels[sizeSamples - 1];

            int r0 = p0.R / partitionsSize, g0 = p0.G / partitionsSize, b0 = p0.B / partitionsSize;
            int r1 = p1.R / partitionsSize, g1 = p1.G / partitionsSize, b1 = p1.B / partitionsSize;

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
#if DEBUG
                Console.WriteLine($"No similar files found (no potential matches) for {path}");
#endif
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
#if DEBUG
                Console.WriteLine($"No similar files found (from {matchingCandidates.Count} potential matches) for {path}");
#endif
                return;
            }

            if (dstDirPath != null)
            {
                allSimilarImages.Add(path);
                foreach (string otherPath in similarImages)
                {
                    allSimilarImages.Add(otherPath);
                }
            }

            lock (stringBuilder)
            {
                stringBuilder.Append(similarImages.Count);
                stringBuilder.Append(" similar files found ");
#if DEBUG
                stringBuilder.Append("(from ");
                stringBuilder.Append(matchingCandidates.Count);
                stringBuilder.Append(" potential matches) ");
#endif
                stringBuilder.Append("for ");
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

    void MoveAllToDirectory(ICollection<string> filesPaths, string directory)
    {
        int successes = 0;
        foreach (string path in filesPaths)
        {
            try
            {
                string fileName = Path.GetFileName(path);
                string newPath = Path.Combine(directory, fileName);
                if (newPath == path)
                {
#if DEBUG
                    Console.WriteLine($"Skipping image file already in similar images directory: '{newPath}'");
#endif
                    successes++;
                    continue;
                }

                if (Path.Exists(newPath))
                {
                    Console.Error.WriteLine($"Another file already exists at destination: '{newPath}'");
                    continue;
                }

                File.Move(path, newPath);
                successes++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error moving similar image '{path}': {ex.Message}");
            }
        }

        int fails = filesPaths.Count - successes;
        Console.WriteLine(
            fails == 0 ?
            $"Moved all {successes} similar files to '{dstDirPath}'" :
            $"Moved {successes} out of {filesPaths.Count} ({fails} failed) similar files to '{dstDirPath}'");
    }
}
