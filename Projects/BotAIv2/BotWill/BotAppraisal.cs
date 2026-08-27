using System;

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
        double score
    )
    {
        Estimate = estimate;
        Nearness = nearness;
        Novelty = novelty;
        Room = room;
        Caution = caution;
        Purse = purse;
        Score = score;
    }

    /// <summary>Gold-equivalent per minute expected, prior and experience together.</summary>
    public double Estimate { get; }

    public double Nearness { get; }

    public double Novelty { get; }

    public double Room { get; }

    public double Caution { get; }

    public double Purse { get; }

    public double Score { get; }

    /// <summary>
    /// What the score was made of, written so it adds up.
    ///
    /// <para>
    /// <b>It read as a product and is not one, and that cost an hour of chasing a defect that was not
    /// there.</b> "8/min = 14 × 0.97 × 0.49 × 0.93 × 0.15 × 1.00" multiplies out to 0.9, not to 8, so the
    /// line looked like a decision whose stated reasons had nothing to do with its stated answer — which is
    /// the exact shape of the worst defects on this shard. The arithmetic was right all along: the factors
    /// are combined as a geometric mean, not a product (see <see cref="BotAppraisal"/> on why), and only the
    /// sentence was wrong. A log that cannot be checked against itself is worse than a shorter one.
    /// </para>
    /// </summary>
    public string Describe()
    {
        var bend = Estimate > 0.0 ? Score / Estimate : 0.0;

        return $"{Score:F0}/min = {Estimate:F0} × {bend:F2}, that being the fifth root of "
               + $"near {Nearness:F2} × new {Novelty:F2} × room {Room:F2} × safe {Caution:F2} × purse {Purse:F2}";
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
    /// <summary>How many factors bend the estimate. The root taken of their product.</summary>
    public const int Considerations = 5;

    /// <summary>
    /// How hard a crowd puts a bot off. At four fifths, work that four bots in five are already doing is
    /// worth about a fifth of what it would be worth alone.
    ///
    /// <para>
    /// This is the whole answer to what the first version's population became: 116 traders to 14 fighters.
    /// Nothing was wrong with any individual decision — trade paid, so everybody traded. A want whose value
    /// does not fall as it gets crowded is a want that everybody ends up having, and utility scoring on its
    /// own starves whole roles. It is arithmetic on a fact every bot can see, in the same spirit as the
    /// squads: nobody has to be told anything.
    /// </para>
    /// </summary>
    public static double CrowdBite { get; set; } = 0.8;

    /// <summary>The least a crowded piece of work may be discounted to. Never zero: somebody has to be third.</summary>
    public static double LeastRoom { get; set; } = 0.1;

    /// <summary>
    /// The least an empty purse may discount work that does not pay in coin.
    ///
    /// Never zero, for exactly the reason the crowding floor is not: a bot with no money still has to be
    /// able to walk somewhere, look for a fight, and pick things up off the ground. See the note where this
    /// is applied for the ten minutes it cost to find out.
    /// </summary>
    public static double LeastPurse { get; set; } = 0.1;

    /// <summary>How hard doing the same thing in the same place lately puts a bot off.</summary>
    public static double RepetitionBite { get; set; } = 0.35;

    /// <summary>What is left of a piece of work in a place where it lately went badly.</summary>
    public static double Suspicion { get; set; } = 0.15;

    /// <summary>
    /// What the work in hand is worth beyond its own score.
    ///
    /// <para>
    /// The single most important number for behaviour that looks deliberate, and the direct answer to the
    /// first version's worst habit: it re-chose from scratch every tick, so a bot two steps into a journey to
    /// town would notice a skeleton and go back to hunting, then notice the town again. <b>Any intention
    /// longer than a second was impossible in principle</b> — not unlikely, impossible. A bonus to whatever
    /// is already being done is the standard fix in utility-based systems, and it is cheap: at a quarter, a
    /// new want has to be clearly better rather than marginally better.
    /// </para>
    /// </summary>
    public static double Inertia { get; set; } = 1.25;

    /// <summary>
    /// The score, and what it was made of.
    ///
    /// <para>
    /// Zero means "not this": another map, cannot be afforded, nothing expected, or no money in it for a bot
    /// that needs money. A zero is never a small number here — it is a refusal, and it stops the arithmetic.
    /// </para>
    /// </summary>
    public static double Weigh(IBotWilful bot, BotDeed deed, double share, out BotWeigh weigh)
    {
        weigh = default;

        var body = bot?.Self;
        var resolve = bot?.Resolve;

        if (body == null || resolve == null || deed == null)
        {
            return 0.0;
        }

        var map = body.Map;

        // Another map is another problem. Nothing here can plan across one, and pretending otherwise is how
        // the first version produced errands to places that were never reached.
        if (map == null || map == Map.Internal || deed.Map != map)
        {
            return 0.0;
        }

        // Cannot pay to start. Checked before anything expensive, which is also why it is first.
        if (deed.Outlay > 0 && BotYield.Wealth(body) < deed.Outlay)
        {
            return 0.0;
        }

        // <b>The claim is corrected before the place is even considered.</b> deed.Expects is a constant
        // somebody typed — forty-five a minute for a sweep, eight for a prowl — and until now nothing on the
        // shard had ever checked one against what the work actually paid. BotCommons.Corrected is the shard
        // measuring its own assertions; the ledger then does what it always did, which is to say what this
        // bot knows about this ground. Two different corrections, in the right order: what the trade is
        // worth, then what the place is worth.
        var claim = BotCommons.Corrected(deed.Kind, deed.Expects);

        var estimate = resolve.Ledger.Expect(deed.Kind, map, deed.Where, claim);

        if (estimate <= 0.0)
        {
            return 0.0;
        }

        // How much of the time this would cost is spent working rather than walking. The reason distance is
        // not a penalty of its own: half an hour of digging is worth a five-minute walk and five minutes of
        // digging is not, and only the ratio says so.
        var work = Math.Max(0.1, deed.Minutes);
        var travel = Tiles(body.Location, deed.Where) * (double)BotWalk.StepDelayMs(false) / 60000.0;
        var nearness = work / (work + travel);

        // Boredom does not compete with work here — it makes repetition wear out faster. A bot with nothing
        // else on will still go back to the same field; a bored one needs it to be worth more.
        var spins = resolve.Ledger.Spins(deed.Kind, map, deed.Where);
        var novelty = 1.0 / (1.0 + spins * RepetitionBite * (1.0 + resolve.Urges.Boredom));

        var room = Math.Clamp(1.0 - share * CrowdBite, LeastRoom, 1.0);

        var caution = resolve.Ledger.Cautious(deed.Kind, map, deed.Where) ? Suspicion : 1.0;

        // A purse of skill does not buy a pickaxe. This is the only place being short of money changes what
        // a bot picks, and when it is not short the factor is exactly one for everything.
        //
        // <b>Floored, and leaving it unfloored stopped a bot doing anything at all.</b> Need is
        // <c>1 - wealth/outlay</c> and reaches exactly one the moment a purse is empty, so every piece of
        // work whose <c>Coin</c> is nought came out at exactly nought — and a nought here is a refusal, as
        // the note at the top of this method says in as many words. Prowling has <c>Coin</c> of nought.
        // Prowling is also this shard's designated answer to having nothing to do: "scored at almost nothing
        // and the right answer anyway — it is a walk to somewhere else". So the bot that most needed to go
        // and find work was the only one forbidden from looking for it. Cedric spent his last coins on
        // scrolls at 22:30 on 25.08.2026 and stood in a field for ten minutes with an empty holding and a
        // contentment of nought, while the summary said seven times that nothing was worth doing.
        //
        // A tenth, exactly as <see cref="LeastRoom"/> is, and for the same reason: being short of money
        // should make paying work far more attractive, not make everything else impossible. The two floors
        // are now the same shape, which is how the crowding factor has always avoided this.
        var purse = Math.Clamp(
            1.0 - resolve.Urges.Need * (1.0 - Math.Clamp(deed.Coin, 0.0, 1.0)),
            LeastPurse,
            1.0
        );

        var product = nearness * novelty * room * caution * purse;

        if (product <= 0.0)
        {
            return 0.0;
        }

        var score = estimate * Math.Pow(product, 1.0 / Considerations);

        weigh = new BotWeigh(estimate, nearness, novelty, room, caution, purse, score);

        return score;
    }

    /// <summary>
    /// Distance the way the engine measures adjacency — the larger of the two axes — so it agrees with
    /// <see cref="BotArrival"/> and with the planner about what "one tile away" means.
    /// </summary>
    private static int Tiles(Point3D from, Point3D to)
    {
        var dx = Math.Abs(from.X - to.X);
        var dy = Math.Abs(from.Y - to.Y);

        return dx > dy ? dx : dy;
    }
}
