using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace WC4MapEditor.Core.Assets;

public sealed record ApkSourceSnapshot(string Root, string Sha256);

public static class ApkProjectSource
{
    private const string CacheManifestName = ".wc4-apk-cache.json";
    private const long MaximumExpandedBytes = 16L * 1024 * 1024 * 1024;
    private const int MaximumEntries = 200_000;
    private static readonly object Gate = new();
    public static string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WC4MapEditor", "apk-cache");

    public static ApkSourceSnapshot Prepare(string path, IProgress<ProjectCopyProgress>? progress = null,
        CancellationToken cancellationToken = default, string? expectedSha256 = null)
    {
        string apkPath = Path.GetFullPath(path);
        GameProjectWorkspace.EnsureNoLinks(apkPath);
        if (!Path.GetExtension(apkPath).Equals(".apk", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("来源文件需要是 .apk 安装包，或选择含 assets 的解包目录。");
        if (expectedSha256 != null && (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit)))
            throw new InvalidDataException("项目描述中的 APK 哈希无效。");
        using var input = new FileStream(apkPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        progress?.Report(new(0, 0, 0, input.Length, "校验来源 APK"));
        string hash = Hash(input, cancellationToken);
        if (expectedSha256 != null && !hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("来源 APK 已变化，与此输出项目记录的 SHA-256 不一致。请使用原安装包，或为新安装包选择另一个输出目录。");
        string cache = Path.Combine(CacheDirectory, hash);
        lock (Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GameProjectWorkspace.EnsureNoLinks(cache);
            if (ValidateCache(cache, hash, cancellationToken))
            {
                progress?.Report(new(1, 1, 0, 0, "已校验 APK 资源缓存"));
                return new(cache, hash);
            }
            input.Position = 0;
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            var entries = ValidateArchive(archive);
            Directory.CreateDirectory(CacheDirectory);
            string staging = Path.Combine(CacheDirectory, ".extract-" + Guid.NewGuid().ToString("N"));
            string? oldCache = null;
            Directory.CreateDirectory(staging);
            try
            {
                long total = entries.Sum(entry => entry.Entry.Length), completedBytes = 0;
                int completedFiles = 0;
                var files = new Dictionary<string, CacheFile>(StringComparer.Ordinal);
                byte[] buffer = new byte[128 * 1024];
                foreach (var (entry, relative) in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string target = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var reader = entry.Open();
                    using var writer = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long length = 0;
                    int read;
                    while ((read = reader.Read(buffer)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        length = checked(length + read);
                        if (length > entry.Length) throw new InvalidDataException($"APK 条目长度不符：{relative}");
                        writer.Write(buffer, 0, read); digest.AppendData(buffer, 0, read);
                    }
                    if (length != entry.Length) throw new InvalidDataException($"APK 条目不完整：{relative}");
                    files.Add(relative, new(length, Convert.ToHexString(digest.GetHashAndReset())));
                    completedBytes += length;
                    progress?.Report(new(++completedFiles, entries.Count, completedBytes, total, $"提取 APK：{relative}"));
                }
                input.Position = 0;
                if (Hash(input, cancellationToken) != hash)
                    throw new IOException("APK 在提取过程中发生变化，请重新加载。");
                // The cache is separate from incremental output and can be rebuilt from the APK.
                File.WriteAllText(Path.Combine(staging, CacheManifestName), JsonSerializer.Serialize(new CacheManifest(hash, files)));
                cancellationToken.ThrowIfCancellationRequested();
                GameProjectWorkspace.EnsureNoLinks(cache);
                if (Directory.Exists(cache))
                {
                    oldCache = Path.Combine(CacheDirectory, ".replaced-" + Guid.NewGuid().ToString("N"));
                    Directory.Move(cache, oldCache);
                }
                try { Directory.Move(staging, cache); }
                catch
                {
                    if (oldCache != null) { Directory.Move(oldCache, cache); oldCache = null; }
                    throw;
                }
                return new(cache, hash);
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                if (oldCache != null) Directory.Delete(oldCache, recursive: true);
            }
        }
    }

    private static List<(ZipArchiveEntry Entry, string Relative)> ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count > MaximumEntries) throw new InvalidDataException("APK 条目数量超过支持范围。");
        var entries = new List<(ZipArchiveEntry, string)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            string name = entry.FullName;
            bool directory = name.EndsWith('/');
            string normalized = directory ? name.TrimEnd('/') : name;
            string[] parts = normalized.Split('/');
            if (normalized.Length == 0 || name.Contains('\\') || parts.Any(part => part.Length == 0 || part is "." or ".."))
                throw new InvalidDataException($"APK 包含不可用路径：{name}");
            // SDK metadata can contain names Windows cannot extract; editors use assets and lib only.
            if (!normalized.StartsWith("assets/", StringComparison.Ordinal) &&
                !normalized.StartsWith("lib/", StringComparison.Ordinal) && normalized is not ("assets" or "lib")) continue;
            if (parts.Any(InvalidComponent)) throw new InvalidDataException($"APK 资源包含不可用路径：{name}");
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType is not (0 or 0x8000 or 0x4000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"APK 不支持链接或特殊条目：{name}");
            if (!names.Add(normalized)) throw new InvalidDataException($"APK 存在重复或大小写冲突路径：{name}");
            if (directory) continue;
            files.Add(normalized);
            total = checked(total + entry.Length);
            if (entry.Length < 0 || total > MaximumExpandedBytes) throw new InvalidDataException("APK 解压大小超过 16 GiB。");
            entries.Add((entry, normalized));
        }
        foreach (string name in names)
        {
            int slash = name.IndexOf('/');
            while (slash >= 0)
            {
                if (files.Contains(name[..slash])) throw new InvalidDataException($"APK 文件与目录冲突：{name}");
                slash = name.IndexOf('/', slash + 1);
            }
        }
        if (!entries.Any(pair => pair.Item2.StartsWith("assets/", StringComparison.Ordinal)))
            throw new InvalidDataException("APK 没有 assets 资源，不能作为游戏项目加载。");
        return entries;
    }

    private static bool InvalidComponent(string part)
    {
        if (string.IsNullOrEmpty(part) || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
            part.Any(c => c < 32 || "<>:\"|?*".Contains(c))) return true;
        string stem = part.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9');
    }

    private static bool ValidateCache(string root, string hash, CancellationToken token)
    {
        string path = Path.Combine(root, CacheManifestName);
        if (!File.Exists(path)) return false;
        GameProjectWorkspace.EnsureNoLinks(path);
        try
        {
            var manifest = JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(path));
            if (manifest?.Sha256 != hash || manifest.Files == null || manifest.Files.Count > MaximumEntries ||
                !Directory.Exists(Path.Combine(root, "assets"))) return false;
            foreach (var (relative, file) in manifest.Files)
            {
                token.ThrowIfCancellationRequested();
                if (relative.Split('/').Any(InvalidComponent) || relative.Contains('\\')) return false;
                string resource = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                GameProjectWorkspace.EnsureNoLinks(resource);
                if (!File.Exists(resource)) return false;
                using var stream = File.OpenRead(resource);
                if (stream.Length != file.Length || Hash(stream, token) != file.Sha256) return false;
            }
            int count = 0;
            var directories = new Stack<string>(); directories.Push(root);
            while (directories.TryPop(out string? directory))
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                if ((attributes & FileAttributes.Directory) != 0) directories.Push(entry); else count++;
            }
            return count == manifest.Files.Count + 1;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return false; }
    }

    private static string Hash(Stream input, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024];
        int read;
        while ((read = input.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private sealed record CacheFile(long Length, string Sha256);
    private sealed record CacheManifest(string Sha256, Dictionary<string, CacheFile> Files);
}
