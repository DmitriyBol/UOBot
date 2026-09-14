using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>What a score was made of. Built for the winner and the runner-up only, and only for the log.</summary>
public readonly struct BotWeigh
{
    public BotWeigh(
        double estimate,
        double nearness,
        double novelty,
        double room,
        double caution,
        double purse,
        double score,
        double stopped = 1.0,
        double revel = 1.0,
        double charter = 1.0,
        double ground = 1.0,
        double calling = 1.0
    )
    {
        Estimate = estimate;
        Nearness = nearness;
        Novelty = novelty;
        Room = room;
        Caution = caution;
        Purse = purse;
        Score = score;
        Stopped = stopped;
        Revel = revel;
        Charter = charter;
        Ground = ground;
        Calling = calling;
    }

    public double Estimate { get; }

    public double Nearness { get; }

    public double Novelty { get; }

    public double Room { get; }

    public double Caution { get; }

    public double Purse { get; }

    public double Stopped { get; }

    public double Revel { get; }

    public double Charter { get; }

    public double Ground { get; }

    public double Calling { get; }

    public double Score { get; }

    public string Describe()
    {
        var bend = Estimate > 0.0 && Calling > 0.0 ? Score / (Estimate * Calling) : 0.0;

        var extra = "";

        if (Stopped < 1.0 || Revel != 1.0 || Ground != 1.0 || Charter != 1.0)
        {
            extra = $" × load {Stopped:F2} × revel {Revel:F2} × ground {Ground:F2} × charter {Charter:F2}";
        }

        var called = Calling == 1.0 ? "" : $"; × {Calling:F2} for {BotCalling.Word(Calling)}";

        return $"{Score:F0}/min = {Estimate:F0} × {bend:F2}, that being the fifth root of "
               + $"near {Nearness:F2} × new {Novelty:F2} × room {Room:F2} × safe {Caution:F2} × purse {Purse:F2}{extra}{called}";
    }

    public override string ToString() => Describe();
}

/// <summary>
/// What a piece of work is worth to this bot, right now. One number, in gold-equivalent per minute, so that
/// every want on the shard competes in the same unit.
///
/// <para>
/// <b>The estimate does the work and the considerations only bend it.</b> That is the opposite way round
/// from the first version, where a goal's attraction was a sum of hand-tuned weights and the actual takings
/// were never measured at all — so nobody could say whether mining was better than hunting, including the
/// bots, and the answer was whatever the weights said it was.
/// </para>
///
/// <para>
/// <b>Multiplied, then rooted.</b> Multiplying normalised factors drives every score towards zero as
/// factors are added, so a sixth consideration would quietly make the whole population less decisive; the
/// geometric mean is the standard compensation for it, and it means adding a consideration changes the
/// ordering without changing the scale. Any factor of zero is a veto and stops the sum early.
/// </para>
/// </summary>
public static class BotAppraisal
{
    public static long Unpaid { get; private set; }

    private static readonly HashSet<string> _unpaidKinds = [];

    public static bool IsUnpaid(string kind) => kind != null && _unpaidKinds.Contains(kind);

    public static long Stopped { get; private set; }

    public static double StoppedShare { get; set; } = 0.02;

    public static Func<string, double> Revelry { get; set; }

    public const int Considerations = 5;

    public static double CrowdBite { get; set; } = 0.8;

    public static double LeastRoom { get; set; } = 0.1;

    public static double LeastPurse { get; set; } = 0.1;

    public static double RepetitionBite { get; set; } = 0.35;

    public static double Suspicion { get; set; } = 0.15;

    public static double Inertia { get; set; } = 1.25;

    public static double Weigh(IBotWilful bot, BotDeed deed, double share, out BotWeigh weigh) =>
        Weigh(bot, deed, share, out weigh, out _);

    public static double Weigh(IBotWilful bot, BotDeed deed, double share, out BotWeigh weigh, out string veto)
    {
        weigh = default;
        veto = null;

        var body = bot?.Self;
        var resolve = bot?.Resolve;

        if (body == null || resolve == null || deed == null)
        {
            veto = "nothing to weigh";

            return 0.0;
        }

        var map = body.Map;

        if (map == null || map == Map.Internal || deed.Map != map)
        {
            veto = $"{deed.Kind} is on another map";

            return 0.0;
        }

        if (deed.Outlay > 0 && BotYield.Wealth(body) < deed.Outlay)
        {
            veto = $"{deed.Kind} costs {deed.Outlay}gp and it has {BotYield.Wealth(body)}gp";

            return 0.0;
        }

        var claim = deed.Unpaid ? deed.Expects : BotCommons.Corrected(deed.Kind, deed.Expects);

        if (deed.Unpaid)
        {
            _unpaidKinds.Add(deed.Kind);
        }

        var estimate = deed.Unpaid ? claim : resolve.Ledger.Expect(deed.Kind, map, deed.Where, claim);

        if (estimate <= 0.0)
        {
            if (deed.Unpaid)
            {
                Unpaid++;

                estimate = 0.01;
            }
            else
            {
                veto = $"{deed.Kind} is expected to pay {estimate:F1}/min here, against a claim of {claim:F1}";

                return 0.0;
            }
        }

        var work = Math.Max(0.1, deed.Minutes);
        var travel = Tiles(body.Location, deed.Where) * (double)BotWalk.StepDelayMs(false) / 60000.0;
        var nearness = work / (work + travel);

        var spins = resolve.Ledger.Spins(deed.Kind, map, deed.Where);
        var novelty = 1.0 / (1.0 + spins * RepetitionBite * (1.0 + resolve.Urges.Boredom));

        var room = Math.Clamp(1.0 - share * CrowdBite, LeastRoom, 1.0);

        var caution = resolve.Ledger.Cautious(deed.Kind, map, deed.Where) ? Suspicion : 1.0;

        var purse = Math.Clamp(
            1.0 - resolve.Urges.Need * (1.0 - Math.Clamp(deed.Coin, 0.0, 1.0)),
            LeastPurse,
            1.0
        );

        var stopped = !deed.Standing && BotLadder.Load(body) > BotLadder.Ceiling(body) ? StoppedShare : 1.0;

        if (stopped < 1.0)
        {
            Stopped++;
        }

        var revel = Revelry?.Invoke(deed.Kind) ?? 1.0;

        var ground = BotLand.Worth(body, map, deed.Where);

        var charter = BotCharter.Worth(body, deed);

        var product = nearness * novelty * room * caution * purse * stopped * revel * ground * charter;

        if (product <= 0.0)
        {
            veto = $"{deed.Kind} weighed out at nothing: near {nearness:F2}, new {novelty:F2}, room {room:F2},"
                + $" safe {caution:F2}, purse {purse:F2}, standing {stopped:F2}, revel {revel:F2}, ground {ground:F2},"
                + $" charter {charter:F2}";

            return 0.0;
        }

        var calling = BotCalling.Worth(body, deed);

        var score = estimate * Math.Pow(product, 1.0 / Considerations) * calling;

        weigh = new BotWeigh(estimate, nearness, novelty, room, caution, purse, score, stopped, revel, charter, ground, calling);

        return score;
    }

    internal static double Travel(Point3D from, Point3D to) =>
        Tiles(from, to) * (double)BotWalk.StepDelayMs(false) / 60000.0;

    private static int Tiles(Point3D from, Point3D to)
    {
        var dx = Math.Abs(from.X - to.X);
        var dy = Math.Abs(from.Y - to.Y);

        return dx > dy ? dx : dy;
    }
}
