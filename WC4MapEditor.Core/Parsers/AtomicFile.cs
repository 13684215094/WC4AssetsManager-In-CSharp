namespace WC4MapEditor.Core.Parsers;

public static class AtomicFile
{
    public static void WriteAllWithStringTable(WC4MapEditor.Core.Config.StringTableParser? table, params (string Path, byte[] Bytes)[] files)
    {
        byte[]? strings = table?.SerializePending();
        WriteAll(strings == null ? files : files.Append((table!.FilePath, strings)).ToArray());
        if (strings != null) table!.AcceptSaved(strings);
        WC4MapEditor.Core.Assets.AssetManager.Default.InvalidateData();
    }

    public static void WriteAll(params (string Path, byte[] Bytes)[] files)
    {
        var staged = new List<(string Path, string Temporary, string Backup)>();
        int installed = 0;
        bool cleanup = true;
        try
        {
            var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var file in files)
            {
                string path = Path.GetFullPath(file.Path);
                if (!paths.Add(path) || Directory.Exists(path))
                    throw new IOException($"Invalid or duplicate output file: {path}");
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                string backup = temporary + ".bak";
                staged.Add((path, temporary, backup));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(file.Bytes);
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(path)) File.Copy(path, backup);
            }
            foreach (var file in staged)
            {
                File.Move(file.Temporary, file.Path, overwrite: true);
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
                    if (File.Exists(file.Temporary)) File.Delete(file.Temporary);
                    if (File.Exists(file.Backup)) File.Delete(file.Backup);
                }
        }
    }

    public static void Write(string path, ReadOnlySpan<byte> bytes)
    {
        string destination = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
