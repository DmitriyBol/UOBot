using System;
using System.Collections.Generic;
using Server.Items;
using Server.Mobiles;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Dividing what a squad took.
///
/// <para>
/// <b>Evenly by worth, not by count.</b> The first version dealt items round-robin and called it fair enough
/// that nobody had grounds to complain — but one bot came away with a katana and another with a rotten skull,
/// and the gold went to whoever opened the corpse, in one pile. Two piles of "one item each" are not two
/// equal shares.
/// </para>
///
/// <para>
/// So: gold is cut by amount, and every other item goes to whoever has received the least worth so far,
/// heaviest item first. That last detail is what makes it work — handing out the valuable things first lets
/// the small ones even up the difference, and handing them out last cannot.
/// </para>
///
/// <para>
/// <b>Settled on the spot, and the dead get nothing.</b> The first version held the corpse untouched until
/// every fallen member had been resurrected, with the survivors standing over it — and standing still is what
/// killed six bots in a ring around a lich. A share-out that waits is a state in which the squad is not going
/// anywhere, and this design has no such states. It is a real loss to whoever died winning the fight, and it
/// is the cheaper of the two losses.
/// </para>
/// </summary>
public static class BotSpoils
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSpoils));

    public static Func<Item, int> Worth { get; set; }

    public static int Earshot { get; set; } = 12;

    public static long Shares { get; private set; }

    public static long Handed { get; private set; }

    public static long GoldSplit { get; private set; }

    public static long Abstained { get; private set; }

    public static long Alone { get; private set; }

    public static void Reset()
    {
        Shares = 0;
        Handed = 0;
        GoldSplit = 0;
        Abstained = 0;
        Alone = 0;
    }

    public static string Describe() =>
        $"{Shares} corpses divided, {Handed} things handed over, {GoldSplit}gp split, {Abstained} shares stood out of, {Alone} corpses left because only somebody who takes no share was there";

    private static readonly List<IBotSquadMember> _claimants = [];

    private static readonly List<Item> _loot = [];

    private static readonly List<long> _given = [];

    private static long _shared;

    public static int Share(BotSquad squad, IBotSquadMember collector, Container corpse)
    {
        if (squad == null || collector?.Self == null || corpse is not { Deleted: false })
        {
            return 0;
        }

        Gather(squad, collector);

        if (_claimants.Count == 0)
        {
            return 0;
        }

        Shares++;

        var gold = SplitGold(corpse);
        var handed = SplitGoods(corpse, collector);

        squad.Won += gold + _shared;

        Comrade();

        if (corpse is Corpse { Owner: BotMobile { Guild: Guilds.Guild theirs } }
            && squad.Leader?.Self?.Guild is Guilds.Guild ours && ours != theirs)
        {
            BotWar.Looted(ours.Name, theirs.Name, gold + (int)Math.Min(int.MaxValue, _shared));
        }

        logger.Information(
            "Squad {Id} split {Count} things and {Gold}gp between {Claimants}",
            squad.Id,
            handed,
            gold,
            _claimants.Count
        );

        return handed;
    }

    private static readonly List<string> _guilds = [];

    private static void Comrade()
    {
        _guilds.Clear();

        for (var i = 0; i < _claimants.Count; i++)
        {
            if (_claimants[i]?.Self?.Guild is Guilds.Guild guild && !_guilds.Contains(guild.Name))
            {
                _guilds.Add(guild.Name);
            }
        }

        for (var i = 0; i < _guilds.Count; i++)
        {
            for (var j = i + 1; j < _guilds.Count; j++)
            {
                BotRegard.Comraded(_guilds[i], _guilds[j]);
            }
        }
    }

    private static void Gather(BotSquad squad, IBotSquadMember collector)
    {
        _claimants.Clear();

        var here = collector.Self.Location;
        var map = collector.Self.Map;
        var members = squad.Members;

        for (var i = 0; i < members.Count; i++)
        {
            var body = members[i].Self;

            if (body is not { Deleted: false, Alive: true } || body.Map != map)
            {
                continue;
            }

            if (Math.Abs(body.X - here.X) > Earshot
                || Math.Abs(body.Y - here.Y) > Earshot
                || Math.Abs(body.Z - here.Z) >= BotArrival.PersonHeight)
            {
                continue;
            }

            _claimants.Add(members[i]);
        }

        Abstain();
    }

    private static void Abstain()
    {
        var abstaining = 0;

        for (var i = 0; i < _claimants.Count; i++)
        {
            if (_claimants[i].Self is BotMobile { Class.Unpaid: true })
            {
                abstaining++;
            }
        }

        if (abstaining == 0)
        {
            return;
        }

        if (abstaining == _claimants.Count)
        {
            Alone++;
            _claimants.Clear();

            return;
        }

        for (var i = _claimants.Count - 1; i >= 0; i--)
        {
            if (_claimants[i].Self is BotMobile { Class.Unpaid: true })
            {
                _claimants.RemoveAt(i);
                Abstained++;
            }
        }
    }

    private static int SplitGold(Container corpse)
    {
        var total = 0;
        var items = corpse.Items;

        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is not Gold coins)
            {
                continue;
            }

            total += coins.Amount;
            coins.Delete();
        }

        if (total <= 0)
        {
            return 0;
        }

        var each = total / _claimants.Count;
        var over = total - each * _claimants.Count;

        for (var i = 0; i < _claimants.Count; i++)
        {
            var amount = each + (i == 0 ? over : 0);

            if (amount <= 0)
            {
                continue;
            }

            _claimants[i].Self.Backpack?.DropItem(new Gold(amount));
        }

        GoldSplit += total;

        return total;
    }

    private static int SplitGoods(Container corpse, IBotSquadMember collector)
    {
        _loot.Clear();

        _shared = 0;

        var items = corpse.Items;

        for (var i = items.Count - 1; i >= 0; i--)
        {
            _loot.Add(items[i]);
        }

        if (_loot.Count == 0)
        {
            return 0;
        }

        _loot.Sort(static (a, b) => Price(b).CompareTo(Price(a)));

        _given.Clear();

        for (var i = 0; i < _claimants.Count; i++)
        {
            _given.Add(0);
        }

        var handed = 0;

        for (var i = 0; i < _loot.Count; i++)
        {
            var item = _loot[i];

            if (item.Deleted)
            {
                continue;
            }

            var poorest = 0;

            for (var c = 1; c < _given.Count; c++)
            {
                if (_given[c] < _given[poorest])
                {
                    poorest = c;
                }
            }

            var taker = _claimants[poorest];
            var pack = taker.Self.Backpack;

            if (pack == null)
            {
                continue;
            }

            corpse.RemoveItem(item);
            pack.DropItem(item);

            var price = Price(item);

            _given[poorest] += price;
            _shared += price;

            if (!ReferenceEquals(taker, collector))
            {
                handed++;
            }
        }

        Handed += handed;

        return handed;
    }

    private static int Price(Item item)
    {
        if (item == null || item.Deleted)
        {
            return 0;
        }

        var worth = Worth;

        return worth == null ? 1 : Math.Max(1, worth(item));
    }
}
