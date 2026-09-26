using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Items;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// What the smith's trade knows that no other trade does: its system, its skill, its hammer, its metal, and
/// the one thing about blacksmithing that is genuinely different — it cannot be done just anywhere.
///
/// <para>
/// <b>A smith needs a forge and an anvil, both, within reach.</b> That is the engine's rule and it is
/// checked by the engine — <c>DefBlacksmithy.CheckAnvilAndForge</c> — so a bot standing next to an anvil in a
/// field can swing a hammer all day and produce a message about needing a forge that nobody reads. The good
/// news is that this shard already solved the location half for another reason entirely: <c>BotGround</c>
/// only writes a forge into its list if there is an anvil within a few tiles of it, because the miner needs
/// the same pair to smelt ore. So "where can I smith" is a question that was already being answered, by the
/// people who needed it for something else.
/// </para>
/// </summary>
public static class BotAnvil
{
    public static CraftSystem System => DefBlacksmithy.CraftSystem;

    public const SkillName Skill = SkillName.Blacksmith;

    public static Type Base => typeof(IronIngot);

    public static Type Metal => Plentiful(double.MaxValue) ?? Base;

    public static int PlentifulMs { get; set; } = 60000;

    public static int LeastPlentiful { get; set; } = 10;

    public static string Reading { get; private set; } = "not read yet";

    private static readonly Dictionary<Type, int> _onBoard = [];

    private static long _readAt;

    public static Type Plentiful(double able)
    {
        var system = System;

        if (system == null)
        {
            return null;
        }

        var now = Core.TickCount;

        if (_onBoard.Count == 0 || now - _readAt >= PlentifulMs)
        {
            Read(system);
            _readAt = now;
        }

        var metals = system.CraftSubRes;

        Type best = null;
        var most = LeastPlentiful - 1;

        for (var i = 0; i < metals.Count; i++)
        {
            var metal = metals.GetAt(i);

            if (metal?.ItemType == null || metal.RequiredSkill > able)
            {
                continue;
            }

            if (!_onBoard.TryGetValue(metal.ItemType, out var held) || held <= most)
            {
                continue;
            }

            best = metal.ItemType;
            most = held;
        }

        return best;
    }

    private static void Read(CraftSystem system)
    {
        _onBoard.Clear();

        var stalls = BotAuction.Listings;

        for (var i = 0; i < stalls.Count; i++)
        {
            var stall = stalls[i];
            var kind = stall?.Kind;

            if (kind == null || stall.IsEmpty)
            {
                continue;
            }

            _onBoard[kind] = _onBoard.TryGetValue(kind, out var held) ? held + stall.Amount : stall.Amount;
        }

        var metals = system.CraftSubRes;
        var say = ValueStringBuilder.Create(128);

        try
        {
            for (var i = 0; i < metals.Count; i++)
            {
                var metal = metals.GetAt(i);

                if (metal?.ItemType == null || !_onBoard.TryGetValue(metal.ItemType, out var held))
                {
                    continue;
                }

                if (say.Length > 0)
                {
                    say.Append(", ");
                }

                say.Append(metal.ItemType.Name);
                say.Append(' ');
                say.Append(held);
            }

            Reading = say.Length == 0 ? "no metal on the board at all, so iron stands" : say.ToString();
        }
        finally
        {
            say.Dispose();
        }
    }

    public static SmithHammer Kit(Mobile bot) => BotOutfit.Oldest<SmithHammer>(bot?.Backpack);

    public static int Ingots(Mobile bot, Type metal = null) => bot?.Backpack?.GetAmount(metal ?? Metal) ?? 0;

    public static double Able(Mobile bot) => bot?.Skills[Skill].Base ?? 0.0;

    public static Type Best(Mobile bot, int need)
    {
        var system = System;
        var pack = bot?.Backpack;

        if (system == null || pack == null)
        {
            return Metal;
        }

        var able = Able(bot);
        var metals = system.CraftSubRes;

        Type best = null;
        var bestNeeds = -1.0;

        for (var i = 0; i < metals.Count; i++)
        {
            var metal = metals.GetAt(i);

            if (metal?.ItemType == null || metal.RequiredSkill > able || metal.RequiredSkill <= bestNeeds)
            {
                continue;
            }

            if (pack.GetAmount(metal.ItemType) < need)
            {
                continue;
            }

            best = metal.ItemType;
            bestNeeds = metal.RequiredSkill;
        }

        return best ?? Plentiful(able) ?? Base;
    }

