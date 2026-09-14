using System;
using Server.Logging;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a fight to any bot healthy enough to want one.
///
/// <para>
/// <b>Nothing decides who is a fighter.</b> No class list, no role check: whoever can beat the thing standing
/// in the field is offered it, and a mage with wrestling at thirty will find that the arithmetic says no. That
/// is the same rule as the pickaxe and the pen — the ability decides, not the name — and it means a crafter
/// caught in a lean patch can go and hit something rather than sitting in a census as "nothing was worth
/// doing".
/// </para>
///
/// <para>
/// <b>Only within the population's own ground, and that is the whole of the answer to the first version's
/// worst night.</b> Four hundred and forty-three deaths, a hundred and four of them one bot resurrecting in
/// the same tile every thirty seconds, all of it in the far zones the population had walked to. A hunt that
/// cannot begin more than a screen and a half from where the bot is standing, in a world already bounded to
/// two hundred tiles around the spawn, cannot build that loop: the bot is never far from where it gets up.
/// </para>
///
/// <para>
/// The cost of this proposer is a real spatial sweep, every time a free bot asks. That is unavoidable and it
/// is the honest exception to "ask the world cheaply": a vein stays where it is and can be remembered, a shop
/// keeper stands still, but a monster walks and respawns, so a remembered one is a lie inside a minute.
/// </para>
/// </summary>
public sealed class BotHunter : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHunter));

    public static double FitAt { get; set; } = 0.8;

    private static bool _saidNoQuarry;

    public string Name => "Hunter";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (body.HitsMax <= 0 || body.Hits < body.HitsMax * FitAt)
        {
            return null;
        }

        if (body is BotMobile { Class.DefendsOnly: true })
        {
            Sworn++;

            return null;
        }

        if (BotThreat.Decide(body, BotMobile.NoticeRange) == BotStand.Outmatched)
        {
            var elsewhere = Hunting(bot, body, map, out var outmatched);

            return elsewhere == Point3D.Zero ? null : new BotProwl(map, elsewhere, outmatched);
        }

        var quarry = BotQuarry.Best(body, BotQuarry.Reach);

        if (quarry != null
            && !Utility.InRange(body.Location, quarry.Location, Near)
            && BotThreat.Overrun(body, quarry, BotMobile.NoticeRange))
        {
            BotQuarry.Crowd(quarry);
            Overrun++;

            quarry = null;
        }

        if (quarry == null)
        {
            Missing(map);

            var ground = Hunting(bot, body, map, out var company);

            return ground == Point3D.Zero ? null : new BotProwl(map, ground, company);
        }

        BotQuarry.Claim(body, quarry);

        var trains = bot.Bond?.Weapon?.Skill ?? SkillName.Wrestling;

        return new BotSlay(quarry, trains);
    }

    private static Point3D Hunting(IBotWilful bot, Mobile body, Map map, out bool company)
    {
        company = false;

        var ledger = bot?.Resolve?.Ledger;
        var home = BotPopulation.Where;
        var roam = Math.Clamp(BotPopulation.Roam / 2, BotQuarry.Reach, Walkable);

        var best = Point3D.Zero;
        var bestPaid = -1.0;
        var bestKnown = -1.0;
        var bestWanted = false;

        var refused = 0;

        var needsCompany = false;
        var bestNeedsCompany = false;

        var quietest = Point3D.Zero;
        var quietestSafety = 0.0;

        var noisy = Noisy(body, map, roam);
        var paying = Paying(body, map, roam);

        var feared = Feared(body, map);

        for (var tries = 0; tries <= Samples + 2; tries++)
        {
            Point3D where;

            needsCompany = false;

            if (tries == 0)
            {
                if (noisy == Point3D.Zero)
                {
                    continue;
                }

                where = noisy;
            }
            else if (tries == 1)
            {
                if (paying == Point3D.Zero)
                {
                    continue;
                }

                where = paying;
            }
            else if (tries == 2)
            {
                if (feared == Point3D.Zero)
                {
                    continue;
                }

                where = feared;
            }
            else
            {
                var x = home.X + Utility.RandomMinMax(-roam, roam);
                var y = home.Y + Utility.RandomMinMax(-roam, roam);

                if (!BotStep.Settle(map, x, y, out var z))
                {
                    continue;
                }

                where = new Point3D(x, y, z);
            }

            if (Utility.InRange(body.Location, where, BotQuarry.Reach))
            {
                continue;
            }

            if (!Utility.InRange(body.Location, where, Walkable))
            {
                Distant++;

                continue;
            }

            if (TroddenOnly && !Walked(map, where))
            {
                Untrodden++;

                continue;
            }

            if (Region.Find(where, map)?.IsPartOf<TownRegion>() == true)
            {
                continue;
            }

            if (BotRefused.Refusing(map, where))
            {
                Darted++;

                continue;
            }

            if (BotReach.Ask(map, body.Location, where, BotArrival.Within(BotQuarry.Reach)) == BotReachVerdict.Sealed)
            {
                continue;
            }

            if (!BotQuad.Dares(body, map, where))
            {
                if (!BotQuad.Together(body, map, where, BotMuster.Reach))
                {
                    Overmatched++;

                    continue;
                }

                if (BotProwl.Raising(map, where))
                {
                    Claimed++;

                    continue;
                }

                needsCompany = true;
            }

            var paid = ledger?.Expect(BotSlay.Trade, map, where, 0.0) ?? 0.0;

            var known = BotPeril.Reading(map, where);

            var safety = BotQuad.Safety(map, where);

            if (safety > BotQuad.TooQuiet)
            {
                Quiet++;
                refused++;

                if (quietest == Point3D.Zero || safety < quietestSafety)
                {
                    quietest = where;
                    quietestSafety = safety;
                }

                continue;
            }

            var wanted = safety <= BotQuad.Wanted;

            var better = best == Point3D.Zero
                || (wanted && !bestWanted)
                || (wanted == bestWanted && (paid > bestPaid || (paid >= bestPaid && known > bestKnown)));

            if (better)
            {
                best = where;
                bestPaid = paid;
                bestKnown = known;
                bestWanted = wanted;
                bestNeedsCompany = needsCompany;
                company = needsCompany;

                if (wanted)
                {
                    Sought++;
                }
            }
        }

        if (best == Point3D.Zero && refused > 0)
        {
            Stranded++;

            return quietest;
        }

        return best;
    }

    private static Point3D Paying(Mobile body, Map map, int roam)
    {
        var rich = BotCommons.Richest(BotSlay.Trade, map, body.Location, roam);

        if (rich == Point3D.Zero || !BotStep.Settle(map, rich.X, rich.Y, out var z))
        {
            return Point3D.Zero;
        }

        var where = new Point3D(rich.X, rich.Y, z);

        if (Utility.InRange(body.Location, where, BotQuarry.Reach))
        {
            return Point3D.Zero;
        }

        if (BotQuad.Baulking(map, where) || BotRefused.Refusing(map, where))
        {
            Rested++;

            return Point3D.Zero;
        }

        return BotReach.Ask(map, body.Location, where, BotArrival.Within(BotQuarry.Reach)) == BotReachVerdict.Sealed
            ? Point3D.Zero
            : where;
    }

    private static Point3D Feared(Mobile body, Map map)
    {
        var middle = BotQuad.WorstNear(map, BotPopulation.Where, FearedReach);

        if (middle == Point2D.Zero || !BotStep.Settle(map, middle.X, middle.Y, out var z))
        {
            return Point3D.Zero;
        }

        var where = new Point3D(middle.X, middle.Y, z);

        if (Utility.InRange(body.Location, where, BotQuarry.Reach))
        {
            return Point3D.Zero;
        }

        return BotReach.Ask(map, body.Location, where, BotArrival.Within(BotQuarry.Reach)) == BotReachVerdict.Sealed
            ? Point3D.Zero
            : where;
    }

    private static Point3D Noisy(Mobile body, Map map, int roam)
    {
        var worst = BotPeril.Worst(map, body.Location, roam, out _);

        if (worst == Point3D.Zero || !BotStep.Settle(map, worst.X, worst.Y, out var z))
        {
            return Point3D.Zero;
        }

        var where = new Point3D(worst.X, worst.Y, z);

        if (Utility.InRange(body.Location, where, BotQuarry.Reach)
            || Region.Find(where, map)?.IsPartOf<TownRegion>() == true
            || BotReach.Ask(map, body.Location, where, BotArrival.Within(BotQuarry.Reach)) == BotReachVerdict.Sealed)
        {
            return Point3D.Zero;
        }

        if (BotQuad.Baulking(map, where) || BotRefused.Refusing(map, where))
        {
            Rested++;

            return Point3D.Zero;
        }

        return where;
    }

    private const int Samples = 8;

    public static int Near { get; set; } = 5;

    public static long Overrun { get; private set; }

    public static long Rested { get; private set; }

    public static bool TroddenOnly { get; set; } = true;

    public static long Untrodden { get; private set; }

    private static bool Walked(Map map, Point3D where)
    {
        if (BotQuad.Trodden(map, where))
        {
            return true;
        }

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if ((dx != 0 || dy != 0)
                    && BotQuad.Trodden(map, new Point3D(where.X + dx * BotQuad.Side, where.Y + dy * BotQuad.Side, where.Z)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static long Distant { get; private set; }

    public static long Darted { get; private set; }

    public static int FearedReach { get; set; } = 800;

    public static int Walkable
    {
        get => _walkable > 0 ? _walkable : Default;
        set => _walkable = value;
    }

    public static int Default { get; set; } = 500;

    private static int _walkable;

    private static void Missing(Map map)
    {
        if (_saidNoQuarry)
        {
            return;
        }

        _saidNoQuarry = true;

        logger.Error(
            "Nothing within {Reach} tiles of the bots on {Map} is worth fighting, so no gold will enter the world",
            BotQuarry.Reach,
            map
        );
    }

    public static long Sworn { get; private set; }

    public static long Quiet { get; private set; }

    public static long Stranded { get; private set; }

    public static long Overmatched { get; private set; }

    public static long Claimed { get; private set; }

    public static long Sought { get; private set; }

    public static string Describe() =>
        $"{Sworn} answers went to classes that only defend; {Quiet} hunting grounds passed over as too quiet (above {BotQuad.TooQuiet:F2}), {Sought} picked for having hurt somebody (at or below {BotQuad.Wanted:F2}), {Stranded} hunters left with nowhere to walk at all because every ground they looked at was too quiet, {Overmatched} grounds passed over for asking more strength than whoever looked had, {Claimed} for somebody already raising a company for them, {Overrun} quarry passed over for the crowd already round it, {Rested} named grounds passed over as resting after refusing somebody and {Darted} sampled ones, {Distant} further from the asker than {Walkable} tiles, which is as far as a road is ever searched for, {Untrodden} on squares nobody has ever stood in or beside, {BotProwl.Baulked} prowls given up for getting no nearer, {BotProwl.Raised} companies raised for ground one bot could not take, {BotProwl.Unraised} given up for not raising one";

    public static void Forget()
    {
        _saidNoQuarry = false;
        Sworn = 0;
        Rested = 0;
        Darted = 0;
        Distant = 0;
        Untrodden = 0;
        Quiet = 0;
        Stranded = 0;
        Overmatched = 0;
        Claimed = 0;
        Overrun = 0;
        Sought = 0;
    }
}
