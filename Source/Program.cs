namespace PhotoOrganizer;

static class Program
{
    static string inDirectory = Directory.GetCurrentDirectory();
    static string? outDirectory = null;
    static bool recursive = false;
    static int widthSamples = 16;
    static int heightSamples = 16;
    static int pixelDifference = 8;

    static async Task<int> Main(string[] args)
    {
        while (args.Length == 0)
        {
            string argsStr = Console.ReadLine()?.Trim() ?? "";
            args = argsStr.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (!ParseArguments(args[1..]))
        {
            return -1;
        }

        switch (args[0])
        {
            case "organize":
                return await new OrganizeImagesCommand(inDirectory, outDirectory ?? inDirectory, recursive).Run();
            case "similar":
                return await new FindSimilarImagesCommand(
                    inDirectory, outDirectory, recursive, widthSamples, heightSamples, pixelDifference).Run();
            default:
                Console.Error.WriteLine("Invalid command. Available commands: organize | similar");
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
                    case "--recursive" or "-r":
                        recursive = true;
                        continue;
                    case "--outDirectory" or "-o":
                        if (!GetKeyValueString(ref i, out outDirectory))
                        {
                            return false;
                        }
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
        Console.WriteLine($"recursive={recursive}");
        Console.WriteLine($"widthSamples={widthSamples}");
        Console.WriteLine($"heightSamples={heightSamples}");
        Console.WriteLine($"pixelDifference={pixelDifference}");
#endif

        return true;
    }
}
