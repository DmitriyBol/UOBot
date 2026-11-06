

using ModernUO.Serialization;
using Server.Items;

namespace Server.BotAI.V2;

public static class BotRangerKit
{

    public const int Livery = BotRegalia.RoyalRed;
}

[SerializationGenerator(0, false)]
public partial class BotRangerBlade : Broadsword
{
    [Constructible]
    public BotRangerBlade()
    {
        Name = "a King's Ranger blade";
        Hue = BotRangerKit.Livery;
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
public partial class BotRangerBow : Bow
{
    [Constructible]
    public BotRangerBow()
    {
        Name = "a King's Ranger bow";
        Hue = BotRangerKit.Livery;
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
public partial class BotRangerShield : HeaterShield
{
    [Constructible]
    public BotRangerShield()
    {
        Name = "a King's Ranger shield";
        Resource = CraftResource.Gold;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotRangerChest : PlateChest
{
    [Constructible]
    public BotRangerChest()
    {
        Name = "a King's Ranger cuirass";
        Resource = CraftResource.Gold;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotRangerLegs : PlateLegs
{
    [Constructible]
    public BotRangerLegs()
    {
        Name = "King's Ranger greaves";
        Resource = CraftResource.Gold;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotRangerArms : PlateArms
{
    [Constructible]
    public BotRangerArms()
    {
        Name = "King's Ranger vambraces";
        Resource = CraftResource.Gold;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotRangerGloves : PlateGloves
{
    [Constructible]
    public BotRangerGloves()
    {
        Name = "King's Ranger gauntlets";
        Resource = CraftResource.Gold;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotRangerGorget : PlateGorget
{
    [Constructible]
    public BotRangerGorget()
    {
        Name = "a King's Ranger gorget";
        Resource = CraftResource.Gold;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotRangerHelm : PlateHelm
{
    [Constructible]
    public BotRangerHelm()
    {
        Name = "a King's Ranger helm";
        Resource = CraftResource.Gold;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotRangerArcherChest : StuddedChest
{
    [Constructible]
    public BotRangerArcherChest()
    {
        Name = "a King's Ranger tunic";
        Hue = BotRangerKit.Livery;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotRangerArcherLegs : StuddedLegs
{
    [Constructible]
    public BotRangerArcherLegs()
    {
        Name = "King's Ranger leggings";
        Hue = BotRangerKit.Livery;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotRangerArcherArms : StuddedArms
{
    [Constructible]
    public BotRangerArcherArms()
    {
        Name = "King's Ranger sleeves";
        Hue = BotRangerKit.Livery;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotRangerArcherGloves : StuddedGloves
{
    [Constructible]
    public BotRangerArcherGloves()
    {
        Name = "King's Ranger bracers";
        Hue = BotRangerKit.Livery;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotRangerArcherGorget : StuddedGorget
{
    [Constructible]
    public BotRangerArcherGorget()
    {
        Name = "a King's Ranger collar";
        Hue = BotRangerKit.Livery;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotRangerArcherCap : LeatherCap
{
    [Constructible]
    public BotRangerArcherCap()
    {
        Name = "a King's Ranger cap";
        Hue = BotRangerKit.Livery;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotRangerCloak : Cloak
{
    [Constructible]
    public BotRangerCloak()
    {
        Name = "a red royal cloak";
        Hue = BotRangerKit.Livery;
        MaxHitPoints = 0;
    }
}

[SerializationGenerator(0, false)]
public partial class BotRangerBook : Spellbook
{

    public const int Circles = 5;

    [Constructible]
    public BotRangerBook() : base(Full())
    {
        Name = "a King's Ranger spellbook";
        Hue = BotRangerKit.Livery;
        LootType = LootType.Blessed;
    }

    private static ulong Full()
    {
        var content = 0ul;

        for (var i = 0; i < Circles * 8; i++)
        {
            content |= 1ul << i;
        }

        return content;
    }
}

[SerializationGenerator(0, false)]
public partial class BotRangerRobe : Robe
{
    [Constructible]
    public BotRangerRobe()
    {
        Name = "a King's Ranger robe";
        Hue = BotRangerKit.Livery;
    }
}

[SerializationGenerator(0, false)]
public partial class BotRangerHealerRobe : Robe
{
    [Constructible]
    public BotRangerHealerRobe()
    {
        Name = "a King's Ranger surgeon's robe";
        Hue = BotRangerKit.Livery;
    }
}
