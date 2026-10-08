using System.Security.Cryptography;

namespace WC4MapEditor.Core.Assets;

public enum ProjectOutputChangeKind { Added, Modified, Identical, Unreadable }
public sealed record ProjectOutputChange(string RelativePath, string OutputPath, string SourcePath,
    ProjectOutputChangeKind Kind, long OutputBytes, long? SourceBytes, string? Error);

public static class ProjectOutputChanges
{
    // Only visit physical output files. Unsaved editor drafts are deliberately not part of this snapshot.
    public static IReadOnlyList<ProjectOutputChange> Scan(GameProjectWorkspace project, CancellationToken cancellationToken = default)
    {
        var result = new List<ProjectOutputChange>();
        var directories = new Stack<string>(); directories.Push(project.OutputRoot);
        while (directories.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested(); GameProjectWorkspace.EnsureNoLinks(directory);
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                GameProjectWorkspace.EnsureNoLinks(path);
                if (Directory.Exists(path)) { directories.Push(path); continue; }
                if (path.Equals(Path.Combine(project.OutputRoot, GameProjectWorkspace.ManifestFileName), GameProjectWorkspace.PathComparison)) continue;
                project.ValidateOutputPath(path);
                string source = project.GetSourcePath(path), relative = Path.GetRelativePath(project.OutputRoot, path).Replace('\\', '/');
                long outputBytes = 0; long? sourceBytes = null;
                try
                {
                    GameProjectWorkspace.EnsureNoLinks(source);
                    var output = new FileInfo(path); var original = new FileInfo(source);
                    outputBytes = output.Length;
                    FileAttributes? attributes;
                    try { attributes = File.GetAttributes(source); }
                    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { attributes = null; }
                    if (attributes == null)
                    {
                        result.Add(new(relative, path, source, ProjectOutputChangeKind.Added, outputBytes, null, null));
                        continue;
                    }
                    if (attributes.Value.HasFlag(FileAttributes.Directory))
                        throw new IOException("来源同路径是目录，不能与输出文件比较。");
                    sourceBytes = original.Length;
                    var modified = output.LastWriteTimeUtc; var sourceModified = original.LastWriteTimeUtc;
                    bool equal = outputBytes == sourceBytes && Fingerprint(path, cancellationToken).AsSpan().SequenceEqual(Fingerprint(source, cancellationToken));
                    output.Refresh(); original.Refresh();
                    if (!output.Exists || !original.Exists || output.Length != outputBytes || original.Length != sourceBytes ||
                        output.LastWriteTimeUtc != modified || original.LastWriteTimeUtc != sourceModified)
                        throw new IOException("比较期间文件已变化，请刷新后再核对。");
                    result.Add(new(relative, path, source, equal ? ProjectOutputChangeKind.Identical : ProjectOutputChangeKind.Modified, outputBytes, sourceBytes, null));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { result.Add(new(relative, path, source, ProjectOutputChangeKind.Unreadable, outputBytes, sourceBytes, ex.Message)); }
            }
        }
        return result.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static byte[] Fingerprint(string path, CancellationToken token)
    {
        using var stream = File.OpenRead(path); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024]; int count;
        while ((count = stream.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
        return hash.GetHashAndReset();
    }
}
