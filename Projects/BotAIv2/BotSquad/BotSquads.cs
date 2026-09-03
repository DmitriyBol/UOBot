using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Every squad on the shard, and the four things that can happen to one: it forms, somebody joins, somebody
/// leaves, it dies.
///
/// <para>
/// <b>One way in.</b> <see cref="Join"/> is the only path into a squad, and the size cap is checked there and
/// nowhere else. That is not tidiness — the first version checked its cap on the recruiting path only, so a
/// bot that stumbled on the same target and called for help was added without any check at all, and companies
/// with a stated maximum of five were found holding twelve.
/// </para>
///
/// <para>
/// <b>The squad's own beat, not the bots'.</b> Rosters, stances and stations change over seconds, not
/// milliseconds, so this runs once a second on its own timer while the bots walk at their own pace. The work
/// per beat is proportional to the number of squads, and the expensive part — working out a station, which
/// ends in a path search — happens only when the anchor has actually moved.
/// </para>
/// </summary>
public static class BotSquads
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSquads));

    /// <summary>How often a squad reconsiders itself. Its life is slow; the bots inside it are not.</summary>
    public const int BeatMs = 1000;

    private static readonly List<BotSquad> _squads = [];

    private static SquadTimer _timer;

    private static int _next;

    public static IReadOnlyList<BotSquad> All => _squads;

    public static int Count => _squads.Count;

    public static long Formed { get; private set; }

    public static long Disbanded { get; private set; }

    public static long Rescues { get; private set; }

    public static long Yields { get; private set; }

    /// <summary>
    /// Bots turned away from a company that had already been taken apart.
    ///
    /// A named nought that should stay small rather than nought: companies really do dissolve in the same
    /// beat somebody decides to join one, and the whole point is that the answer is now "no" instead of a bot
    /// spending the rest of the shard's life in a company that does not exist.
    /// </summary>
    public static long Buried { get; private set; }

    public static bool Running => _timer != null;

    public static void Start()
    {
        if (_timer != null)
        {
            return;
        }

        // Seeded from a real tick rather than left at zero: these counters can start enormous and wrap.
        _saidTick = Core.TickCount;

        _timer = new SquadTimer(TimeSpan.FromMilliseconds(BeatMs));
        _timer.Start();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    public static void Reset()
    {
        for (var i = _squads.Count - 1; i >= 0; i--)
        {
            Dissolve(_squads[i], "the world was reloaded");
        }

        _squads.Clear();
        _next = 0;
        Formed = 0;
        Disbanded = 0;
        Rescues = 0;
        Yields = 0;
        Buried = 0;
        BotSquad.Forget();

        BotSpoils.Reset();
    }

    /// <summary>
    /// The companies in a sentence, and how many bots they are holding.
    ///
    /// <para>
    /// <b>The count of bots bound to a company was the one figure this line did not carry, and it is the one
    /// with a consequence.</b> A bound bot does not take part in the auction at all — the ladder answers for
    /// it — so a company standing about is not merely idle, it is a hole in the population's working time
    /// that nothing else on the shard reports. On 03.09.2026 at 08:13 the stall watch caught Faron 2 the
    /// Healer four minutes into "fell in with company 3", and answering how common that was meant grepping
    /// an hour of log for the phrase. Squads standing, formed and disbanded were all here; how many bots
    /// were inside them was not.
    /// </para>
    /// </summary>
    public static string Describe()
    {
        var bound = 0;

        for (var i = 0; i < _squads.Count; i++)
        {
            bound += _squads[i]?.Count ?? 0;
        }

        return $"{Count} squads standing holding {bound} bots, {Formed} formed and {Disbanded} disbanded, {Rescues} times one of them was set upon, {Yields} tiles given up to whoever belonged on them, {Buried} turned away from a company that no longer existed, {BotSquad.Released} let go for doing nothing for a company that was doing nothing; {BotSpoils.Describe()}";
    }

    /// <summary>
    /// Calls a squad together. Whoever calls it leads it.
    ///
    /// <para>
    /// A founder who cannot fight is refused, and that refusal is one of the first version's plainer lessons.
    /// Its rung order put "this is dangerous, call for help" <em>above</em> "I am running out of health", so a
    /// bot on its last few points would declare a company it could not itself take part in; the company
    /// counted only able fighters, found none, and disbanded in the same tick — and the same bot posted it
    /// again on the next. Whoever is in that state should be running, not recruiting.
    /// </para>
    /// </summary>
    public static BotSquad Form(IBotSquadMember leader)
    {
        if (leader?.Self is not { Deleted: false, Alive: true } || !leader.AbleToFight)
        {
            return null;
        }

        if (leader.Squad != null)
        {
            return leader.Squad;
        }

        var squad = new BotSquad(++_next, leader);

        _squads.Add(squad);
        Formed++;

        squad.Attach(leader);
        leader.Squad = squad;

        logger.Information("{Name} called a squad together", leader.Self.Name);

        return squad;
    }

    /// <summary>
    /// Puts a bot in a squad. The only way in, and therefore the only place the cap is enforced.
    /// </summary>
    public static bool Join(BotSquad squad, IBotSquadMember member)
    {
        if (squad == null || member?.Self is not { Deleted: false, Alive: true })
        {
            return false;
        }

        // <b>A company that has been taken apart is not a company, and every other test here passes for
        // one.</b> Dissolve drops the squad out of the list the timer walks and clears the Squad of everybody
        // in it, but the object goes on answering Count, Ceiling and Map exactly as before — through a leader
        // whose own Squad is now null. So a bot could fall in with a company that had ceased to exist one
        // second earlier, and then never get out of it, because nothing thinks about a squad that is not in
        // the list. See BotSquad.Disbanded for the two log lines that say so.
        if (squad.Disbanded)
        {
            Buried++;

            return false;
        }

        if (member.Squad == squad)
        {
            return true;
        }

        // The company's own ceiling rather than the shard's, because a harrowing was ordered six strong and
        // a muster is worth five. See BotSquad.Ceiling: it is MaxSize for everybody who does not ask.
        if (member.Squad != null || squad.Count >= squad.Ceiling || squad.Map != member.Self.Map)
        {
            return false;
        }

        squad.Attach(member);
        member.Squad = squad;

        return true;
    }

    /// <summary>
    /// Takes a bot out. Its station errand goes with it — the errand underneath, whatever the bot was doing
    /// before it joined, is still there.
    /// </summary>
    public static void Leave(IBotSquadMember member)
    {
        var squad = member?.Squad;

        if (squad == null)
        {
            return;
        }

        squad.Detach(member);
        member.Squad = null;
    }

    /// <summary>The squad this bot is in, or null.</summary>
    public static BotSquad Of(IBotSquadMember member) => member?.Squad;

    /// <summary>Whether these two are in the same squad. Used wherever "one of ours" has to mean something.</summary>
    public static bool Together(IBotSquadMember a, IBotSquadMember b) =>
        a?.Squad != null && ReferenceEquals(a.Squad, b?.Squad);

    /// <summary>
    /// One of ours has been set upon. This is the whole of the shared mind, and it is one line of consequence:
    /// the squad now has a focus, and the formation now anchors on whoever was hit.
    ///
    /// Nobody is asked to come and help. Every station is derived from the anchor, so the moment the anchor
    /// moves onto the member under attack, every other member is already walking towards it — including the
    /// ones a hundred feet away on a sweep, whose patches were derived from that same anchor.
    /// </summary>
    public static void Note(IBotSquadMember member, Mobile attacker)
    {
        var squad = member?.Squad;

        if (squad == null || attacker is not { Deleted: false, Alive: true })
        {
            return;
        }

        // The strongest thing around the member that was hit, not necessarily the thing that hit it. On a
        // graveyard the nearest hostile is always a skeleton, and the first version's companies formed against
        // the nearest: six bots would declare a band against a skeleton, commit at once because a skeleton is
        // trivial, all go and kill it — while the lich that was actually killing them carried on casting.
        var worst = BotThreat.Strongest(member.Self, Reach);

        squad.Engage(worst ?? attacker, member);
        Rescues++;
    }

    /// <summary>
    /// How far around a member the squad looks when working out what is attacking it. The whole company is
    /// counted at this range, so it has to be wide enough that the far knot of a sweep is included and narrow
    /// enough that the next field is not.
    /// </summary>
    public static int Reach { get; set; } = 12;

    /// <summary>
    /// Whether the holder of a tile should give it up to whoever is asking.
    ///
    /// <para>
    /// Two trees with a gap between them, a mage standing in the gap, something hostile on the far side. The
    /// mage dies there in seconds; the blade behind it would take minutes. So the blade asks, and the mage
    /// yields — not because anything recognised a chokepoint, but because the tile belongs to the rank that
    /// stands nearer the threat, and the formation already says which rank that is.
    /// </para>
    ///
    /// <para>
    /// It does not work the other way round. A mage cannot move a blade: the blade is where it belongs.
    /// </para>
    /// </summary>
    public static bool ShouldYield(IBotSquadMember holder, Mobile asker)
    {
        if (holder?.Self is not { Deleted: false, Alive: true } body || asker is not IBotSquadMember member)
        {
            return false;
        }

        // Inside one company the formation decides, because there the tiles mean something: a shield wall
        // that reshuffles itself every time somebody wants past is not a wall.
        //
        // <b>While it is a shield wall.</b> A company that is not fighting holds no wall — its stations are
        // only where everybody happens to be standing — so the rank rule there protects nothing and is four
        // bots refusing each other in a field. Edda, Faron, Gerda and Calla stood at (1298-1303, 1070-1073)
        // for eight minutes on 03.09.2026, every one of them reported "in a company", every one holding an
        // errand of its own that the company had no opinion about, and none outranking any other. In a fight
        // this branch still runs and rank still decides, exactly as before.
        if (holder.Squad is { Stance: BotSquadStance.Fighting } && ReferenceEquals(holder.Squad, member.Squad))
        {
            if (!BotFormation.OutranksFor(member, holder))
            {
                return false;
            }

            Yields++;

            return true;
        }

        // <b>And everybody else gets out of the way, which they flatly refused to do until now.</b> This
        // began and ended at "same company, and the asker outranks you" — so two bots from different
        // companies, or the twenty-odd in none at all, would stand facing each other for ever. With
        // thirty-four bots crowding one training field and one bank counter it showed up as a third of every
        // step on the shard being refused by the engine (11028 of 33015 in one window) and four bots at a
        // time reported stuck by the stall watch, none of them overloaded and none of them lost.
        //
        // The one standing still yields to the one who is going somewhere. That is the whole rule and it is
        // what a person would do in a doorway. It cannot deadlock only because the test below is motion
        // rather than intent: a bot that has not moved lately has nothing to lose by a step, and two that
        // have are not in each other's way for long.
        // <b>Moving, not Walking, and the difference is the whole of whether this rule can deadlock.</b>
        // Walking asks whether the bot holds a plan with tiles left in it, which a bot whose every step is
        // refused does for as long as it stands there. So the rule that reads "the one standing still yields
        // to the one who is going somewhere" was in fact "nobody with a plan yields to anybody", and four
        // bots with plans through each other's tiles held that position for four minutes at (1344, 878) on
        // 03.09.2026. Moving asks whether a step has actually been taken lately, which is what the rule
        // always meant and what makes its own argument true: two bots both moving are both moving.
        if (body is BotMobile { Journey.Moving: true })
        {
            return false;
        }

        Yields++;

        return true;
    }

    /// <summary>
    /// Which way somebody giving up a tile should step: away from whatever the squad is dealing with, or away
    /// from the asker when there is nothing.
    ///
    /// Not merely to clear the tile — to clear it in the direction the yielder wanted to go anyway. A caster
    /// pushed out of a gap should end up behind the line, which is its own station.
    /// </summary>
    public static Direction YieldAwayFrom(IBotSquadMember holder, Mobile asker)
    {
        var body = holder?.Self;

        if (body == null)
        {
            return Direction.North;
        }

        var from = holder.Squad?.Focus ?? asker;

        if (from == null)
        {
            return body.Direction;
        }

        // Four points round the compass from whatever it is backing away from.
        var towards = (int)(body.GetDirectionTo(from) & Direction.Mask);

        return (Direction)((towards + 4) & 0x7);
    }

    /// <summary>
    /// How often the state of companies is said out loud.
    ///
    /// <para>
    /// <b>"No squads formed" is not a measurement, and for two evenings it was all there was.</b> The count
    /// went from fifteen in twenty minutes to none, twice over, and nothing in the log distinguished a
    /// population with nothing worth ganging up on from one too scattered to gather two helpers — which want
    /// opposite fixes. This says both, beside each other, every five minutes.
    /// </para>
    /// </summary>
    public static int SayEveryMs { get; set; } = 300000;

    private static long _saidTick;

    /// <summary>One beat: every squad reconsiders itself, and the ones that are finished are cleared away.</summary>
    public static void Update()
    {
        if (Core.TickCount - _saidTick >= SayEveryMs)
        {
            _saidTick = Core.TickCount;

            logger.Information("Companies: {Standing}; {Muster}; {Enlist}", Describe(), BotMuster.Describe(), BotEnlister.Describe());

            // Two facts about fighting that had nowhere else to be said, on the only clock in this assembly
            // that ticks slowly enough to say them: who came to whose aid, and who is swinging its fists.
            logger.Information("Arms: {Cries}; {Hands}; {Scrolls}", BotCry.Describe(), BotArms.Describe(), BotArmoury.Describe());

            // The bow's own line. It earns one because "the archer never kites" survived two nights of being
            // blamed on other things, and a decision nobody counts is a decision nobody can argue with.
            logger.Information("Bows: {Kites}", BotSlay.Bows());

            // The board, from the asking side. Counters were written for both of these and printed nowhere,
            // which is the same fault as having none: "the board is empty" and "nobody has looked at the
            // board" are different facts and were producing the same silence.
            logger.Information(
                "Needs: {Gear}; {Metal}; {Forge}; {Thread}",
                BotUpkeep.Describe(),
                BotBullion.Describe(),
                BotSmith.Describe(),
                BotTailor.Describe()
            );

            // <b>The ground the whole economy is dug out of, and it has never once been printed.</b>
            // BotGround.Describe existed and went to exactly two places: a gump nobody has open, and the
            // world reload — which is to say nowhere. So how much rock this island has, how much of it is
            // behind a wall and how much turned out to be a mirage were facts nobody could read. It is the
            // same fault the market's own summary had, and BotBeat.Summarise carries the note about it.
            logger.Information("The ground: {What}; {Stables}", BotGround.Describe(), BotStable.Describe());
        }

        for (var i = _squads.Count - 1; i >= 0; i--)
        {
            var squad = _squads[i];

            string over;

            try
            {
                over = squad.Update();

                if (over == null)
                {
                    continue;
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Squad {Id} threw while thinking; it has been disbanded", squad.Id);

                over = "it threw while thinking";
            }

            Dissolve(squad, over);
        }
    }

    /// <summary>
    /// Takes a whole company apart, for whoever raised it.
    ///
    /// <para>
    /// <b>Update's own rule is that whoever set the charge owns ending the company, and there was no way for
    /// an owner to do it.</b> BotScout.Disband says in its summary that it lets the party go, and what it had
    /// was Leave, which detaches one bot — the leader. The other five inherited a new leader with no errand
    /// and stood there: a company only dissolves itself at nought members or at one uncharged, and five bots
    /// on the Bound rung have no work of their own to walk away to. On 03.09.2026 every bot the stall watch
    /// caught standing still was reported "in a company", and the captain beside them "on its own".
    /// </para>
    /// </summary>
    public static void Disband(BotSquad squad, string why)
    {
        if (squad == null || !_squads.Contains(squad))
        {
            return;
        }

        Dissolve(squad, why);
    }

    private static void Dissolve(BotSquad squad, string why)
    {
        // First, so that a Join arriving in the same beat is refused rather than attaching to a corpse.
        squad.Bury();

        var members = squad.Members;

        for (var i = members.Count - 1; i >= 0; i--)
        {
            members[i].Squad = null;
        }

        _squads.Remove(squad);
        Disbanded++;

        logger.Information("Squad {Id} is no more: {Why}", squad.Id, why);
    }

    private sealed class SquadTimer : Timer
    {
        public SquadTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => Update();
    }
}
