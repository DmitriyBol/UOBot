using System;
using System.Collections.Generic;
using Server.Items;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// Finding something worth fighting, and finding what it left behind.
///
/// <para>
/// <b>Every judgement here is already written.</b> <c>BotThreat</c> knows what fighting power is (how long a
/// thing lasts multiplied by how hard it hits), what counts as hostile, and what our side comes to including
/// whoever happens to be standing nearby. This file adds one question to that: of the things I could beat,
/// which is the biggest. Nothing about combat is re-decided.
/// </para>
///
/// <para>
/// <b>The biggest I can beat, not the nearest.</b> Distance is already priced by the appraisal's nearness
/// factor, so a hunter that preferred the closest rat would be paid twice for being lazy. And the whole shape
/// of the first version's death loop was fighting things it could not beat, so the test is the same tolerance
/// the flight decision uses — it must lose by the same arithmetic it would flee by.
/// </para>
///
/// <para>
/// <b>No memory of where the monsters are.</b> Unlike a vein or a shop, a creature walks and respawns, so a
/// remembered one is a lie within a minute. The sweep is fresh every time and that is the cost of this
/// proposer; which patches of ground pay is remembered instead, by the ledger, which is the right place for a
/// fact about ground.
/// </para>
/// </summary>
public static class BotQuarry
{
    public static int Reach { get; set; } = 30;

    public static int Notice { get; set; } = 12;

    public static double Daring { get; set; } = 1.0;

    public static int LootReach { get; set; } = 2;

    public static int ClaimMs { get; set; } = 45000;

    private static readonly Dictionary<Serial, (Serial Hunter, long Tick)> _claims = [];

    public static void Claim(Mobile hunter, Mobile quarry)
    {
        if (hunter == null || quarry == null)
        {
            return;
        }

        if (!Held(quarry, out var by) || by == hunter.Serial)
        {
            _claims[quarry.Serial] = (hunter.Serial, Core.TickCount);
        }
    }

    public static bool Ours(Mobile hunter, Mobile quarry) =>
        hunter != null && quarry != null && (!Held(quarry, out var by) || by == hunter.Serial);

    public static void Release(Mobile quarry)
    {
        if (quarry != null)
        {
            _claims.Remove(quarry.Serial);
        }
    }

    private static bool Held(Mobile quarry, out Serial by)
    {
        by = Serial.Zero;

        if (!_claims.TryGetValue(quarry.Serial, out var claim))
        {
            return false;
        }

        if (Core.TickCount - claim.Tick >= ClaimMs)
        {
            _claims.Remove(quarry.Serial);

            return false;
        }

        by = claim.Hunter;

        return true;
    }

    public static int ShunMs { get; set; } = 120000;

    public static int HopelessMs { get; set; } = 900000;

    private static readonly Dictionary<Serial, long> _shunned = [];

    public static void Shun(Mobile quarry) => Shun(quarry, Sentence(quarry));

    public static int StillWithin { get; set; } = 2;

    public static long Reshunned { get; private set; }

    private static readonly Dictionary<Serial, (int Times, Point3D At)> _unreached = [];

    private static int Sentence(Mobile quarry)
    {
        if (quarry == null)
        {
            return ShunMs;
        }

        var times = _unreached.TryGetValue(quarry.Serial, out var last) && quarry.InRange(last.At, StillWithin)
            ? last.Times + 1
            : 1;

        if (_unreached.Count >= 4096)
        {
            _unreached.Clear();
        }

        _unreached[quarry.Serial] = (times, quarry.Location);

        if (times == 1)
        {
            return ShunMs;
        }

        Reshunned++;

        return (int)System.Math.Min((long)ShunMs << System.Math.Min(times - 1, 8), HopelessMs);
    }

    public static void Shun(Mobile quarry, int ms)
    {
        if (quarry != null)
        {
            var until = Core.TickCount + ms;

            if (!_shunned.TryGetValue(quarry.Serial, out var standing) || until - standing > 0)
            {
                _shunned[quarry.Serial] = until;
            }
        }
    }

    public static bool Shunned(Mobile quarry)
    {
        if (!_shunned.TryGetValue(quarry.Serial, out var until))
        {
            return false;
        }

        if (Core.TickCount - until < 0)
        {
            return true;
        }

        _shunned.Remove(quarry.Serial);

        return false;
    }

