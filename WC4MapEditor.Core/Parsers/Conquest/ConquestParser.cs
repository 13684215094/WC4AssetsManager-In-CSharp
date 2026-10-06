using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.BTL;

namespace WC4MapEditor.Core.Parsers.Conquest;

public class ConquestParser : BattleParser
{
    public ConquestParser() { }
    public ConquestParser(string path)
    {
        if (!LoadHexFile(path)) throw new InvalidDataException(LastError);
    }
    public ConquestParser(byte[] bytes) => SetMapData(BTLParser.LoadFromBytes(bytes, ""), bytes);

    public static MapData CreateNewMapData(int mapWidth, int mapHeight, int numLegions, int mapNumber = 1)
        => BTLParser.CreateNew(mapWidth, mapHeight, numLegions, mapNumber);

    public bool CreateNew(int mapWidth, int mapHeight, int numLegions, int mapNumber = 1)
        => Create(mapWidth, mapHeight, numLegions, mapNumber);
}
