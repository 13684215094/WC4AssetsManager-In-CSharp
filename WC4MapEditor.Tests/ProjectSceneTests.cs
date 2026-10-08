using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers.World;
using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Services;

static class ProjectSceneTests
{
    public static void Run(Action<string, Action> test)
    {
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