    public static void Keep(Mobile body, Dictionary<Type, int> keep, int amount)
    {
        var system = System;

        if (system == null || keep == null)
        {
            keep?.TryAdd(Base, amount);

            return;
        }

        keep[Base] = amount;

        if (body == null)
        {
            return;
        }

        var able = Able(body);
        var metals = system.CraftSubRes;

        for (var i = 0; i < metals.Count; i++)
        {
            var metal = metals.GetAt(i);

            if (metal?.ItemType != null && metal.RequiredSkill <= able)
            {
                keep[metal.ItemType] = amount;
            }
        }
    }

    public static int Fetch(IBotWilful bot, Mobile body, int need)
    {
        var system = System;
        var pack = body?.Backpack;

        if (system == null || pack == null || bot == null)
        {
            return 0;
        }

        var able = Able(body);
        var metals = system.CraftSubRes;
        var back = 0;

        for (var i = 0; i < metals.Count; i++)
        {
            var metal = metals.GetAt(i);

            if (metal?.ItemType == null || metal.RequiredSkill > able)
            {
                continue;
            }

            back += BotAuction.Reclaim(bot, metal.ItemType);

            if (Ingots(body, metal.ItemType) >= need)
            {
                break;
            }
        }

        return back;
    }

    public static int Reach { get; set; } = 2;

    public static bool AtASmithy(Mobile bot)
    {
        if (bot == null)
        {
            return false;
        }

        DefBlacksmithy.CheckAnvilAndForge(bot, Reach, out var anvil, out var forge);

        return anvil && forge;
    }

    public static Point3D Between(Map map, Point3D forge, Point3D from)
    {
        var best = Point3D.Zero;

        if (map == null || map == Map.Internal)
        {
            return best;
        }

        var bestOff = int.MaxValue;
        var span = Reach * 2;

        foreach (var item in map.GetItemsInRange(forge, span))
        {
            if (BotGround.IsAnvilId(item.ItemID) && Math.Abs(item.Z - forge.Z) <= BotArrival.PersonHeight)
            {
                Stand(map, forge, item.Location, from, ref best, ref bestOff);
            }
        }

        for (var dx = -span; dx <= span; dx++)
        {
            for (var dy = -span; dy <= span; dy++)
            {
                foreach (var tile in map.Tiles.GetStaticAndMultiTiles(forge.X + dx, forge.Y + dy))
                {
                    if (BotGround.IsAnvilId(tile.ID) && Math.Abs(tile.Z - forge.Z) <= BotArrival.PersonHeight)
                    {
                        Stand(map, forge, new Point3D(forge.X + dx, forge.Y + dy, tile.Z), from, ref best, ref bestOff);
                    }
                }
            }
        }

        return best;
    }

    private static void Stand(Map map, Point3D forge, Point3D anvil, Point3D from, ref Point3D best, ref int bestOff)
    {
        for (var x = forge.X - Reach; x <= forge.X + Reach; x++)
        {
            for (var y = forge.Y - Reach; y <= forge.Y + Reach; y++)
            {
                if (Math.Abs(x - anvil.X) > Reach || Math.Abs(y - anvil.Y) > Reach)
                {
                    continue;
                }

                if (!BotStep.Ground(map, x, y, forge.Z, BotArrival.PersonHeight / 2, out var z)
                    || !map.CanFit(x, y, z, BotArrival.PersonHeight, false, true))
                {
                    continue;
                }

                var off = Math.Max(Math.Abs(x - from.X), Math.Abs(y - from.Y));

                if (off < bestOff)
                {
                    best = new Point3D(x, y, z);
                    bestOff = off;
                }
            }
        }
    }

    public static int Stock(Mobile bot)
    {
        var system = System;
        var pack = bot?.Backpack;

        if (system == null || pack == null)
        {
            return 0;
        }

        var able = Able(bot);
        var metals = system.CraftSubRes;
        var most = pack.GetAmount(Base);

        for (var i = 0; i < metals.Count; i++)
        {
            var metal = metals.GetAt(i);

            if (metal?.ItemType == null || metal.RequiredSkill > able)
            {
                continue;
            }

            var held = pack.GetAmount(metal.ItemType);

            if (held > most)
            {
                most = held;
            }
        }

        return most;
    }

    public static int Tries { get; set; } = 3;

    public static CraftItem Choose(Mobile bot) =>
        BotCraftwork.Choose(bot, System, Skill, Base, Stock(bot) / Math.Max(1, Tries));

    public static CraftItem Recipe(Mobile bot, Type wanted) =>
        BotCraftwork.Recipe(bot, System, Skill, Base, wanted);

    public static bool Swing(Mobile bot, CraftItem recipe, BaseTool tool, Type metal = null) =>
        BotCraftwork.Swing(bot, System, recipe, metal ?? Base, tool);

    public static int Made(Mobile bot, Type kind) => BotCraftwork.Made(bot, kind);

    public static List<Item> Gather(Mobile bot, Type kind) => BotCraftwork.Gather(bot, kind);
}