    public static int CrowdMs { get; set; } = 120000;

    private static readonly Dictionary<Serial, long> _crowded = [];

    public static int HandMs { get; set; } = 10000;

    private static readonly Dictionary<Serial, long> _handed = [];

    public static void Hand(Mobile quarry)
    {
        if (quarry != null)
        {
            _handed[quarry.Serial] = Core.TickCount + HandMs;
        }
    }

    public static bool Handed(Mobile quarry)
    {
        if (quarry == null || !_handed.TryGetValue(quarry.Serial, out var until))
        {
            return false;
        }

        if (Core.TickCount - until < 0)
        {
            return true;
        }

        _handed.Remove(quarry.Serial);

        return false;
    }

    public static void Crowd(Mobile quarry)
    {
        if (quarry != null)
        {
            _crowded[quarry.Serial] = Core.TickCount + CrowdMs;
        }
    }

    public static bool Crowded(Mobile quarry)
    {
        if (!_crowded.TryGetValue(quarry.Serial, out var until))
        {
            return false;
        }

        if (Core.TickCount - until < 0)
        {
            return true;
        }

        _crowded.Remove(quarry.Serial);

        return false;
    }

    private static readonly Dictionary<Type, (long Gold, int Kills)> _paid = [];

    public static double Untried { get; set; } = 25.0;

    public static void Paid(Type kind, int gold)
    {
        if (kind == null)
        {
            return;
        }

        _paid.TryGetValue(kind, out var sofar);

        _paid[kind] = (sofar.Gold + Math.Max(0, gold), sofar.Kills + 1);

        Killed(kind);
    }

    public static bool PricesRisk { get; set; } = true;

    public static int RiskHalfMs { get; set; } = 3600000;

    public static double RiskLeast { get; set; } = 3.0;

    public static double RiskFair { get; set; } = 0.25;

    public static double RiskSteep { get; set; } = 2.0;

    public static double RiskMost { get; set; } = 8.0;

    private sealed class Record
    {
        public double Kills;

        public double Deaths;

        public long Tick;
    }

    private static readonly Dictionary<Type, Record> _records = [];

    public static long PricedOut { get; private set; }

    public static long CompanyPricedOut { get; private set; }

    public static long CompanyChoiceOut { get; private set; }

    public static long ZonePricedOut { get; private set; }

    public static long PayUnheeded { get; private set; }

    private static Record Aged(Type kind, long now, bool make)
    {
        if (!_records.TryGetValue(kind, out var record))
        {
            if (!make)
            {
                return null;
            }

            if (_records.Count > 1024)
            {
                _records.Clear();
            }

            record = new Record { Tick = now };
            _records[kind] = record;

            return record;
        }

        var span = now - record.Tick;

        if (span > 0 && RiskHalfMs > 0)
        {
            var keep = Math.Pow(0.5, span / (double)RiskHalfMs);

            record.Kills *= keep;
            record.Deaths *= keep;
            record.Tick = now;
        }

        return record;
    }

    private static void Killed(Type kind)
    {
        if (kind == null)
        {
            return;
        }

        Aged(kind, Core.TickCount, true).Kills += 1.0;
    }

    public static void Fell(Mobile killer)
    {
        if (killer is not BaseCreature { Deleted: false, Controlled: false, Summoned: false } creature || killer is BotMobile)
        {
            return;
        }

        Aged(creature.GetType(), Core.TickCount, true).Deaths += 1.0;
    }

    public static double Price(Type kind)
    {
        if (!PricesRisk || kind == null)
        {
            return 1.0;
        }

        var record = Aged(kind, Core.TickCount, false);

        return record == null ? 1.0 : Formula(record.Deaths, record.Kills);
    }

    public static double Formula(double deaths, double kills)
    {
        if (deaths < RiskLeast)
        {
            return 1.0;
        }

        var perKill = deaths / (kills + 1.0);

        return Math.Clamp(1.0 + RiskSteep * (perKill - RiskFair), 1.0, Math.Max(1.0, RiskMost));
    }

