using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The band's fence: the one member of The Shadow who commits no crime, and keeps its goods and its errands.
///
/// <para>
/// <b>Patrick's order of 17.09.2026, night, and his word on it an hour later.</b> "Once there are two of them in the
/// guild let them take in a keeper, who looks after the band's goods and orders. The keeper is shy of every unlawful
/// act." And: "the Baron and the Captain cannot be keepers, but the Sage and the Architect can. If they are found in the
/// company of thieves — say within ten or fifteen tiles of sight — they become criminals, and taking them is worth
/// 5000gp. But they may buy the witness off, or kill it. A killed witness tells nobody; a witness that gets away tells."
/// </para>
///
/// <para>
/// <b>Known by his trade, not by a name in the store.</b> The band's roll is a list of names and nothing else, and a
/// shape it cannot read is a shape that loses the whole record; so the fence is simply the member whose class is a
/// Sage's or an Architect's. Neither robs, neither leads, both can walk into any town the red members cannot, and that
/// is the whole of why the band wants one.
/// </para>
/// </summary>
public static class BotFence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotFence));

    public static bool Running { get; set; } = true;

    public static int LeastMembers { get; set; } = 2;

    public static int SeekEveryMs { get; set; } = 300000;

    public static int CompanyWithin { get; set; } = 4;

    public static int DwellMs { get; set; } = 10000;

    public static int SettleMs { get; set; } = 120000;

    private static long? _woke;

    public static int SeenWithin { get; set; } = 12;

    public static int WatchMs { get; set; } = 3000;

    public static int Hush { get; set; } = 500;

    public static int Most { get; set; } = 1500;

    public static int HeadPrice { get; set; } = 5000;

    public static int HushMs { get; set; } = 600000;

    public static long Taken { get; private set; }

    public static long None { get; private set; }

    public static long Caught { get; private set; }

    public static long Bribed { get; private set; }

    public static long BribeGold { get; private set; }

    public static double Odds { get; set; } = 1.2;

    public static long Hunted { get; private set; }

    public static long Outmatched { get; private set; }

    public static long Settling { get; private set; }

    public static long Brushed { get; private set; }

    public static long Broke { get; private set; }

    public static long Asked { get; private set; }

    public static long Pocketed { get; private set; }

    public static long Told { get; private set; }

    public static long Exposed { get; private set; }

    public static int Lot { get; set; } = 20;

    public static int Spares { get; set; } = 2;

    public static double Prior { get; set; } = 320.0;

    public static int Load { get; set; } = 20;

    public static int Kinds { get; set; } = 3;

    public static long Offers { get; private set; }

    public static long Supplied { get; private set; }

    private static long _soughtTick;

    private static long _watchedTick;

    private static Serial? _company;

    private static long _companyTick;

    private static readonly Dictionary<Serial, long> _hushed = [];

    public static bool Is(Mobile m) =>
        Running && m is BotMobile { Deleted: false } bot && bot.Class is BotSage or BotArchitect && BotUnderworld.Member(bot);

    public static BotMobile Who
    {
        get
        {
            if (!Running || !BotUnderworld.Exists)
            {
                return null;
            }

            var bots = BotPopulation.Bots;

            for (var i = 0; i < bots.Count; i++)
            {
                if (Is(bots[i]))
                {
                    return bots[i];
                }
            }

            return null;
        }
    }

    public static void Beat(long now)
    {
        if (!Running || !BotUnderworld.Exists)
        {
            return;
        }

        if (now - _soughtTick >= SeekEveryMs)
        {
            _soughtTick = now;

            if (BotUnderworld.Thieves >= LeastMembers && Who == null)
            {
                Seek();
            }
        }

        if (now - _watchedTick >= WatchMs)
        {
            _watchedTick = now;
            Watch();
            Ground();
        }
    }

    private static void Ground()
    {
        if (Who is not { Deleted: false, Alive: true, Fallen: false } fence || fence.Map is not { } map
            || map == Map.Internal || fence.Squad != null)
        {
            return;
        }

        if (BotOutlaw.Jailed(fence) || !(BotOutlaw.IsWanted(fence) || BotOutlaw.IsRed(fence)))
        {
            return;
        }

        if (fence.Resolve?.Deed is BotHoleUp or BotSilence or BotBrawl or BotSentence)
        {
            return;
        }

        if (BotUnderworld.Hideout == Point3D.Zero || BotLadder.Standing(fence) is not (BotStanding.Free or BotStanding.Busy))
        {
            return;
        }

        BotWill.Press(fence, new BotHoleUp(map, BotUnderworld.Hideout), "there is a price on its head");
    }

    private static void Seek()
    {
        var bots = BotPopulation.Bots;
        BotMobile best = null;
        var bestAt = -1.0;
        var trade = 0;
        var passed = 0;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is not { Deleted: false, Alive: true } || bot.Class is not (BotSage or BotArchitect))
            {
                continue;
            }

            trade++;

            if (bot.Class.Leads || BotUnderworld.Member(bot) || BotOutlaw.Outlaw(bot) || BotOutlaw.Jailed(bot)
                || bot.Squad != null)
            {
                passed++;

                continue;
            }

            var worth = bot.Class.MainSkill is { } skill ? bot.Skills[skill].Base : 0.0;

            if (worth <= bestAt)
            {
                continue;
            }

            best = bot;
            bestAt = worth;
        }

        if (best == null)
        {
            None++;

            logger.Information(
                "The Shadow wanted a fence and found none: {Trade} of the island's bots keep the right trade and {Passed} of those were spoken for",
                trade,
                passed
            );

            return;
        }

        if (!BotUnderworld.Take(best))
        {
            None++;

            logger.Information("The Shadow would have taken {Name} as its fence and the guild would not have it", best.Name);

            return;
        }

        Taken++;

        logger.Information(
            "The Shadow took in {Name} the {Class} as its fence, to keep the band's goods and errands",
            best.Name,
            best.Class.Name
        );
    }

    private static void Watch()
    {
        if (Who is not { Deleted: false, Alive: true } fence || fence.Map is not { } map || map == Map.Internal)
        {
            return;
        }

        if (BotOutlaw.Outlaw(fence) || BotOutlaw.Jailed(fence) || fence.Hidden)
        {
            return;
        }

        var woke = Core.TickCount;

        _woke ??= woke;

        if (woke - _woke.Value < SettleMs)
        {
            Settling++;

            return;
        }

        BotMobile thief = null;

        foreach (var near in map.GetMobilesInRange<BotMobile>(fence.Location, CompanyWithin))
        {
            if (near != fence && near is { Deleted: false, Alive: true, Hidden: false } && BotUnderworld.Member(near)
                && !BotOutlaw.Jailed(near))
            {
                thief = near;

                break;
            }
        }

        var now = Core.TickCount;

        if (thief == null)
        {
            _company = null;

            return;
        }

        if (_company != thief.Serial)
        {
            _company = thief.Serial;
            _companyTick = now;
        }

        if (now - _companyTick < DwellMs)
        {
            Brushed++;

            return;
        }

        foreach (var near in map.GetMobilesInRange<BotMobile>(fence.Location, SeenWithin))
        {
            if (near == fence || near is not { Deleted: false, Alive: true, Hidden: false } witness
                || BotUnderworld.Member(witness) || !witness.InLOS(fence))
            {
                continue;
            }

            if (_hushed.TryGetValue(witness.Serial, out var dealt) && now - dealt < HushMs)
            {
                continue;
            }

            _hushed[witness.Serial] = now;
            Caught++;
            Seen(fence, witness, thief);

            return;
        }
    }

    private static void Seen(BotMobile fence, BotMobile witness, BotMobile thief)
    {
        var purse = fence.Backpack?.GetAmount(typeof(Gold)) ?? 0;
        var asks = Math.Clamp((witness.Backpack?.GetAmount(typeof(Gold)) ?? 0) / 2, Hush, Math.Max(Hush, Most));

        var fromPack = Math.Min(purse, asks);
        var fromBank = asks - fromPack;
        var took = fromPack <= 0 || fence.Backpack?.ConsumeTotal(typeof(Gold), fromPack) == true;

        if (took && fromBank > 0 && !Mobiles.Banker.Withdraw(fence, fromBank))
        {
            if (fromPack > 0)
            {
                fence.AddToBackpack(new Gold(fromPack));
            }

            took = false;
        }

        if (took)
        {
            witness.AddToBackpack(new Gold(asks));
            Bribed++;
            BribeGold += asks;

            BotVoice.Aloud(fence, $"Not a word of this, {witness.Name}. Here.");

            logger.Information(
                "{Name} the fence bought {Witness}'s silence for {Gold}gp at ({X}, {Y}), seen there with {Thief}",
                fence.Name,
                witness.Name,
                asks,
                fence.X,
                fence.Y,
                thief.Name
            );

            return;
        }

        if (purse < asks)
        {
            Broke++;
            Asked += asks;
        }

        if (BotThreat.Now(fence) < BotThreat.Now(witness) * Odds)
        {
            Outmatched++;

            Tell(fence, witness);

            return;
        }

        Hunted++;

        BotVoice.Aloud(fence, $"You saw nothing, {witness.Name}.");

        if (!BotWill.Press(fence, new BotSilence(witness), $"{witness.Name} saw it with {thief.Name}"))
        {
            Tell(fence, witness);
        }
    }

    public static void Tell(BotMobile fence, Mobile witness)
    {
        if (fence is not { Deleted: false })
        {
            return;
        }

        Told++;

        if (!BotOutlaw.Want(fence, witness?.Name, $"keeping The Shadow's goods, told of by {witness?.Name ?? "somebody"}"))
        {
            return;
        }

        Exposed++;

        var priced = BotCity.Head(fence, HeadPrice, witness?.Name ?? "the city");

        logger.Information(
            "{Witness} told the Baron of {Name} the fence, seen at ({X}, {Y}): {Answer}",
            witness?.Name ?? "somebody",
            fence.Name,
            fence.X,
            fence.Y,
            priced
        );
    }

    public static Dictionary<Type, int> Wants()
    {
        Dictionary<Type, int> want = [];

        if (!Running || !BotUnderworld.Exists)
        {
            return want;
        }

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is not { Deleted: false } || Is(bot) || !BotUnderworld.Member(bot))
            {
                continue;
            }

            foreach (var (kind, count) in BotUnload.Keeps(bot))
            {
                if (count <= 0 || count == int.MaxValue)
                {
                    continue;
                }

                want.TryGetValue(kind, out var asked);
                want[kind] = Math.Max(asked, count * Math.Max(1, Spares));
            }
        }

        if (BotLair.Chest is { } chest)
        {
            for (var i = 0; i < chest.Items.Count; i++)
            {
                if (chest.Items[i] is not { Deleted: false } item || !want.TryGetValue(item.GetType(), out var asked))
                {
                    continue;
                }

                want[item.GetType()] = asked - Math.Max(1, item.Amount);
            }
        }

        List<Type> filled = null;

        foreach (var (kind, left) in want)
        {
            if (left <= 0)
            {
                (filled ??= []).Add(kind);
            }
        }

        for (var i = 0; filled != null && i < filled.Count; i++)
        {
            want.Remove(filled[i]);
        }

        return want;
    }

    public static bool Shortest(Mobile keeper, out Type wanted, out int amount)
    {
        wanted = null;
        amount = 0;

        var kept = keeper is IBotWilful will ? BotUnload.Keeps(will) : [];
        var held = Holding(keeper as BotMobile, null);
        var worst = 0;

        foreach (var (kind, left) in Wants())
        {
            if (left <= worst)
            {
                continue;
            }

            kept.TryGetValue(kind, out var mine);

            held.TryGetValue(kind, out var has);

            var buy = mine + Math.Min(left, Math.Max(1, Lot)) - has;

            if (buy <= 0)
            {
                continue;
            }

            wanted = kind;
            amount = buy;
            worst = left;
        }

        if (wanted == null)
        {
            return false;
        }

        Offers++;

        return true;
    }

    public static Dictionary<Type, int> Holding(BotMobile bot, Dictionary<Type, int> kinds)
    {
        Dictionary<Type, int> held = [];

        if (bot?.Backpack is not { } pack)
        {
            return held;
        }

        for (var i = 0; i < pack.Items.Count; i++)
        {
            if (pack.Items[i] is not { Deleted: false, Movable: true } item || item is Gold
                || BotBinding.IsBound(item, bot.Bond) || kinds?.ContainsKey(item.GetType()) == false)
            {
                continue;
            }

            var kind = item.GetType();
            held[kind] = (held.TryGetValue(kind, out var many) ? many : 0) + Math.Max(1, item.Amount);
        }

        return held;
    }

    public static Dictionary<Type, int> Spare(BotMobile bot, Dictionary<Type, int> asked)
    {
        Dictionary<Type, int> spare = [];
        var kept = BotUnload.Keeps(bot);

        foreach (var (kind, many) in Holding(bot, asked))
        {
            kept.TryGetValue(kind, out var mine);

            if (many - mine > 0)
            {
                spare[kind] = many - mine;
            }
        }

        return spare;
    }

    public static bool Carrying(BotMobile bot)
    {
        if (!Is(bot) || Wants() is not { Count: > 0 } asked)
        {
            return false;
        }

        var spare = Spare(bot, asked);

        if (spare.Count >= Math.Max(1, Kinds))
        {
            return true;
        }

        var things = 0;

        foreach (var (_, many) in spare)
        {
            things += many;
        }

        return things >= Math.Max(1, Load);
    }

    public static long Sent { get; private set; }

    public static void Sending() => Sent++;

    public static void Stocked(int things)
    {
        if (things > 0)
        {
            Supplied += things;
        }
    }

    public static void Drew(int gold)
    {
        if (gold > 0)
        {
            Pocketed += gold;
        }
    }

    public static string Describe() =>
        Who is { } fence
            ? $"The Shadow's fence is {fence.Name} the {fence.Class?.Name}: {Caught} times seen in the band's company ({Brushed} looks at company too brief to count, {Settling} in the first minutes of a boot), {Bribed} witnesses bought off for {BribeGold}gp, {Hunted} set on, {Outmatched} too strong to set on, {Told} told and {Exposed} prices put on its head; {Offers} looks wanting something for the band, {Sent} errands set out on it and {Supplied} things bought into the chest; {Broke} witnesses it could not afford (they asked {Asked}gp) and {Pocketed}gp drawn out of the chest to pay them with; {BotHoleUp.Describe()}"
            : $"The Shadow has no fence ({Taken} taken in so far, {None} sweeps found nobody of the trade free, {Caught} times one was seen with the band)";

    public static void Forget()
    {
        Taken = 0;
        None = 0;
        Caught = 0;
        Bribed = 0;
        BribeGold = 0;
        Hunted = 0;
        Outmatched = 0;
        Told = 0;
        Exposed = 0;
        Offers = 0;
        Sent = 0;
        Supplied = 0;
        Brushed = 0;
        Settling = 0;
        _company = null;
        BotHoleUp.Forget();
        Broke = 0;
        Asked = 0;
        Pocketed = 0;
        _hushed.Clear();
    }
}
