using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;

namespace WC4MapEditor.Core.Services;

public static class MapEditingBinding
{
    public static void Bind(MapData map, FileStateManager fileState, string sceneType,
        CommandManager commands, EditModeManager modes, HexInfoService hexInfo, HexSelector selection)
    {
        fileState.OpenFile(map, map.FilePath, sceneType);
        var context = commands.GetContext() ?? new CommandContext();
        context.MapData = map;
        context.UndoManager = fileState.UndoManager;
        commands.SetContext(context);
        hexInfo.SetMapData(map);
        modes.SetUndoManager(fileState.UndoManager);
        modes.SetSceneType(EditModeManager.GetSceneTypeForMap(map));
        modes.Initialize(map);
        var available = modes.GetAvailableModes(EditModeManager.GetSceneTypeForMap(map));
        if (!available.Contains(modes.CurrentMode))
            modes.SwitchMode(available.FirstOrDefault());
        selection.ClearSelection();
        selection.CancelSelectionRect();
    }

    public static void Unbind(MapData? map, CommandManager commands, EditModeManager modes, HexInfoService hexInfo)
    {
        var context = commands.GetContext();
        if (map == null || !ReferenceEquals(context?.MapData, map)) return;
        context!.MapData = null;
        context.UndoManager = null;
        hexInfo.SetMapData(null);
        modes.Deinitialize();
    }
}
