using System.IO.Compression;

if (args.Length == 2 && args[0] == "--verify")
{
    return VerifyArchive(Path.GetFullPath(args[1]));
}

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: Packager <source-directory> <output.zip>\n" +
        "       Packager --verify <archive.zip>");
    return 2;
}

var sourceDirectory = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
if (!Directory.Exists(sourceDirectory))
{
    Console.Error.WriteLine($"Source directory does not exist: {sourceDirectory}");
    return 2;
}

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
if (File.Exists(outputPath))
{
    File.Delete(outputPath);
}

var rootName = Path.GetFileName(sourceDirectory.TrimEnd(Path.DirectorySeparatorChar));
using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);

foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
{
    var relativePath = Path.GetRelativePath(sourceDirectory, filePath).Replace('\\', '/');
    var entry = archive.CreateEntry(
        $"{rootName}/{relativePath}",
        CompressionLevel.SmallestSize);

    if (!OperatingSystem.IsWindows())
    {
        var mode = (int)File.GetUnixFileMode(filePath);
        entry.ExternalAttributes = mode << 16;
    }

    using var input = File.OpenRead(filePath);
    using var output = entry.Open();
    input.CopyTo(output);
}

Console.WriteLine($"Created {outputPath}");
return 0;

static int VerifyArchive(string archivePath)
{
    if (!File.Exists(archivePath))
    {
        Console.Error.WriteLine($"Archive does not exist: {archivePath}");
        return 2;
    }

    using var archive = ZipFile.OpenRead(archivePath);
    var paths = new HashSet<string>(StringComparer.Ordinal);
    long extractedBytes = 0;

    foreach (var entry in archive.Entries)
    {
        paths.Add(entry.FullName);
        using var input = entry.Open();
        input.CopyTo(Stream.Null);
        extractedBytes += entry.Length;
    }

    var isWindows = archivePath.Contains("windows", StringComparison.OrdinalIgnoreCase);
    var requiredSuffixes = isWindows
        ? new[] { "/ZaloShortcuts.exe", "/uiohook.dll", "/README.txt" }
        : new[]
        {
            "/Zalo Shortcuts.app/Contents/Info.plist",
            "/Zalo Shortcuts.app/Contents/MacOS/ZaloShortcuts",
            "/Zalo Shortcuts.app/Contents/MacOS/libuiohook.dylib",
            "/Zalo Shortcuts.app/Contents/Resources/AppIcon.icns",
            "/README.txt"
        };

    foreach (var suffix in requiredSuffixes)
    {
        if (!paths.Any(path => path.EndsWith(suffix, StringComparison.Ordinal)))
        {
            Console.Error.WriteLine($"Required archive entry is missing: *{suffix}");
            return 1;
        }
    }

    if (!isWindows)
    {
        var executable = archive.Entries.Single(
            entry => entry.FullName.EndsWith(
                "/Zalo Shortcuts.app/Contents/MacOS/ZaloShortcuts",
                StringComparison.Ordinal));
        var unixMode = (executable.ExternalAttributes >> 16) & 0x1FF;
        if ((unixMode & 0x49) == 0)
        {
            Console.Error.WriteLine("The macOS application executable lost its execute permission.");
            return 1;
        }
    }

    Console.WriteLine(
        $"Verified {Path.GetFileName(archivePath)}: " +
        $"{archive.Entries.Count} files, {extractedBytes:N0} uncompressed bytes.");
    return 0;
}
