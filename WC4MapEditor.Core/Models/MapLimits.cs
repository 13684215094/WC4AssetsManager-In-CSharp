namespace WC4MapEditor.Core.Models;

public static class MapLimits
{
    // Editor resource budgets, not claims about Android runtime limits.
    public const int MaxCells = 1_000_000;
    public const int MaxFileBytes = 256 * 1024 * 1024;
    public const long MaxOperationBytes = 256L * 1024 * 1024;

    public static int Area(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Map dimensions must be positive.");
        long area = checked((long)width * height);
        if (area > MaxCells)
            throw new ArgumentOutOfRangeException(nameof(width), $"Editor budget is {MaxCells} cells.");
        return (int)area;
    }

    public static int FileSize(long size)
    {
        if (size < 0 || size > MaxFileBytes)
            throw new InvalidDataException($"File exceeds the {MaxFileBytes}-byte editor budget.");
        return (int)size;
    }

    public static short SignedCoordinate(int index)
    {
        if (index < 0 || index > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(index), "Unit/trap coordinates must be 0..32767.");
        return (short)index;
    }

    public static ushort BuildingCoordinate(int index)
    {
        if (index < 0 || index > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(index), "Building coordinates must be 0..65535.");
        return (ushort)index;
    }
}
