using System.Text.Json;

namespace WC4MapEditor.Core.Assets;

public sealed record ProjectCopyProgress(int CompletedFiles, int TotalFiles, long CopiedBytes, long TotalBytes, string RelativePath);

public sealed class GameProjectWorkspace
{
    public const string ManifestFileName = ".wc4-project.json";
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public string SourceRoot { get; }
    public string OutputRoot { get; }
    public string AssetsRelativePath { get; }
    public string AssetsRoot => Path.GetFullPath(Path.Combine(OutputRoot, AssetsRelativePath));

    private GameProjectWorkspace(string sourceRoot, string outputRoot, string assetsRelativePath)
    {
        SourceRoot = sourceRoot;
        OutputRoot = outputRoot;
        AssetsRelativePath = assetsRelativePath;
    }

    public static GameProjectWorkspace Create(string sourceDirectory, string outputDirectory,
        IProgress<ProjectCopyProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        string source = NormalizeDirectory(sourceDirectory);
        string output = NormalizeDirectory(outputDirectory);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Project directory does not exist: {source}");
        EnsureNoLinks(source);
        EnsureNoLinks(output);
        if (IsWithin(source, output) || IsWithin(output, source))
            throw new ArgumentException("Source and output directories must be separate and cannot contain each other.");
        EnsureEmptyDestination(output);
        string assetsRelative = FindAssetsRelativePath(source);
        var files = new List<(string Path, string Relative, long Length)>();
        var directories = new List<string>();
        var pending = new Stack<string>();
        pending.Push(source);
        while (pending.TryPop(out string? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Linked project entries are not supported: {path}");
                string relative = Path.GetRelativePath(source, path);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(relative);
                    pending.Push(path);
                }
                else if (!relative.Equals(ManifestFileName, PathComparison))
                    files.Add((path, relative, new FileInfo(path).Length));
            }
        }

        long totalBytes = files.Sum(file => file.Length);
        long copiedBytes = 0;
        string parent = Path.GetDirectoryName(output) ?? throw new ArgumentException("Output cannot be a volume root.");
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, $".wc4-copy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            foreach (string directory in directories) Directory.CreateDirectory(Path.Combine(staging, directory));
            var buffer = new byte[81920];
            int completed = 0;
            progress?.Report(new(0, files.Count, 0, totalBytes, ""));
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = Path.Combine(staging, file.Relative);
                using (var input = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    int read;
                    while ((read = input.Read(buffer)) != 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        target.Write(buffer, 0, read);
                        copiedBytes += read;
                    }
                    target.Flush(flushToDisk: true);
                }
                File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(file.Path));
                progress?.Report(new(++completed, files.Count, copiedBytes, totalBytes, file.Relative));
            }
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = new ProjectManifest { SourceRoot = source, AssetsRelativePath = assetsRelative };
            File.WriteAllText(Path.Combine(staging, ManifestFileName), JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true }));

            // Publish only the completed copy. Existing edited projects are never overwritten.
            EnsureNoLinks(output);
            EnsureEmptyDestination(output);
            bool removedEmptyDirectory = Directory.Exists(output);
            if (removedEmptyDirectory) Directory.Delete(output, recursive: false);
            try { Directory.Move(staging, output); }
            catch
            {
                if (removedEmptyDirectory && !Directory.Exists(output)) Directory.CreateDirectory(output);
                throw;
            }
            return new(source, output, assetsRelative);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    public static GameProjectWorkspace Open(string outputDirectory)
    {
        string output = NormalizeDirectory(outputDirectory);
        EnsureNoLinks(output);
        string manifestPath = Path.Combine(output, ManifestFileName);
        EnsureNoLinks(manifestPath);
        var manifest = JsonSerializer.Deserialize<ProjectManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("Invalid game project manifest.");
        if (manifest.Version != 1 || string.IsNullOrWhiteSpace(manifest.SourceRoot) ||
            string.IsNullOrWhiteSpace(manifest.AssetsRelativePath))
            throw new InvalidDataException("Unsupported or incomplete game project manifest.");
        string source = NormalizeDirectory(manifest.SourceRoot);
        if (IsWithin(source, output) || IsWithin(output, source))
            throw new InvalidDataException("Source and output directories overlap.");
        string assets = Path.GetFullPath(Path.Combine(output, manifest.AssetsRelativePath));
        if (!IsWithin(assets, output) || !Directory.Exists(assets))
            throw new InvalidDataException("The project assets directory is missing or outside the output directory.");
        EnsureNoLinks(assets);
        EnsureTreeHasNoLinks(output);
        return new(source, output, Path.GetRelativePath(output, assets));
    }

    public string GetEditablePath(string path)
    {
        string full = Path.GetFullPath(path);
        if (IsWithin(full, SourceRoot))
            full = Path.GetFullPath(Path.Combine(OutputRoot, Path.GetRelativePath(SourceRoot, full)));
        ValidateOutputPath(full);
        return full;
    }

    public void ValidateOutputPath(string path)
    {
        string full = Path.GetFullPath(path);
        if (!IsWithin(full, OutputRoot) || IsWithin(full, SourceRoot))
            throw new ArgumentException("Select a file inside this project's output directory.");
        EnsureNoLinks(full);
    }

    private static string FindAssetsRelativePath(string source)
    {
        if (Path.GetFileName(source).Equals("assets", StringComparison.OrdinalIgnoreCase)) return ".";
        foreach (string relative in new[] { "assets", Path.Combine("WC4DATA", "assets"), Path.Combine("Resource", "WC4DATA", "assets") })
            if (Directory.Exists(Path.Combine(source, relative))) return relative;
        throw new DirectoryNotFoundException("Select a game project containing assets, or the assets directory itself.");
    }

    private static string NormalizeDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool IsWithin(string path, string root)
        => path.Equals(root, PathComparison) || path.StartsWith(Path.EndsInDirectorySeparator(root)
            ? root : root + Path.DirectorySeparatorChar, PathComparison);

    private static void EnsureEmptyDestination(string output)
    {
        if (File.Exists(output) || (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()))
            throw new IOException("Choose a new or empty output directory. To continue editing, open the existing project.");
    }

    private static void EnsureNoLinks(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current != null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Linked paths are not supported: {current}");
            current = Path.GetDirectoryName(current);
        }
    }

    private static void EnsureTreeHasNoLinks(string root)
    {
        var directories = new Stack<string>();
        directories.Push(root);
        while (directories.TryPop(out string? directory))
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Linked project entries are not supported: {path}");
            if ((attributes & FileAttributes.Directory) != 0) directories.Push(path);
        }
    }

    private sealed class ProjectManifest
    {
        public int Version { get; set; } = 1;
        public string SourceRoot { get; set; } = "";
        public string AssetsRelativePath { get; set; } = "";
    }
}
