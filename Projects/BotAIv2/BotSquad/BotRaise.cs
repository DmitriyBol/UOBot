using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Spells.Eighth;

namespace Server.BotAI.V2;

/// <summary>
/// <b>A company raises its own where they fell, with what this era gives it to raise them with (02.10.2026, Patrick: "It is not
/// right that bots dying in dungeons cannot loot themselves. Either the bot must run back to loot itself, or the group should
/// carry resurrection scrolls for such cases").</b>
///
/// <para>
/// Measured before (01.10 11:32 – 02.10 11:35, builds 386–389): 2,062 deaths, 47 of them underground (10 doing a company's
/// work, 37 alone), and not one of those corpses gone back for; the only raising on the shard was the delve's, three to a party,
/// which raises with nothing — no scroll, no spell, no caster, ten seconds and up — and no party went down at all after the wipe.
/// Meanwhile 980 deaths fell doing a company's work (36,897gp of pocket and 12,803gp of supplies on the corpses), and every one of
/// those bots was carried home a minute later and its pack left where it lay.
/// </para>
///
/// <para>
/// <b>The means are the engine's and nothing else (UOR, read in <c>Projects/UOContent</c>):</b> the Resurrection spell, eighth
/// circle, cast from a scroll by a caster whose Magery the engine rolls between 50 and 90 for it (<c>MagerySpell.GetCastSkills</c>,
/// the scroll two circles easier) or from the book between 70 and 110 with bloodmoss, garlic and ginseng; fifty mana either way;
/// the scroll and the mana spent only when the cast takes, the herbs on every try (<c>Spell.CheckSequence</c>). A bandage
/// wound by a healer of 80 Healing and 80 Anatomy, at (Healing − 68) / 50 (<c>BandageContext.EndHeal</c>), five seconds longer
/// than a heal. Ankhs and the healers of the towns are where the ordinary revival already happens. Nothing else in this era
/// raises a body: no Gift of Life, no Sacrifice, no Necromancy.
/// </para>
///
/// <para>
/// <b>The one thing the engine cannot do for a bot is answer.</b> Both means end in <c>ResurrectGump</c> sent to the ghost, and
/// a bot has no client to press "accept" on it: the gump goes to a null connection and is gone. So the spell is cast and aimed
/// through the engine (<c>BotMend.Begin</c>/<c>Aim</c>'s idiom) and its taking is read off what the engine spends — the scroll
/// gone, or fifty mana — and the ghost's answer is <see cref="Mobile.Resurrect"/>, called here, which is exactly what the gump
/// calls. The bandage's own roll lives inside the engine's timer and spends nothing that could be read, so it is rolled here by
/// the engine's own formula, with the engine's skill gains. And the ghost manifests (war mode) when its raiser stands beside it,
/// because a living mobile cannot see a ghost that has not (<c>Mobile.CanSee</c>) — which is how a player is raised too.
/// </para>
///
/// <para>
/// Raised with the engine's ten hits and no mana rather than the bot's own full rising (<see cref="EngineHealth"/>), and its own
/// corpse gone through at once (<see cref="BotReclaim.Strip"/>). Then it is pressed back into its company.
/// </para>
/// </summary>
public static class BotRaise
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRaise));

    public static bool Running { get; set; } = true;

    public static int HoldMs { get; set; } = 120000;

    public static int Reach { get; set; } = 18;

    public static int ClaimMs { get; set; } = 40000;

    public static int MaxTries { get; set; } = 20;

    public static int ClearReach { get; set; } = 8;

    public static int PerCompany { get; set; } = 3;

    public static bool EngineHealth { get; set; } = true;

    public static int RaisedHits { get; set; } = 10;

    public static int JudgeMs { get; set; } = 3000;

    public static int Stock { get; set; } = 1;

    public static double StockChance { get; set; } = 0.10;

    public static int StockReserve { get; set; } = 300;

    public const int Spell = BotArsenal.SpellResurrection;

    public enum Means
    {
        None,

        Scroll,

        Book,

        Bandage
    }

    private static bool _read;

    private static double _scrollMin;

    private static double _scrollMax;

    private static double _bookMin;

    private static double _bookMax;

    private static int _mana = 50;

    private static Type[] _herbs = [];

    private static void Read(Mobile caster)
    {
        if (_read || caster?.Backpack is not { } pack)
        {
            return;
        }

        var scroll = new ResurrectionSpell(caster, pack);
        scroll.GetCastSkills(out _scrollMin, out _scrollMax);

        var book = new ResurrectionSpell(caster);
        book.GetCastSkills(out _bookMin, out _bookMax);
        _mana = book.GetMana();
        _herbs = book.Reagents ?? [];

        _read = true;

        logger.Information(
            "Raising read off the engine: Resurrection from a scroll asks {SMin:F0}–{SMax:F0} Magery, from the book {BMin:F0}–{BMax:F0} with {Herbs}, {Mana} mana either way; a bandage asks 80 Healing and 80 Anatomy at (Healing − 68) / 50",
            _scrollMin,
            _scrollMax,
            _bookMin,
            _bookMax,
            string.Join(", ", Array.ConvertAll(_herbs, h => h.Name)),
            _mana
        );
    }

    private static double Roll(double skill, double min, double max) =>
        max <= min ? (skill >= min ? 1.0 : 0.0) : Math.Clamp((skill - min) / (max - min), 0.0, 1.0);

    public static double ScrollChance(Mobile body)
    {
        if (body == null)
        {
            return 0.0;
        }

        Read(body);

        return !_read || body.ManaMax < _mana ? 0.0 : Roll(body.Skills[SkillName.Magery].Value, _scrollMin, _scrollMax);
    }

    public static bool Reads(Mobile body) => Stock > 0 && ScrollChance(body) >= StockChance;

    public static bool KeepsScroll(Mobile body)
    {
        if (!Reads(body))
        {
            return false;
        }

        return body.Skills[SkillName.Magery].Value < _bookMin;
    }

    public static Means Best(Mobile body, out double chance, out Item scroll)
    {
        chance = 0.0;
        scroll = null;

        var pack = body?.Backpack;

        if (pack == null)
        {
            return Means.None;
        }

        Read(body);

        if (!_read)
        {
            return Means.None;
        }

        var best = Means.None;
        var magery = body.Skills[SkillName.Magery].Value;

        if (body.ManaMax >= _mana)
        {
            var held = pack.FindItemByType<ResurrectionScroll>();

            if (held != null)
            {
                var odds = Roll(magery, _scrollMin, _scrollMax);

                if (odds > chance)
                {
                    chance = odds;
                    best = Means.Scroll;
                    scroll = held;
                }
            }

            if (BotGrimoire.Holds(body, Spell) && Herbed(pack))
            {
                var odds = Roll(magery, _bookMin, _bookMax);

                if (odds > chance)
                {
                    chance = odds;
                    best = Means.Book;
                    scroll = null;
                }
            }
        }

        var healing = body.Skills[SkillName.Healing].Value;
        var anatomy = body.Skills[SkillName.Anatomy].Value;

        if (healing >= 80.0 && anatomy >= 80.0 && pack.FindItemByType<Bandage>() != null)
        {
            var odds = Math.Clamp((healing - 68.0) / 50.0, 0.0, 1.0);

            if (odds > chance)
            {
                chance = odds;
                best = Means.Bandage;
                scroll = null;
            }
        }

        return chance > 0.0 ? best : Means.None;
    }

    private static bool Herbed(Container pack)
    {
        for (var i = 0; i < _herbs.Length; i++)
        {
            if (pack.GetAmount(_herbs[i]) < 1)
            {
                return false;
            }
        }

        return true;
    }

    public static int Mana => _mana;

    public static bool Takes(Mobile ghost) =>
        ghost?.Map is { } map && map != Map.Internal && map.CanFit(ghost.Location, 16, false, false)
        && ghost.Region?.IsPartOf("Khaldun") != true;

    public static bool Unholy(Mobile ghost) => ghost is { Criminal: true } || ghost?.Kills >= 5;

    public static int BandageMs(Mobile healer)
    {
        var dex = healer?.Dex ?? 0;
        var seconds = dex >= 100 ? 3.0 : dex >= 40 ? 4.0 : 5.0;

        return (int)((seconds + 5.0) * 1000);
    }

    private sealed class Pending
    {
        public BotSquad Company;

        public long Fell;

        public long Hope;

        public bool Judged;

        public bool Counted;
    }

    private static readonly Dictionary<Serial, Pending> _pending = [];

    internal static void Await(BotMobile ghost, BotSquad company)
    {
        if (ghost == null || company == null)
        {
            return;
        }

        _pending[ghost.Serial] = new Pending { Company = company, Fell = ghost.FellTick };
    }

    internal static bool Awaited(BotMobile ghost) => ghost != null && _pending.ContainsKey(ghost.Serial);

    internal static void Looked(BotMobile ghost, bool hope)
    {
        if (ghost != null && _pending.TryGetValue(ghost.Serial, out var pending))
        {
            pending.Judged = true;

            if (hope)
            {
                pending.Hope = Core.TickCount;
            }
        }
    }

    internal static void Done(BotMobile ghost)
    {
        if (ghost != null)
        {
            _pending.Remove(ghost.Serial);
        }
    }

    public static bool Holding(BotMobile ghost)
    {
        if (BotInfirmaries.Holding(ghost))
        {
            return true;
        }

        if (!Running || ghost == null || !_pending.TryGetValue(ghost.Serial, out var pending))
        {
            return false;
        }

        var now = Core.TickCount;

        if (pending.Company is not { Disbanded: false } || pending.Fell != ghost.FellTick || now - pending.Fell >= HoldMs)
        {
            if (pending.Company is { Disbanded: true } && pending.Fell == ghost.FellTick)
            {
                NoteUnraised(Unraised.Broken);
            }

            _pending.Remove(ghost.Serial);

            return false;
        }

        if (!pending.Judged)
        {
            return now - pending.Fell < JudgeMs;
        }

        var holds = pending.Hope != 0 && now - pending.Hope < 2 * BotSquads.BeatMs + 500;

        if (holds && !pending.Counted && now - pending.Fell >= BotPopulation.ReviveMs)
        {
            pending.Counted = true;
            Held++;
        }

        return holds;
    }

    public static long Held { get; private set; }

    internal static bool Rise(BotMobile ghost, Mobile raiser, Means means, double chance, int tries, BotSquad company)
    {
        if (ghost is not { Deleted: false } || ghost.Alive)
        {
            return false;
        }

        var corpse = ghost.Remains;
        var where = ghost.Location;
        var taken = 0;
        var coins = 0;

        if (corpse is { Deleted: false })
        {
            BotReclaim.Strip(ghost, corpse, out taken, out coins);
        }

        ghost.Resurrect();

        if (!ghost.Alive)
        {
            Refused++;

            return false;
        }

        if (corpse is not { Deleted: false } || corpse.Items.Count == 0)
        {
            ghost.Remains = null;
        }

        if (EngineHealth)
        {
            ghost.Hits = Math.Min(ghost.HitsMax, Math.Max(1, RaisedHits));
            ghost.Mana = 0;
        }

        ghost.Rearm();

        Raised++;
        Things += taken;
        Coin += coins;

        switch (means)
        {
            case Means.Scroll:
                ByScroll++;
                break;
            case Means.Book:
                ByBook++;
                break;
            case Means.Bandage:
                ByBandage++;
                break;
        }

        logger.Information(
            "{Raiser} raised {Name} the {Class} where it fell at ({X}, {Y}) in company {Company}, by {Means} at {Chance:P0} a try after {Tries} tries; it took {Taken} things and {Coins}gp back off its own corpse at once",
            raiser?.Name,
            ghost.Name,
            ghost.Class?.Name,
            where.X,
            where.Y,
            company?.Id ?? 0,
            Said(means),
            chance,
            tries,
            taken,
            coins
        );

        BotEvents.Post(
            "raise",
            ghost,
            $"raised where it fell at ({where.X}, {where.Y}) by {raiser?.Name} with {Said(means)}; {taken} things and {coins}gp back off its corpse",
            null,
            BotJson.Object(
                w =>
                {
                    w.WriteString("raiser", raiser?.Name);
                    w.WriteString("means", means.ToString());
                    w.WriteNumber("chance", Math.Round(chance, 3));
                    w.WriteNumber("tries", tries);
                    w.WriteNumber("things", taken);
                    w.WriteNumber("coin", coins);
                    w.WriteNumber("company", company?.Id ?? 0);
                }
            )
        );

        BotDelveParty.Spent(ghost);

        BotBiography.Raised(raiser, ghost, where);

        Done(ghost);

        return true;
    }

    public static string Said(Means means) =>
        means switch
        {
            Means.Scroll => "a resurrection scroll",
            Means.Book => "the spell from its book",
            Means.Bandage => "a bandage",
            _ => "nothing"
        };

    public static long Fallen { get; private set; }

    public static long Raised { get; private set; }

    public static long ByScroll { get; private set; }

    public static long ByBook { get; private set; }

    public static long ByBandage { get; private set; }

    public static long Things { get; private set; }

    public static long Coin { get; private set; }

    public static long Tries { get; private set; }

    public static long Failed { get; private set; }

    public static long Casts { get; private set; }

    public static long Rejoined { get; private set; }

    public static long Refused { get; private set; }

    public static long NobodyAble { get; private set; }

    public static long NobodyNear { get; private set; }

    public static long TriedAndFailed { get; private set; }

    public static long FightHeld { get; private set; }

    public static long CarriedHome { get; private set; }

    public static long Spent { get; private set; }

    public static long Ground { get; private set; }

    public static long Outlawed { get; private set; }

    public static long Broken { get; private set; }

    public static long ManaWaits { get; private set; }

    public static long FightWaits { get; private set; }

    public static long Handed { get; private set; }

    internal static void NoteFallen() => Fallen++;

    internal static void NoteTry(bool took)
    {
        Tries++;

        if (!took)
        {
            Failed++;
        }
    }

    internal static void NoteCast() => Casts++;

    internal static void NoteRejoined() => Rejoined++;

    internal static void NoteManaWait() => ManaWaits++;

    internal static void NoteFightWait() => FightWaits++;

    internal static void NoteHanded() => Handed++;

    internal enum Unraised
    {
        NobodyAble,
        NobodyNear,
        TriedAndFailed,
        FightHeld,
        CarriedHome,
        Spent,
        Ground,
        Outlawed,
        Broken
    }

    internal static void NoteUnraised(Unraised why)
    {
        switch (why)
        {
            case Unraised.NobodyAble:
                NobodyAble++;
                break;
            case Unraised.NobodyNear:
                NobodyNear++;
                break;
            case Unraised.TriedAndFailed:
                TriedAndFailed++;
                break;
            case Unraised.FightHeld:
                FightHeld++;
                break;
            case Unraised.CarriedHome:
                CarriedHome++;
                break;
            case Unraised.Spent:
                Spent++;
                break;
            case Unraised.Ground:
                Ground++;
                break;
            case Unraised.Outlawed:
                Outlawed++;
                break;
            case Unraised.Broken:
                Broken++;
                break;
        }
    }

    public static string Describe() =>
        !Running
            ? "companies do not raise their fallen"
            : $"raised where they fell: {Fallen} members fell in a company, {Raised} raised on the spot ({ByScroll} by a scroll, {ByBook} by the spell from a book, {ByBandage} by a bandage; {Casts} casts begun, {Tries} tries, {Failed} that did not take, {Refused} the engine would not stand up), {Things} things and {Coin}gp back off their own corpses at once, {Rejoined} pressed back into their company, {Held} revivals held for it; "
              + $"not raised: {NobodyAble} with nobody in the company able (no scroll at 50 Magery, no book at 70 with its herbs, no 80/80 healer with cloth), {NobodyNear} with nobody able within {Reach} tiles, {TriedAndFailed} tried and missed, {FightHeld} the fight never cleared, {CarriedHome} carried home first, {Spent} past the company's {PerCompany} raisings, {Ground} on ground that takes no body, {Outlawed} criminals and murderers, {Broken} the company broke up; "
              + $"{FightWaits} beats waited for the fight to clear, {ManaWaits} for the mana, {Handed} raisers given up on for another; carrying: {BotLifeline.Describe()}";

    public static void Forget()
    {
        _pending.Clear();
        Held = 0;
        Fallen = 0;
        Raised = 0;
        ByScroll = 0;
        ByBook = 0;
        ByBandage = 0;
        Things = 0;
        Coin = 0;
        Tries = 0;
        Failed = 0;
        Casts = 0;
        Rejoined = 0;
        Refused = 0;
        NobodyAble = 0;
        NobodyNear = 0;
        TriedAndFailed = 0;
        FightHeld = 0;
        CarriedHome = 0;
        Spent = 0;
        Ground = 0;
        Outlawed = 0;
        Broken = 0;
        ManaWaits = 0;
        FightWaits = 0;
        Handed = 0;
        BotLifeline.Forget();
    }
}

