using System.Diagnostics;

namespace FileOrganizer
{
    public static class DirectoryUtils
    {
        public static async ValueTask ApplyToAllFilesAsync(DirectoryInfo dir, Func<FileInfo, ValueTask> action, bool recursive = false)
        {
            var tasks = SimpleObjectPool<List<Task<ValueTask>>>.Get();

            try
            {
                foreach (var f in dir.EnumerateFiles())
                {
                    // Note: We call `Task.Run` to force the `action` to run in parallel even if it runs synchronously
                    tasks.Add(Task.Run(() => action(f)));
                }

                if (recursive)
                {
                    foreach (var d in dir.EnumerateDirectories())
                    {
                        // Note: We call `Task.Run` to force each recursion call to run in parallel
                        tasks.Add(Task.Run(() => ApplyToAllFilesAsync(d, action, true)));
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
        public static async ValueTask ApplyToAllFilesAsync(string path, Func<FileInfo, ValueTask> action, bool recursive = false)
            => await ApplyToAllFilesAsync(new DirectoryInfo(path), action, recursive);
    }
}
