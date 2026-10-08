namespace WC4MapEditor.Core.Models;

public readonly struct HexCoord(int col, int row)
{
    public int Col { get; } = col;
    public int Row { get; } = row;

    public static HexCoord FromIndex(int index, int mapWidth) =>
        new HexCoord(index % mapWidth, index / mapWidth);

    public int ToIndex(int mapWidth) => Row * mapWidth + Col;

    public HexCoord[] GetNeighbors()
    {
        // The camera uses flat-top hexes with odd columns shifted down.
        return (Col & 1) == 0
            ? [
                new(Col, Row - 1), new(Col + 1, Row - 1), new(Col + 1, Row),
                new(Col, Row + 1), new(Col - 1, Row), new(Col - 1, Row - 1)
            ]
            : [
                new(Col, Row - 1), new(Col + 1, Row), new(Col + 1, Row + 1),
                new(Col, Row + 1), new(Col - 1, Row + 1), new(Col - 1, Row)
            ];
    }

    public int DistanceTo(HexCoord other)
    {
        int x1 = Col;
        int z1 = Row - (Col - (Col & 1)) / 2;
        int y1 = -x1 - z1;
        int x2 = other.Col;
        int z2 = other.Row - (other.Col - (other.Col & 1)) / 2;
        int y2 = -x2 - z2;
        return (Math.Abs(x1 - x2) + Math.Abs(y1 - y2) + Math.Abs(z1 - z2)) / 2;
    }

    public HexCoord GetNeighbor(int edge)
    {
        if ((uint)edge >= 6) throw new ArgumentOutOfRangeException(nameof(edge));
        return GetNeighbors()[edge];
    }

    public HexCoord GetNeighbor(BTLDirection direction) => GetNeighbor(direction switch
    {
        BTLDirection.North => 0, BTLDirection.Northeast => 1, BTLDirection.Southeast => 2,
        BTLDirection.South => 3, BTLDirection.Southwest => 4, BTLDirection.Northwest => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), "East and west are not hex edges.")
    });

    public override bool Equals(object? obj) =>
        obj is HexCoord other && Col == other.Col && Row == other.Row;

    public override int GetHashCode() => HashCode.Combine(Col, Row);

    public override string ToString() => $"({Col}, {Row})";

    public static bool operator ==(HexCoord a, HexCoord b) => a.Col == b.Col && a.Row == b.Row;
    public static bool operator !=(HexCoord a, HexCoord b) => !(a == b);
}
