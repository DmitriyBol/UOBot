using Server.Items;
using Server.Mobiles;

namespace Server.BotAI.Mind;

public class BotDebugger : PlayerMobile
{

    public static int RobeHue { get; set; } = 0x481;

    public static AccessLevel SeenBy { get; set; } = AccessLevel.Administrator;

    public long Hops { get; private set; }

    public BotDebugger(Serial serial) : base(serial)
    {
    }

    public BotDebugger()
    {
    }

    public void Awaken(string name) => Awaken(name, RobeHue);

    public void Awaken(string name, int hue)
    {
        Name = name;

        Player = true;

        Body = 0x190;
        Female = false;

        Hue = hue;
        HairItemID = 0x203C;
        HairHue = hue;

        Blessed = true;
        Hidden = true;
        AccessLevel = SeenBy;

        Hits = HitsMax;
        Stam = StamMax;
        Mana = ManaMax;

        AddItem(new Backpack { Movable = false });

        Dress(new Robe(hue));
        Dress(new Sandals(hue));
    }

    private void Dress(Item item)
    {
        if (item == null)
        {
            return;
        }

        item.Movable = false;
        item.LootType = LootType.Blessed;

        if (!EquipItem(item))
        {
            item.Delete();
        }
    }

    public bool Hover(Map map, Point3D where)
    {
        if (Deleted || map == null || map == Map.Internal)
        {
            return false;
        }

        if (Map == map && Location == where)
        {
            return false;
        }

        MoveToWorld(where, map);

        Hidden = true;
        Hops++;

        return true;
    }

    public override void Serialize(IGenericWriter writer)
    {
        base.Serialize(writer);

        writer.Write(0);
    }

    public override void Deserialize(IGenericReader reader)
    {
        base.Deserialize(reader);

        reader.ReadInt();
    }

    public override bool ShouldCheckStatTimers => false;

    public override string ToString() => $"{Name} the debugger";
}
