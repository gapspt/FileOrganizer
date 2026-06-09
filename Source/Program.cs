using System.Diagnostics;

namespace FileOrganizer;

static class Program
{
    static string inDirectory = Directory.GetCurrentDirectory();
    static string? outDirectory = null;
    static bool dryRun = false;
    static int recursionLevels = 0;
    static int widthSamples = 16;
    static int heightSamples = 16;
    static int pixelDifference = 8;

    static async Task<int> Main(string[] args)
    {
#if DEBUG
        dryRun = true;
#endif

        if (args.Length == 0)
        {
            while (args.Length == 0)
            {
                string argsStr = Console.ReadLine()?.Trim() ?? "";
                args = argsStr.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            Console.WriteLine();
        }

        if (args.Any(a => a == "--help" || a == "-h"))
        {
            PrintUsage();
            return 0;
        }

        if (!ParseArguments(args[1..]))
        {
            Console.WriteLine();
            PrintUsage();
            return -1;
        }

        inDirectory = Path.GetFullPath(inDirectory);
        if (outDirectory != null)
        {
            outDirectory = Path.GetFullPath(outDirectory);
        }

        switch (args[0])
        {
            case "organize":
                return await new OrganizeCommand(inDirectory, outDirectory ?? inDirectory, dryRun, recursionLevels).Run();
            case "similar":
                return await new FindSimilarCommand(
                    inDirectory, outDirectory, dryRun, recursionLevels, widthSamples, heightSamples, pixelDifference).Run();
            case "help":
                PrintUsage();
                return 0;
            default:
                Console.Error.WriteLine("Invalid command.\n");
                PrintUsage();
                return -1;
        }
    }

    static bool ParseArguments(string[] args)
    {
        bool ParseKeyValueInt(ref int argIndex, out int value, int min = int.MinValue, int max = int.MaxValue)
        {
            if (++argIndex >= args.Length)
            {
                Console.Error.WriteLine($"Invalid value for {args[argIndex - 1]}: no value provided");
                value = default;
                return false;
            }
            if (!int.TryParse(args[argIndex], out value) || value < min || value > max)
            {
                Console.Error.WriteLine($"Invalid value for {args[argIndex - 1]}: {args[argIndex]}");
                return false;
            }
            return true;
        }
        bool GetKeyValueString(ref int argIndex, out string value)
        {
            if (++argIndex >= args.Length)
            {
                Console.Error.WriteLine($"Invalid value for {args[argIndex - 1]}: no value provided");
                value = "";
                return false;
            }
            value = args[argIndex];
            return true;
        }

        bool inArg = false;
        bool samplesArg = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg.StartsWith('-'))
            {
                switch (arg)
                {
                    case "--dryRun" or "-dry":
                        dryRun = true;
                        continue;
                    case "--recursionLevels" or "-r":
                        if (!ParseKeyValueInt(ref i, out recursionLevels, 0))
                        {
                            return false;
                        }
                        continue;
                    case "--outDirectory" or "-o":
                        if (!GetKeyValueString(ref i, out outDirectory))
                        {
                            return false;
                        }
                        continue;
                    case "--widthSamples" or "-w":
                        if (!ParseKeyValueInt(ref i, out widthSamples, 1, 512))
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
                        if (!ParseKeyValueInt(ref i, out heightSamples, 1, 512))
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

            if (!inArg)
            {
                inArg = true;
                inDirectory = arg;
                continue;
            }

            Console.Error.WriteLine($"Invalid argument: {arg}");
            return false;
        }

#if DEBUG
        Console.WriteLine($"inDirectory={inDirectory}");
        Console.WriteLine($"outDirectory={outDirectory}");
        Console.WriteLine($"dryRun={dryRun}");
        Console.WriteLine($"recursionLevels={recursionLevels}");
        Console.WriteLine($"widthSamples={widthSamples}");
        Console.WriteLine($"heightSamples={heightSamples}");
        Console.WriteLine($"pixelDifference={pixelDifference}");
        Console.WriteLine();
#endif

        return true;
    }

    static void PrintUsage()
    {
        // 80 chars max   |12345678901234567890123456789012345678901234567890123456789012345678901234567890|
        Console.WriteLine("Usage:");
        Console.WriteLine("  FileOrganizer <command> [<inDirectory>] [options]");
        Console.WriteLine("\nCommands:");
        Console.WriteLine("  organize      Organize files into separate folders.");
        Console.WriteLine("  similar       Find similar files.");
        Console.WriteLine("\nAll commands:");
        Console.WriteLine("  <inDirectory>                 Input directory in which to search for images");
        Console.WriteLine("                                (defaults to the current working directory).");
        Console.WriteLine("  --dryRun, -dry                Does not make any change, only reports the");
        Console.WriteLine("                                changes that would be made.");
        Console.WriteLine("  --recursionLevels, -r <N>     Recurse into input directory's subdirectories");
        Console.WriteLine("                                for at most N levels deep (default 0).");
        Console.WriteLine("  --help, -h                    Show this help message.");
        Console.WriteLine("\norganize:");
        Console.WriteLine("  --outDirectory, -o <path>     Output directory where to move the files that");
        Console.WriteLine("                                are found (defaults to the input directory).");
        Console.WriteLine("\nsimilar:");
        Console.WriteLine("  --outDirectory, -o <path>     Output directory where to move the files that");
        Console.WriteLine("                                are found to be similar to any other one.");
        Console.WriteLine("                                If not provided, no files will be moved, only");
        Console.WriteLine("                                their original path will be reported.");
        Console.WriteLine("  --widthSamples, -w <1-512>    Number of width samples for image files");
        Console.WriteLine("                                (default 16, or height samples if provided).");
        Console.WriteLine("  --heightSamples, -h <1-512>   Number of height samples for image files");
        Console.WriteLine("                                (default 16, or width samples if provided).");
        Console.WriteLine("  --pixelDifference, -d <N>     Pixel difference threshold for image files");
        Console.WriteLine("                                (default 8).");
        Console.WriteLine("\nExamples:");
        Console.WriteLine("  FileOrganizer organize ./Files -o ./FilesSorted");
        Console.WriteLine("  FileOrganizer findsimilar ./Files -r -w 16 -h 16 -d 8");
        Console.WriteLine();
    }

    [Conditional("DEBUG")]
    internal static void WriteLineDebug(string message)
    {
        Console.WriteLine(message);
    }
}
