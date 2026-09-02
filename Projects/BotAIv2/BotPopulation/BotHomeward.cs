using System;

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

    /// <summary>
    /// How far from the camp a bot has to be before going back is worth offering at all.
    ///
    /// Inside this it is already among the shops, the seams and the counters, and walking to the middle of
    /// them would be an errand that changes nothing.
    /// </summary>
    public static int Away { get; set; } = 120;

    /// <summary>Near enough to be home. The camp is a place, not a tile — see BotArrival.Within.</summary>
    public static int Arrived { get; set; } = 20;

    /// <summary>
    /// What this is worth a minute. Deliberately tiny.
    ///
    /// <para>
    /// It is not nought, and that distinction is the whole of the design. At nought the auction would score
    /// it at nought and a bot with nothing to do would go on having nothing to do, which is the state this
    /// exists to end. At a few coins it loses to every real offer on the shard and beats only silence.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// How long the walk will take, honestly.
    ///
    /// <para>
    /// <b>This number is what keeps the appraisal from vetoing the one errand written for distant bots.</b>
    /// The auction weighs an offer by <c>work / (work + travel)</c> — the share of the time that is spent
    /// working rather than walking — so a deed that claims to take a moment and is four hundred tiles away
    /// scores near nought, every time, by construction. That is correct for a dig and it would be fatal
    /// here: the further a bot has strayed the more it needs this and the less it would be offered it.
    /// Declaring the walk as the work is not a way round the rule, it is the truth: the walk IS the errand,
    /// and told that, the arithmetic gives it a fair hearing without a single exception being carved.
    /// </para>
    /// </summary>
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
    /// <summary>Bots asked, and what became of the question. Each case counted apart.</summary>
    public static long Asked { get; private set; }

    public static long Near { get; private set; }

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

        if (Utility.InRange(body.Location, BotPopulation.Where, BotHomeward.Away))
        {
            Near++;

            return null;
        }

        Sent++;

        return new BotHomeward(home, BotPopulation.Where);
    }

    public static void Forget()
    {
        Asked = 0;
        Near = 0;
        Sent = 0;
    }

    /// <summary>One line, every case named, no branch called other.</summary>
    public static string Describe() =>
        $"{Asked} asked whether to go home: {Near} were already within {BotHomeward.Away} tiles of it, {Sent} were sent back";
}
