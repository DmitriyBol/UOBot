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

    public static long Buried { get; private set; }

    public static long Enemies { get; private set; }

    public static long Inside { get; private set; }

    public static bool Running => _timer != null;

    public static void Start()
    {
        if (_timer != null)
        {
            return;
        }

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
        Steady = 0;
        Yields = 0;
        Buried = 0;
        Rebuffs = 0;
        Hurries = 0;
        Comings = 0;
        Protected = 0;
        ProtectedIdle = 0;
        _due.Clear();
        Afield = 0;
        CalledPast = 0;
        Sufficed = 0;
        LeftAtWork = 0;
        CarriersSpared = 0;
        FightersSpared = 0;
        ThievesSpared = 0;
        PressedIn = 0;
        Friendly = 0;
        Enemies = 0;
        Inside = 0;
        BotSquad.Forget();
        BotRaise.Forget();

        BotSpoils.Reset();
    }

    public static int Bound
    {
        get
        {
            var held = 0;

            for (var i = 0; i < _squads.Count; i++)
            {
                held += _squads[i]?.Count ?? 0;
            }

            return held;
        }
    }

    public static string Describe()
    {
        var bound = Bound;

        return $"{Count} squads standing holding {bound} bots, {Formed} formed and {Disbanded} disbanded, {BotSquad.Sundered} stations withheld for lying across a dungeon's edge from the member, {BotSquad.StationRested} withheld from a member resting after a place it could not reach ({BotSquad.StationShorted} set resting for a search that came back short of its place, {BotWalk.ShortStations} as the short plan was drawn, {BotWalk.StationsLetGo} places let go at their second short plan), {BotSquad.StationFar} withheld in a fight from a member further than {BotSquad.StationReach} tiles from the anchor and left to its enlistment's walk,{Rescues} times a company with no enemy was set upon and took on the strongest thing attacking it ({Afield} blows on a member standing further than {BotSquad.AnswerReach} tiles from its leader left to the member), {Steady} blows that left the company on the enemy it was already fighting ({Friendly} more by a bot of a guild not at war with it, passed over as friendly fire, {Inside} from inside the company itself, {Enemies} bots refused a place for a guild hostile to the leader's, {Distant} for standing more than {JoinReach} tiles from it), {Yields} tiles given up to whoever belonged on them, {Buried} turned away from a company that no longer existed, {Duellists} called while in a duel or held at the arenas and left there, {BotSquad.Released} let go for doing nothing for a company that was doing nothing, {Rebuffs} times one of them was handed back something it had already given up on, {BotSquad.Unowned} charges taken back because the errand holding them had ended; {BotSquad.Blinded} beats stood near enough to fight with no line to the thing ({BotSquad.NearLeader} of them set on the strongest other attacker they could see, else the enemy nearest the leader), {BotSquad.Refused} refused the blow by the engine and {BotSquad.Unsteadied} were shooters that had moved too recently to fire, {BotSquad.Blindfights} fights given up because nobody could land one at all, {BotFormation.Unanchored} stations answered with standing fast because nothing round the enemy could be walked to ({BotFormation.Uncomponented} candidates passed over for lying in another part of the walkers' graph), {BotSquad.Conjured} spells thrown by the back ranks and {BotSquad.Mended} heals aimed by their medics and mages ({BotSquad.Tended} of them between fights), against {BotSquad.Dry} beats with nothing they could pay for; the enemy: {BotSquad.Engaged} taken on, {BotSquad.ToStrongest} of them the strongest thing attacking a company that had none and {BotSquad.ToStronger} a stronger attacker the company left its enemy for ({BotSquad.LeaderCalls} of these the leader's own combatant inside the {BotSquad.TieShare:P0} tie), {BotSquad.HeldFor} beats a stronger attacker waited out the {BotSquad.FocusHoldMs / 1000}s hold, {BotSquad.Switched} switches away from an enemy still standing ({BotSquad.SwitchedSoon} of them inside the hold), {BotSquad.Downed} went down, {BotSquad.Closing} beats the never-got-near clock was torn up for a company still closing on its enemy; withdrawals: {BotSquad.Withdrawn()}; {BotSquad.Afielded()}; {BotCompanyRecord.Describe()}; {BotSquad.Supported()}; {BotRaise.Describe()}; calling: {CalledPast} called past the usual {BotSquad.Usual} because the fight asked more, {Sufficed} calls that stopped short of the {BotSquad.MaxSize} for having {BotSquad.CallOdds:F1} times what they were to meet, {LeftAtWork} bots within a call left at work the enlistment would have cost them ({CarriersSpared} of them carrying another bot's goods; {FightersSpared} hunters at their fight left to it by a call, a harrowing or a delve's party; {ThievesSpared} thieves at their turn in a crowd left to it), {PressedIn} pressed to the enlistment on being called; {BotSquad.Defended} beats a member struck in melee was left on what struck it, {BotSquad.Excused} places not handed to a member running, binding its own wounds or raising one of ours, {BotSquad.RoadSpared} looks for a fight a delve's company skipped on its road, {BotSquad.Paced} turns a marching leader was slowed to for a member behind it (beats a member behind was not waited for, by what it held instead of the enlistment: {BotSquad.Unpaced()}); first blows on the company's enemy, by what the leader was doing when the company took it on: {BotSquad.Responsiveness()}; {BotSpoils.Describe()}";
    }

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

        if (BotTrading.Refuses(leader.Self))
        {
            return null;
        }

        var squad = new BotSquad(++_next, leader);

        _squads.Add(squad);
        Formed++;

        squad.Attach(leader);
        leader.Squad = squad;

        logger.Information("{Name} called a squad together", leader.Self.Name);

        return squad;
    }

    public static long Duellists { get; private set; }

    public static bool Join(BotSquad squad, IBotSquadMember member)
    {
        if (squad == null || member?.Self is not { Deleted: false, Alive: true })
        {
            return false;
        }

        if (squad.Disbanded)
        {
            Buried++;

            return false;
        }

        if (member.Squad == squad)
        {
            return true;
        }

        if (BotDuel.Duelling(member.Self))
        {
            Duellists++;

            return false;
        }

        if (BotTrading.Refuses(member.Self))
        {
            return false;
        }

        if (member.Squad != null || squad.Count >= squad.Ceiling || squad.Map != member.Self.Map)
        {
            return false;
        }

        if (!Reaches(squad, member.Self))
        {
            Distant++;

            return false;
        }

        if (squad.Leader?.Self is Mobile lead && BotRegard.Hostile(lead, member.Self))
        {
            Enemies++;

            return false;
        }

        squad.Attach(member);
        member.Squad = squad;

        return true;
    }

    public static bool Enough(BotSquad squad, double strength, double asked)
    {
        if (squad.Count >= squad.Ceiling)
        {
            return true;
        }

        if (squad.Count < BotSquad.Usual)
        {
            return false;
        }

        if (asked <= 0.0 || strength >= asked * BotSquad.CallOdds)
        {
            Sufficed++;

            return true;
        }

        return false;
    }

    public static void Past() => CalledPast++;

    public static long CalledPast { get; private set; }

    public static long Sufficed { get; private set; }

    public static bool Enrol(BotSquad squad, IBotSquadMember other, Map map, Point3D where, string why, bool force = false)
    {
        if (!force && other is IBotWilful wilful && !Pressable(wilful))
        {
            LeftAtWork++;

            if (Carrying(wilful))
            {
                CarriersSpared++;
            }
            else if (Fighting(wilful))
            {
                FightersSpared++;
            }
            else if (Thieving(wilful))
            {
                ThievesSpared++;
            }

            return false;
        }

        if (!Join(squad, other))
        {
            return false;
        }

        if (other is IBotWilful called && BotWill.Press(called, new BotEnlist(squad, map, where, true), why))
        {
            PressedIn++;
        }

        return true;
    }

    public static bool Pressable(IBotWilful bot)
    {
        var resolve = bot?.Resolve;

        if (resolve?.Deed is not { } deed)
        {
            return true;
        }

        if (deed.Alongside)
        {
            return false;
        }

        if (deed is BotRescue { Own: true })
        {
            return true;
        }

        if (BotInfirmaries.Spares(deed))
        {
            return false;
        }

        if (Carrying(bot))
        {
            return false;
        }

        if (Fighting(bot))
        {
            return false;
        }

        if (Thieving(bot))
        {
            return false;
        }

        return BotWill.Resume && deed.Resumes && resolve.Paused == null;
    }

    public static bool SpareFighters { get; set; } = true;

    public static bool Fighting(IBotWilful bot) => SpareFighters && bot?.Resolve?.Deed is BotSlay { Engaged: true };

    public static long FightersSpared { get; private set; }

    public static void SpareFighter() => FightersSpared++;

    public static bool SpareThieves { get; set; } = true;

    public static bool Thieving(IBotWilful bot) => SpareThieves && bot?.Resolve?.Deed is BotPickpocket;

    public static long ThievesSpared { get; private set; }

    public static void SpareThief() => ThievesSpared++;

    public static bool SpareCarriers { get; set; } = true;

    public static bool Carrying(IBotWilful bot) => SpareCarriers && bot?.Resolve?.Deed is BotDeliver or BotHaul;

    public static long CarriersSpared { get; private set; }

    public static void Spare() => CarriersSpared++;

    public static long LeftAtWork { get; private set; }

    public static long PressedIn { get; private set; }

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

    public static BotSquad Of(IBotSquadMember member) => member?.Squad;

    public static bool Together(IBotSquadMember a, IBotSquadMember b) =>
        a?.Squad != null && ReferenceEquals(a.Squad, b?.Squad);

    public static void Note(IBotSquadMember member, Mobile attacker)
    {
        var squad = member?.Squad;

        if (squad == null || attacker is not { Deleted: false, Alive: true })
        {
            return;
        }

        if (attacker is BotMobile other && member.Self is Mobile self && !BotRegard.AtWar(self, other))
        {
            Friendly++;

            return;
        }

        if (attacker is IBotSquadMember { Squad: { } theirs } && ReferenceEquals(theirs, squad))
        {
            Inside++;

            return;
        }

        if (squad.Mustering)
        {
            squad.Struck(attacker);
        }

        if (squad.Focus is { Deleted: false, Alive: true } focus)
        {
            if (Protects && member.Self is { HitsMax: > 0 } hurt
                && (hurt.Hits < hurt.HitsMax * BotCry.DireAt || BotLadder.Failing(hurt))
                && (BotThreat.Power(attacker) >= BotThreat.Power(focus) * Switch || focus.Combatant is not IBotSquadMember { Squad: { } on } || !ReferenceEquals(on, squad))
                && squad.StrongestAttacking(attacker) is { } stronger && (ReferenceEquals(stronger, attacker) || BotThreat.Power(attacker) >= BotThreat.Power(stronger) * BotSquad.TieShare)
                && !BotQuarry.Shunned(attacker) && !squad.Bleeding(attacker)
                && (BotSquad.Caster(attacker) || !squad.Beyond(attacker, 0.0, BotSquad.WithdrawOdds) && !BotSquad.Shuns(attacker, squad.Able())))
            {
                if (BotThreat.Power(attacker) < BotThreat.Power(focus) * Switch)
                {
                    ProtectedIdle++;
                }

                squad.Engage(attacker, member);
                Protected++;

                return;
            }

            Steady++;

            return;
        }

        var pick = squad.StrongestAttacking(attacker);

        if (pick == null)
        {
            Afield++;

            return;
        }

        if (BotQuarry.Shunned(pick))
        {
            Rebuffs++;

            return;
        }

        if (squad.Bleeding(pick) || !BotSquad.Caster(pick) && (squad.Beyond(pick, 0.0, BotSquad.WithdrawOdds) || BotSquad.Shuns(pick, squad.Able())))
        {
            squad.ShyOf(pick);

            return;
        }

        squad.Engage(pick, member);
        Rescues++;
    }

    public static bool AnswersAtOnce { get; set; } = true;

    public static long Hurries { get; private set; }

    public static long Comings { get; private set; }

    private static readonly List<BotSquad> _due = [];

    public static void Hurry(BotSquad squad)
    {
        if (!AnswersAtOnce || squad == null || squad.Due || squad.Disbanded)
        {
            return;
        }

        squad.Due = true;
        _due.Add(squad);
    }

    public static void Answer()
    {
        if (_due.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _due.Count; i++)
        {
            var squad = _due[i];

            if (!squad.Due)
            {
                continue;
            }

            try
            {
                squad.AnswerNow();
                Hurries++;
            }
            catch (Exception e)
            {
                squad.Due = false;

                logger.Error(e, "Squad {Id} threw answering between its beats; it carries on at its next", squad.Id);
            }
        }

        _due.Clear();
    }

    public static void Coming(IBotSquadMember member, Mobile creature)
    {
        var squad = member?.Squad;

        if (!AnswersAtOnce || squad == null || creature is not { Deleted: false, Alive: true })
        {
            return;
        }

        if (squad.Mustering && squad.Focus is { Deleted: false, Alive: true })
        {
            squad.Struck(creature);

            return;
        }

        if (squad.Focus is { Deleted: false, Alive: true })
        {
            return;
        }

        var pick = squad.StrongestAttacking(creature);

        if (pick == null || BotQuarry.Shunned(pick) || squad.Bleeding(pick))
        {
            return;
        }

        if (!BotSquad.Caster(pick) && (squad.Beyond(pick, 0.0, BotSquad.WithdrawOdds) || BotSquad.Shuns(pick, squad.Able())))
        {
            squad.ShyOf(pick);

            return;
        }

        squad.Engage(pick, member);
        Comings++;
    }

    public static string DescribeAnswers() =>
        $"{Hurries} answers by a company between its one-second beats (an enemy just taken on, a member become a patient or failing: its members turned and its medics asked at once), {Comings} companies with no enemy took on a creature seen coming for a member before its blow, {Protected} turned at a blow to protect a member under {BotCry.DireAt:P0} of its health ({Protected - ProtectedIdle} from a thing {Switch:F1} times their enemy, {ProtectedIdle} from a weaker thing while their enemy was on none of them); {BotSquad.Guests} wounded bots not of the company brought to its medics with no enemy standing, {BotSquad.GuestHeals} heals its medics chose one for";

    public static bool Protects { get; set; } = true;

    public static long Protected { get; private set; }

    public static long ProtectedIdle { get; private set; }

    public static long Rebuffs { get; private set; }

    public static long Afield { get; private set; }

    public static int JoinReach { get; set; } = 400;

    public static long Distant { get; private set; }

    public static bool Reaches(BotSquad squad, Mobile body) =>
        body != null && (squad?.Leader?.Self is not Mobile head || body.InRange(head.Location, JoinReach));

    public static long Friendly { get; private set; }

    public static int Reach { get; set; } = 12;

    public static double Switch { get; set; } = 1.5;

    public static long Steady { get; private set; }

    public static bool ShouldYield(IBotSquadMember holder, Mobile asker)
    {
        if (holder?.Self is not { Deleted: false, Alive: true } body || asker is not IBotSquadMember member)
        {
            return false;
        }

        if (holder.Squad is { Stance: BotSquadStance.Fighting } && ReferenceEquals(holder.Squad, member.Squad))
        {
            if (!BotFormation.OutranksFor(member, holder))
            {
                return false;
            }

            Yields++;

            return true;
        }

        if (body is BotMobile { Journey.Moving: true })
        {
            return false;
        }

        Yields++;

        return true;
    }

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

        var towards = (int)(body.GetDirectionTo(from) & Direction.Mask);

        return (Direction)((towards + 4) & 0x7);
    }

    public static int SayEveryMs { get; set; } = 300000;

    private static long _saidTick;

    public static void Update()
    {
        if (Core.TickCount - _saidTick >= SayEveryMs)
        {
            _saidTick = Core.TickCount;

            logger.Information("Companies: {Standing}; {Muster}; {Bands}; {Enlist}", Describe(), BotMuster.Describe(), BotBand.Describe(), BotEnlister.Describe());

            logger.Information(
                "Arms: {Cries}, {Outmatched} rescues passed over for the crowd round the foe; {Hands}; {Scrolls}; {Mending}; {Salve}; {Standing}; {Hire}; {Flight}; {Brawls}; {Outlaws}; {Robbers}; {Patrols}; {Assailed} times a bot set upon by a red hit back",
                BotCry.Describe(),
                BotRescuer.Outmatched,
                BotArms.Describe(),
                BotArmoury.Describe(),
                BotMedic.Describe(),
                BotSalve.Describe() + ", " + BotSurgeon.Describe() + "; " + BotMend.Describe(),
                BotAttendant.Describe() + "; " + BotHouseCalls.Describe(),
                BotRetainer.Describe(),
                BotFugitive.Describe() + "; " + BotBolt.Describe(),
                BotBrawl.Describe(),
                BotOutlaw.Describe(),
                BotRobber.Describe(),
                BotManhunt.Describe(),
                BotDefender.Assailed
            );

            logger.Information("Underworld: {Underworld}", BotUnderworld.Describe());

            logger.Information("Bows: {Kites}", BotSlay.Bows());

            logger.Information(
                "Needs: {Gear}; {Metal}; {Forge}; {Tinker}; {Thread}; {Arrows}; {Bottles}; {Skillet}; {Stores}; {Filled} things were kept in a pack at the counter for a carrier to take to somebody's want, {Shed} lots were put in the bank box on the spot by bots too heavy to walk, {Exposed} trips were begun because a pack was worth more than a bot should be carrying about, {Hoarding} because it held more stones of somebody else's goods than it should, {KitTrips} because capped kinds rode over their number and {BankTrips} because it stood at a bank with surplus (offers), {Stored} lots of {Stowed} things went into bank boxes ({Boxless} times the box would not take them; {Unlotted} things the porter left out of its count for having no room in the box; {Overheld} units put away after waiting their time for a want near; {Unbanked}gp the bank refused for a full box),{Dumped} things nobody would buy were left on the ground by bots that could not walk ({Stranded} of these errands were finished with no counter known at all, {Cornered} were offered to a bot with nothing the market wants so that the dropping could be reached, and {Immovable} of those found nothing to drop either; {Treadmills} stand-still passes refused for leaving a bot no lighter than the last), and {Sent} kills were chosen because the board wanted what the carcass carries",
                BotUpkeep.Describe(),
                BotBullion.Describe(),
                BotSmith.Describe(),
                BotTinkerer.Describe() + "; bows: " + BotBowyer.Describe() + "; weaving: " + BotWeaver.Describe() + $"; {BotTailor.OwnCloth} sewing stints on the weaver's own cloth, {BotProvision.Cut} bandages cut instead of bought",
                BotTailor.Describe(),
                BotFletcher.Describe(),
                BotAlchemist.Describe() + "; scrolls: " + BotScribes.Describe(),
                BotCook.Describe(),
                BotStores.Describe(),
                BotUnload.Filled,
                BotUnload.Shed,
                BotUnload.Exposed,
                BotUnload.Hoarding,
                BotUnload.KitTrips,
                BotUnload.BankTrips,
                BotUnload.Stored,
                BotUnload.Stowed,
                BotUnload.Boxless,
                BotUnload.Unlotted,
                BotUnload.Overheld,
                BotUnload.Unbanked,
                BotUnload.Dumped,
                BotUnload.Stranded,
                BotUnload.Cornered,
                BotUnload.Immovable,
                BotUnload.Treadmills,
                BotQuarry.Sent
            );

        logger.Information("Market: {What}", BotMarket.Describe());

        logger.Information("Packs: {What}", BotUnload.Weighed());

            logger.Information(
                "The ground: {What}; {Wood}; {Stables}",
                BotGround.Describe(),
                BotWoodsman.Describe(),
                BotStable.Describe()
            );
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
