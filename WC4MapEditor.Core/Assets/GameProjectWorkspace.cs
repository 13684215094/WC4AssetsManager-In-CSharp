using System.Text.Json;

namespace WC4MapEditor.Core.Assets;

public sealed record ProjectCopyProgress(int CompletedFiles, int TotalFiles, long CopiedBytes, long TotalBytes, string RelativePath);

public sealed class GameProjectWorkspace
{
    public const string ManifestFileName = ".wc4-project.json";
    internal static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public string SourceRoot { get; }
    public string SourcePath { get; }
    public string? SourceApkSha256 { get; }
    public bool IsApkSource => SourceApkSha256 != null;
    public string OutputRoot { get; }
    public string AssetsRelativePath { get; }
    public string AssetsRoot => Path.GetFullPath(Path.Combine(OutputRoot, AssetsRelativePath));

    public string SourceAssetsRoot => Path.GetFullPath(Path.Combine(SourceRoot, AssetsRelativePath));
    public bool IsIncremental { get; }

    private GameProjectWorkspace(string sourceRoot, string outputRoot, string assetsRelativePath, bool incremental,
        string? sourcePath = null, string? sourceApkSha256 = null)
    {
        SourceRoot = sourceRoot;
        SourcePath = sourcePath ?? sourceRoot;
        SourceApkSha256 = sourceApkSha256;
        OutputRoot = outputRoot;
        AssetsRelativePath = assetsRelativePath;
        IsIncremental = incremental;
        ProjectFiles.Register(this);
    }

    public static GameProjectWorkspace Create(string sourceDirectory, string outputDirectory,
        IProgress<ProjectCopyProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        string input = NormalizeDirectory(sourceDirectory.Trim().Trim('"'));
        string output = NormalizeDirectory(outputDirectory);
        if (File.Exists(input))
        {
            EnsureNoLinks(input);
            if (IsWithin(input, output)) throw new ArgumentException("输出目录不能包含来源 APK。");
            EnsureEmptyDestination(output);
            var apk = ApkProjectSource.Prepare(input, progress, cancellationToken);
            return CreateDirectoryProject(apk.Root, output, progress, cancellationToken, input, apk.Sha256);
        }
        return CreateDirectoryProject(input, output, progress, cancellationToken);
    }

    private static GameProjectWorkspace CreateDirectoryProject(string source, string output,
        IProgress<ProjectCopyProgress>? progress, CancellationToken cancellationToken,
        string? sourceApk = null, string? sourceApkSha256 = null)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Project directory does not exist: {source}");
        EnsureNoLinks(source);
        EnsureNoLinks(output);
        if (IsWithin(source, output) || IsWithin(output, source))
            throw new ArgumentException("Source and output directories must be separate and cannot contain each other.");
        EnsureEmptyDestination(output);
        string assetsRelative = FindAssetsRelativePath(source);
        progress?.Report(new(0, 0, 0, 0, "验证原项目"));
        EnsureTreeHasNoLinks(source, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new(1, 1, 0, 0, "创建增量项目"));
        cancellationToken.ThrowIfCancellationRequested();
        string parent = Path.GetDirectoryName(output) ?? throw new ArgumentException("Output cannot be a volume root.");
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, $".wc4-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            // Only metadata and the resource root are created; assets remain in the source.
            Directory.CreateDirectory(Path.Combine(staging, assetsRelative));
            File.WriteAllText(Path.Combine(staging, ManifestFileName), JsonSerializer.Serialize(
                new ProjectManifest { Version = sourceApk == null ? 2 : 3, SourceRoot = source,
                    SourceApk = sourceApk, SourceApkSha256 = sourceApkSha256, AssetsRelativePath = assetsRelative },
                new JsonSerializerOptions { WriteIndented = true }));
            cancellationToken.ThrowIfCancellationRequested();
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
            return new(source, output, assetsRelative, incremental: true, sourceApk, sourceApkSha256);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    public static string GetDefaultOutputDirectory(string sourceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        string source = NormalizeDirectory(sourceDirectory.Trim().Trim('"'));
        string parent = Path.GetDirectoryName(source)
            ?? throw new ArgumentException("请选择游戏项目目录，不能选择磁盘根目录。");
        string name = Path.GetFileName(source);
        if (!Directory.Exists(source) && Path.GetExtension(source).Equals(".apk", StringComparison.OrdinalIgnoreCase))
            name = Path.GetFileNameWithoutExtension(source);
        return Path.Combine(parent, name + "-edited");
    }

