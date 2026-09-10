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
        double score,
        double stopped = 1.0,
        double revel = 1.0,
        double ground = 1.0
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
        Ground = ground;
    }

    /// <summary>Gold-equivalent per minute expected, prior and experience together.</summary>
    public double Estimate { get; }

    public double Nearness { get; }

    public double Novelty { get; }

    public double Room { get; }

    public double Caution { get; }

    public double Purse { get; }

    /// <summary>The carrying-ceiling factor. One unless the bot is over its load and the work needs a step.</summary>
    public double Stopped { get; }

    /// <summary>Whatever the watcher declared worth doing this quarter of an hour. One nearly always.</summary>
    public double Revel { get; }

    /// <summary>Whose land the work stands on. One on ground no guild has claimed, which is most of it.</summary>
    public double Ground { get; }

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

        // <b>And then three more factors were added over a fortnight and none of them was ever printed.</b>
        // The note above is about a line that could not be checked against itself; the same thing happened
        // again quietly, because the carrying ceiling, the watcher's revels and now the guild lands all
        // multiply into the product while the sentence went on naming five things. A bot refusing work at a
        // fiftieth for a full pack read as a bot refusing work for no stated reason at all.
        //
        // Printed only when they bite, which is the compromise: these lines are written thousands of times
        // an hour and all three are one nearly always, so the common sentence is exactly the one Patrick
        // already reads — and the moment any of them is doing something, it says so.
        var extra = "";

        if (Stopped < 1.0 || Revel != 1.0 || Ground != 1.0)
        {
            extra = $" × load {Stopped:F2} × revel {Revel:F2} × ground {Ground:F2}";
        }

        return $"{Score:F0}/min = {Estimate:F0} × {bend:F2}, that being the fifth root of "
               + $"near {Nearness:F2} × new {Novelty:F2} × room {Room:F2} × safe {Caution:F2} × purse {Purse:F2}{extra}";
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
    /// <summary>Times an unpaid deed was let past the earnings veto.</summary>
    public static long Unpaid { get; private set; }

    /// <summary>
    /// Offers marked down because the bot is past its carrying ceiling and the work needs a step.
    ///
    /// Its own bucket: this reading high is not a fault in the work, it is a population that is loading
    /// itself to a standstill, and the number to look at beside it is how often BotUnload had to sell from
    /// where the bot stood.
    ///
    /// <b>It counts a mark-down and never counted a decision, and reading it as one wasted a night.</b> The
    /// number climbed exactly as designed all through 09.09.2026 while the bots it counted went on taking
    /// the very work it was marking, because a fiftieth under a fifth root is 0.46, and 0.46 of a wage a bot
    /// cannot earn still beats all of a wage it can. What the ceiling decides now is an order, and the
    /// order is counted where orders are made: <c>BotWill.Grounded</c> and <c>BotWill.Dislodged</c> are the
    /// pair that say whether anything came of this one.
    /// </summary>
    public static long Stopped { get; private set; }

    /// <summary>
    /// What work that needs a step is worth to a bot that cannot take one. A fiftieth.
    ///
    /// <para>
    /// <b>It does not do what this used to claim, and no number here could.</b> The claim was "enough that
    /// anything doable on the spot wins, never so little that it becomes a refusal", and a factor cannot
    /// meet both demands at once, because <see cref="Considerations"/> takes the fifth root of the product:
    /// a fiftieth reaches the score as 0.46, which loses to nothing, and the value that would win outright
    /// is the value that vetoes. Lysa the Woodsman took a chop at 229/min over her own unloading on
    /// 09.09.2026 with this factor doing exactly what it was set to do.
    /// </para>
    ///
    /// <para>
    /// The winning is done by rank in <c>BotWill.Auction</c> now. What is left here is the half a factor is
    /// good at and the half that is true: work a bot cannot start is worth less to it, so it is priced
    /// lower and the log line says by how much. It still ranks two walking errands against each other
    /// honestly, and it still holds an unloading in hand against a passing better price.
    /// </para>
    /// </summary>
    public static double StoppedShare { get; set; } = 0.02;

    /// <summary>
    /// A multiplier on one kind of work, set by something outside this assembly, or null.
    ///
    /// <para>
    /// <b>The one seam through which the watcher may move the population without commanding it.</b> Argus
    /// lives in the minds assembly, which depends on this one and must never be depended on in return — so
    /// it cannot be called from here. It fills this in instead, and what arrives is a number in exactly the
    /// same shape as crowding and caution: a factor, competing honestly with them.
    /// </para>
    ///
    /// <para>
    /// Deliberately not a command. A revel that triples the worth of mining does not order anybody to mine;
    /// it makes mining beat what else is on offer for those it beats, and a bot with something better in
    /// front of it goes on doing that. A revel nobody enters is a fact about the price, and the watcher is
    /// told so when it pays nobody.
    /// </para>
    /// </summary>
    public static Func<string, double> Revelry { get; set; }

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
    public static double Weigh(IBotWilful bot, BotDeed deed, double share, out BotWeigh weigh) =>
        Weigh(bot, deed, share, out weigh, out _);

    /// <summary>
    /// The same score, and which of the five refusals it was when the answer is nought.
    ///
    /// <para>
    /// <b>Five different facts left this method as the same zero.</b> Another map, cannot afford it, nothing
    /// expected there, and a product flattened to nought are separate faults with separate repairs, and on
    /// 02.09.2026 five bots stood at the Britain shops with 350–540gp apiece while the record said only "1
    /// offered work, none of it scored above nothing". Naming the refusal is the difference between knowing
    /// that and knowing what to fix.
    /// </para>
    /// </summary>
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

        // Another map is another problem. Nothing here can plan across one, and pretending otherwise is how
        // the first version produced errands to places that were never reached.
        if (map == null || map == Map.Internal || deed.Map != map)
        {
            veto = $"{deed.Kind} is on another map";

            return 0.0;
        }

        // Cannot pay to start. Checked before anything expensive, which is also why it is first.
        if (deed.Outlay > 0 && BotYield.Wealth(body) < deed.Outlay)
        {
            veto = $"{deed.Kind} costs {deed.Outlay}gp and it has {BotYield.Wealth(body)}gp";

            return 0.0;
        }

        // <b>The claim is corrected before the place is even considered.</b> deed.Expects is a constant
        // somebody typed — forty-five a minute for a sweep, eight for a prowl — and until now nothing on the
        // shard had ever checked one against what the work actually paid. BotCommons.Corrected is the shard
        // measuring its own assertions; the ledger then does what it always did, which is to say what this
        // bot knows about this ground. Two different corrections, in the right order: what the trade is
        // worth, then what the place is worth.
        var claim = BotCommons.Corrected(deed.Kind, deed.Expects);

        // <b>And the ledger is not asked about work that is not about money either.</b> The ledger's answer
        // is what this bot has earned on this ground, which is exactly the wrong question to put to an errand
        // whose whole purpose is ground nobody has stood on: the frontier has paid nobody anything by
        // definition, so the answer is nought, every time, for ever.
        //
        // Measured on 08.09.2026, and it is why the Baron would not scout. He took it once — "35/min = 40 ×
        // 0.88" — and from the next review on it read "over scout ... at 0/min" while he walked round the
        // town at six. Both of his offices are unpaid by construction, so the one with the higher claim has
        // to be allowed to say so; judging them on takings ranks them both at nothing and then picks between
        // two nothings on tie-breaks.
        var estimate = deed.Unpaid ? claim : resolve.Ledger.Expect(deed.Kind, map, deed.Where, claim);

        if (estimate <= 0.0)
        {
            // Work that says it is not about money is not judged on money. It still has to be the best thing
            // on offer to be taken - it comes in at the smallest number that is not a refusal, so anything
            // that pays at all beats it - but it can no longer be thrown away for being what it is.
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

        // <b>A factor, not a veto, and the veto version cost three crafters twenty-seven minutes apiece.</b>
        // Past its carrying ceiling the engine refuses a bot every move, so work that begins with a walk is
        // work it cannot start — but refusing such work outright leaves a bot with nothing at all whenever
        // the one errand it *can* do is unavailable for reasons of its own. Measured within the hour of
        // writing it: Ulric, Wulfric and Roderic stood on the Free rung for 1677, 1640 and 1632 seconds,
        // every offer refused with "mine needs a step, and this bot is carrying 242 of 222", and not one of
        // them ever reached the unloading that would have freed it.
        //
        // <b>And a factor with a floor is not a preference either, which is the correction of 09.09.2026.</b>
        // What stood here promised that "at a fiftieth, anything that can be done standing still wins
        // outright". It never did: the score takes the fifth root of the product, so a fiftieth arrives as
        // 0.46, and half of a wage a bot cannot earn goes on beating all of a wage it can. The middle
        // position was imaginary. Between the veto that made monuments and the multiplier that changed
        // nothing there is no third number to find, because the two failures are one root apart.
        //
        // What this still is, and it is worth keeping: an honest price. Work a bot cannot begin is worth
        // less to it than work it can, the log line says by how much, and two walking errands are still
        // ranked against each other properly. What decides is one rung up, in BotWill.Auction, where a full
        // pack orders the offers instead of pricing them; the floor the note above is about lives on there
        // unchanged, because a rank with nothing above it changes no answer at all. See BotDeed.Standing.
        var stopped = !deed.Standing && BotLadder.Load(body) > BotLadder.Ceiling(body) ? StoppedShare : 1.0;

        if (stopped < 1.0)
        {
            Stopped++;
        }

        // Whatever the watcher has declared worth doing this quarter of an hour. One when it has declared
        // nothing, which is nearly always. See Revelry.
        var revel = Revelry?.Invoke(deed.Kind) ?? 1.0;

        // Whose ground the work stands on. One everywhere no guild has built, which on this island is most
        // of it — so this costs nothing until a hall goes up, and it can never be nought. See BotLand: a
        // claim that refused would be four guilds between them forbidding the population to work.
        var ground = BotLand.Worth(body, map, deed.Where);

        var product = nearness * novelty * room * caution * purse * stopped * revel * ground;

        if (product <= 0.0)
        {
            veto = $"{deed.Kind} weighed out at nothing: near {nearness:F2}, new {novelty:F2}, room {room:F2},"
                + $" safe {caution:F2}, purse {purse:F2}, standing {stopped:F2}, revel {revel:F2}, ground {ground:F2}";

            return 0.0;
        }

        var score = estimate * Math.Pow(product, 1.0 / Considerations);

        weigh = new BotWeigh(estimate, nearness, novelty, room, caution, purse, score, stopped, revel, ground);

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
