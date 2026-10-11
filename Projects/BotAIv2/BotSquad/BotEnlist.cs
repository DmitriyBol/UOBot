using System;
using Server.Mobiles;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Falling in with a company that is already fighting, rather than starting a fight of your own beside it.
///
/// <para>
/// <b>A company could only ever be joined at the moment it was formed, and after that it was closed.</b>
/// <c>BotBand</c> and <c>BotSweep</c> both sweep up whoever is standing nearby when they call the roll, and
/// neither ever looks again — so a bot that walked into a graveyard thirty seconds later found five of its
/// own people fighting a troll, four of them in a formation with room for a fifth, and had no way at all to
/// go and stand with them. What it did instead was pick its own quarry two tiles away and fight it alone,
/// which is the one thing a company exists to stop.
/// </para>
///
/// <para>
/// <b>It is an offer to the newcomer, not a summons from the company.</b> Written the other way round — the
/// squad reaching out and pulling people in — it would be the squad deciding what somebody else's afternoon
/// is for, and this shard has refused that shape since its first week. The bot weighs falling in against
/// everything else it could do, in the same auction, and a miner with a good vein is entitled to walk past.
/// </para>
/// </summary>
public sealed class BotEnlist : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotEnlist));

    public const string Trade = "enlist";

    public static double Prior { get; set; } = 24.0;

    public static double WorkMinutes { get; set; } = 3.0;

    public static int Reach { get; set; } = 32;

    public static double Left { get; set; } = 0.4;

    public static long Ending { get; private set; }

    public static void Passed() => Ending++;

    public static long Lonely { get; private set; }

    public static void Alone() => Lonely++;

    public static long Hostile { get; private set; }

    public static double Odds { get; set; } = 3.0;

    public static long Overmanned { get; private set; }

    public static void Manned() => Overmanned++;

    public static void Foe() => Hostile++;

    public static long Unlawful { get; private set; }

    public static void Crime() => Unlawful++;

    public static long Remote { get; private set; }

    public static void Far() => Remote++;

    public static long Doomed { get; private set; }

    public static void Hopeless() => Doomed++;

    public static void Forget()
    {
        Ending = 0;
        Lonely = 0;
        Hostile = 0;
        Overmanned = 0;
        Unlawful = 0;
        Remote = 0;
        Doomed = 0;
        Trailing = 0;
        Struck = 0;
        Unserved = 0;
        MoonFollowed = 0;
        BindKept = 0;
    }

    private readonly BotSquad _squad;

    private readonly Map _map;

    private readonly Point3D _where;

    private bool _joined;

    public BotEnlist(BotSquad squad, Map map, Point3D where) : this(squad, map, where, false)
    {
    }

    public BotEnlist(BotSquad squad, Map map, Point3D where, bool levied)
    {
        _squad = squad;
        _map = map;
        _where = where;
        _pressedIn = levied;
    }

    public static long Released { get; private set; }

    public override string Kind => Trade;

    public override bool Summons => true;

    public override bool Still => true;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override bool Alongside => true;

    public override bool Resumes => true;

    public override bool Committed => true;

    public override bool Pressing(IBotWilful bot) => Standing();

    public override string Stage =>
        _joined || _pressedIn
            ? $"with company {_squad?.Id}, {_squad?.Count} of them"
            : $"falling in with company {_squad?.Id}, {_squad?.Count} of them";

    private bool _pressedIn;

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (bot is not IBotSquadMember member)
        {
            return BotDoing.Failed("not the sort of thing that joins companies");
        }

        if (!Standing())
        {
            if (_joined || _pressedIn)
            {
                _letGo = !_served;

                if (_letGo)
                {
                    Unserved++;
                }

                return BotDoing.Done(_served ? "the company broke up" : $"company {_squad?.Id} broke up before it stood with it");
            }

            return BotDoing.Failed("the company was gone before it got there");
        }

        if (member.Squad != null)
        {
            if (member.Squad == _squad)
            {
                _pressedIn = true;

                var doing = Holding(member);

                if (doing.Kind == BotDoingKind.Work && !_served)
                {
                    var now = Core.TickCount;

                    if (_workTick != 0)
                    {
                        _with += Math.Min(now - _workTick, 1000);
                    }

                    _workTick = now;
                    _served = _inFight || _with >= ServeMs;
                }

                return doing;
            }

            return BotDoing.Done("fell in with another company on the way");
        }

        if (_pressedIn || _joined)
        {
            Released++;
            _letGo = !_served;

            if (_letGo)
            {
                Unserved++;

                return BotDoing.Done($"company {_squad.Id} let it go before it stood with it");
            }

            return BotDoing.Done($"company {_squad.Id} went on without it");
        }

        var anchor = _squad.Anchor;

        if (!body.InRange(anchor, BotSquad.PressReach))
        {
            return BotDoing.Walk(_map, anchor, BotArrival.Within(BotSquad.PressReach - 1), $"falling in with company {_squad.Id}");
        }

        if (!BotSquads.Join(_squad, member))
        {
            return BotDoing.Failed($"company {_squad.Id} had no room by the time it arrived");
        }

        _joined = true;

        logger.Information("{Name} fell in with company {Id}, now {Count} strong", body.Name, _squad.Id, _squad.Count);

        return BotDoing.Work($"fell in with company {_squad.Id}");
    }

    private BotDoing Holding(IBotSquadMember member)
    {
        if (member.Squad == null)
        {
            return BotDoing.Done("the company broke up");
        }

        if (BotPassage.Steer(member, _squad, out var steered))
        {
            return steered;
        }

        var self = member.Self;
        var anchor = _squad.Anchor;

        if (_squad.Mustering && _squad.Focus is { Deleted: false, Alive: true } mustered && self is { Deleted: false, Alive: true }
            && anchor != Point3D.Zero)
        {
            _closeTries = 0;
            _stillUntil = 0;
            _inFight = true;

            if (self.InRange(anchor, BotSquad.FieldReach) || member.Journey is { Active: true })
            {
                return BotDoing.Work($"forming up with company {_squad.Id} against {mustered.Name}");
            }

            if (!ReferenceEquals(_toFight, mustered))
            {
                _toFight = mustered;
                ToTheFight++;
            }

            return BotDoing.Walk(_map, anchor, BotArrival.Within(CloseUp), $"to company {_squad.Id}'s muster against {mustered.Name}");
        }

        if (_squad.Stance == BotSquadStance.Fighting && _squad.Focus is { Deleted: false, Alive: true } foe
            && self is { Deleted: false, Alive: true } && foe.Map == self.Map)
        {
            _closeTries = 0;
            _stillUntil = 0;

            if (_squad.Binding(self) || BotMend.Winding(self))
            {
                _inFight = true;
                BindKept++;

                return BotDoing.Work($"binding one of company {_squad.Id}'s own in its fight with {foe.Name}");
            }

            var going = member.Journey is { Active: true } road && road.Target != Point3D.Zero
                        && Math.Max(Math.Abs(road.Target.X - foe.X), Math.Abs(road.Target.Y - foe.Y)) <= BotSquad.PressReach;

            if (going || self.InRange(foe.Location, BotSquad.PressReach))
            {
                _inFight = true;

                return BotDoing.Work($"in company {_squad.Id}'s fight with {foe.Name}");
            }

            if (foe is BaseCreature && BotWalk.Unreached(self, foe))
            {
                if (_squad.Unreachable(foe))
                {
                    return BotDoing.Work($"company {_squad.Id} let {foe.Name} go: no road any of us drew reached it");
                }

                UnreachedFight++;

                return BotDoing.Failed($"no road reached company {_squad.Id}'s fight with {foe.Name} at {foe.Location}");
            }

            if (!ReferenceEquals(_toFight, foe))
            {
                _toFight = foe;
                ToTheFight++;
            }

            return BotDoing.Walk(_map, foe, BotArrival.Within(BotSquad.PressReach - 2), $"to company {_squad.Id}'s fight with {foe.Name}");
        }

        if (self is { Deleted: false, Alive: true } && anchor != Point3D.Zero && self.InRange(anchor, CloseUp))
        {
            _closeTries = 0;
            _closeAt = Point3D.Zero;
        }

        if (self is { Deleted: false, Alive: true } && anchor != Point3D.Zero && !self.InRange(anchor, CloseUp))
        {
            if (_squad.Leader?.Self is { Deleted: false, Alive: true } across
                && Math.Max(Math.Abs(self.X - across.X), Math.Abs(self.Y - across.Y)) > BotDelve.GateApart
                && BotGates.MoongateAt(self.Location, across.Location) is { } moon && BotGates.Travel(self, moon))
            {
                MoonFollowed++;
                member.Journey?.Finish();

                return BotDoing.Work($"through the moongate after company {_squad.Id}");
            }

            if (_squad.Leader?.Self is { Deleted: false, Alive: true } beyond
                && Math.Max(Math.Abs(self.X - beyond.X), Math.Abs(self.Y - beyond.Y)) > BotDelve.GateApart
                && BotGates.MoongateNear(self.Location, beyond.Location, MoonReach) is { } near)
            {
                return BotDoing.Walk(_map, near.From, BotArrival.Exactly, $"to the moongate after company {_squad.Id}");
            }

            var journey = member.Journey;
            var bound = journey is { Moving: true } && journey.Target != Point3D.Zero
                && Math.Max(Math.Abs(journey.Target.X - anchor.X), Math.Abs(journey.Target.Y - anchor.Y)) <= CloseUp;

            if (!bound)
            {
                var off = Math.Max(Math.Abs(self.X - anchor.X), Math.Abs(self.Y - anchor.Y));

                if (Core.TickCount - _stillUntil < 0)
                {
                    return BotDoing.Work($"with company {_squad.Id}, {off} tiles off and no nearer to be had");
                }

                if (member is BotMobile { LastWalk: BotWalkResult.GaveUp } && off <= HoldNear)
                {
                    GaveUpNear++;
                    HeldNear++;
                    _closeTries = 0;
                    _stillUntil = Core.TickCount + HoldStillMs;

                    return BotDoing.Work($"with company {_squad.Id}, {off} tiles off and no nearer to be had");
                }

                var leadWalking = _squad.Leader?.Journey is { Moving: true };

                if (leadWalking)
                {
                    Trailing++;
                }
                else if (Core.TickCount - _closeTick >= CloseTryMs || _closeTries == 0)
                {
                    _closeTick = Core.TickCount;
                    _closeTries++;
                }

                if (!leadWalking && _closeTries >= CloseTries)
                {
                    _closeTries = 0;

                    if (off <= HoldNear)
                    {
                        HeldNear++;
                        _stillUntil = Core.TickCount + HoldStillMs;

                        return BotDoing.Work($"with company {_squad.Id}, {off} tiles off and no nearer to be had");
                    }

                    Unclosed++;
                    BotSquads.Leave(member);

                    return BotDoing.Failed($"could not close on company {_squad.Id}: {off} tiles off after {CloseTries} walks that came no nearer");
                }

                ClosedUp++;

                var lead = _squad.Leader?.Self;

                var arrive = CloseUp > 2 ? CloseUp - 2 : 1;

                if (lead is { Deleted: false, Alive: true } && lead.Map == _map && !ReferenceEquals(lead, self))
                {
                    var stale = _closeAt != Point3D.Zero && self.InRange(_closeAt, arrive);

                    if (_closeAt == Point3D.Zero || !lead.InRange(_closeAt, CloseUp) || stale)
                    {
                        if (stale && lead.InRange(_closeAt, CloseUp))
                        {
                            Reaimed++;
                        }

                        _closeAt = lead.Location;
                        Repointed++;
                    }

                    return BotDoing.Walk(_map, _closeAt, BotArrival.Within(arrive), $"closing up on {lead.Name}, company {_squad.Id}");
                }

                return BotDoing.Walk(_map, anchor, BotArrival.Within(arrive), $"closing up on company {_squad.Id}");
            }
        }

        return BotDoing.Work($"with company {_squad.Id}, {_squad.Count} of us");
    }

    public static long ClosedUp { get; private set; }

    public static long ToTheFight { get; private set; }

    public static long BindKept { get; private set; }

    private Mobile _toFight;

    public static int CloseTries { get; set; } = 8;

    public static int HoldNear { get; set; } = 24;

    public static int HoldStillMs { get; set; } = 30000;

    public static long HeldNear { get; private set; }

    public static long GaveUpNear { get; private set; }

    private Point3D _closeAt;

    public static long Repointed { get; private set; }

    public static long Reaimed { get; private set; }

    public static long Unclosed { get; private set; }

    public static long Trailing { get; private set; }

    private long _closeTick;

    public static int CloseTryMs { get; set; } = 5000;

    private int _closeTries;

    private long _stillUntil;

    public static int CloseUp { get; set; } = 6;

    public override void Drop(IBotWilful bot)
    {
        if (bot is IBotSquadMember member && member.Squad != null && ReferenceEquals(member.Squad, _squad)
            && !ReferenceEquals(_squad.Leader, member) && !_squad.Disbanded)
        {
            BotSquads.Leave(member);
            Struck++;
        }
    }

    public static int MoonReach { get; set; } = 120;

    public static long MoonFollowed { get; private set; }

    public static long UnreachedFight { get; private set; }

    public static long Struck { get; private set; }

    public static long Unserved { get; private set; }

    public static int ServeMs { get; set; } = 15000;

    public override bool KeptOut => _letGo;

    private bool _served;

    private bool _inFight;

    private bool _letGo;

    private long _workTick;

    private long _with;

    private bool Standing() =>
        _squad is { Count: >= 2, Disbanded: false } && _squad.Map == _map && _squad.Leader?.Self is { Deleted: false, Alive: true };
}

