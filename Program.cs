using PhotoOrganizer;

string path = Directory.GetCurrentDirectory();
bool recursive = false;

ParseArguments();

await DirectoryUtils.ApplyToAllFilesAsync(path, ProcessFile, recursive);

void ParseArguments()
{
    bool pathArg = false;

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

        if (!pathArg)
        {
            pathArg = true;
            path = arg;
            continue;
        }

        Console.Error.WriteLine($"Invalid argument: {arg}");
        return;
    }
}

static ValueTask ProcessFile(FileInfo info)
{
    try
    {
        if (ImageMetadataUtils.TryGetDateTaken(info.FullName, out var dateTaken))
        {
            Console.WriteLine($"{info.FullName} - Date taken: {dateTaken:yyyy-MM-dd HH:mm:ss}");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error reading image metadata for '{info.FullName}': {ex.Message}");
    }

    return ValueTask.CompletedTask;
}