    public static double Priced(BaseCreature creature) =>
        creature == null ? 0.0 : BotThreat.Power(creature) * Price(creature.GetType());

    public static double ZonePrice(BotZoneKindCount[] kinds, bool company = false)
    {
        var most = 1.0;

        if (!PricesRisk || kinds == null)
        {
            return most;
        }

        for (var i = 0; i < kinds.Length; i++)
        {
            if (kinds[i].Live <= 0 || KindOf(kinds[i].Kind) is not { } type)
            {
                continue;
            }

            most = Math.Max(most, company ? BotCompanyRecord.Price(type) : Price(type));
        }

        return most;
    }

    internal static void ZoneRefused() => ZonePricedOut++;

    private static Type KindOf(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (!_kindTypes.TryGetValue(name, out var type))
        {
            type = AssemblyHandler.FindTypeByName(name);
            _kindTypes[name] = type;
        }

        return type;
    }

    private static string DescribePrices()
    {
        if (!PricesRisk)
        {
            return "off";
        }

        var now = Core.TickCount;
        List<(string Name, double Price, double Deaths, double Kills)> priced = [];

        foreach (var (kind, _) in _records)
        {
            var record = Aged(kind, now, false);
            var price = Price(kind);

            if (record != null && price > 1.0)
            {
                priced.Add((kind.Name, price, record.Deaths, record.Kills));
            }
        }

        if (priced.Count == 0)
        {
            return "none";
        }

        priced.Sort(static (a, b) => b.Price.CompareTo(a.Price));

        List<string> named = [];

        for (var i = 0; i < priced.Count && i < 8; i++)
        {
            named.Add($"{priced[i].Name} x{priced[i].Price:F1} ({priced[i].Deaths:F0} of ours to {priced[i].Kills:F0} of them)");
        }

        return string.Join(", ", named);
    }

    public static void Rewarded(Type kind, int gold)
    {
        if (kind == null || gold <= 0)
        {
            return;
        }

        _paid.TryGetValue(kind, out var sofar);

        _paid[kind] = (sofar.Gold + gold, sofar.Kills);
    }

    public static double Bounty { get; set; } = 200.0;

    public static long Sent { get; private set; }

    private static Type Butchered(MeatType meat) =>
        meat switch
        {
            MeatType.Bird    => typeof(RawBird),
            MeatType.LambLeg => typeof(RawLambLeg),
            _                => typeof(RawRibs)
        };

    public static double Sought(BaseCreature creature, Mobile hunter = null)
    {
        if (creature == null)
        {
            return 0.0;
        }

        var worth = 0.0;

        var hides = Demanded(typeof(Hides)) || BotCharter.Wants(hunter, "hides");
        var feathers = Demanded(typeof(Feather)) || Demanded(typeof(Arrow)) || BotCharter.Wants(hunter, "feathers") || BotFletcher.Starved;
        var wool = Demanded(typeof(Wool)) || BotCharter.Wants(hunter, "wool");
        var meat = BotCharter.Wants(hunter, "meat");

        if (creature.Feathers > 0 && feathers)
        {
            worth += Bounty;
        }

        if (creature.Hides > 0 && hides)
        {
            worth += Bounty;
        }

        if (creature.Wool > 0 && wool)
        {
            worth += Bounty;
        }

        if (creature.Meat > 0 && (meat || Demanded(Butchered(creature.MeatType))))
        {
            worth += Bounty;
        }

        return worth;
    }

