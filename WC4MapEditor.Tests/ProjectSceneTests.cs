using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers.World;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Services;

static class ProjectSceneTests
{
    public static void Run(Action<string, Action> test)
    {
        test("binding A to B redirects console modifiers hex info and undo to B", () =>
        {
            var commands = CommandManager.Instance;
            var modes = EditModeManager.Instance;
            var hexInfo = new HexInfoService();
            var selector = new HexSelector();
            var first = BTLParser.CreateNew(7, 3);
            var second = BTLParser.CreateNew(4, 2);
            first.Armies.Add(Army.CreateDefault(0));
            second.Armies.Add(Army.CreateDefault(0));
            var firstState = new FileStateManager();
            var secondState = new FileStateManager();
            var oldContext = commands.GetContext();
            int notifications = 0;
            void OnModified() => notifications++;
            CliCommandHost.Instance.DataModified += OnModified;
            try
            {
                MapEditingBinding.Bind(first, firstState, "stage", commands, modes, hexInfo, selector);
                var before = first.GetTerrain(0);
                var after = before;
                after.Reserved3 = 81;
                firstState.UndoManager.ExecuteAndRecord(new TerrainChangeCommand(first, "A edit", [(0, 0, before, after)]));
                selector.AddToSelection(6, 2, 7, 3);
                selector.BeginSelectionRect(1, 1);
                MapEditingBinding.Bind(second, secondState, "stage", commands, modes, hexInfo, selector);
                Check(!selector.HasSelection && !selector.IsSelectionRectActive && hexInfo.GetHexCellInfo(0, 0)?.MapWidth == 4,
                    "selection or hex info still targets A");
                Check(modes.GetModifier<ArmyModifier>()!.GetDataAt(0, 0) is Army, "modifier not rebound to B");
                Check(CliCommandHost.Instance.Execute("army remove-all") == 0 && first.Armies.Count == 1 && second.Armies.Count == 0 &&
                    notifications == 1, "console removed A's army after switching to B");
                Check(second.IsModified && secondState.IsDirty && secondState.Undo() && second.Armies.Count == 1 &&
                    first.Armies.Count == 1 && secondState.Redo() && second.Armies.Count == 0, "console delete did not use B's undo or dirty state");
                var bAfter = second.GetTerrain(0); bAfter.Reserved3 = 92;
                secondState.UndoManager.ExecuteAndRecord(new TerrainChangeCommand(second, "B edit", [(0, 0, second.GetTerrain(0), bAfter)]));
                Check(secondState.Undo() && first.GetTerrain(0).Reserved3 == 81 && second.GetTerrain(0).Reserved3 == 0,
                    "B undo modified A");
                MapEditingBinding.Bind(first, firstState, "stage", commands, modes, hexInfo, selector);
                Check(firstState.Undo() && first.GetTerrain(0).Reserved3 == 0 && secondState.Redo() && second.GetTerrain(0).Reserved3 == 92,
                    "switching back discarded either document's undo history");
                MapEditingBinding.Unbind(second, commands, modes, hexInfo);
                Check(ReferenceEquals(commands.GetContext()!.MapData, first), "disposing an inactive scene unbound current A");
                MapEditingBinding.Unbind(first, commands, modes, hexInfo);
                Check(commands.GetContext()!.MapData == null && hexInfo.GetHexCellInfo(0, 0) == null && !modes.IsInitialized,
                    "closed map remained a command target");
            }
            finally
            {
                CliCommandHost.Instance.DataModified -= OnModified;
                commands.SetContext(oldContext ?? new CommandContext());
                modes.Deinitialize();
            }
        });

        test("console army list removal and trap removal work on BTL v3 with document undo", () =>
        {
            var map = BTLParser.CreateNew(4, 2); map.Header.BtlVersion = 3;
            map.ArmiesV3.Add(Army_3.CreateDefault(0));
            map.Traps.Add(Trap.CreateDefault(0));
            var state = new FileStateManager();
            var commands = CommandManager.Instance; var modes = EditModeManager.Instance;
            var previous = commands.GetContext();
            using var output = new StringWriter();
            CliCommandHost.Instance.SetOutput(output);
            try
            {
                MapEditingBinding.Bind(map, state, "stage", commands, modes, new HexInfoService(), new HexSelector());
                Check(CliCommandHost.Instance.Execute("army list") == 0 && output.ToString().Contains("部队总数: 1"), "v3 army list was empty");
                Check(CliCommandHost.Instance.Execute("army remove-all") == 0 && map.ArmiesV3.Count == 0 && state.Undo() &&
                    map.ArmiesV3.Count == 1 && state.Redo() && map.ArmiesV3.Count == 0, "v3 army deletion or history failed");
                Check(CliCommandHost.Instance.Execute("trap remove-all") == 0 && map.Traps.Count == 0 && state.Undo() && map.Traps.Count == 1,
                    "trap removal did not use document undo");
            }
            finally
            {
                commands.SetContext(previous ?? new CommandContext()); modes.Deinitialize(); CliCommandHost.Instance.SetOutput(Console.Out);
            }
        });

        test("disposing an old scene's command registration retains the replacement handler", () =>
        {
            var commands = new CommandManager();
            int oldCalls = 0, newCalls = 0;
            Action<string[]> oldHandler = _ => oldCalls++;
            Action<string[]> newHandler = _ => newCalls++;
            commands.RegisterCommand("undo", oldHandler);
            commands.RegisterCommand("undo", newHandler);
            commands.UnregisterCommand("undo", oldHandler);
            commands.ExecuteCommand("undo");
            Check(newCalls == 1 && oldCalls == 0, "old scene removed or invoked the replacement command");
            commands.UnregisterCommand("undo", newHandler);
            Check(!commands.HasCommand("undo"), "closed scene's command remains registered");
        });

        test("edit modes follow terrain source instead of conquest filename", () =>
        {
            var modes = WC4MapEditor.Core.Modifiers.EditModeManager.Instance;
            var local = BTLParser.CreateNew(7, 3);
            local.FilePath = "conquest-local.btl";
            var external = BTLParser.CreateNew(7, 3, mapNumber: 1);
            external.FilePath = "stage-external.btl";
            string localType = WC4MapEditor.Core.Modifiers.EditModeManager.GetSceneTypeForMap(local);
            string externalType = WC4MapEditor.Core.Modifiers.EditModeManager.GetSceneTypeForMap(external);
            Check(modes.GetAvailableModes(localType).Contains(WC4MapEditor.Core.Modifiers.EditMode.TerrainPaint) &&
                !modes.GetAvailableModes(externalType).Contains(WC4MapEditor.Core.Modifiers.EditMode.TerrainPaint), "filename controls terrain editing");
        });

        test("scene undo and redo remain attached to each document across release and restore", () =>
        {
            var manager = RenderSceneManager.Instance;
            manager.ClearAllScenes();
            try
            {
                var first = BTLParser.CreateNew(7, 3);
                int a = manager.CreateScene("a", "", RenderSceneType.Stage);
                manager.SetSceneMapData(a, first);
                var state = manager.GetScene(a)!.FileState!;
                var before = first.GetTerrain(0);
                var after = before;
                after.Reserved3 = 81;
                state.UndoManager.ExecuteAndRecord(new TerrainChangeCommand(first, "first", [(0, 0, before, after)]));
                int b = manager.CreateScene("b", "", RenderSceneType.Stage);
                var second = BTLParser.CreateNew(7, 3);
                manager.SetSceneMapData(b, second);
                manager.ActivateScene(b);
                manager.ReleaseSceneMemory(a);
                var loaded = manager.LoadSceneFromDisk(a)!;
                var restored = new FileStateManager();
                restored.OpenFile(loaded, "", "stage");
                Check(restored.Undo() && loaded.GetTerrain(0).Equals(before) && second.GetTerrain(0).Reserved3 == 0, "history cleared or targets another scene");
                manager.ActivateScene(b);
                manager.ReleaseSceneMemory(a);
                restored.OpenFile(manager.LoadSceneFromDisk(a)!, "", "stage");
                Check(restored.Redo() && loaded.GetTerrain(0).Reserved3 == 81, "redo lost on scene switch");
            }
            finally { manager.ClearAllScenes(); }
        });

        test("scene history eviction respects byte budget and retains pending map data on disk", () =>
        {
            var manager = RenderSceneManager.Instance;
            manager.ClearAllScenes();
            long budget = manager.RetainedHistoryBudgetBytes;
            try
            {
                manager.RetainedHistoryBudgetBytes = 0;
                var map = BTLParser.CreateNew(7, 3);
                int id = manager.CreateScene("large", "", RenderSceneType.Stage);
                manager.SetSceneMapData(id, map);
                var after = map.GetTerrain(0);
                after.Reserved3 = 73;
                manager.GetScene(id)!.FileState!.UndoManager.ExecuteAndRecord(new TerrainChangeCommand(map, "edit", [(0, 0, map.GetTerrain(0), after)]));
                manager.ActivateScene(manager.CreateScene("other", "", RenderSceneType.Stage));
                manager.ReleaseSceneMemory(id);
                Check(manager.GetScene(id)!.MapData == null && manager.GetScene(id)!.FileState == null, "budget ignored");
                var loaded = manager.LoadSceneFromDisk(id)!;
                Check(loaded.IsModified && loaded.GetTerrain(0).Reserved3 == 73 && manager.GetScene(id)!.FileState!.UndoManager.UndoCount == 0, "eviction lost unsaved map or kept old history");
            }
            finally { manager.RetainedHistoryBudgetBytes = budget; manager.ClearAllScenes(); }
        });

        test("terrain province belong and delegate commands all participate in the history budget", () =>
        {
            var map = WorldParser.CreateNew(7, 3);
            var commands = new IUndoableCommand[]
            {
                new TerrainChangeCommand(map, "terrain", [(0, 0, new(), new())]),
                new ProvinceChangeCommand(map, "province", [(0, 0, new(), new())]),
                new BelongChangeCommand(map, "belong", [(0, 0, 0, 1)]),
                new TerrainFullSnapshotCommand(map, "terrain full", new TerrainData[21]),
                new ProvinceFullSnapshotCommand(map, "province full", new Province[21]),
                new BelongFullSnapshotCommand(map, "belong full", new byte[21]),
                new DelegateCommand("delegate", () => { }, () => { }, 1024)
            };
            foreach (var command in commands)
            {
                Check(command.EstimatedBytes > 0, "missing memory estimate");
                var history = new UndoManager(maxResizeSnapshotBytes: command.EstimatedBytes);
                history.Record(command);
                history.Record(command);
                Check(history.UndoCount == 1 && history.EstimatedBytes <= command.EstimatedBytes, "command exceeded budget");
                Check(history.Undo() && history.Redo() && history.EstimatedBytes <= command.EstimatedBytes, "undo redo budget or state");
            }
            var failure = new UndoManager();
            failure.Record(new DelegateCommand("failure", () => { }, () => throw new IOException("failure")));
            try { failure.Undo(); } catch (IOException) { }
            Check(failure.UndoCount == 1 && failure.RedoCount == 0, "failed undo discarded command");
        });

        test("switching with a full history budget protects the target and evicts the old scene after activation", () =>
        {
            var manager = RenderSceneManager.Instance;
            manager.ClearAllScenes();
            long budget = manager.RetainedHistoryBudgetBytes;
            try
            {
                manager.RetainedHistoryBudgetBytes = 0;
                int oldId = manager.CreateScene("old", "", RenderSceneType.Stage);
                int targetId = manager.CreateScene("target", "", RenderSceneType.Stage);
                foreach (int id in new[] { oldId, targetId })
                {
                    var map = BTLParser.CreateNew(7, 3);
                    manager.SetSceneMapData(id, map);
                    var before = map.GetTerrain(0);
                    var after = before;
                    after.Reserved3 = (byte)id;
                    manager.GetScene(id)!.FileState!.UndoManager.ExecuteAndRecord(
                        new TerrainChangeCommand(map, "edit", [(0, 0, before, after)]));
                }
                manager.ActivateScene(oldId);
                var target = manager.LoadSceneFromDisk(targetId)!;
                manager.ReleaseSceneMemory(oldId, targetId);
                Check(ReferenceEquals(target, manager.GetScene(targetId)!.MapData), "release evicted target while switching");
                manager.ActivateScene(targetId);
                Check(manager.GetScene(oldId)!.MapData == null && manager.GetScene(oldId)!.HistoryEvicted,
                    "switch did not enforce inactive history budget or report eviction");
                var old = manager.LoadSceneFromDisk(oldId)!;
                Check(old.IsModified && old.GetTerrain(0).Reserved3 == oldId, "eviction lost pending old scene");
            }
            finally { manager.RetainedHistoryBudgetBytes = budget; manager.ClearAllScenes(); }
        });

        test("recording an operation too large for history still marks the document dirty and clearing history does not", () =>
        {
            var map = BTLParser.CreateNew(7, 3);
            var state = new FileStateManager();
            state.OpenFile(map, "", "stage");
            state.UndoManager.ExecuteAndRecord(new DelegateCommand("oversized", () => { }, () => { },
                MapLimits.MaxOperationBytes + 1));
            Check(!state.UndoManager.CanUndo && state.IsDirty && map.IsModified, "trimmed edit appears saved");
            state.MarkSaved();
            state.UndoManager.Clear();
            Check(!state.IsDirty && !map.IsModified, "clearing saved history marks content dirty");
            state.CloseFile();
        });

        test("world province edit mode supports undo save and incremental reload", () =>
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
            string source = Path.Combine(directory, "source");
            Directory.CreateDirectory(Path.Combine(source, "assets"));
            var modes = WC4MapEditor.Core.Modifiers.EditModeManager.Instance;
            try
            {
                WorldParser.SaveToFile(WorldParser.CreateNew(7, 3), Path.Combine(source, "assets/world.bin"));
                var project = GameProjectWorkspace.Create(source, Path.Combine(directory, "output"));
                var doc = ProjectMapDocument.Load(project, Path.Combine(project.AssetsRoot, "world.bin"));
                modes.Initialize(doc.Map);
                var history = new UndoManager();
                modes.SetUndoManager(history);
                modes.SetSceneType("world");
                Check(modes.GetAvailableModes("world").Contains(WC4MapEditor.Core.Modifiers.EditMode.ProvinceEdit), "world province mode missing");
                modes.RecordMultiCellProvinceChange("province", () => doc.Map.SetProvince(0, Province.Create(88)));
                history.Undo();
                Check(doc.Map.GetProvince(0).ProvinceValue == 0xFFFF, "province undo failed");
                history.Redo();
                doc.Save(doc.Map.FilePath);
                Check(ProjectMapDocument.Load(project, doc.Map.FilePath).Map.GetProvince(0).ProvinceValue == 88, "province save lost");
            }
            finally { modes.Deinitialize(); Directory.Delete(directory, true); }
        });

