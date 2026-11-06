using System;
﻿using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotHarrower : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHarrower));

    public static int Range
    {
        get => _range > 0 ? _range : BotPopulation.Roam;
        set => _range = value;
    }

    private static int _range;

    public string Name => "Baron";

    public BotStanding Rung => BotStanding.Free;

    public static long Asked { get; private set; }

    public static long NotABaron { get; private set; }

    public static long Held { get; private set; }

    public static long Unfit { get; private set; }

    public static long Quiet { get; private set; }

    public static long Sealed { get; private set; }

    public static long Roadless { get; private set; }

    public static long Roundabout { get; private set; }

    public static int MostDetour { get; set; } = 200;

    public static long Unfooted { get; private set; }

    public static long Unready { get; private set; }

    public static long Resting { get; private set; }

    public static long Offered { get; private set; }

    public static long Hunting { get; private set; }

    public static long Bountied { get; private set; }

    public static long Guilded { get; private set; }

    public static long Overwhelmed { get; private set; }

    public static long Unmanned { get; private set; }

    private static bool _said;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (body is not BotMobile { Class: BotBaron })
        {
            NotABaron++;

            return null;
        }

        Asked++;

        if (BotHarrow.Resting(body))
        {
            Resting++;

            return null;
        }

        if (!BotSquads.Running)
        {
            return null;
        }

        if (bot is not IBotSquadMember { Squad: null })
        {
            Held++;

            return null;
        }

        if (body.HitsMax <= 0 || body.Hits < body.HitsMax * BotHunter.FitAt)
        {
            Unfit++;

            return null;
        }

        if (BotUnderworld.RaidDue(out var den))
        {
            return new BotRaid(map, den, bot.Bond?.Weapon?.Skill ?? SkillName.Wrestling, true);
        }

        var red = BotOutlaw.AtLarge(map, body.Location, Range);

        if (red != null)
        {
            Hunting++;

            return new BotManhunt(red, bot.Bond?.Weapon?.Skill ?? SkillName.Wrestling);
        }

        var unready = 0;
        var takeable = Takeable(map, body.Location, () => unready++);

        var bountied = BotCity.Bountied(map, body.Location, Range);
        var quad = bountied != Point3D.Zero && takeable(bountied) ? BotQuad.At(map, bountied) : null;

        if (quad != null)
        {
            Bountied++;
        }

        quad ??= BotQuad.Direst(
            map,
            body.Location,
            Range,
            at =>
            {
                if (!takeable(at))
                {
                    return false;
                }

                if (BotReeve.Keeps(map, at))
                {
                    Guilded++;

                    return false;
                }

                return true;
            }
        );

        if (unready > 0)
        {
            Unready++;
        }

        if (quad == null)
        {
            Quiet++;

            return null;
        }

        var square = BotQuad.Stand(quad);

        if (square == Point3D.Zero)
        {
            Unfooted++;

            return null;
        }

        if (BotHarrow.Taken(map, square))
        {
            BotHarrow.Declined();

            return null;
        }

        if (BotHarrow.Supply(map, body.Location, body) < BotHarrow.Least)
        {
            Unmanned++;

            return null;
        }

        Offered++;

        Once(body, square, quad.Deaths);

        return new BotHarrow(map, square, quad.Deaths);
    }

    public static Func<Point3D, bool> Takeable(Map map, Point3D from, Action refused = null)
    {
        var ready = BotHarrow.Musterable();

        return at =>
        {
            if (BotQuad.Damning(map, at) && ready < BotHarrow.Grandmasters)
            {
                refused?.Invoke();

                return false;
            }

            if (BotPeril.Overwhelms(map, at, from, BotHarrow.Expected(map, at, BotKept.LoneBot), out _, count: false))
            {
                Overwhelmed++;

                return false;
            }

            return Reachable(map, from, at);
        };
    }

    private static bool Reachable(Map map, Point3D from, Point3D square)
    {

        if (BotRoads.Covers(map, square.X, square.Y))
        {
            if (BotRoads.FromHome(map, square.X, square.Y) < 0)
            {
                Roadless++;

                return false;
            }

            var road = BotRoads.FromHome(map, square.X, square.Y);
            var home = BotRoads.Home;
            var detour = road < 0 ? -1 : road - Math.Max(Math.Abs(square.X - home.X), Math.Abs(square.Y - home.Y));

            if (detour > MostDetour)
            {
                Roundabout++;

                return false;
            }
        }

        if (BotReach.Ask(map, from, square, BotArrival.Within(BotHarrow.Side / 3)) != BotReachVerdict.Sealed)
        {
            return true;
        }

        Sealed++;

        return false;
    }

    private static void Once(Mobile body, Point3D square, int dead)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information(
            "{Name} has been offered the first harrowing on this shard: ({X}, {Y}), where {Dead} have died",
            body.Name,
            square.X,
            square.Y,
            dead
        );
    }

    public static string Describe() =>
        Asked == 0
            ? $"no Baron has ever been offered a harrowing ({NotABaron} answers went to bots that are not Barons)"
            : $"{Asked} times a Baron was asked: {Offered} were offered ground, {Hunting} a patrol against a murderer, {Guilded} dire squares left to the guild that holds them, {Overwhelmed} passed over for a fight {BotPeril.Overwhelm:F1} times the company a levy would raise, {Bountied} ground the city put a bounty on, {Held} were already leading a company, {Unfit} were too hurt, {Quiet} found nowhere reading at or below {BotQuad.Dire:F2}, {Sealed} found the worst of it behind something, {Roadless} on ground the road map says has no road from home, {Roundabout} more than {MostDetour} tiles round by road, {Unfooted} found nowhere in it to stand, {Unmanned} found fewer than {BotHarrow.Least} within call, {Resting} came too soon after a muster that failed, {Unready} passed over damned ground the island cannot yet raise a company for ({BotHarrow.Musterable()} of the {BotHarrow.Grandmasters} needed are fit for it today); {BotHarrow.Describe()}";

    public static void Forget()
    {
        _said = false;
        Asked = 0;
        NotABaron = 0;
        Held = 0;
        Unfit = 0;
        Quiet = 0;
        Sealed = 0;
        Roadless = 0;
        Roundabout = 0;
        Bountied = 0;
        Guilded = 0;
        Overwhelmed = 0;
        Unfooted = 0;
        Offered = 0;
        Resting = 0;
        Unready = 0;

        BotHarrow.Forget();
    }
}
