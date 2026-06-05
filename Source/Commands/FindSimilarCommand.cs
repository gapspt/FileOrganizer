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
    readonly bool dryRun;
    readonly int pixelDifference;

    readonly int sizeSamples;
    readonly ResizeOptions imageResizeOptions;

    readonly int imagesPartitionsSize;
    readonly DimensionPartition<(string, Rgba32[])> imagesPartitioned = new();

    // Note: Using a tuple (string?, List<string>?) below, so that in most cases where one size maps to only one file,
    // we don't need to allocate a new list just to hold that single file
    readonly Dictionary<long, (string?, List<string>?)> audioFilesBySize = new();
    readonly Dictionary<long, (string?, List<string>?)> videoFilesBySize = new();

    readonly HashSet<string> allSimilarFiles = new();

    readonly StringBuilder stringBuilder = new();

    public FindSimilarCommand(string srcDirPath, string? dstDirPath, bool recursive, bool dryRun,
        int widthSamples, int heightSamples, int pixelDifference)
    {
        this.srcDirPath = srcDirPath;
        this.dstDirPath = dstDirPath;
        this.recursive = recursive;
        this.dryRun = dryRun;
        this.pixelDifference = pixelDifference;

        sizeSamples = widthSamples * heightSamples;
        imageResizeOptions = new()
        {
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Bicubic,
            Compand = true, // Gamma correction
            Size = new(widthSamples, heightSamples),
        };

        imagesPartitionsSize = Math.Max(1, pixelDifference);
    }

    public async Task<int> Run()
    {
        if (dstDirPath != null)
        {
            if (!Directory.Exists(dstDirPath))
            {
                Console.Error.WriteLine($"Destination does not exist or is not a directory: '{dstDirPath}'");
                return -1;
            }
            if (FileUtils.PathsReferToSameLocation(dstDirPath, srcDirPath))
            {
                Console.Error.WriteLine(
                    $"Destination for similar files is the same as the input files directory: '{srcDirPath}'");
                return -1;
            }
        }

        await FileUtils.ApplyToAllFilesAsync(srcDirPath, ProcessFile, recursive);

        if (dstDirPath != null && allSimilarFiles.Count > 0)
        {
            MoveAllSimilarFiles();
        }

        return 0;
    }

    async ValueTask ProcessFile(FileInfo file)
    {
        var category = await FileTypeDetector.DetectCategoryFromContent(file.FullName);
        switch (category)
        {
            case FileCategory.Audio:
                await ProcessGenericFile(file, audioFilesBySize);
                break;
            case FileCategory.Image:
                await ProcessImageFile(file);
                break;
            case FileCategory.Video:
                await ProcessGenericFile(file, videoFilesBySize);
                break;
            default:
#if DEBUG
                Console.WriteLine($"Skipping unknown file type: '{file.FullName}'");
#endif
                break;
        }
    }

    async ValueTask ProcessGenericFile(FileInfo file, Dictionary<long, (string?, List<string>?)> filesBySize)
    {
        string path = file.FullName;
        long size = file.Length;

        List<string>? matchingCandidates = null;
        List<string>? similarFound = null;
        try
        {
            lock (filesBySize)
            {
                if (!filesBySize.TryGetValue(size, out var paths))
                {
                    filesBySize[size] = (path, null);
                    return;
                }

                List<string>? list = paths.Item2;
                if (list == null) // Only one item exists, create the list in order to hold multiple items
                {
                    Debug.Assert(paths.Item1 != null);
                    list = [paths.Item1];
                    filesBySize[size] = (null, list);
                }

                matchingCandidates = SimpleObjectPool<List<string>>.Get();
                matchingCandidates.AddRange(list);

                list.Add(path);
            }

            similarFound = SimpleObjectPool<List<string>>.Get();
            foreach (var otherPath in matchingCandidates)
            {
                if (await FileUtils.EqualFileContentsAsync(path, otherPath))
                {
                    similarFound.Add(otherPath);
                }
            }

            HandleSimilarFilesFound(path, similarFound, matchingCandidates.Count);
        }
        catch (Exception e)
        {
            Console.Error.Write(e);
        }
        finally
        {
            matchingCandidates?.Clear();
            SimpleObjectPool<List<string>>.ReturnIfNotNull(matchingCandidates);

            similarFound?.Clear();
            SimpleObjectPool<List<string>>.ReturnIfNotNull(similarFound);
        }
    }

    async ValueTask ProcessImageFile(FileInfo file)
    {
        List<(string, Rgba32[])>? matchingCandidates = null;
        List<string>? similarFound = null;
        try
        {
            string path = file.FullName;

            Rgba32[] pixels = new Rgba32[sizeSamples];
            await MediaUtils.GetImageResized(path, pixels, imageResizeOptions);

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

            int r0 = p0.R / imagesPartitionsSize, g0 = p0.G / imagesPartitionsSize, b0 = p0.B / imagesPartitionsSize;
            int r1 = p1.R / imagesPartitionsSize, g1 = p1.G / imagesPartitionsSize, b1 = p1.B / imagesPartitionsSize;

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

            similarFound = SimpleObjectPool<List<string>>.Get();
            foreach (var (otherPath, otherPixels) in matchingCandidates)
            {
                if (MediaUtils.ArePixelsSimilar(pixels, otherPixels, pixelDifference))
                {
                    similarFound.Add(otherPath);
                }
            }

            HandleSimilarFilesFound(path, similarFound, matchingCandidates.Count);
        }
        catch (Exception e)
        {
            Console.Error.Write(e);
        }
        finally
        {
            matchingCandidates?.Clear();
            SimpleObjectPool<List<(string, Rgba32[])>>.ReturnIfNotNull(matchingCandidates);

            similarFound?.Clear();
            SimpleObjectPool<List<string>>.ReturnIfNotNull(similarFound);
        }
    }

    void HandleSimilarFilesFound(string path, IList<string> similarFound, int? candidates = null)
    {
        if (similarFound.Count == 0)
        {
#if DEBUG
            Console.WriteLine(candidates.HasValue ?
                $"No similar files found (from {candidates.Value} potential matches) for {path}" :
                $"No similar files found for {path}");
#endif
            return;
        }

        if (dstDirPath != null)
        {
            lock (allSimilarFiles)
            {
                allSimilarFiles.Add(path);
                foreach (string otherPath in similarFound)
                {
                    allSimilarFiles.Add(otherPath);
                }
            }
        }

        lock (stringBuilder)
        {
            stringBuilder.Append(similarFound.Count);
            stringBuilder.Append(" similar files found ");
#if DEBUG
            if (candidates.HasValue)
            {
                stringBuilder.Append("(from ");
                stringBuilder.Append(candidates.Value);
                stringBuilder.Append(" potential matches) ");
            }
#endif
            stringBuilder.Append("for ");
            stringBuilder.Append(path);
            foreach (string otherPath in similarFound)
            {
                stringBuilder.Append("\n  ");
                stringBuilder.Append(otherPath);
            }
            Console.WriteLine(stringBuilder);
            stringBuilder.Clear();
        }
    }

    void MoveAllSimilarFiles()
    {
        Debug.Assert(dstDirPath != null && allSimilarFiles.Count > 0);

        int successes = 0;
        foreach (string path in allSimilarFiles)
        {
            try
            {
                string relativePath = Path.GetRelativePath(srcDirPath, path);
                string newPath = Path.Join(dstDirPath, relativePath);

                if (Path.Exists(newPath))
                {
                    Console.Error.WriteLine($"Another file already exists at destination: '{newPath}'");
                    continue;
                }

                string? destinationDirectory = Path.GetDirectoryName(newPath);
                Debug.Assert(destinationDirectory != null);
                if (!dryRun)
                {
                    Directory.CreateDirectory(destinationDirectory);
                    File.Move(path, newPath, false);
                }
                else
                {
                    Console.WriteLine($"Moved similar file '{path}' to '{newPath}'");
                }
                successes++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error moving similar file '{path}': {ex.Message}");
            }
        }

        int fails = allSimilarFiles.Count - successes;
        Console.WriteLine(
            fails == 0 ?
            $"Moved all {successes} similar files to '{dstDirPath}'" :
            $"Moved {successes} out of {allSimilarFiles.Count} ({fails} failed) similar files to '{dstDirPath}'");
    }
}
