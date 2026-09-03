using System.Collections.Generic;
using Server.Regions;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Going to look for a fight, when there is nothing to fight where the bot is standing.
///
/// <para>
/// <b>Without this a fighter is only as good as where somebody put it.</b> Hunting begins when something
/// hostile is within sight, and a town forbids fighting outright — so a population raised in a city has
/// warriors that stand about for ever, and a population raised at a graveyard hunts only that graveyard until
/// it is empty. Neither is a bot deciding anything; both are a bot being placed well or badly.
/// </para>
///
/// <para>
/// <b>It is priced at almost nothing on purpose.</b> Walking somewhere produces no coin, no goods and no
/// skill, so it wins only when the auction has nothing else to offer at all — which is exactly when a bot
/// should be out looking rather than standing still. The moment anything worth fighting comes into reach, the
/// hunt itself scores an order of magnitude higher and takes over at the next review.
/// </para>
///
/// <para>
/// And it ends itself the instant it has worked: a bot that walks into sight of a quarry does not finish the
/// walk first. "Stand and complete the errand" is the shape of bug this project keeps finding in itself.
/// </para>
/// </summary>
public sealed class BotProwl : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotProwl));

    /// <summary>The ledger's key.</summary>
    public const string Trade = "prowl";

    /// <summary>
    /// What looking for a fight is reckoned at per minute. Below every trade on the shard, and above nothing,
    /// which is the whole of its place in the order.
    /// </summary>
    public static double Prior { get; set; } = 8.0;

    public static double WorkMinutes { get; set; } = 2.0;

    /// <summary>
    /// How near the ground a bot has to get before the look counts as taken.
    ///
    /// <para>
    /// <b>Its own number, and sharing one with the search radius made this errand a no-op by
    /// construction.</b> Arrival used to be <see cref="BotQuarry.Reach"/> — the same fifty tiles that
    /// <see cref="BotHunter"/> requires a candidate to be <em>outside</em> of before it may be offered at
    /// all. So a prowl was legal only beyond the line and finished the moment the bot came back inside it:
    /// the whole errand was crossing one boundary, which is one step from where it was handed out. The
    /// evening of 24.08.2026 has a hundred and twenty-three of them, every one paying nothing, ninety
    /// lasting twelve seconds and several lasting one — a population that read as busy hunting and was
    /// flickering on the spot.
    /// </para>
    ///
    /// <para>
    /// Small, because the point of walking somewhere is to be there. Nothing is wasted by going the whole
    /// way: the bot searches its full reach every beat as it walks, and the check above ends the errand the
    /// instant anything worth fighting appears — so the journey <em>is</em> the search, and only its end is
    /// being defined here.
    /// </para>
    /// </summary>
    public static int ArriveWithin { get; set; } = 8;

    /// <summary>
    /// How many beats the walk may go without getting any nearer before the ground is given up on.
    ///
    /// <para>
    /// <b>Because <see cref="Bend"/> only fires when the road refuses, and a road that never refuses is the
    /// commoner failure.</b> A walk that is simply never completed says nothing to anybody: on the night of
    /// 02-03.09.2026 the roll-call found Edda the Brawler holding a prowl for 250 seconds at 1651,1112 and
    /// Kerrin the Warrior for 324 seconds at 1296,1560, neither arriving, neither finishing, neither leaving
    /// a patch two tiles across — and the only thing that noticed was the debugger, four minutes late. The
    /// same silence and the same cure as the long walk to a seam in <c>BotDig</c>: measure the closing of the
    /// distance rather than the attempts, because the walk is meant to be long and it is stopping to get
    /// closer that is the fault.
    /// </para>
    ///
    /// <para>
    /// Two hundred beats, about forty seconds at a turn every two hundred milliseconds, and the same number
    /// the seam uses for the same reason.
    /// </para>
    /// </summary>
    public static int TrekLimit { get; set; } = 200;

    /// <summary>Prowls given up because the bot stopped getting nearer. See <see cref="TrekLimit"/>.</summary>
    public static long Baulked { get; private set; }

    private readonly Map _map;

    private readonly Point3D _where;

    /// <summary>The closest this bot has come, and how many beats since that last improved.</summary>
    private int _nearest = int.MaxValue;

    private int _stalled;

    public BotProwl(Map map, Point3D where) : this(map, where, false)
    {
    }

    /// <param name="company">
    /// Whether this ground asks more strength than the bot has on its own. When it does, the first beat
    /// raises a company out of whoever is standing there and the errand fails honestly if it cannot — the
    /// obligation in Patrick's order of 03.09.2026: pass the threshold, or bring people who together do.
    /// </param>
    public BotProwl(Map map, Point3D where, bool company)
    {
        _map = map;
        _where = where;
        _company = company;
    }

    /// <summary>Whether this ground was accepted on the strength of a company that does not exist yet.</summary>
    private readonly bool _company;

    private bool _raised;

    /// <summary>Companies raised because the ground asked more than one bot could bring.</summary>
    public static long Raised { get; private set; }

    /// <summary>Errands given up because the company could not be raised after all.</summary>
    public static long Unraised { get; private set; }

    /// <summary>
    /// How long one bot's claim on a square holds off everybody else's company.
    ///
    /// Long enough for the company to form and set out, and short enough that a claim by a bot which then
    /// died or was outbid does not fence the square off. A minute.
    /// </summary>
    public static int ClaimMs { get; set; } = 60000;

    private static readonly Dictionary<(int Map, int X, int Y), long> _raising = [];

    public static bool Raising(Map map, Point3D where)
    {
        var key = BotQuad.Key(map, where);

        return _raising.TryGetValue(key, out var when) && Core.TickCount - when < ClaimMs;
    }

    private static void Claim(Map map, Point3D where) => _raising[BotQuad.Key(map, where)] = Core.TickCount;

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    /// <summary>Nothing. Walking teaches a bot nothing, and saying otherwise would make walking a career.</summary>
    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override int Made => 0;

    public override string Stage => $"looking for a fight near {_where}";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        // <b>The obligation, discharged before a step is taken.</b> This ground was accepted only because the
        // bots standing here would clear its threshold together, so the company is raised now, out of whoever
        // is still standing here — which may be fewer than when it was weighed, and if it is, the errand ends
        // rather than walking one bot into ground that was refused to one bot.
        if (_company && !_raised)
        {
            _raised = true;

            Claim(_map, _where);

            if (bot is IBotSquadMember { Squad: null } member && BotSquads.Running)
            {
                var squad = BotSquads.Form(member);

                if (squad != null)
                {
                    foreach (var mobile in _map.GetMobilesInRange<Mobile>(body.Location, BotMuster.Reach))
                    {
                        if (squad.Count >= squad.Ceiling)
                        {
                            break;
                        }

                        if (mobile != body && mobile is IBotSquadMember { Squad: null } other
                            && mobile is IBotAlly { AbleToFight: true })
                        {
                            BotSquads.Join(squad, other);
                        }
                    }

                    // Held together for the walk, exactly as a scouting party is: a company with nothing to
                    // fight yet is dissolved on its next beat otherwise, and there is nothing to fight until
                    // it arrives. See BotSquad.Charged.
                    squad.Charged = true;
                }
            }

            if (!BotQuad.Dares(body, _map, _where))
            {
                Unraised++;

                return BotDoing.Failed($"could not raise enough strength for ({_where.X}, {_where.Y})");
            }

            Raised++;

            logger.Information(
                "{Name} raised a company for ({X}, {Y}), which asks {Muscle:F0} of strength",
                body.Name,
                _where.X,
                _where.Y,
                BotQuad.Muscle(_map, _where)
            );
        }

        // The errand's whole purpose, met. Finished rather than pressed on with: the hunt is worth ten times
        // this and will be chosen the moment anything is asked.
        //
        // Asked through BotQuarry.Worthwhile rather than by looking for a quarry directly, because "there is
        // something here" and "the hunter will offer it to me" are not the same statement, and where they
        // came apart the bot finished and retook this errand ten times a second. See that method.
        // <b>Something has already chosen, and a bot out looking for a fight does not get to walk past one.</b>
        // Worthwhile below asks what this bot would <em>pick</em> — the biggest thing it could take alone,
        // unclaimed, not shunned, not crowded — and every one of those filters is right for choosing and
        // wrong for being chosen. An orc that has settled on a mage is not a candidate the mage is weighing:
        // it is coming either way, and the only question left is whether the mage spends the next minute
        // running in front of it. Cedric spent it running.
        var picked = BotThreat.Hunter(body, BotMobile.NoticeRange);

        // <b>Unless it is a fight this bot has already been refused, in which case handing over is a circle.</b>
        // BotSlay gives a fight up on the numbers standing round the quarry and marks the creature crowded —
        // one bot cannot have it. But the creature is still on this bot, so this test fired again, the errand
        // finished again, the auction offered the fight again, and it was refused again. On 02.09.2026 between
        // 22:46 and 22:56 that came to 123 prowls taken, 97 ended, 51 of those ended here, against 50 refusals
        // for "too many of them around" — Joss went round it 25 times and Orin 17, and each lap wrote another
        // nought per minute against the very ground the crown had just started sending companies to.
        //
        // Walking on is also the right answer and not merely the quiet one: a bot that has judged itself
        // outnumbered and stays where the numbers are has decided nothing. Crowded lasts two minutes, so the
        // creature gets one more honest hand-over after that if it is still interested.
        // <b>Once, and the first attempt at this was wrong in an instructive way.</b> The condition was
        // Crowded alone — the mark BotSlay writes when it gives a fight up on the numbers — on the theory
        // that the loop ran between prowl and a refused rescue. The window of 23:18 to 23:31 on 02.09.2026
        // refuted it: 345 prowls taken, 189 ending here, and no fight of any kind recorded in between. The
        // auction was not refusing the fight; it was never being offered one, because it only offers a fight
        // the bot would choose and something that has chosen the bot is very often not that. A hand-over
        // with no receiver is a loop. So it is offered once — see BotQuarry.Hand — and after that walking on
        // is the answer, which is also what a bot nobody will fight for ought to be doing.
        if (picked != null && !BotQuarry.Crowded(picked) && !BotQuarry.Handed(picked))
        {
            BotQuarry.Hand(picked);

            return BotDoing.Done("something has picked this fight for us");
        }

        if (BotQuarry.Worthwhile(body))
        {
            return BotDoing.Done("something worth fighting");
        }

        if (body.InRange(_where, ArriveWithin))
        {
            // Arrived, and still nothing. Not a failure — the ground was worth a look and now this bot knows
            // it is empty, which is what the ledger is for.
            return BotDoing.Done("nothing here");
        }

        var gap = System.Math.Max(System.Math.Abs(body.X - _where.X), System.Math.Abs(body.Y - _where.Y));

        if (gap < _nearest)
        {
            _nearest = gap;
            _stalled = 0;
        }
        else if (++_stalled >= TrekLimit)
        {
            // Said on the map first, exactly as Bend does and for the reason given there: what one bot proves
            // about ground is true for all of them, and the worst square goes to the front of every prowl's
            // list. The reading is not touched — the square is as dangerous as it was; what is learned is
            // that this bot could not get there.
            BotPeril.Baulked(_map, _where);

            // <b>And to the quadrant record, which is the other map this ground can be chosen from and the
            // one that was never told.</b> BotHunter.Feared asks BotQuad.WorstNear; the baulk above goes to
            // BotPeril; so a square the quadrant record thinks is the worst on the island went on being
            // handed to every hunter that asked, however many of them had already failed to reach it. On
            // 03.09.2026, 119 prowls ended at this same line against one square, (1005, 1335), and hunting
            // finished a third as often as in the run before.
            BotQuad.Baulk(_map, _where);
            Baulked++;

            return BotDoing.Failed($"got no nearer than {gap} tiles to ({_where.X}, {_where.Y})");
        }

        return BotDoing.Walk(_map, _where, BotArrival.Within(ArriveWithin), "looking for a fight");
    }

    /// <summary>
    /// The ground could not be walked to. There is nowhere else this errand could go — it <em>is</em> the
    /// walk — so it gives up, and says so on the map first.
    ///
    /// <para>
    /// <b>Marking it is the whole point of overriding this at all.</b> The peril map's worst square goes to
    /// the front of every prowl's candidate list, so a dangerous square nobody can reach is offered to every
    /// idle fighter on the island, over and over: 209 failures on (1308, 1380) in twenty-five minutes on
    /// 27.08.2026, out of 257 failed prowls in total. <c>BotPeril.Baulked</c> was written for exactly this
    /// and says so in its own note — "a lone bot reading this map for somewhere to prowl is kept off it
    /// too" — but only companies ever wrote the mark, and a company is the rarest thing on the shard. What
    /// one bot proves about ground is true for all of them.
    /// </para>
    ///
    /// <para>
    /// The reading is not touched. The square is every bit as dangerous as it was; the only thing learned is
    /// that nobody can get there.
    /// </para>
    /// </summary>
    public override bool Bend(IBotWilful bot)
    {
        BotPeril.Baulked(_map, _where);
        BotQuad.Baulk(_map, _where);

        return false;
    }
}
