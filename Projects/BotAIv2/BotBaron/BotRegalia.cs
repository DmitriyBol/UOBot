using ModernUO.Serialization;
using Server.Items;

namespace Server.BotAI.V2;

public static class BotRegalia
{

    public const int RoyalRed = 0x26;
}

[SerializationGenerator(0, false)]
public partial class BotBaronHalberd : Halberd
{
    [Constructible]
    public BotBaronHalberd()
    {
        Name = "the Baron's halberd";
        Hue = BotRegalia.RoyalRed;
    }

    public override void OnHit(Mobile attacker, Mobile defender, double damageBonus = 1.0)
    {
        var hits = HitPoints;
        var max = MaxHitPoints;

        base.OnHit(attacker, defender, damageBonus);

        if (Deleted)
        {
            return;
        }

        MaxHitPoints = max;
        HitPoints = hits;
    }
}

[SerializationGenerator(0, false)]
public partial class BotBaronChest : PlateChest
{
    [Constructible]
    public BotBaronChest()
    {
        Name = "the Baron's cuirass";

        Resource = CraftResource.Gold;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotBaronLegs : PlateLegs
{
    [Constructible]
    public BotBaronLegs()
    {
        Name = "the Baron's greaves";
        Resource = CraftResource.Gold;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotBaronArms : PlateArms
{
    [Constructible]
    public BotBaronArms()
    {
        Name = "the Baron's vambraces";
        Resource = CraftResource.Gold;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotBaronGloves : PlateGloves
{
    [Constructible]
    public BotBaronGloves()
    {
        Name = "the Baron's gauntlets";
        Resource = CraftResource.Gold;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotBaronGorget : PlateGorget
{
    [Constructible]
    public BotBaronGorget()
    {
        Name = "the Baron's gorget";
        Resource = CraftResource.Gold;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotBaronHelm : PlateHelm
{
    [Constructible]
    public BotBaronHelm()
    {
        Name = "the Baron's helm";
        Resource = CraftResource.Gold;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotBaronCloak : Cloak
{
    [Constructible]
    public BotBaronCloak()
    {
        Name = "a red royal cloak";
        Hue = BotRegalia.RoyalRed;
        MaxHitPoints = 0;
    }
}
