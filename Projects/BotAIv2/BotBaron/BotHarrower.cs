using System;
﻿using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers the Baron the ground that has killed the most people, and five bots to take there.
///
/// <para>
/// <b>It reads a different column of the same table the captain reads, and the difference is the whole
/// point.</b> <see cref="BotPatrol"/> asks <c>BotPeril.Worst</c> — a decaying frequency, which answers
/// "where is blood being spilt lately" and is exactly right for deciding where a company should be standing
/// before anything happens. This asks <c>BotPeril.Deadliest</c>, a count that does not fade, which answers
/// "where has it already gone wrong". A square can be top of one list and absent from the other, and both
/// lists are correct.
/// </para>
///
/// <para>
/// <b>Every refusal is named, and there is no bucket called "other".</b> A harrowing needs a Baron, a
/// Baron who is not already leading one, one who is fit to walk into it, ground with the dead on it and a
/// way through to that ground. When none is happening, which of those five was missing is the only
/// question worth being able to answer — an unnamed nought is the failure this shard has paid for more
/// than any other.
/// </para>
///
/// <para>
/// <b>It does not count volunteers, and it used to.</b> The offer was refused outright unless five free
/// bots were standing within forty tiles at the instant it was weighed, which is arithmetic about one
/// second of a working population and produced a Baron who never left town. Raising a company is the
/// errand's own business now — he goes to the square and calls for five minutes; see
/// <see cref="BotHarrow.MusterMs"/>. A muster that comes to nothing is counted there, by name, where it
/// actually happened.
/// </para>
/// </summary>
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

    public static long Unfooted { get; private set; }

    public static long Unready { get; private set; }

    public static long Resting { get; private set; }

    public static long Offered { get; private set; }

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

        var unready = 0;
        var quad = BotQuad.Direst(map, body.Location, Range, Takeable(map, body.Location, () => unready++));

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

            return Reachable(map, from, at);
        };
    }

    private static bool Reachable(Map map, Point3D from, Point3D square)
    {
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
            : $"{Asked} times a Baron was asked: {Offered} were offered ground, {Held} were already leading a company, {Unfit} were too hurt, {Quiet} found nowhere reading at or below {BotQuad.Dire:F2}, {Sealed} found the worst of it behind something, {Unfooted} found nowhere in it to stand, {Resting} came too soon after a muster that failed, {Unready} passed over damned ground the island cannot yet raise a company for ({BotHarrow.Musterable()} of the {BotHarrow.Grandmasters} needed are fit for it today); {BotHarrow.Describe()}";

    public static void Forget()
    {
        _said = false;
        Asked = 0;
        NotABaron = 0;
        Held = 0;
        Unfit = 0;
        Quiet = 0;
        Sealed = 0;
        Unfooted = 0;
        Offered = 0;
        Resting = 0;
        Unready = 0;

        BotHarrow.Forget();
    }
}