    public static GameProjectWorkspace LoadOrCreate(string sourceDirectory, string? outputDirectory = null,
        IProgress<ProjectCopyProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory)) throw new ArgumentException("请先填写游戏项目来源目录。");
        string source = NormalizeDirectory(sourceDirectory.Trim().Trim('"'));
        if (!Directory.Exists(source) && !File.Exists(source)) throw new FileNotFoundException($"游戏项目目录或 APK 不存在：{source}");
        EnsureNoLinks(source);
        if (Directory.Exists(source)) FindAssetsRelativePath(source);
        string output = string.IsNullOrWhiteSpace(outputDirectory)
            ? GetDefaultOutputDirectory(source) : NormalizeDirectory(outputDirectory.Trim().Trim('"'));
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(Path.Combine(output, ManifestFileName)))
        {
            progress?.Report(new(0, 0, 0, 0, "验证已有项目"));
            return Open(output, source, progress, cancellationToken);
        }
        return Create(source, output, progress, cancellationToken);
    }

    public static GameProjectWorkspace Open(string outputDirectory, string? expectedSourceDirectory = null,
        IProgress<ProjectCopyProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        string output = NormalizeDirectory(outputDirectory);
        EnsureNoLinks(output);
        string manifestPath = Path.Combine(output, ManifestFileName);
        EnsureNoLinks(manifestPath);
        var manifest = JsonSerializer.Deserialize<ProjectManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("Invalid game project manifest.");
        if (manifest.Version is not (1 or 2 or 3) || string.IsNullOrWhiteSpace(manifest.SourceRoot) ||
            string.IsNullOrWhiteSpace(manifest.AssetsRelativePath) || Path.IsPathRooted(manifest.AssetsRelativePath))
            throw new InvalidDataException("Unsupported or incomplete game project manifest.");
        string source = NormalizeDirectory(manifest.SourceRoot);
        string input = manifest.Version == 3 ? manifest.SourceApk ?? "" : source;
        if (string.IsNullOrWhiteSpace(input)) throw new InvalidDataException("APK 项目缺少来源安装包路径。");
        input = NormalizeDirectory(input);
        if (expectedSourceDirectory != null && !input.Equals(NormalizeDirectory(expectedSourceDirectory), PathComparison))
            throw new InvalidDataException("此输出目录属于另一个来源项目，请选择匹配的来源目录或另一个输出目录。");
        if (manifest.Version == 3)
        {
            if (string.IsNullOrWhiteSpace(manifest.SourceApkSha256)) throw new InvalidDataException("APK 项目缺少来源哈希。");
            if (IsWithin(input, output)) throw new InvalidDataException("输出目录不能包含来源 APK。");
            source = ApkProjectSource.Prepare(input, progress, cancellationToken, manifest.SourceApkSha256).Root;
        }
        if (IsWithin(source, output) || IsWithin(output, source))
            throw new InvalidDataException("Source and output directories overlap.");
        string assets = Path.GetFullPath(Path.Combine(output, manifest.AssetsRelativePath));
        if (!IsWithin(assets, output) || !Directory.Exists(assets))
            throw new InvalidDataException("The project assets directory is missing or outside the output directory.");
        if (manifest.Version >= 2)
        {
            string sourceAssets = Path.GetFullPath(Path.Combine(source, manifest.AssetsRelativePath));
            if (!IsWithin(sourceAssets, source) || !Directory.Exists(sourceAssets))
                throw new DirectoryNotFoundException("The source assets directory is required to reopen an incremental project.");
            EnsureNoLinks(sourceAssets);
            EnsureTreeHasNoLinks(source, cancellationToken);
        }
        EnsureTreeHasNoLinks(output, cancellationToken);
        return new(source, output, Path.GetRelativePath(output, assets), manifest.Version >= 2,
            input, manifest.Version == 3 ? manifest.SourceApkSha256 : null);
    }

    public static string ReadSourceDirectory(string outputDirectory)
    {
        string output = NormalizeDirectory(outputDirectory);
        string manifestPath = Path.Combine(output, ManifestFileName);
        EnsureNoLinks(manifestPath);
        var manifest = JsonSerializer.Deserialize<ProjectManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("项目描述无效。");
        if (manifest.Version is not (1 or 2 or 3) || string.IsNullOrWhiteSpace(manifest.SourceRoot))
            throw new InvalidDataException("项目描述版本不受支持，或没有来源目录。");
        return NormalizeDirectory(manifest.Version == 3 ? manifest.SourceApk
            ?? throw new InvalidDataException("APK 项目没有来源安装包路径。") : manifest.SourceRoot);
    }

    public string GetEditablePath(string path)
    {
        string full = Path.GetFullPath(path);
        if (IsWithin(full, SourceRoot))
            full = Path.GetFullPath(Path.Combine(OutputRoot, Path.GetRelativePath(SourceRoot, full)));
        ValidateOutputPath(full);
        return full;
    }

    public bool IsSourcePath(string path) => IsWithin(Path.GetFullPath(path), SourceRoot) ||
        (IsApkSource && Path.GetFullPath(path).Equals(SourcePath, PathComparison));

    public string GetReadPath(string path)
    {
        string output = GetEditablePath(path);
        if (!IsIncremental || File.Exists(output) || Directory.Exists(output)) return output;
        string source = GetSourcePath(output);
        EnsureNoLinks(source);
        return source;
    }

    internal string GetSourcePath(string outputPath)
        => Path.GetFullPath(Path.Combine(SourceRoot, Path.GetRelativePath(OutputRoot, outputPath)));

    public void ValidateOutputPath(string path)
    {
        string full = Path.GetFullPath(path);
        if (!IsWithin(full, OutputRoot) || IsWithin(full, SourceRoot) || full.Equals(OutputRoot, PathComparison) ||
            full.Equals(Path.Combine(OutputRoot, ManifestFileName), PathComparison))
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

    internal static bool IsWithin(string path, string root)
        => path.Equals(root, PathComparison) || path.StartsWith(Path.EndsInDirectorySeparator(root)
            ? root : root + Path.DirectorySeparatorChar, PathComparison);

    private static void EnsureEmptyDestination(string output)
    {
        if (File.Exists(output) || (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()))
            throw new IOException("Choose a new or empty output directory. To continue editing, open the existing project.");
    }

    internal static void EnsureNoLinks(string path)
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

    private static void EnsureTreeHasNoLinks(string root, CancellationToken cancellationToken = default)
    {
        var directories = new Stack<string>();
        directories.Push(root);
        while (directories.TryPop(out string? directory))
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        public string? SourceApk { get; set; }
        public string? SourceApkSha256 { get; set; }
    }
}
