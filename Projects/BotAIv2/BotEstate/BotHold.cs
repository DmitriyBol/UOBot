using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Mobiles;
using Server.Multis;

namespace Server.BotAI.V2;

/// <summary>
/// Standing in a square your guild has laid claim to, for as long as the claim takes.
///
/// <para>
/// <b>The whole of "the guild gathers in the quadrant".</b> There is nothing to do here and that is the
/// point: the claim is won by being there, in numbers, where anybody who minds can find you. See
/// <c>BotClaim.Gather</c> for why it is the peak over the window rather than a headcount at the end.
/// </para>
///
/// <para>
/// <b>No claim on the errand, unlike every other guild office.</b> The steward, the fitter, the hirer and
/// the remover are each one bot's work and <c>BotOffice</c> keeps them to one; this one wants as many of the
/// guild as will come, so nothing stops a second member taking it. That is the only difference between this
/// and the four of them, and it is the reason it exists as its own file.
/// </para>
///
/// <para>
/// Nothing is earned. See <c>BotBaron</c> for the twelve minutes a bot once stood still because unpaid work
/// was being marked down for being unpaid.
/// </para>
/// </summary>
public sealed class BotHold : BotDeed
{
    public const string Trade = "stake";

    public static double Prior { get; set; } = 200.0;

    public static double WorkMinutes => Math.Max(1.0, BotClaim.MusterMs / 60000.0);

    public static int Reach { get; set; } = 8;

    public static long Stood { get; private set; }

    public static long Ended { get; private set; }

    private readonly Guild _guild;

    private readonly Map _map;

    private readonly Point3D _middle;

    public BotHold(Guild guild, Map map, Point3D middle)
    {
        _guild = guild;
        _map = map;
        _middle = middle;
    }

    public override string Kind => Trade;

    internal Point3D Middle => _middle;

    public override Map Map => _map;

    public override Point3D Where => _middle;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override bool Unpaid => true;

    public override string Stage => $"holding the square at {_middle.X},{_middle.Y} for {_guild?.Name}";

    public override bool Summons => true;

    public override bool Steadfast => true;

    public override bool Still => _standing;

    private bool _standing;

    public override bool Bend(IBotWilful bot) => false;

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _guild == null)
        {
            return BotDoing.Failed("no body");
        }

        var bid = BotClaim.Making(_guild);

        if (bid == null)
        {
            Ended++;

            return BotDoing.Done($"the claim on {_middle.X},{_middle.Y} is decided");
        }

        if (!body.InRange(_middle, Reach) && BotQuad.Key(_map, body.Location) != BotQuad.Key(_map, _middle))
        {
            _standing = false;

            return BotDoing.Walk(_map, _middle, BotArrival.Within(Reach), $"to the square at {_middle.X},{_middle.Y}");
        }

        _standing = true;
        _arrived = true;
        Stood++;

        return BotDoing.Work($"standing for {_guild.Name} on {_middle.X},{_middle.Y}");
    }

    private bool _arrived;

    public override void Drop(IBotWilful bot)
    {
        if (!_arrived && bot?.Self is Mobile body && BotClaim.Making(_guild) != null)
        {
            BotHolder.Missed(body, _middle);
        }

        base.Drop(bot);
    }

    public static string Describe() =>
        Stood + Ended == 0 ? "nobody has stood for a claim" : $"{Stood} beats stood on claimed ground, {Ended} musters that outlived their claim";

    public static void Forget()
    {
        Stood = 0;
        Ended = 0;
    }
}

/// <summary>
/// Decides which square a guild wants, declares the claim, and sends its members to stand on it.
///
/// <para>
/// <b>The declaring and the standing are one proposer because the first member to be asked does both.</b>
/// <c>BotClaim.Open</c> refuses a second claim from the same guild, so whichever member is offered work
/// first opens the claim and the rest are handed the standing. Splitting them would need an officer, a claim
/// on the officer, and a way for the officer to tell the others — which is three mechanisms to arrange what
/// one dictionary entry already arranges.
/// </para>
///
/// <para>
/// <b>What it wants, in order.</b> Open ground first while the free claims last, because free ground is the
/// cheapest thing a guild can own. Then somebody else's, if the purse will stand it — and it takes the
/// square for itself rather than merely striking a name off it whenever it can afford to, because a square
/// held is worth more than a square denied. Only then open ground at the full price.
/// </para>
/// </summary>
public sealed class BotHolder : IBotProposer
{
    public static int Look { get; set; } = 180;

    public static double Worst { get; set; } = BotQuad.Unsafe;

    public static long Asked { get; private set; }

    public static long Sent { get; private set; }

    public static long Opened { get; private set; }

    public static long Landless { get; private set; }

    public static int Spare { get; set; } = 1;

    public static int MissedMs { get; set; } = 600000;

    public static long Shy { get; private set; }

    private static readonly Dictionary<Serial, (Point3D Middle, long Tick)> _missed = [];

    public static void Missed(Mobile body, Point3D middle)
    {
        if (body != null)
        {
            _missed[body.Serial] = (middle, Core.TickCount);
        }
    }

    public static long Enough { get; private set; }

