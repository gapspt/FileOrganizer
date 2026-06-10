using System.Diagnostics;
using System.Text;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FileOrganizer;

class FindSimilarCommand
{
    readonly string srcDirPath;
    readonly string? dstDirPath;
    readonly bool dryRun;
    readonly int recursionLevels;
    readonly int pixelDifference;

    readonly int sizeSamples;
    readonly ResizeOptions imageResizeOptions;

    readonly int imagesPartitionsSize;
    readonly DimensionPartition<(string, Rgba32[])> imagesPartitioned = new();

    // Note: Using a tuple (string?, List<string>?) below, so that in most cases where one size maps to only one file,
    // we don't need to allocate a new list just to hold that single file
    readonly Dictionary<long, (string?, List<string>?)> audioFilesBySize = new();
    readonly Dictionary<long, (string?, List<string>?)> videoFilesBySize = new();

    readonly Dictionary<string, List<string>> similarFilesClusters = new();

    readonly StringBuilder stringBuilder = new();

    public FindSimilarCommand(string srcDirPath, string? dstDirPath, bool dryRun, int recursionLevels,
        int widthSamples, int heightSamples, int pixelDifference)
    {
        Debug.Assert(Path.IsPathFullyQualified(srcDirPath));
        Debug.Assert(dstDirPath == null || Path.IsPathFullyQualified(dstDirPath));

        this.srcDirPath = srcDirPath;
        this.dstDirPath = dstDirPath;
        this.dryRun = dryRun;
        this.recursionLevels = recursionLevels;
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

        await FileUtils.ApplyToAllFilesAsync(srcDirPath, ProcessFile, recursionLevels);

        if (dstDirPath != null && similarFilesClusters.Count > 0)
        {
            MoveAllSimilarFiles();
        }

        return 0;
    }

    async ValueTask ProcessFile(string path, string[] relativePathComponents)
    {
        Debug.Assert(Path.IsPathFullyQualified(path));
        if (!path.StartsWith(srcDirPath))
        {
            Debug.Assert(false);
            throw new InvalidProgramException("Mismatch between file path and base directory path");
        }

        var category = await FileTypeDetector.DetectCategoryFromContent(path);
        switch (category)
        {
            case FileCategory.Audio:
                await ProcessGenericFile(path, audioFilesBySize);
                break;
            case FileCategory.Image:
                await ProcessImageFile(path);
                break;
            case FileCategory.Video:
                await ProcessGenericFile(path, videoFilesBySize);
                break;
            default:
                Program.WriteLineDebug($"Skipping unknown file type: '{path}'");
                break;
        }
    }

    async ValueTask ProcessGenericFile(string path, Dictionary<long, (string?, List<string>?)> filesBySize)
    {
        List<string>? matchingCandidates = null;
        List<string>? similarFound = null;
        try
        {
            long size = new FileInfo(path).Length;

            lock (filesBySize)
            {
                if (!filesBySize.TryGetValue(size, out var paths))
                {
                    filesBySize[size] = (path, null);

                    HandleSimilarFilesFound(path, null, 0);
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

    async ValueTask ProcessImageFile(string path)
    {
        List<(string, Rgba32[])>? matchingCandidates = null;
        List<string>? similarFound = null;
        try
        {
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
                HandleSimilarFilesFound(path, null, 0);
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

    void HandleSimilarFilesFound(string path, IList<string>? similarFound, int candidates)
    {
        if (candidates == 0)
        {
            Debug.Assert(similarFound == null || similarFound.Count == 0);
            Program.WriteLineDebug($"No similar files found (no potential matches) for {path}");
            return;
        }
        if (similarFound == null || similarFound.Count == 0)
        {
            Program.WriteLineDebug($"No similar files found (from {candidates} potential matches) for {path}");
            return;
        }

        if (dstDirPath != null)
        {
            lock (similarFilesClusters)
            {
                if (!similarFilesClusters.TryGetValue(path, out var cluster))
                {
                    cluster = SimpleObjectPool<List<string>>.Get();
                    cluster.Add(path);
                    similarFilesClusters.Add(path, cluster);
                }

                // Merge with existing clusters
                foreach (string otherPath in similarFound)
                {
                    if (!similarFilesClusters.TryGetValue(otherPath, out var otherCluster))
                    {
                        // No cluster for the other path yet
                        cluster.Add(otherPath);
                        similarFilesClusters.Add(otherPath, cluster);
                        continue;
                    }

                    if (otherCluster == cluster)
                    {
                        continue; // Already merged
                    }

                    // Merge with existing cluster
                    cluster.AddRange(otherCluster);
                    foreach (var otherClusterPath in otherCluster)
                    {
                        similarFilesClusters[otherClusterPath] = cluster;
                    }
                    otherCluster.Clear();
                    SimpleObjectPool<List<string>>.Return(otherCluster);
                }

                Debug.Assert(cluster.Count >= 2);
                Debug.Assert(cluster.ToHashSet().Count == cluster.Count, "Cluster must not have duplicates");
            } // lock (similarFilesClusters)
        }

        lock (stringBuilder)
        {
            stringBuilder.Append(similarFound.Count);
            stringBuilder.Append(" similar files found ");
#if DEBUG
            stringBuilder.Append("(from ");
            stringBuilder.Append(candidates);
            stringBuilder.Append(" potential matches) ");
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
        Debug.Assert(dstDirPath != null && similarFilesClusters.Count > 0);

        int clusters = 0;
        int files = 0;
        int successes = 0;
        int clusterId = 0;
        foreach (var cluster in similarFilesClusters.Values)
        {
            if (cluster.Count == 0)
            {
                continue; // Cluster already handled
            }
            Debug.Assert(cluster.Count >= 2);

            clusters++;
            files += cluster.Count;
            clusterId++;

            foreach (var path in cluster)
            {
                try
                {
                    string relativePath = Path.GetRelativePath(srcDirPath, path);
                    string newPath = Path.Join(dstDirPath, $"Similar_{clusterId}", relativePath);

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
            cluster.Clear(); // The same cluster will appear multiple times, but we want to handle it only once
        }

        int fails = files - successes;
        Console.WriteLine(fails == 0 ?
            $"Moved all {files} similar files ({clusters} clusters) to '{dstDirPath}'" :
            $"Moved {successes} out of {files} ({fails} failed) similar files ({clusters} clusters) to '{dstDirPath}'");
    }
}
