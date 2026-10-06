using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.BTL;

namespace WC4MapEditor.Core.Parsers.Stage;

public class StageParser : BattleParser
{
    public StageParser() { }
    public StageParser(string path)
    {
        if (!LoadHexFile(path)) throw new InvalidDataException(LastError);
    }

    public static MapData CreateNewMapData(int mapWidth, int mapHeight, int numLegions)
        => BTLParser.CreateNew(mapWidth, mapHeight, numLegions, 0);

    public bool CreateNew(int mapWidth, int mapHeight, int numLegions)
        => Create(mapWidth, mapHeight, numLegions, 0);
}