/// <summary>
/// Offers a free bot a place in whatever company is fighting within sight of it.
///
/// <para>
/// <b>Only companies that are actually in a fight, and only ones with room.</b> A company marching or
/// scouting has no need of a fifth body and would only be slowed by one arriving from behind; a company that
/// is <em>Fighting</em> is short-handed by definition, because that is what being in a fight means. And a
/// full one is not offered at all, so nobody ever walks forty tiles to be turned away.
/// </para>
/// </summary>
public sealed class BotEnlister : IBotProposer
{
    public string Name => "Enlist";

    public BotStanding Rung => BotStanding.Free;

    public static long Asked { get; private set; }

    public static long Held { get; private set; }

    public static long Unfit { get; private set; }

    public static long None { get; private set; }

    public static long Walled { get; private set; }

    public static long Sent { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive || !BotSquads.Running)
        {
            return null;
        }

        Asked++;

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

        if (BotUnderworld.Member(body) && !BotFence.Is(body))
        {
            Unfit++;

            return null;
        }

        if (BotTrading.Passes(body))
        {
            return null;
        }

        var squad = Nearest(body, map);

        if (squad == null)
        {
            None++;

            return null;
        }

        Sent++;

        return new BotEnlist(squad, map, squad.Anchor);
    }

    private static BotSquad Nearest(Mobile body, Map map)
    {
        var squads = BotSquads.All;

        BotSquad best = null;
        var bestAway = double.MaxValue;

        for (var i = 0; i < squads.Count; i++)
        {
            var squad = squads[i];

            if (squad.Stance != BotSquadStance.Fighting || squad.Count >= squad.Ceiling || squad.Map != map)
            {
                continue;
            }

            if (squad.Count < 2)
            {
                BotEnlist.Alone();

                continue;
            }

            var anchor = squad.Anchor;

            if (anchor == Point3D.Zero || !Utility.InRange(body.Location, anchor, BotEnlist.Reach))
            {
                continue;
            }

            if (squad.Leader?.Self is Mobile lead && BotRegard.Hostile(body, lead))
            {
                BotEnlist.Foe();

                continue;
            }

            if (squad.Focus is { Deleted: false } fighting && body.IsHarmfulCriminal(fighting))
            {
                BotEnlist.Crime();

                continue;
            }

            if (!BotSquads.Reaches(squad, body))
            {
                BotEnlist.Far();

                continue;
            }

            if (squad.Count >= 3 && squad.Focus is { Deleted: false, HitsMax: > 0 } focus &&
                focus.Hits < focus.HitsMax * BotEnlist.Left)
            {
                BotEnlist.Passed();

                continue;
            }

            if (squad.Count >= BotSquad.Usual && squad.Focus is { Deleted: false } against
                && Strength(squad) >= BotThreat.Power(against) * BotEnlist.Odds)
            {
                BotEnlist.Manned();

                continue;
            }

            if (squad.Focus is { Deleted: false } doomed && !BotSquad.Caster(doomed)
                && (squad.Beyond(doomed, BotThreat.Power(body), BotSquad.WithdrawOdds) || BotSquad.Shuns(doomed, squad.Able() + BotThreat.Power(body))))
            {
                BotEnlist.Hopeless();

                continue;
            }

            if (squad.Focus is BaseCreature unreached && BotWalk.Unreached(body, unreached))
            {
                Walled++;

                continue;
            }

            if (BotReach.Ask(map, body.Location, anchor, BotArrival.Within(BotEnlist.Reach)) == BotReachVerdict.Sealed)
            {
                Walled++;

                continue;
            }

            var away = body.GetDistanceToSqrt(anchor);

            if (away < bestAway)
            {
                bestAway = away;
                best = squad;
            }
        }

        return best;
    }

    private static double Strength(BotSquad squad)
    {
        var members = squad.Members;
        var sum = 0.0;

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i] is IBotAlly { AbleToFight: true } && members[i].Self is { Deleted: false, Alive: true } self)
            {
                sum += BotThreat.Power(self);
            }
        }

        return sum;
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been offered a place in a company"
            : $"{Asked} asked: {Sent} sent to fall in, {Held} were already in a company, {Unfit} were too hurt to be any help, {None} had no company fighting within {BotEnlist.Reach} tiles with room in it, {Walled} passed one over for having no way through to it, {BotEnlist.Ending} passed one over whose fight was nearly won, {BotEnlist.Lonely} passed over a company of one, {BotEnlist.Hostile} passed over a hostile guild's, {BotEnlist.Overmanned} passed over one already {BotEnlist.Odds:F0} times its enemy's strength, {BotEnlist.Unlawful} passed over one whose fight would have made it a criminal, {BotEnlist.Remote} passed over one whose leader stood more than {BotSquads.JoinReach} tiles off, {BotEnlist.Doomed} passed over one fighting something reckoned past ×{BotSquad.WithdrawOdds:F1} it and this bot together; in the companies, {BotEnlist.ClosedUp} walks to close up ({BotEnlist.Repointed} points set afresh as the leader moved on, {BotEnlist.Reaimed} of them because the member stood at its point still more than {BotEnlist.CloseUp} tiles from the leader), {BotEnlist.ToTheFight} walks to the company's fight rather than to its leader ({BotEnlist.BindKept} beats a member binding one of ours was left to it instead), {BotEnlist.HeldNear} stood near their company for want of a nearer tile ({BotEnlist.GaveUpNear} at once, the walker having given the close-up up) and {BotEnlist.Unclosed} let go for want of any ({BotEnlist.Trailing} close-up beats behind a leader that was itself walking, no try counted), {BotEnlist.Released} levied members released when their company went on, {BotEnlist.Unserved} levied members let go or whose company broke up before they had stood with it {BotEnlist.ServeMs / 1000}s (kept out of the share), {BotEnlist.Struck} struck off their company's roster when their enlistment ended, {BotEnlist.UnreachedFight} let go for finding no road to their company's fight ({BotSquad.UnreachedLet} fights let go by a company none of whose members could reach)";

    public static void Forget()
    {
        Asked = 0;
        Walled = 0;
        Held = 0;
        Unfit = 0;
        None = 0;
        Sent = 0;
        BotEnlist.Forget();
    }
}