/// <summary>
/// <b>The scroll a company's caster carries for the one of it that falls (02.10.2026).</b> Offers a bot that reads a resurrection
/// scroll well enough to be worth it (<see cref="BotRaise.Reads"/>: one chance in ten a try, fifty mana) one to keep in its pack,
/// bought the way every scroll above the third circle is bought on this shard — off one of ours that has one at its shop by the bank,
/// or asked for aloud at the market's price, for a scribe to write and bring (<see cref="BotAcquire"/>). No shopkeeper of this era
/// sells the eighth circle. Never with the last of the purse (<see cref="BotRaise.StockReserve"/>), and kept, not sold, at the
/// counter (<c>BotUnload.Keeps</c>).
/// </summary>
public sealed class BotLifeline : IBotProposer
{
    public string Name => "Lifeline";

    public BotStanding Rung => BotStanding.Free;

    public static long Asked { get; private set; }

    public static long Carrying { get; private set; }

    public static long Broke { get; private set; }

    public static long FromBank { get; private set; }

    public static long Bought { get; private set; }

    public static long Spoken { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (!BotRaise.Running || map == null || map == Map.Internal || !body.Alive || !BotRaise.Reads(body))
        {
            return null;
        }

        Asked++;

        var kind = typeof(ResurrectionScroll);

        if ((body.Backpack?.GetAmount(kind) ?? 0) >= BotRaise.Stock)
        {
            Carrying++;

            return null;
        }

        var forInfirmary = BotInfirmaries.Stocks(body);
        var purse = forInfirmary ? BotYield.Spendable(body) : body.Backpack?.TotalGold ?? 0;
        var reserve = forInfirmary ? BotInfirmaries.ScrollKeep : BotRaise.StockReserve;

        if (purse <= reserve)
        {
            Broke++;

            return null;
        }

        if (BotMarket.FromBank(bot, kind, BotRaise.Stock) > 0)
        {
            FromBank++;

            return null;
        }

        var want = BotMarket.Wanted(bot, kind);

        BotShops.Survey(map, body.Location);

        var front = BotShopkeep.Best(bot, kind, 1, out var ask, out _, out _);

        if (front != null && purse - ask >= reserve)
        {
            Bought++;

            return BotAcquire.AtShop(kind, BotRaise.Spell, front, ask, toCast: true);
        }

        if (want != null)
        {
            return null;
        }

        var offer = BotMarket.Worth(kind, BotGrimoire.ShopPrice(BotGrimoire.Circle(BotRaise.Spell)));

        if (purse - offer < reserve)
        {
            Broke++;

            return null;
        }

        Spoken++;

        return BotAcquire.Board(kind, BotRaise.Spell, map, body.Location, offer, toCast: true);
    }

    public static string Describe() =>
        $"{Asked} able to read a resurrection scroll asked: {Carrying} already carrying {BotRaise.Stock}, {Broke} at or under {BotRaise.StockReserve}gp, {FromBank} took their own out of the bank, {Bought} sent to a shop of ours, {Spoken} asked the island for one";

    public static void Forget()
    {
        Asked = 0;
        Carrying = 0;
        Broke = 0;
        FromBank = 0;
        Bought = 0;
        Spoken = 0;
    }
}
