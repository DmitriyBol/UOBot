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
        LeaderGuarded = 0;
        Friendly = 0;
        Enemies = 0;
        Inside = 0;
        BotSquad.Forget();

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

        return $"{Count} squads standing holding {bound} bots, {Formed} formed and {Disbanded} disbanded, {BotSquad.Sundered} stations withheld for lying across a dungeon's edge from the member, {BotSquad.StationRested} withheld from a member resting after a place it could not reach ({BotSquad.StationShorted} set resting for a search that came back short of its place, {BotWalk.ShortStations} as the short plan was drawn, {BotWalk.StationsLetGo} places let go at their second short plan), {Rescues} times one of them was set upon and the company turned on it, {Steady} blows that left the company on the enemy it was already fighting ({Friendly} more by a bot of a guild not at war with it, passed over as friendly fire, {Inside} from inside the company itself, {Enemies} bots refused a place for being at war with the leader, {Distant} for standing more than {JoinReach} tiles from it), {Yields} tiles given up to whoever belonged on them, {Buried} turned away from a company that no longer existed, {BotSquad.Released} let go for doing nothing for a company that was doing nothing, {Rebuffs} times one of them was handed back something it had already given up on, {BotSquad.Unowned} charges taken back because the errand holding them had ended; {BotSquad.Blinded} beats stood near enough to fight with no line to the thing ({BotSquad.NearLeader} of them set on the enemy nearest the leader instead), {LeaderGuarded} blows in melee on a leader that turned its company to what struck it, {BotSquad.Refused} refused the blow by the engine and {BotSquad.Unsteadied} were shooters that had moved too recently to fire, {BotSquad.Blindfights} fights given up because nobody could land one at all, {BotFormation.Unanchored} stations answered with standing fast because nothing round the enemy could be walked to ({BotFormation.Uncomponented} candidates passed over for lying in another part of the walkers' graph), {BotSquad.Conjured} spells thrown by the back ranks and {BotSquad.Mended} heals landed by their medics, against {BotSquad.Dry} beats with nothing they could pay for; {BotSquad.Defended} beats a member struck in melee was left on what struck it, {BotSquad.Excused} places not handed to a member running or binding its own wounds, {BotSquad.RoadSpared} looks for a fight a delve's company skipped on its road, {BotSquad.Paced} turns a marching leader was slowed to for a member behind it (beats a member behind was not waited for, by what it held instead of the enlistment: {BotSquad.Unpaced()}); first blows on the company's enemy, by what the leader was doing when the company took it on: {BotSquad.Responsiveness()}; {BotSpoils.Describe()}";
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

        var squad = new BotSquad(++_next, leader);

        _squads.Add(squad);
        Formed++;

        squad.Attach(leader);
        leader.Squad = squad;

        logger.Information("{Name} called a squad together", leader.Self.Name);

        return squad;
    }

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

        if (member.Squad != null || squad.Count >= squad.Ceiling || squad.Map != member.Self.Map)
        {
            return false;
        }

        if (!Reaches(squad, member.Self))
        {
            Distant++;

            return false;
        }

        if (squad.Leader?.Self is Mobile lead && BotRegard.AtWar(lead, member.Self))
        {
            Enemies++;

            return false;
        }

        squad.Attach(member);
        member.Squad = squad;

        return true;
    }

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

        if (squad.Has(attacker))
        {
            Inside++;

            return;
        }

        if (ReferenceEquals(member, squad.Leader) && Guards(squad, member.Self, attacker))
        {
            LeaderGuarded++;
            squad.Engage(attacker, member);

            return;
        }

        var worst = BotThreat.Strongest(member.Self, Reach);
        var pick = worst ?? attacker;

        if (BotQuarry.Shunned(pick))
        {
            Rebuffs++;

            return;
        }

        if (squad.Focus is { Deleted: false, Alive: true } current && !ReferenceEquals(current, pick)
            && member.Self is { } hit && current.Map == hit.Map && hit.InRange(current.Location, Reach)
            && BotThreat.Power(pick) < BotThreat.Power(current) * Switch)
        {
            Steady++;

            return;
        }

        squad.Engage(pick, member);
        Rescues++;
    }

    public static long Rebuffs { get; private set; }

    public static long LeaderGuarded { get; private set; }

    public static int LeaderReach { get; set; } = 2;

    private static bool Guards(BotSquad squad, Mobile lead, Mobile attacker) =>
        lead is { Deleted: false, Alive: true } && attacker.Map == lead.Map && lead.InRange(attacker.Location, LeaderReach)
        && squad.Focus is { Deleted: false, Alive: true } enemy && !ReferenceEquals(enemy, attacker)
        && !ReferenceEquals(enemy.Combatant, lead) && !BotQuarry.Shunned(attacker);

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

            logger.Information("Companies: {Standing}; {Muster}; {Enlist}", Describe(), BotMuster.Describe(), BotEnlister.Describe());

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
                "Needs: {Gear}; {Metal}; {Forge}; {Tinker}; {Thread}; {Arrows}; {Bottles}; {Skillet}; {Stores}; {Filled} things off a pack went straight to somebody's standing order, {Bespoken} trips to a counter were begun because the board wanted something in the pack, {Shed} things were listed on the spot by bots too heavy to walk, {Exposed} trips were begun because a pack was worth more than a bot should be carrying about, {Hoarding} because it held more stones of somebody else's goods than it should, {Stored} lots of {Stowed} things the market would not take went into bank boxes ({Boxless} times the box would not take them either; {Unlotted} things the porter left out of its count for having neither a lot nor room in the box), {Dumped} things nobody would buy were left on the ground by bots that could not walk ({Stranded} of these errands were finished with no counter known at all, {Cornered} were offered to a bot with nothing the market wants so that the dropping could be reached, and {Immovable} of those found nothing to drop either), and {Sent} kills were chosen because the board wanted what the carcass carries",
                BotUpkeep.Describe(),
                BotBullion.Describe(),
                BotSmith.Describe(),
                BotTinkerer.Describe() + "; bows: " + BotBowyer.Describe() + "; weaving: " + BotWeaver.Describe() + $"; {BotTailor.OwnCloth} sewing stints on the weaver's own cloth, {BotProvision.Cut} bandages cut instead of bought",
                BotTailor.Describe(),
                BotFletcher.Describe(),
                BotAlchemist.Describe(),
                BotCook.Describe(),
                BotStores.Describe(),
                BotUnload.Filled,
                BotUnload.Bespoken,
                BotUnload.Shed,
                BotUnload.Exposed,
                BotUnload.Hoarding,
                BotUnload.Stored,
                BotUnload.Stowed,
                BotUnload.Boxless,
                BotUnload.Unlotted,
                BotUnload.Dumped,
                BotUnload.Stranded,
                BotUnload.Cornered,
                BotUnload.Immovable,
                BotQuarry.Sent
            );

        logger.Information("Market: {What}", BotAuction.Describe());

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