        test("scene caching retains output path and latest edits across save and reload", () =>
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "source", "assets"));
            var manager = RenderSceneManager.Instance;
            manager.ClearAllScenes();
            try
            {
                var project = GameProjectWorkspace.Create(Path.Combine(directory, "source"), Path.Combine(directory, "output"));
                foreach (bool world in new[] { false, true })
                {
                    string path = Path.Combine(project.AssetsRoot, world ? "world.bin" : "stage.btl");
                    var map = world ? WorldParser.CreateNew(5, 3) : BTLParser.CreateNew(5, 3);
                    if (world) WorldParser.SaveToFile(map, path); else BTLParser.SaveToFile(map, path);
                    var edited = map.GetTerrain(0);
                    edited.Reserved3 = 73;
                    map.SetTerrain(0, edited);
                    map.IsModified = true;
                    int id = manager.CreateScene("project", path, world ? RenderSceneType.Test : RenderSceneType.Stage, project);
                    manager.SetSceneMapData(id, map, ProjectMapDocument.Attach(project, map));
                    Check(manager.CacheSceneToDisk(id), "cache failed");
                    Check(map.FilePath == path && map.IsModified, "cache replaced output path or dirty state");
                    manager.ReleaseSceneMemory(id);
                    var loaded = manager.LoadSceneFromDisk(id)!;
                    Check(loaded.FilePath == path && loaded.GetTerrain(0).Reserved3 == 73 && loaded.IsModified, "cache reload lost output path, edit or dirty state");
                    var fileState = new FileStateManager();
                    fileState.OpenFile(loaded, loaded.FilePath, world ? "world" : "stage");
                    Check(fileState.IsDirty, "restored edits appear saved");
                    string renamed = Path.Combine(project.AssetsRoot, world ? "renamed.bin" : "renamed.btl");
                    if (world) WorldParser.SaveToFile(loaded, renamed); else BTLParser.SaveToFile(loaded, renamed);
                    manager.MarkSceneSaved(id, renamed);
                    fileState.MarkSaved(renamed);
                    Check(!fileState.IsDirty && !loaded.IsModified && fileState.CurrentFilePath == renamed, "saved file state has old path or dirty flag");
                    Check(manager.GetScene(id)!.Project == project && manager.GetScene(id)!.MapFilePath == renamed, "scene lost project or new output path");
                    manager.ReleaseSceneMemory(id);
                    loaded = manager.LoadSceneFromDisk(id)!;
                    Check(loaded.FilePath == renamed && !loaded.IsModified, "save/reload returned to old path or dirty flag");
                }
            }
            finally
            {
                manager.ClearAllScenes();
                Directory.Delete(directory, recursive: true);
            }
        });

        test("new project map survives scene cache before its first save", () =>
        {
            var manager = RenderSceneManager.Instance;
            manager.ClearAllScenes();
            try
            {
                foreach (bool world in new[] { false, true })
                {
                    var map = world ? WorldParser.CreateNew(7, 3) : BTLParser.CreateNew(7, 3);
                    map.IsModified = true;
                    int id = manager.CreateScene("new", "", world ? RenderSceneType.Test : RenderSceneType.Stage);
                    manager.SetSceneMapData(id, map);
                    Check(manager.CacheSceneToDisk(id), "new map cache failed");
                    manager.ReleaseSceneMemory(id);
                    var loaded = manager.LoadSceneFromDisk(id)!;
                    Check(loaded.FilePath == "" && loaded.IsModified && loaded.FileKind == map.FileKind && loaded.MapWidth == 7, "unsaved map identity was lost");
                }
            }
            finally { manager.ClearAllScenes(); }
        });

        test("failed linked-map cache retains in-memory edits and prevents scene switch", () =>
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
            string source = Path.Combine(directory, "source");
            Directory.CreateDirectory(Path.Combine(source, "assets"));
            var manager = RenderSceneManager.Instance;
            manager.ClearAllScenes();
            try
            {
                WorldParser.SaveToFile(WorldParser.CreateNew(13, 9), Path.Combine(source, "assets", "world.bin"));
                BTLParser.SaveToFile(BTLParser.CreateNew(5, 3, mapNumber: 1), Path.Combine(source, "assets", "stage.btl"));
                var project = GameProjectWorkspace.Create(source, Path.Combine(directory, "output"));
                var doc = ProjectMapDocument.Load(project, Path.Combine(project.AssetsRoot, "stage.btl"));
                int id = manager.CreateScene("linked", doc.Map.FilePath, RenderSceneType.Stage, project);
                manager.SetSceneMapData(id, doc.Map, doc);
                manager.ActivateScene(id);
                Check(manager.CacheSceneToDisk(id), "initial cache failed");
                var terrain = doc.Map.GetTerrain(0);
                terrain.Reserved3 = 73;
                doc.Map.SetTerrain(0, terrain);
                Check(!manager.CacheSceneToDisk(id), "linked terrain edits were silently discarded");
                manager.ReleaseSceneMemory(id);
                Check(manager.GetScene(id)!.MapData == doc.Map, "failed cache released edited map");
                int target = manager.CreateScene("other", "", RenderSceneType.Test, project);
                bool rejected = false;
                try { manager.RequestSceneSwitch(target); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected && manager.CurrentSceneId == id && doc.Map.GetTerrain(0).Reserved3 == 73,
                    "failed switch lost edits or changed active scene");
            }
            finally
            {
                manager.ClearAllScenes();
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
