namespace WC4MapEditor.Core.Assets;

public static class GameUiPreviewExport
{
    public static string ValidateDestination(string destination, string assetsRoot)
    {
        string full = Path.GetFullPath(destination);
        var project = ProjectFiles.FindProject(assetsRoot);
        string source = project?.SourceRoot ?? Path.GetFullPath(assetsRoot);
        if (GameProjectWorkspace.IsWithin(full, source) || project?.IsSourcePath(full) == true ||
            project != null && GameProjectWorkspace.IsWithin(full, project.OutputRoot))
            throw new InvalidOperationException("预览 PNG 请导出到游戏来源和修改输出以外的目录。");
        if (!Path.GetExtension(full).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("预览图需要 .png 扩展名。");
        GameProjectWorkspace.EnsureNoLinks(full);
        return full;
    }
}
