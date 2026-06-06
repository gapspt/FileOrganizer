using System.Security.Cryptography;

namespace FileOrganizer;

public static class FileUtils
{
    public static bool PathsReferToSameLocation(string path1, string path2)
    {
        return path1 == path2 || Path.GetRelativePath(path1, path2) == ".";
    }

    public static async ValueTask ApplyToAllFilesAsync(
        DirectoryInfo dir, Func<FileInfo, ValueTask> action, int recursionLevels = 0)
    {
        var tasks = SimpleObjectPool<List<Task<ValueTask>>>.Get();

        try
        {
            foreach (var f in dir.EnumerateFiles())
            {
                // Note: We call `Task.Run` to force the `action` to run in parallel even if it runs synchronously
                tasks.Add(Task.Run(() => action(f)));
            }

            if (--recursionLevels >= 0)
            {
                foreach (var d in dir.EnumerateDirectories())
                {
                    // Note: We call `Task.Run` to force each recursion call to run in parallel
                    tasks.Add(Task.Run(() => ApplyToAllFilesAsync(d, action, recursionLevels)));
                }
            }

            foreach (var task in tasks)
            {
                await await task;
            }
        }
        finally
        {
            tasks.Clear();
            SimpleObjectPool<List<Task<ValueTask>>>.Return(tasks);
        }
    }
    public static ValueTask ApplyToAllFilesAsync(string path, Func<FileInfo, ValueTask> action, int recursionLevels = 0)
        => ApplyToAllFilesAsync(new DirectoryInfo(path), action, recursionLevels);

    public static async ValueTask<bool> EqualFileContentsAsync(string path1, string path2)
    {
        FileInfo fi1 = new(path1);
        FileInfo fi2 = new(path2);
        if (!fi1.Exists || !fi2.Exists || fi1.Length != fi2.Length)
        {
            return false;
        }

        using var fs1 = File.OpenRead(path1);
        using var fs2 = File.OpenRead(path2);

        const int bufferSize = 81920;
        var pool = System.Buffers.ArrayPool<byte>.Shared;

        byte[]? buf1 = null;
        byte[]? buf2 = null;
        try
        {
            buf1 = pool.Rent(bufferSize);
            buf2 = pool.Rent(bufferSize);

            var mem1 = buf1.AsMemory(0, bufferSize);
            var mem2 = buf2.AsMemory(0, bufferSize);

            while (true)
            {
                int read1 = await fs1.ReadAsync(mem1);
                int read2 = await fs2.ReadAsync(mem2);
                if (read1 == 0 || read2 == 0)
                {
                    return read1 == read2;
                }

                while (read1 != read2)
                {
                    int newRead;
                    if (read1 < read2)
                    {
                        newRead = await fs1.ReadAsync(mem1[read1..read2]);
                        read1 += newRead;
                    }
                    else
                    {
                        newRead = await fs2.ReadAsync(mem2[read2..read1]);
                        read2 += newRead;
                    }
                    if (newRead == 0)
                    {
                        return false;
                    }
                }

                if (!buf1.AsSpan(0, read1).SequenceEqual(buf2.AsSpan(0, read2)))
                {
                    return false;
                }
            }
        }
        finally
        {
            if (buf1 != null) pool.Return(buf1);
            if (buf2 != null) pool.Return(buf2);
        }
    }

    /// <summary>
    /// Deletes the file at <paramref name="toDelete"/> only if it is not the same file as
    /// <paramref name="toKeep"/>. Returns true if the file was deleted; or false if it was not deleted, when it is
    /// determined that it is the same file as <paramref name="toKeep"/>.
    /// </summary>
    public static bool SafeDeleteIfNotSameFile(string toDelete, string toKeep, bool checkPaths = true)
    {
        if (checkPaths && PathsReferToSameLocation(toDelete, toKeep))
        {
            return false;
        }

        // In the exceptional case where the paths are detected as being different but they actually
        // point to the same file, deleting the file would be catastrophic! To prevent that, we instead
        // rename the file first, then verify the other file is still there, and only then we delete it.

        string toDeleteFileName = Path.GetFileName(toDelete);
        string? toDeleteDirPath = Path.GetDirectoryName(toDelete);
        string tempPath;
        for (int len = toDeleteFileName.Length; true; len++)
        {
            string tempName = RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz", len);
            tempPath = Path.Join(toDeleteDirPath, tempName);
            if (!Path.Exists(tempPath))
            {
                break;
            }
        }

        if (!File.Exists(toDelete) || !File.Exists(toKeep))
        {
            throw new FileNotFoundException("One or both files to delete and to keep does not exist");
        }

        try
        {
            File.Move(toDelete, tempPath, false);
            if (File.Exists(toKeep))
            {
                // Delete only if the other file at the new path still exists
                File.Delete(tempPath);
                return true;
            }
            else // Renaming the file also made the other file at the new path to disappear
            {
                File.Move(tempPath, toDelete, false);
                if (!File.Exists(toDelete) || !File.Exists(toKeep))
                {
                    // This should never happen, it is purely for sanity
                    throw new IOException($"Failed file path restoration from '{tempPath}' to '{toDelete}'");
                }
                return false;
            }
        }
        catch
        {
            try
            {
                File.Move(tempPath, toDelete, false); // Possibly a second attempt
                return false;
            }
            catch { }
            throw;
        }
    }
}
