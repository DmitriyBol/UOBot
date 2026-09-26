using System;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Walking back to where the population lives, when there is nothing else to do and the bot is a long way
/// from it.
///
/// <para>
/// <b>Thirty-four proposers and not one of them ever said "come back".</b> Everything this shard offers is
/// offered relative to where a bot already stands: a seam near it, a shopkeeper near it, something worth
/// fighting near it. So a bot that wanders — after quarry, after a shop, behind a company — ends up
/// somewhere with none of those, is offered nothing, and stands there. Nothing in the world is wrong with
/// it and nothing will ever fetch it.
/// </para>
///
/// <para>
/// <b>Measured 02.09.2026.</b> Six casters — Perri, Quill, Bryn, Edda, Faron and Doran, every one of them
/// newly raised and holding between nought and thirty gold — stood together at (1397, 1822), three hundred
/// and fifty tiles south of the camp at (1440, 1470), holding no work at all. Four of them had been there
/// long enough for the shard's own stall detector to complain, one for sixteen minutes. They were not
/// stuck, not hurt and not out of stamina: they were simply somewhere that offers nothing to a bot with no
/// money, and there was no errand in the world whose answer was "then go home".
/// </para>
///
/// <para>
/// <b>It does not need to beat anything and must not.</b> The worth below is a few coins a minute, so any
/// real work — a dig, a sale, a fight, a lesson — wins the auction against it every time. This is what a
/// bot does when the answer to "what is worth doing here" is nothing, and the cure for that is to be
/// somewhere else.
/// </para>
/// </summary>
public sealed class BotHomeward : BotDeed
{
    public const string Trade = "homeward";

    public static int Away { get; set; } = 120;

    public static int Arrived { get; set; } = 20;

    public static double BarrenFor { get; set; } = 0.5;

    public static double Worth { get; set; } = 5.0;

    private readonly Map _map;

    private readonly Point3D _home;

    public BotHomeward(Map map, Point3D home)
    {
        _map = map;
        _home = home;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _home;

    public override double Expects => Worth;

    public override bool Unpaid => true;

    public override double Minutes =>
        Math.Max(0.2, Tiles() * BotWalk.StepDelayMs(BotMobile.Runs) / 60000.0);

    private double Tiles() =>
        _map == null ? 0.0 : Math.Sqrt(Math.Pow(_home.X - _at.X, 2) + Math.Pow(_home.Y - _at.Y, 2));

    private Point3D _at;

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || body.Map != _map)
        {
            return BotDoing.Failed("nowhere to go home to");
        }

        _at = body.Location;

        if (Utility.InRange(body.Location, _home, Arrived))
        {
            return BotDoing.Done("home");
        }

        return BotDoing.Walk(_map, _home, BotArrival.Within(Arrived), "going home");
    }

    public override string Stage => "going home";
}

/// <summary>
/// Offers the walk home, and only to a bot that is a long way from it.
///
/// <para>
/// On the <c>Free</c> rung, like every other piece of ordinary work: a bot that is bleeding, being hit or
/// marching with a company has better things to do than travel, and all three of those are decided below
/// this rung and never reach here.
/// </para>
/// </summary>
public sealed class BotHomer : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHomer));

    public static long Asked { get; private set; }

    public static long Near { get; private set; }

    public static long Busy { get; private set; }

    public static long Sent { get; private set; }

    public string Name => "Homeward";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var home = BotPopulation.Home;

        if (body == null || home == null || home == Map.Internal || body.Map != home || !body.Alive)
        {
            return null;
        }

        Asked++;

        var hearth = body is BotMobile own ? BotSeat.Home(own) : BotPopulation.Where;

        if (Utility.InRange(body.Location, hearth, BotHomeward.Away))
        {
            Near++;

            return null;
        }

        var barren = bot.Resolve?.Urges?.BarrenMinutes(Core.TickCount) >= BotHomeward.BarrenFor;

        if (!barren && !(body is BotMobile loose && BotDungeon.Under(body.Location) && !BotDelveParty.Delving(body)
                         && BotThreat.Decide(loose, BotMobile.NoticeRange) == BotStand.Nothing))
        {
            Busy++;

            return null;
        }

        if (body is BotMobile below && BotDungeon.Under(body.Location) && !BotDelveParty.Delving(body))
        {
            if (BotPopulation.Carry(below))
            {
                below.Journey?.Finish();
                Surfaced++;

                logger.Information(
                    "{Name} was underground with no party to bring it up and no work for {Minutes:F1} minutes, and has been put back on the island at {Where}",
                    body.Name,
                    BotHomeward.BarrenFor,
                    body.Location
                );
            }

            return null;
        }

        Sent++;

        return new BotHomeward(home, hearth);
    }

    public static long Surfaced { get; private set; }

    public static void Forget()
    {
        Asked = 0;
        Near = 0;
        Busy = 0;
        Sent = 0;
        Surfaced = 0;
    }

    public static string Describe() =>
        $"{Asked} asked whether to go home: {Near} were already within {BotHomeward.Away} tiles of it, "
        + $"{Busy} were far but had not been out of work for {BotHomeward.BarrenFor:F1} minutes yet, {Sent} were sent back"
        + $" and {Surfaced} were underground with no party and were brought up instead";
}