    private static int Holding(Guild ours, Point3D middle)
    {
        var members = ours?.Members;

        if (members == null)
        {
            return 0;
        }

        var holding = 0;

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i] is BotMobile { Deleted: false, Resolve.Deed: BotHold hold } && hold.Middle == middle)
            {
                holding++;
            }
        }

        return holding;
    }

    public static long Nothing { get; private set; }

    public static long Poor { get; private set; }

    public static long Walled { get; private set; }

    public static long Unreachable { get; private set; }

    public static long Rested { get; private set; }

    public static long Refused { get; private set; }

    public string Name => "staker";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        if (!BotEstate.Running || !BotClaim.Running)
        {
            return null;
        }

        if (bot?.Self is not BotMobile { Deleted: false, Alive: true } body || body.Map == null ||
            body.Map == Map.Internal)
        {
            return null;
        }

        if (body.Guild is not Guild ours)
        {
            return null;
        }

        if (BotUnderworld.Band(ours))
        {
            return null;
        }

        Asked++;

        var bid = BotClaim.Making(ours);

        if (bid != null)
        {
            if (BotReach.Ask(body.Map, body.Location, bid.Middle, BotArrival.Within(BotHold.Reach)) == BotReachVerdict.Sealed)
            {
                Walled++;

                return null;
            }

            if (BotRefused.Refusing(body.Map, bid.Middle))
            {
                Refused++;

                return null;
            }

            if (_missed.TryGetValue(body.Serial, out var miss) && miss.Middle == bid.Middle
                && Core.TickCount - miss.Tick < MissedMs)
            {
                Shy++;

                return null;
            }

            if (Holding(ours, bid.Middle) >= BotClaim.Gather + Spare)
            {
                Enough++;

                return null;
            }

            Sent++;

            return new BotHold(ours, bid.Map, bid.Middle);
        }

        var hall = BotEstate.Hall(ours);

        if (hall is not { Deleted: false } || hall.Map != body.Map)
        {
            Landless++;

            return null;
        }

        var fund = BotEstate.Fund(ours);
        var mine = BotClaim.Holds(ours.Name);

        if (!Want(ours, hall, fund, mine, out var middle, out var want))
        {
            return null;
        }

        var opened = BotClaim.Open(ours, hall.Map, middle, want);

        if (opened == null)
        {
            return null;
        }

        Opened++;

        return new BotHold(ours, opened.Map, opened.Middle);
    }

    private static bool Want(Guild ours, BaseHouse hall, int fund, int mine, out Point3D middle, out BotClaim.Kind want)
    {
        middle = Point3D.Zero;
        want = BotClaim.Kind.Settle;

        var map = hall.Map;
        var best = int.MaxValue;
        var poor = false;

        foreach (var quad in BotQuad.All)
        {
            if (quad == null || quad.Map != map || quad.Safety < Worst)
            {
                continue;
            }

            var at = new Point3D(
                quad.X * BotQuad.Side + BotQuad.Side / 2,
                quad.Y * BotQuad.Side + BotQuad.Side / 2,
                0
            );

            var gap = Math.Max(Math.Abs(at.X - hall.X), Math.Abs(at.Y - hall.Y));

            if (gap > Look || gap >= best)
            {
                continue;
            }

            var owner = BotClaim.Owner(map, at);

            if (owner == ours.Name)
            {
                continue;
            }

            if (BotClaim.Resting(ours.Name, BotQuad.Key(map, at)))
            {
                Rested++;

                continue;
            }

            var kind = owner == null
                ? BotClaim.Kind.Settle
                : fund >= BotClaim.Ousting || !BotClaim.Bought(map, at)
                    ? BotClaim.Kind.Oust
                    : BotClaim.Kind.Strip;

            if (fund < BotClaim.Cost(ours.Name, BotQuad.Key(map, at), kind))
            {
                poor = true;

                continue;
            }

            var settled = BotClaim.Middle(map, at);

            if (settled == Point3D.Zero)
            {
                continue;
            }

            if (BotReach.Ask(map, hall.Location, settled, BotArrival.Within(BotHold.Reach), tally: false) == BotReachVerdict.Sealed)
            {
                Unreachable++;

                continue;
            }

            best = gap;
            middle = settled;
            want = kind;
        }

        if (middle == Point3D.Zero)
        {
            if (poor)
            {
                Poor++;
            }
            else
            {
                Nothing++;
            }

            return false;
        }

        return true;
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been looked at for a claim"
            : $"the staker looked {Asked} times: {Opened} claims opened, {Sent} sent to stand on one, "
            + $"{Enough} not sent because the square already had its muster of {BotClaim.Gather} and {Spare} to spare, {Landless} guilds had no hall to want ground near, {Nothing} found no square worth claiming within "
            + $"{Look} tiles, {Poor} could not pay for the one they wanted, {Unreachable} passed over a square the hall cannot reach; {Walled} members were not sent because they cannot reach it from where they stand, {Refused} because a walk to it lately gave up and {Shy} because their own walk to it lately ended somewhere else; {Rested} squares passed over where the guild lately failed to muster; {BotHold.Describe()}";

    public static void Forget()
    {
        Asked = 0;
        Sent = 0;
        Opened = 0;
        Landless = 0;
        Nothing = 0;
        Poor = 0;
        Walled = 0;
        Unreachable = 0;
        Rested = 0;
        Refused = 0;
        Shy = 0;
        _missed.Clear();
    }
}
