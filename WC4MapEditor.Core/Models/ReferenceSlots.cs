namespace WC4MapEditor.Core.Models;

public sealed record ReferenceSlotLists(IReadOnlyList<int> References, IReadOnlyList<int> Levels);

// Empty feature levels mean runtime-selected levels. Never manufacture levels while reordering.
public static class ReferenceSlots
{
    public static ReferenceSlotLists Move(IReadOnlyList<int> references, IReadOnlyList<int> levels, int index, int target)
    {
        Validate(references, levels);
        if ((uint)index >= references.Count || (uint)target >= references.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var ids = references.ToList(); var resultLevels = levels.ToList();
        int id = ids[index]; ids.RemoveAt(index); ids.Insert(target, id);
        if (resultLevels.Count > 0)
        {
            int level = resultLevels[index]; resultLevels.RemoveAt(index); resultLevels.Insert(target, level);
        }
        return new(ids, resultLevels);
    }

    public static ReferenceSlotLists Remove(IReadOnlyList<int> references, IReadOnlyList<int> levels, int index)
    {
        Validate(references, levels);
        if ((uint)index >= references.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var ids = references.ToList(); var resultLevels = levels.ToList();
        ids.RemoveAt(index);
        if (resultLevels.Count > 0) resultLevels.RemoveAt(index);
        return new(ids, resultLevels);
    }

    public static ReferenceSlotLists Set(IReadOnlyList<int> references, IReadOnlyList<int> levels, int index, int reference, int? level)
    {
        Validate(references, levels);
        if ((uint)index > references.Count || reference < 0 || level < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (levels.Count == 0 && references.Count > 0 && level.HasValue)
            throw new InvalidOperationException("现有特性没有显式等级。请先在高级数组中明确所有等级，再逐项选择等级。");
        if (levels.Count > 0 && !level.HasValue)
            throw new InvalidOperationException("已有显式等级，替换或新增时需要选择项目中定义的等级。");
        var ids = references.ToList(); var resultLevels = levels.ToList();
        if (index == ids.Count)
        {
            ids.Add(reference); if (level.HasValue) resultLevels.Add(level.Value);
        }
        else { ids[index] = reference; if (level.HasValue) resultLevels[index] = level.Value; }
        return new(ids, resultLevels);
    }

    private static void Validate(IReadOnlyList<int> references, IReadOnlyList<int> levels)
    {
        if (levels.Count != 0 && levels.Count != references.Count)
            throw new InvalidOperationException($"引用有 {references.Count} 项，等级有 {levels.Count} 项。请先在高级数组中明确修正，现有数据仍保留。");
    }
}
