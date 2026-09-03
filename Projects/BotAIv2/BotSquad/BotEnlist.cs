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

    /// <summary>The ledger's key.</summary>
    public const string Trade = "enlist";

    /// <summary>
    /// What falling in is reckoned at per minute before experience corrects it.
    ///
    /// <para>
    /// A little under a muster's, because it is the same work with the walk already half done by somebody
    /// else — the fight is found, the company is formed, and what is being offered is a place in it. It has
    /// to be worth more than prowling an empty field or nobody would ever come, and less than a hunt already
    /// in hand or bots would abandon fights to join fights.
    /// </para>
    /// </summary>
    public static double Prior { get; set; } = 70.0;

    public static double WorkMinutes { get; set; } = 3.0;

    /// <summary>How far a bot will go to fall in with a company.</summary>
    public static int Reach { get; set; } = 40;

    private readonly BotSquad _squad;

    private readonly Map _map;

    private readonly Point3D _where;

    private bool _joined;

    public BotEnlist(BotSquad squad, Map map, Point3D where)
    {
        _squad = squad;
        _map = map;
        _where = where;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override bool Alongside => true;

    /// <summary>
    /// Urgent, and for the same reason a rescue is: a fight that is happening now will not be happening in
    /// a minute, and a company that is one body short is short of it at this moment or not at all.
    /// </summary>
    public override bool Pressing(IBotWilful bot) => Standing();

    public override string Stage =>
        _joined
            ? $"fell in with company {_squad?.Id}"
            : $"falling in with company {_squad?.Id}, {_squad?.Count} of them";

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
            return _joined
                ? BotDoing.Done("the company broke up")
                : BotDoing.Failed("the company was gone before it got there");
        }

        // Already in one, possibly this one. Either way there is nothing further to do here, and the fighting
        // itself is the squad's business from now on.
        if (member.Squad != null)
        {
            return member.Squad == _squad
                ? Holding(member)
                : BotDoing.Done("fell in with another company on the way");
        }

        var anchor = _squad.Anchor;

        if (!body.InRange(anchor, BotSquad.PressReach))
        {
            return BotDoing.Walk(_map, anchor, BotArrival.Within(BotSquad.PressReach - 1), $"falling in with company {_squad.Id}");
        }

        if (!BotSquads.Join(_squad, member))
        {
            // Filled up on the way, or moved to another facet. An honest ending: somebody else got there.
            return BotDoing.Failed($"company {_squad.Id} had no room by the time it arrived");
        }

        _joined = true;

        logger.Information("{Name} fell in with company {Id}, now {Count} strong", body.Name, _squad.Id, _squad.Count);

        return BotDoing.Work($"fell in with company {_squad.Id}");
    }

    /// <summary>
    /// In the company and staying in it.
    ///
    /// <para>
    /// <b>Work, and the fence around it is the squad's own life rather than a clock here.</b> A member is
    /// Bound, so its own auction is skipped and this undertaking is what stands between it and having
    /// nothing at all; it must therefore last exactly as long as the company does and not one beat longer.
    /// The squad disbands after eight quiet seconds, which ends this the same second.
    /// </para>
    /// </summary>
    private BotDoing Holding(IBotSquadMember member) =>
        member.Squad == null
            ? BotDoing.Done("the company broke up")
            : BotDoing.Work($"with company {_squad.Id}, {_squad.Count} of us");

    public override void Drop(IBotWilful bot)
    {
    }

    /// <summary>Whether the company is still a company worth walking to.</summary>
    private bool Standing() =>
        _squad is { Count: >= 2 } && _squad.Map == _map && _squad.Leader?.Self is { Deleted: false, Alive: true };
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

    /// <summary>Companies passed over because there is no way through to where they are fighting.</summary>
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

        var squad = Nearest(body, map);

        if (squad == null)
        {
            None++;

            return null;
        }

        Sent++;

        return new BotEnlist(squad, map, squad.Anchor);
    }

    /// <summary>The nearest company that is in a fight, has room, and is on this bot's own facet.</summary>
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

            var anchor = squad.Anchor;

            if (anchor == Point3D.Zero || !Utility.InRange(body.Location, anchor, BotEnlist.Reach))
            {
                continue;
            }

            // <b>Near is not the same as reachable, and a company fighting on a roof is both.</b> A fight
            // anchors wherever the fight is; this picked the nearest one by the crow's flight, so a company
            // dealing with something one storey up was the best offer going for everybody underneath it. The
            // reach ledger already knew — 33 enlist errands ended "no way through to (1361, 1483, 30)" in
            // ninety minutes on 03.09.2026, one tile from a pocket of 63 filed at (1362, 1482, 30) — and it
            // was being asked by the walker after the errand had been taken instead of by the chooser before.
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

    public static string Describe() =>
        Asked == 0
            ? "nobody has been offered a place in a company"
            : $"{Asked} asked: {Sent} sent to fall in, {Held} were already in a company, {Unfit} were too hurt to be any help, {None} had no company fighting within {BotEnlist.Reach} tiles with room in it, {Walled} passed one over for having no way through to it";

    public static void Forget()
    {
        Asked = 0;
        Walled = 0;
        Held = 0;
        Unfit = 0;
        None = 0;
        Sent = 0;
    }
}
