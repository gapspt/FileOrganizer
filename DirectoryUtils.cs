using System.Diagnostics;

namespace PhotoOrganizer
{
    public static class DirectoryUtils
    {
        public static async ValueTask ApplyToAllFilesAsync(DirectoryInfo dir, Func<FileInfo, ValueTask> action, bool recursive = false)
        {
            var tasks = ObjectPool<List<Task<ValueTask>>>.Get();

            foreach (var f in dir.EnumerateFiles())
            {
                tasks.Add(Task.Run(() => action(f)));
            }

            if (recursive)
            {
                foreach (var d in dir.EnumerateDirectories())
                {
                    tasks.Add(Task.Run(() => ApplyToAllFilesAsync(d, action, true)));
                }
            }

            foreach (var task in tasks)
            {
                await await task;
            }

            tasks.Clear();
            ObjectPool<List<Task<ValueTask>>>.Return(tasks);
        }
        public static async ValueTask ApplyToAllFilesAsync(string path, Func<FileInfo, ValueTask> action, bool recursive = false)
            => await ApplyToAllFilesAsync(new DirectoryInfo(path), action, recursive);
    }
}
