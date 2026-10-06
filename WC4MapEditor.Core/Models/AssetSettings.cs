namespace WC4MapEditor.Core.Models;

// Read-only catalog projections. These are never used to rewrite full tables.
public sealed class SkillSettings
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public int Level { get; set; }
    public int UpgradeId { get; set; }
}

public sealed class ArmySettings
{
    public int Id { get; set; }
    public int Army { get; set; }
    public int Type { get; set; }
    public string Name { get; set; } = "";
    public int MaxFormation { get; set; }
}
