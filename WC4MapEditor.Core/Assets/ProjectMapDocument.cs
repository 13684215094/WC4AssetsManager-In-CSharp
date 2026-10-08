using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers.World;
using System.Runtime.CompilerServices;

namespace WC4MapEditor.Core.Assets;

public sealed class ProjectMapDocument
{
    private static readonly ConditionalWeakTable<MapData, ProjectMapDocument> Documents = new();
    private readonly GameProjectWorkspace _project;
    private byte[]? _externalTerrain;
    private (int MapNumber, int ClipX, int ClipY, int Width, int Height) _capture;

    public MapData Map { get; }
    public string? ExternalWorldPath { get; private set; }

    private ProjectMapDocument(GameProjectWorkspace project, MapData map)
    {
        _project = project;
        Map = map;
        _capture = (map.Header.MapNumber, map.Header.MapClipX, map.Header.MapClipY, map.MapWidth, map.MapHeight);
        if (map.FileKind == MapFileKind.Battle && map.Header.MapNumber != 0) LoadExternalTerrain();
    }

    public static ProjectMapDocument Load(GameProjectWorkspace project, string path)
    {
        string editable = project.GetEditablePath(path);
        var map = Path.GetExtension(editable).Equals(".btl", StringComparison.OrdinalIgnoreCase)
            ? BTLParser.LoadFromFile(editable) : WorldParser.LoadFromFile(editable);
        return Attach(project, map);
    }

    public static ProjectMapDocument Attach(GameProjectWorkspace project, MapData map)
    {
        string? editable = !string.IsNullOrEmpty(map.FilePath) ? project.GetEditablePath(map.FilePath) : null;
        var document = Documents.GetValue(map, key => new(project, key));
        if (!string.Equals(document._project.OutputRoot, project.OutputRoot,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("This map is already attached to another project.");
        document.ValidateChanges();
        if (editable != null) map.FilePath = editable;
        return document;
    }

    private void LoadExternalTerrain()
    {
        if (Map.Header.MapNumber != 1)
            throw new InvalidDataException("Only external world map 1 is defined for the WC4 1.30.0 project.");
        ExternalWorldPath = Path.Combine(_project.AssetsRoot, "world.bin");
        _project.ValidateOutputPath(ExternalWorldPath);
        var world = WorldParser.LoadFromFile(ExternalWorldPath);
        int x = Map.Header.MapClipX, y = Map.Header.MapClipY;
        if (x < 0 || x >= world.MapWidth || y < 0 || (long)y + Map.MapHeight > world.MapHeight || Map.MapWidth > world.MapWidth)
            throw new InvalidDataException("The BTL capture is outside world.bin.");
        bool modified = Map.IsModified;
        // WC4 1.30.0 has capture windows that cross the world's right edge.
        for (int row = 0; row < Map.MapHeight; row++)
        for (int col = 0; col < Map.MapWidth; col++)
            Map.SetTerrain(col, row, world.GetTerrain((x + col) % world.MapWidth, y + row));
        Map.IsModified = modified;
        _externalTerrain = Map.TerrainsToBytes();
        _capture = (Map.Header.MapNumber, x, y, Map.MapWidth, Map.MapHeight);
    }

    public void ValidateChanges()
    {
        if (Map.FileKind == MapFileKind.Battle && Map.Header.MapNumber != _capture.MapNumber)
            throw new InvalidOperationException("Changing the BTL's terrain source requires linked map conversion, which is not supported here.");
        if (_externalTerrain != null)
        {
            var currentCapture = (Map.Header.MapNumber, Map.Header.MapClipX, Map.Header.MapClipY, Map.MapWidth, Map.MapHeight);
            if (currentCapture != _capture || !_externalTerrain.AsSpan().SequenceEqual(Map.TerrainsToBytes()))
                throw new InvalidOperationException("This BTL uses read-only terrain from world.bin. Edit world.bin separately; linked terrain or capture changes cannot be saved here.");
        }
    }

    public void Save(string path)
    {
        _project.ValidateOutputPath(path);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (Map.FileKind == MapFileKind.Battle ? extension != ".btl" :
            Map.FileKind != MapFileKind.World || extension is not (".bin" or ".dat"))
            throw new ArgumentException("Save BTL maps as .btl and world maps as .bin or .dat; format conversion is not supported here.");
        ValidateChanges();
        if (Map.FileKind == MapFileKind.Battle) BTLParser.SaveToFile(Map, path);
        else WorldParser.SaveToFile(Map, path);
    }
}
