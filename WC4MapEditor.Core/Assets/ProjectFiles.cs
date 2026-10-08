using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace WC4MapEditor.Core.Assets;

// Logical paths point into output. Resolve each read separately so partial overrides work.
public static class ProjectFiles
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, GameProjectWorkspace> Projects = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    internal static void Register(GameProjectWorkspace project)
    {
        lock (Gate)
        {
            foreach (string root in Projects.Keys.Where(root => !Directory.Exists(root)).ToArray()) Projects.Remove(root);
            Projects[project.OutputRoot] = project;
        }
    }

    public static GameProjectWorkspace? FindProject(string path)
    {
        string full = Path.GetFullPath(path);
        lock (Gate)
            return Projects.Values.Where(p => GameProjectWorkspace.IsWithin(full, p.OutputRoot))
                .OrderByDescending(p => p.OutputRoot.Length).FirstOrDefault();
    }

    public static string ResolveRead(string path)
        => FindProject(path)?.GetReadPath(path) ?? path;

    public static bool Exists(string? path) => !string.IsNullOrEmpty(path) && File.Exists(ResolveRead(path));
    public static byte[] ReadAllBytes(string path) => File.ReadAllBytes(ResolveRead(path));
    public static string ReadAllText(string path) => File.ReadAllText(ResolveRead(path));
    public static string ReadAllText(string path, Encoding encoding) => File.ReadAllText(ResolveRead(path), encoding);
    public static string[] ReadAllLines(string path) => File.ReadAllLines(ResolveRead(path));
    public static string[] ReadAllLines(string path, Encoding encoding) => File.ReadAllLines(ResolveRead(path), encoding);
    public static FileStream OpenRead(string path) => File.OpenRead(ResolveRead(path));
    public static Task<byte[]> ReadAllBytesAsync(string path, CancellationToken token = default)
        => File.ReadAllBytesAsync(ResolveRead(path), token);
    public static Task<string> ReadAllTextAsync(string path, CancellationToken token = default)
        => File.ReadAllTextAsync(ResolveRead(path), token);
    public static Task<string[]> ReadAllLinesAsync(string path, CancellationToken token = default)
        => File.ReadAllLinesAsync(ResolveRead(path), token);

    public static bool DirectoryExists(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var project = FindProject(path);
        if (project != null) GameProjectWorkspace.EnsureNoLinks(path);
        if (project?.IsIncremental == true)
        {
            string source = project.GetSourcePath(path);
            GameProjectWorkspace.EnsureNoLinks(source);
            return Directory.Exists(path) || Directory.Exists(source);
        }
        return Directory.Exists(path);
    }

    public static IEnumerable<string> EnumerateFiles(string directory, string pattern = "*", SearchOption option = SearchOption.TopDirectoryOnly)
    {
        var project = FindProject(directory);
        var paths = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (project?.IsIncremental == true)
        {
            string source = project.GetSourcePath(directory);
            GameProjectWorkspace.EnsureNoLinks(source);
            if (Directory.Exists(source))
                foreach (string file in EnumerateSafe(source, pattern, option))
                    paths[Path.GetRelativePath(source, file)] = Path.Combine(directory, Path.GetRelativePath(source, file));
        }
        if (Directory.Exists(directory))
            foreach (string file in project == null ? Directory.EnumerateFiles(directory, pattern, option) : EnumerateSafe(directory, pattern, option))
                paths[Path.GetRelativePath(directory, file)] = file;
        return paths.Values.Where(p => project == null || !p.Equals(Path.Combine(project.OutputRoot, GameProjectWorkspace.ManifestFileName), GameProjectWorkspace.PathComparison)).ToArray();
    }

    public static string[] GetFiles(string directory, string pattern = "*", SearchOption option = SearchOption.TopDirectoryOnly)
        => EnumerateFiles(directory, pattern, option).ToArray();

    private static IEnumerable<string> EnumerateSafe(string root, string pattern, SearchOption option)
    {
        GameProjectWorkspace.EnsureNoLinks(root);
        var directories = new Stack<string>();
        directories.Push(root);
        while (directories.TryPop(out string? directory))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Linked project entries are not supported: {entry}");
                if ((attributes & FileAttributes.Directory) != 0 && option == SearchOption.AllDirectories)
                    directories.Push(entry);
            }
            foreach (string file in Directory.EnumerateFiles(directory, pattern)) yield return file;
        }
    }

    internal static bool ContentEquals(string path, ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.SequenceEqual(right)) return true;
        string extension = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (extension == ".json")
            {
                var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
                return JsonNode.DeepEquals(JsonNode.Parse(Encoding.UTF8.GetString(left).TrimStart('\uFEFF'), documentOptions: options),
                    JsonNode.Parse(Encoding.UTF8.GetString(right).TrimStart('\uFEFF'), documentOptions: options));
            }
            if (extension == ".xml")
            {
                static XElement Parse(ReadOnlySpan<byte> bytes)
                {
                    string text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Trim();
                    if (text.StartsWith("<?xml", StringComparison.Ordinal)) text = text[(text.IndexOf("?>", StringComparison.Ordinal) + 2)..];
                    var root = XElement.Parse("<Resource>" + text + "</Resource>");
                    foreach (var element in root.Descendants())
                    {
                        var attributes = element.Attributes().OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).ToArray();
                        element.RemoveAttributes();
                        element.Add(attributes);
                    }
                    return root;
                }
                return XNode.DeepEquals(Parse(left), Parse(right));
            }
        }
        catch (Exception ex) when (ex is JsonException or System.Xml.XmlException or ArgumentException) { }
        return false;
    }
}
