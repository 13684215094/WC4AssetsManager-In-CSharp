namespace WC4MapEditor.Core.Parsers;

public static class AtomicFile
{
    public static void WriteAllWithStringTable(WC4MapEditor.Core.Config.StringTableParser? table, params (string Path, byte[] Bytes)[] files)
    {
        byte[]? strings = table?.SerializePending();
        WriteAll(strings == null ? files : files.Append((table!.FilePath, strings)).ToArray());
        WC4MapEditor.Core.Assets.AssetManager.Default.InvalidateData();
    }

    public static void WriteAll(params (string Path, byte[] Bytes)[] files)
    {
        var staged = new List<(string Path, string Temporary, string Backup, bool Delete)>();
        var accepted = new List<(byte[] Candidate, byte[] Saved)>();
        int installed = 0;
        bool cleanup = true;
        try
        {
            ResourceSaveTracker.Verify(files);
            var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var file in files)
            {
                string path = Path.GetFullPath(file.Path);
                if (!paths.Add(path) || Directory.Exists(path))
                    throw new IOException($"Invalid or duplicate output file: {path}");
                var project = WC4MapEditor.Core.Assets.ProjectFiles.FindProject(path);
                project?.ValidateOutputPath(path);
                bool delete = false;
                byte[] saved = file.Bytes;
                if (project?.IsIncremental == true)
                {
                    string source = project.GetSourcePath(path);
                    WC4MapEditor.Core.Assets.GameProjectWorkspace.EnsureNoLinks(source);
                    byte[]? original = File.Exists(source) ? File.ReadAllBytes(source) : null;
                    delete = original != null && WC4MapEditor.Core.Assets.ProjectFiles.ContentEquals(path, file.Bytes, original);
                    if (delete) saved = original!;
                    accepted.Add((file.Bytes, saved));
                    if (delete && !File.Exists(path)) continue;
                }
                else accepted.Add((file.Bytes, saved));
                if (!delete && File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(file.Bytes)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                string backup = temporary + ".bak";
                staged.Add((path, temporary, backup, delete));
                if (!delete)
                {
                    using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    stream.Write(file.Bytes);
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(path)) File.Copy(path, backup);
            }
            ResourceSaveTracker.Verify(files);
            foreach (var file in staged)
            {
                if (file.Delete) File.Delete(file.Path);
                else File.Move(file.Temporary, file.Path, overwrite: true);
                installed++;
            }
        }
        catch (Exception writeError)
        {
            // Multi-file replacement is not crash-atomic. Retain backups if
            // an I/O error also prevents restoring an already replaced file.
            var errors = new List<Exception> { writeError };
            foreach (var file in staged.Take(installed).Reverse())
            {
                try
                {
                    if (File.Exists(file.Backup)) File.Move(file.Backup, file.Path, overwrite: true);
                    else File.Delete(file.Path);
                }
                catch (Exception rollbackError) { errors.Add(rollbackError); cleanup = false; }
            }
            if (errors.Count > 1) throw new AggregateException("Save and rollback failed; .tmp.bak files were retained.", errors);
            throw;
        }
        finally
        {
            if (cleanup)
                foreach (var file in staged)
                {
                    try
                    {
                        if (File.Exists(file.Temporary)) File.Delete(file.Temporary);
                        if (File.Exists(file.Backup)) File.Delete(file.Backup);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        System.Diagnostics.Debug.WriteLine($"Cannot clean save staging files for {file.Path}: {ex.Message}");
                    }
                }
        }
        ResourceSaveTracker.Accept(accepted);
        if (staged.Count > 0) WC4MapEditor.Core.Assets.AssetManager.Default.InvalidateData();
    }

    public static void Write(string path, byte[] bytes) => WriteAll((path, bytes));
    public static void Write(string path, ReadOnlySpan<byte> bytes) => WriteAll((path, bytes.ToArray()));
}
