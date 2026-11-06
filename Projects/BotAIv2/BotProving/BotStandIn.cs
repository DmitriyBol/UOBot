using System;
using System.Collections.Generic;
using Server.Commands;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

public class BotStandIn : PlayerMobile
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotStandIn));

    public static long Returned { get; private set; }

    public BotStandIn(Serial serial) : base(serial)
    {
    }

    public BotStandIn()
    {
    }

    public string Of { get; private set; }

    public int Taken { get; private set; }

    public int Healed { get; private set; }

    public int Copied { get; private set; }

    public override void OnDamage(int amount, Mobile from, bool willKill)
    {
        base.OnDamage(amount, from, willKill);

        Taken += Math.Max(0, Math.Min(amount, Hits));
    }

    public override void OnHeal(ref int amount, Mobile from)
    {
        base.OnHeal(ref amount, from);

        Healed += Math.Max(0, Math.Min(amount, HitsMax - Hits));
    }

    public static BotStandIn Copy(BotMobile bot, Map map, Point3D at)
    {
        if (bot is not { Deleted: false } || map == null || map == Map.Internal)
        {
            return null;
        }

        var double_ = new BotStandIn
        {

            Player = true,
            Of = bot.Name,
            Name = $"{bot.Name}'s double",
            Female = bot.Female,
            Body = bot.Body,
            Hue = bot.Hue,
            HairItemID = bot.HairItemID,
            HairHue = bot.HairHue
        };

        double_.AddItem(new Backpack { Movable = false });

        double_.StatCap = bot.StatCap;
        double_.RawStr = bot.RawStr;
        double_.RawDex = bot.RawDex;
        double_.RawInt = bot.RawInt;

        for (var i = 0; i < bot.Skills.Length && i < double_.Skills.Length; i++)
        {
            var from = bot.Skills[i];
            var to = double_.Skills[i];

            if (from == null || to == null)
            {
                continue;
            }

            to.Cap = from.Cap;
            to.Base = from.Base;
        }

        var worn = bot.Items;

        for (var i = 0; i < worn.Count; i++)
        {
            var item = worn[i];

            if (item == null || item.Deleted || item is Container || item.Layer is Layer.Mount or Layer.Hair or Layer.FacialHair
                or Layer.Backpack or Layer.Bank or Layer.ShopBuy or Layer.ShopResale or Layer.ShopSell)
            {
                continue;
            }

            var copy = Clone(item);

            if (copy == null)
            {
                continue;
            }

            if (double_.EquipItem(copy))
            {
                double_.Copied++;
            }
            else
            {
                copy.Delete();
            }
        }

        var pack = bot.Backpack;

        if (pack != null)
        {

            _carried.Clear();
            Gather(pack, _carried);

            for (var i = 0; i < _carried.Count; i++)
            {
                var copy = Clone(_carried[i]);

                if (copy == null)
                {
                    continue;
                }

                double_.Backpack.DropItem(copy);
                double_.Copied++;
            }

            _carried.Clear();
        }

        double_.Closes = bot.Class?.Closes == true;
        double_.Shoots = bot.Class?.Role == BotRole.Ranged;

        double_.Hits = double_.HitsMax;
        double_.Stam = double_.StamMax;
        double_.Mana = double_.ManaMax;

        double_.NextCombatTime = Core.TickCount;

        double_.MoveToWorld(at, map);

        return double_;
    }

    private static bool Fights(Item item) =>
        item is { Deleted: false } and (Bandage or BasePotion or BaseReagent or SpellScroll or Spellbook or Arrow or Bolt or BaseWeapon);

    private static readonly List<Item> _carried = [];

    private static void Gather(Container container, List<Item> into)
    {
        var items = container.Items;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            if (item is Spellbook || item is not Container inner)
            {
                if (Fights(item))
                {
                    into.Add(item);
                }

                continue;
            }

            Gather(inner, into);
        }
    }

    public bool Closes { get; private set; }

    public bool Shoots { get; private set; }

    public int Draws { get; private set; }

    public bool Draw(bool melee)
    {
        var pack = Backpack;

        if (pack == null || Deleted || !Alive)
        {
            return false;
        }

        var held = Weapon as Item;

        if (held != null && held.Parent != this)
        {
            held = null;
        }

        if (held is BaseWeapon inHand && inHand is BaseRanged != melee)
        {
            return true;
        }

        BaseWeapon wanted = null;
        var best = -1.0;
        var items = pack.Items;

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not BaseWeapon weapon || weapon is BaseRanged == melee
                || weapon.Layer is not (Layer.OneHanded or Layer.TwoHanded))
            {
                continue;
            }

            if (weapon is BaseRanged shooter && (shooter.AmmoType == null || pack.GetAmount(shooter.AmmoType) <= 0))
            {
                continue;
            }

            var worth = (weapon.MinDamage + weapon.MaxDamage) / 2.0;

            if (worth > best)
            {
                best = worth;
                wanted = weapon;
            }
        }

        if (wanted == null)
        {
            return false;
        }

        if (held != null)
        {
            pack.DropItem(held);
        }

        var shield = wanted.Layer == Layer.TwoHanded ? FindItemOnLayer(Layer.TwoHanded) : null;

        if (shield != null)
        {
            pack.DropItem(shield);
        }

        if (EquipItem(wanted))
        {
            Draws++;

            return true;
        }

        return false;
    }

    public string Holding()
    {
        var weapon = Weapon as BaseWeapon;
        var name = weapon == null || weapon.Parent != this ? "nothing" : weapon.GetType().Name;
        var ammo = weapon is BaseRanged { AmmoType: { } kind } ? Backpack?.GetAmount(kind) ?? 0 : -1;

        return ammo < 0 ? name : $"{name} with {ammo} to fire";
    }

    private static Item Clone(Item source)
    {
        try
        {
            var ctor = source.GetType().GetConstructor(out var count);

            if (ctor == null)
            {
                return null;
            }

            object[] args = null;

            if (count > 0)
            {
                args = new object[count];
                Array.Fill(args, Type.Missing);
            }

            var copy = Dupe.DoDupe(source, ctor, args);

            if (copy == null)
            {
                return null;
            }

            copy.BlessedFor = null;

            return copy;
        }
        catch (Exception e)
        {
            logger.Warning(e, "Could not copy {Item} for a double", source?.GetType().Name);

            return null;
        }
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

        Returned++;
        Timer.DelayCall(Delete);
    }
}
