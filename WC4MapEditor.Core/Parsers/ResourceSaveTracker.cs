using System.Runtime.CompilerServices;

namespace WC4MapEditor.Core.Parsers;

// Update only the document that prepared a save, after the entire batch succeeds.
internal static class ResourceSaveTracker
{
    private sealed record Pending(Action Verify, Action<byte[]> Accept);
    private static readonly ConditionalWeakTable<byte[], Pending> Prepared = new();

    public static void Register(byte[] bytes, Action verify, Action<byte[]> accept)
        => Prepared.Add(bytes, new(verify, accept));

    public static void Verify(IEnumerable<(string Path, byte[] Bytes)> files)
    {
        foreach (var file in files)
            if (Prepared.TryGetValue(file.Bytes, out var pending)) pending.Verify();
    }

    public static void Accept(IEnumerable<(byte[] Candidate, byte[] Saved)> files)
    {
        foreach (var file in files)
            if (Prepared.TryGetValue(file.Candidate, out var pending))
            {
                Prepared.Remove(file.Candidate);
                pending.Accept(file.Saved);
            }
    }
}
