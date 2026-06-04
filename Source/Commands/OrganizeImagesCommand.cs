using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PhotoOrganizer;

class OrganizeImagesCommand
{
    readonly string srcDirPath;
    readonly string dstDirPath;
    readonly bool recursive;

    public OrganizeImagesCommand(string srcDirPath, string dstDirPath, bool recursive)
    {
        this.srcDirPath = srcDirPath;
        this.dstDirPath = dstDirPath;
        this.recursive = recursive;
    }

    public async Task<int> Run()
    {
        if (!Directory.Exists(dstDirPath))
        {
            Console.Error.WriteLine($"Destination does not exist or is not a directory: '{dstDirPath}'");
            return -1;
        }

        await DirectoryUtils.ApplyToAllFilesAsync(srcDirPath, ProcessFile, recursive);

        return 0;
    }

    async ValueTask ProcessFile(FileInfo file)
    {
        try
        {
            string origPath = file.FullName;

            if (!await ImageProcessor.IsImage(origPath))
            {
#if DEBUG
                Console.WriteLine($"Skipping non image file: '{origPath}'");
#endif
                return;
            }

            string subDirName = GetPhotoYear(file) ?? "Unknown";
            string newDirectoryPath = Path.Combine(dstDirPath, subDirName);
            string newPath = Path.Combine(newDirectoryPath, file.Name);

            if (newPath == origPath)
            {
#if DEBUG
                Console.WriteLine($"Skipping image file already in correct location: '{origPath}'");
#endif
                return;
            }

            if (Path.Exists(newPath))
            {
                Console.Error.WriteLine($"Another file already exists at destination: '{newPath}'");
                return;
            }

            Directory.CreateDirectory(newDirectoryPath);
            file.MoveTo(newPath, false);
            Console.WriteLine($"Moved file '{origPath}' to '{newPath}'");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error organizing file '{file.FullName}': {ex.Message}");
            return;
        }
    }

    static readonly Regex regexGooglePixelPhoto = new(@"\APXL_\d{8}_\d{9}\.jpg\z");
    static string? GetPhotoYear(FileInfo file)
    {
        string fileName = file.Name;

        if (IsMatch(regexGooglePixelPhoto, fileName))
        {
            return fileName.Substring(4, 4); // "PXL_20230123_123456789.jpg" -> "2023"
        }

        if (ImageMetadataUtils.TryGetDateTaken(file.FullName, out var dateTaken))
        {
            return $"{dateTaken.Year}";
        }

        return null;
    }

    static bool IsMatch(Regex r, string s)
    {
        var m = r.Match(s);
        return m.Success && m.Length == s.Length;
    }
}