    private static bool Demanded(Type kind)
    {
        var wants = BotMarket.Wants;

        for (var i = 0; i < wants.Count; i++)
        {
            if (wants[i].IsOpen && wants[i].Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    public static double Pays(Type kind) =>
        kind != null && _paid.TryGetValue(kind, out var known) && known.Kills > 0
            ? (double)known.Gold / known.Kills
            : Untried;

    public static bool PayFirst { get; set; } = true;

    public static int PayReach { get; set; } = 2;

    public static int PayKnown { get; set; } = 3;

    public static double ZoneLeast { get; set; } = 0.5;

    public static double ZoneMost { get; set; } = 2.0;

    public static long PaidFirst { get; private set; }

    public static long BarrenPassed { get; private set; }

    public static long ZoneWeighed { get; private set; }

    public static long Dear { get; private set; }

    public static bool Barren(Type kind) =>
        kind != null && _paid.TryGetValue(kind, out var known) && known.Kills >= PayKnown && known.Gold <= 0;

    public static BaseCreature Paying(IBotWilful bot, Mobile body)
    {
        var near = Best(body, Reach);

        if (!PayFirst || body == null || near != null && !Barren(near.GetType()) || BotYield.Spendable(body) >= BotHunter.PoorBelow)
        {
            return near;
        }

        var wide = Best(body, Reach * Math.Max(1, PayReach), true);

        if (wide == null)
        {
            return near;
        }

        PaidFirst++;

        return wide;
    }

    private static readonly Dictionary<string, Type> _kindTypes = [];

    public static double ZonePays(BotZoneKindCount[] kinds, Mobile body)
    {
        if (!PayFirst || kinds == null || kinds.Length == 0 || body == null || BotYield.Spendable(body) >= BotHunter.PoorBelow)
        {
            return 1.0;
        }

        var weight = 0;
        var pays = 0.0;

        for (var i = 0; i < kinds.Length; i++)
        {
            var type = KindOf(kinds[i].Kind);

            if (type == null || !_paid.TryGetValue(type, out var known) || known.Kills <= 0)
            {
                continue;
            }

            if (Price(type) > 1.0)
            {
                PayUnheeded++;

                continue;
            }

            var live = Math.Max(1, kinds[i].Live);

            weight += live;
            pays += live * ((double)known.Gold / known.Kills);
        }

        if (weight <= 0)
        {
            return 1.0;
        }

        ZoneWeighed++;

        return Math.Clamp(0.5 + pays / weight / Math.Max(1.0, Untried), ZoneLeast, ZoneMost);
    }

    public static void Forget()
    {
        _claims.Clear();
        _shunned.Clear();
        _unreached.Clear();
        _crowded.Clear();
        _paid.Clear();
        Sent = 0;
        InTown = 0;
        OnGuarded = 0;
        ChosenAttacking = 0;
        ChosenFighting = 0;
        ChosenGrazing = 0;
        ChosenFightingBack = 0;
        PaidFirst = 0;
        BarrenPassed = 0;
        ZoneWeighed = 0;
        Dear = 0;
        _records.Clear();
        BotCompanyRecord.Forget();
        PricedOut = 0;
        CompanyPricedOut = 0;
        CompanyChoiceOut = 0;
        ZonePricedOut = 0;
        PayUnheeded = 0;
    }

    public static string Describe()
    {
        var kinds = 0;
        var paying = 0;

        foreach (var (_, known) in _paid)
        {
            kinds++;

            if (known.Gold > 0)
            {
                paying++;
            }
        }

        return $"{kinds} kinds of creature killed and priced, {paying} of them worth the trouble; {Walled} passed over by a company as shut off from where it stood and {Afloat} as swimming where nobody can stand; {Penned} passed over by a lone hunter as standing in a pocket shut off from it, {Unwelcome} as standing on refused ground and {Daunted} as standing on ground asking more strength than it brought ({(ReadsGround ? "passed over" : "only counted")}); {Reshunned} left alone for longer for being found unreachable again where they stood before; hunters sent after {ChosenAttacking} creatures at their throat, {ChosenFighting} that fight whoever comes near and {ChosenGrazing} of the grazing kind ({ChosenFightingBack} of them of a kind that fights back when struck) ({(HostileFirst ? "hostile first" : "by pay alone")}); a poor hunter (under {BotHunter.PoorBelow}gp) looks {PayReach} times as far first for a kind that pays: {PaidFirst} found one, {BarrenPassed} of a kind known to carry nothing passed over in that look, {ZoneWeighed} zone looks weighed by what their kinds pay{(PayFirst ? "" : " (off)")}; {Dear} quarry passed over by a caster as not paying for {BotSlay.CastFloor} casts; priced for the lives they cost (strength × the kind's price, {RiskLeast:F0} deaths of ours inside the hour's weighing before any price): {PricedOut} lone hunters' choices changed, {CompanyPricedOut} creatures passed over in a company's look ({CompanyChoiceOut} more between the company's choice gate and the tolerance), {ZonePricedOut} zones or their creatures passed over in a hunter's zone look, {PayUnheeded} kinds' pay (bounties included) left out of a poor hunter's zone weighing; prices now: {DescribePrices()}";
    }

    public static BaseCreature Best(Mobile bot, int range) => Best(bot, range, false);

    public static BaseCreature Best(Mobile bot, int range, bool payingOnly)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        var ours = BotThreat.Power(bot);

        if (ours <= 0.0)
        {
            return null;
        }

        var daring = BotTraits.Daring(bot);

        var strength = BotQuad.Strength(bot);

        var casts = BotSlay.CastsByGold && bot is BotMobile { Class.Role: BotRole.Caster } && BotStrike.Can(bot)
            ? BotProvision.CastCost(bot, BotStrike.Best(bot))
            : 0;

        BaseCreature best = null;
        var bestPower = 0.0;
        var bestPays = -1.0;
        var bestRank = -1;

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(bot.Location, range))
        {
            if (!BotThreat.Hostile(bot, creature) || !BotPopulation.Within(map, creature.Location, bot))
            {
                continue;
            }

            if (OutsideTowns && creature.Region?.IsPartOf<TownRegion>() == true)
            {
                InTown++;

                continue;
            }

            if (OutsideTowns && GuardedRegion.IsHostileCreature(creature) && BotHaven.Guarded(map, creature.Location))
            {
                OnGuarded++;

                continue;
            }

            if (!Ours(bot, creature) || Shunned(creature) || Crowded(creature))
            {
                continue;
            }

            var power = BotThreat.Power(creature);

            if (power <= 0.0 || power > ours * daring)
            {
                continue;
            }

            if (payingOnly && Barren(creature.GetType()))
            {
                BarrenPassed++;

                continue;
            }

            if (casts > 0 && !ReferenceEquals(creature.Combatant, bot)
                && Pays(creature.GetType()) + Sought(creature, bot) < casts * BotSlay.CastFloor)
            {
                Dear++;

                continue;
            }

            var pays = Pays(creature.GetType()) + Sought(creature, bot);

            var rank = !HostileFirst ? 0
                : ReferenceEquals(creature.Combatant, bot) ? 3
                : Fights(creature) ? 2
                : FightsBack(creature) ? 1
                : 0;

            if (best != null
                && (rank < bestRank || rank == bestRank && (pays < bestPays || pays == bestPays && power <= bestPower)))
            {
                continue;
            }

            if (power * Price(creature.GetType()) > ours * daring)
            {
                PricedOut++;

                continue;
            }

            if (HunterReadsRefusals && BotRefused.Refusing(map, creature.Location))
            {
                Unwelcome++;

                continue;
            }

            if (ShutOff(map, bot.Location, creature.Location))
            {
                Penned++;

                continue;
            }

            if (BotQuad.MuscleNear(map, creature.Location, GroundWithin) > strength)
            {
                Daunted++;

                if (ReadsGround)
                {
                    continue;
                }
            }

            best = creature;
            bestPower = power;
            bestPays = pays;
            bestRank = rank;
        }

        return best;
    }

    internal static bool Fights(BaseCreature creature) =>
        creature.FightMode is not (FightMode.None or FightMode.Aggressor) || creature.Combatant != null;

    internal static bool FightsBack(BaseCreature creature) => creature.FightMode == FightMode.Aggressor;

    public static bool OutsideTowns { get; set; } = true;

    public static long InTown { get; private set; }

    public static long OnGuarded { get; private set; }

    public static bool HostileFirst { get; set; } = true;

    public static long ChosenAttacking { get; private set; }

    public static long ChosenFighting { get; private set; }

    public static long ChosenGrazing { get; private set; }

    public static void Chosen(BaseCreature quarry, Mobile hunter)
    {
        if (Sought(quarry, hunter) > 0.0)
        {
            Sent++;
        }

        if (quarry == null)
        {
            return;
        }

        if (ReferenceEquals(quarry.Combatant, hunter))
        {
            ChosenAttacking++;
        }
        else if (Fights(quarry))
        {
            ChosenFighting++;
        }
        else
        {
            ChosenGrazing++;

            if (FightsBack(quarry))
            {
                ChosenFightingBack++;
            }
        }
    }

    public static long ChosenFightingBack { get; private set; }

    public static bool Worthwhile(Mobile bot) =>
        bot != null
        && BotThreat.Decide(bot, BotMobile.NoticeRange) != BotStand.Outmatched
        && Best(bot, Reach) != null;

    public static long Walled { get; private set; }

    public static long Penned { get; private set; }

    public static long Unwelcome { get; private set; }

    public static bool HunterReadsRefusals { get; set; } = true;

    public static bool ReadsGround { get; set; } = true;

    public static int GroundWithin { get; set; } = 12;

    public static long Daunted { get; private set; }

    private static bool ShutOff(Map map, Point3D from, Point3D at) =>
        BotReach.Ask(map, from, at, BotArrival.Exactly) == BotReachVerdict.Sealed;

    public static long Afloat { get; private set; }

    public static BaseCreature Company(Mobile bot, int range) => Company(bot, range, 0.0, out _);

    public static BaseCreature Company(Mobile bot, int range, double odds) => Company(bot, range, odds, out _);

    public static BaseCreature Company(Mobile bot, int range, out CompanyRefusal why) => Company(bot, range, 0.0, out why);

    /// <summary>Why no company could be called, when none could. See <see cref="Company"/>.</summary>
    public enum CompanyRefusal
    {
        None,

        Alone,

        AllSmall,

        AllTooBig,

        Nothing
    }

    public static BaseCreature Company(Mobile bot, int range, double odds, out CompanyRefusal why)
    {
        why = CompanyRefusal.Nothing;

        var map = bot?.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        var gate = odds > 0.0 ? Math.Min(odds, BotThreat.Tolerance) : BotThreat.Tolerance;

        var alone = BotThreat.Power(bot);

        if (alone <= 0.0)
        {
            return null;
        }

        var together = BotThreat.OurPower(bot, range);

        if (together <= alone)
        {
            why = CompanyRefusal.Alone;

            return null;
        }

        BaseCreature best = null;
        var bestPower = 0.0;
        var bestLetIn = false;
        var small = 0;
        var big = 0;

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(bot.Location, range))
        {
            if (!BotThreat.Hostile(bot, creature) || !BotPopulation.Within(map, creature.Location, bot))
            {
                continue;
            }

            if (!Ours(bot, creature) || Shunned(creature))
            {
                continue;
            }

            if (creature.CanSwim && !BotStep.Settle(map, creature.X, creature.Y, out _))
            {
                Afloat++;

                continue;
            }

            if (ShutOff(map, bot.Location, creature.Location))
            {
                Walled++;

                continue;
            }

            if (BotRefused.Refusing(map, creature.Location))
            {
                Walled++;

                continue;
            }

            var power = BotThreat.Power(creature);

            var priced = power * Price(creature.GetType());

            if (priced <= alone * Daring)
            {
                small++;

                continue;
            }

            var forCompany = power * BotCompanyRecord.Price(creature.GetType());

            if (forCompany > together * gate)
            {
                big++;

                if (power <= together * gate)
                {
                    CompanyPricedOut++;
                }

                if (forCompany <= together * BotThreat.Tolerance)
                {
                    CompanyChoiceOut++;
                }

                continue;
            }

            if (power <= bestPower)
            {
                continue;
            }

            best = creature;
            bestPower = power;

            bestLetIn = priced > together * gate;
        }

        if (best != null)
        {
            why = CompanyRefusal.None;

            if (bestLetIn)
            {
                BotCompanyRecord.Looked();
            }
        }
        else if (big > 0)
        {
            why = CompanyRefusal.AllTooBig;
        }
        else if (small > 0)
        {
            why = CompanyRefusal.AllSmall;
        }

        return best;
    }

    public static Corpse Remains(Map map, Point3D where, Mobile fallen)
    {
        if (map == null || map == Map.Internal || fallen == null)
        {
            return null;
        }

        foreach (var item in map.GetItemsInRange(where, LootReach))
        {
            if (item is Corpse corpse && !corpse.Deleted && corpse.Owner == fallen)
            {
                return corpse;
            }
        }

        return null;
    }
}
