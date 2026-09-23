using System;
using System.Collections.Generic;
using Server.Items;
using Server.Mobiles;

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
        var feathers = Demanded(typeof(Feather)) || Demanded(typeof(Arrow)) || BotCharter.Wants(hunter, "feathers");
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

        if (worth > 0.0)
        {
            Sent++;
        }

        return worth;
    }

    private static bool Demanded(Type kind)
    {
        var wants = BotAuction.Wants;

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

    public static void Forget()
    {
        _claims.Clear();
        _shunned.Clear();
        _unreached.Clear();
        _crowded.Clear();
        _paid.Clear();
        Sent = 0;
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

        return $"{kinds} kinds of creature killed and priced, {paying} of them worth the trouble; {Walled} passed over by a company as shut off from where it stood and {Afloat} as swimming where nobody can stand; {Penned} passed over by a lone hunter as standing in a pocket shut off from it, {Unwelcome} as standing on refused ground and {Daunted} as standing on ground asking more strength than it brought ({(ReadsGround ? "passed over" : "only counted")}); {Reshunned} left alone for longer for being found unreachable again where they stood before";
    }

    public static BaseCreature Best(Mobile bot, int range)
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

        var strength = BotQuad.Strength(bot);

        BaseCreature best = null;
        var bestPower = 0.0;
        var bestPays = -1.0;

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(bot.Location, range))
        {
            if (!BotThreat.Hostile(bot, creature) || !BotPopulation.Within(map, creature.Location))
            {
                continue;
            }

            if (!Ours(bot, creature) || Shunned(creature) || Crowded(creature))
            {
                continue;
            }

            var power = BotThreat.Power(creature);

            if (power <= 0.0 || power > ours * Daring)
            {
                continue;
            }

            var pays = Pays(creature.GetType()) + Sought(creature, bot);

            if (best != null && (pays < bestPays || pays == bestPays && power <= bestPower))
            {
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
        }

        return best;
    }

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

    public static BaseCreature Company(Mobile bot, int range) => Company(bot, range, out _);

    /// <summary>Why no company could be called, when none could. See <see cref="Company"/>.</summary>
    public enum CompanyRefusal
    {
        None,

        Alone,

        AllSmall,

        AllTooBig,

        Nothing
    }

    public static BaseCreature Company(Mobile bot, int range, out CompanyRefusal why)
    {
        why = CompanyRefusal.Nothing;

        var map = bot?.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

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
        var small = 0;
        var big = 0;

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(bot.Location, range))
        {
            if (!BotThreat.Hostile(bot, creature) || !BotPopulation.Within(map, creature.Location))
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

            if (power <= alone * Daring)
            {
                small++;

                continue;
            }

            if (power > together * BotThreat.Tolerance)
            {
                big++;

                continue;
            }

            if (power <= bestPower)
            {
                continue;
            }

            best = creature;
            bestPower = power;
        }

        if (best != null)
        {
            why = CompanyRefusal.None;
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
