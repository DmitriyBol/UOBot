using System;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Keeps a provisioned class in bandages, reagents and arrows. The crown's stores, not its purse.
///
/// <para>
/// <b>Supplies rather than coin, and the distinction is the whole reason this exists instead of a wage.</b>
/// Gold handed to a bot enters the population and competes with every price on the shard: the Baron's
/// stipend is one such tap, allowed once and deliberately, with a note explaining why it was allowed. A
/// second one would have been a second inflationary source on a shard that spent a day learning what the
/// first one does to a median purse. Stock replaced in a pack never becomes money — it is bound, so it
/// cannot be sold, cannot be dropped into a corpse and cannot reach the market.
/// </para>
///
/// <para>
/// <b>Topped up rather than granted, so a ranger cannot accumulate.</b> The stock is brought back to what
/// its class was born with and no further. A bot that has spent nothing is given nothing, which also makes
/// the counter honest: what this hands out over an evening is exactly what the rangers actually used.
/// </para>
///
/// <para>
/// <b>Asked on the population's own clock rather than on a timer of its own.</b> There are five of these
/// bots on the shard and the question is three dictionary lookups; a timer would be a second clock to
/// reason about for no gain. Throttled per bot so a ranger in a long fight is not re-counted every tick.
/// </para>
/// </summary>
public static class BotQuartermaster
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotQuartermaster));

    /// <summary>
    /// How often one bot's stores are looked at.
    ///
    /// Ten seconds. Half a minute was chosen for bandages and arrows, which are spent slowly; a mage in a
    /// fight throws a spell every few seconds and each one takes a reagent of several kinds, so "the crown
    /// keeps them supplied" has to mean the pack refills faster than a fight empties it. It is five bots and
    /// eleven dictionary lookups.
    /// </summary>
    public static int EveryMs { get; set; } = 10000;

    /// <summary>
    /// How many arrows a provisioned archer is kept at.
    ///
    /// Not read off the kit like bandages and reagents are: ammunition is issued by the weapon's own option
    /// rather than by a number on the kit, so there is nothing there to read back. Two hundred is a long
    /// afternoon of shooting and well under what a pack will hold.
    /// </summary>
    public static int Arrows { get; set; } = 200;

    /// <summary>
    /// The eight reagents this era's magery uses, in the order <c>BotOutfit</c> hands them out at birth.
    ///
    /// Named here rather than derived, and the same eight types birth uses, so a mage restocked by the crown
    /// carries exactly what a mage is born carrying. If that list ever changes, both places change together
    /// or a provisioned mage quietly ends up with a satchel the shard does not issue.
    /// </summary>
    private static readonly Type[] Eight =
    [
        typeof(SulfurousAsh),
        typeof(BlackPearl),
        typeof(Garlic),
        typeof(Ginseng),
        typeof(SpidersSilk),
        typeof(Nightshade),
        typeof(Bloodmoss),
        typeof(MandrakeRoot)
    ];

    private static readonly System.Collections.Generic.Dictionary<Serial, long> _looked = [];

    /// <summary>Items handed out, all told, by kind.</summary>
    public static long Bandages { get; private set; }

    public static long Reagents { get; private set; }

    public static long Shafts { get; private set; }

    /// <summary>Bottles issued. The supply that decides whether a company comes back.</summary>
    public static long Bottles { get; private set; }

    /// <summary>Bots looked at, and how many of those needed anything.</summary>
    public static long Asked { get; private set; }

    public static long Supplied { get; private set; }

    /// <summary>Brings this bot's stores back up, if it is the sort the crown keeps.</summary>
    public static void Keep(BotMobile bot)
    {
        if (bot?.Class is not { Provisioned: true } sort || bot.Deleted || !bot.Alive)
        {
            return;
        }

        var pack = bot.Backpack;

        if (pack == null)
        {
            return;
        }

        var now = Core.TickCount;

        // Compared by subtraction against a stamp that was itself a real tick, never against a zero this
        // field never held.
        if (_looked.TryGetValue(bot.Serial, out var last) && now - last < EveryMs)
        {
            return;
        }

        _looked[bot.Serial] = now;

        Asked++;

        var given = 0;

        given += Top(bot, pack, typeof(Bandage), sort.Kit?.Bandages ?? 0, out var bandages);
        Bandages += bandages;

        if (sort.Kit is { Reagents: > 0 })
        {
            given += Reagent(bot, pack, sort.Kit.Reagents, out var reagents);
            Reagents += reagents;
        }

        if (sort.Role == BotRole.Ranged)
        {
            given += Top(bot, pack, typeof(Arrow), Arrows, out var shafts);
            Shafts += shafts;
        }

        // <b>And the bottles, which were the omission that killed the first company.</b> A potion is the only
        // mending in this game that works while something is hitting you — a cast is broken by a blow and a
        // bandage slips — so BotMobile.Gasp reaches for one the moment a bot drops under fifteen per cent.
        // It found nothing: a ranger is issued one of each at birth, drinks it, and has no gold to buy
        // another, because a provisioned class has no gold at all by design. Bandages and reagents were
        // replaced here and the one supply that decides whether they live was not.
        var draughts = BotArsenal.Draughts;

        for (var i = 0; i < draughts.Count; i++)
        {
            var kind = BotArsenal.Potion(draughts[i]);

            if (kind == null)
            {
                continue;
            }

            given += Top(bot, pack, kind, sort.PotionLimit(draughts[i]), out var bottles);
            Bottles += bottles;
        }

        if (given > 0)
        {
            Supplied++;
        }
    }

    /// <summary>
    /// Brings one kind of stock back to a number. Returns how many items were made, and how many units.
    /// </summary>
    private static int Top(Mobile bot, Container pack, Type kind, int want, out int units)
    {
        units = 0;

        if (want <= 0 || kind == null)
        {
            return 0;
        }

        var held = pack.GetAmount(kind);

        if (held >= want)
        {
            return 0;
        }

        var short_ = want - held;
        var made = BotBinding.Make(kind, short_);

        if (made == null)
        {
            return 0;
        }

        // Bound before it is placed, like everything the world hands a bot: weightless, and death does not
        // take it. It is also what keeps this from being money — nothing bound can be sold on this shard.
        BotBinding.Bind(made, (bot as BotMobile)?.Bond);

        if (!pack.TryDropItem(bot, made, false))
        {
            made.Delete();

            return 0;
        }

        units = short_;

        return 1;
    }

    /// <summary>
    /// The eight reagents, each brought back to the same number.
    ///
    /// Every one of them separately, because the engine spends them separately: a mage with sixty of seven
    /// reagents and none of the eighth cannot cast the spell that wants the eighth, and a single count would
    /// have read that as a full satchel.
    /// </summary>
    private static int Reagent(Mobile bot, Container pack, int want, out int units)
    {
        units = 0;

        var made = 0;

        for (var i = 0; i < Eight.Length; i++)
        {
            made += Top(bot, pack, Eight[i], want, out var some);
            units += some;
        }

        return made;
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody on this shard is kept at the crown's expense"
            : $"{Asked} looks at the crown's stores: {Supplied} needed topping up; {Bandages} bandages, {Reagents} reagents, {Shafts} arrows and {Bottles} bottles issued";

    public static void Forget()
    {
        _looked.Clear();
        Asked = 0;
        Supplied = 0;
        Bandages = 0;
        Reagents = 0;
        Shafts = 0;
        Bottles = 0;
    }
}
