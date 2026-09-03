using System;
using System.Collections.Generic;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// Where each member of a squad ought to be standing, worked out rather than assigned.
///
/// <para>
/// <b>Nobody is told anything.</b> Every member computes every station from the same three facts — the
/// roster, the anchor and the threat axis — in the same order, and therefore arrives at the same answer.
/// This is the one genuinely good idea the first version had about collective behaviour, and it came out of
/// spreading bots over a hunting ground: order by serial, cut the ground into a grid, take the cell at your
/// own index. No messages, so no desynchronisation, no orphaned assignments, and two bots never pick the
/// same patch. A shared mind does not need a shared mailbox; it needs shared arithmetic.
/// </para>
///
/// <para>
/// <b>The shape is not fixed — the order is.</b> A formation can be any shape at all, and trying to specify
/// one would be specifying the wrong thing. What must hold is the ordering along the line to the threat:
/// blades in front, bows behind them, casters and healers behind those, everybody else at the back. That is
/// a single number per role, and every arrangement that satisfies it is acceptable.
/// </para>
///
/// <para>
/// <b>Built from the leader, forward of the leader.</b> If the leader is an archer, the melee ring is still
/// in front — in front of <em>him</em>. The anchor is whoever the squad is organised around, not whoever
/// happens to be closest to the enemy.
/// </para>
/// </summary>
public static class BotFormation
{
    /// <summary>
    /// Where each role stands, measured along the line to the threat. Positive is towards it.
    ///
    /// The spread is deliberately shallow — four tiles from the front rank to the back — because the squad
    /// also has to hold together within about five tiles. A deeper formation looks better on paper and
    /// produces a rear rank that is out of earshot of its own front.
    /// </summary>
    public static int RingFor(BotRole role) =>
        role switch
        {
            BotRole.Melee => 2,
            BotRole.Ranged => 0,
            BotRole.Caster => -1,
            BotRole.Medic => -1,
            _ => -2
        };

    /// <summary>
    /// Tiles between neighbours in the same rank. Two rather than one so that a file has room to walk past
    /// its neighbour instead of asking it to move.
    /// </summary>
    public const int FileSpacing = 2;

    /// <summary>
    /// How far a station may be from the anchor at all. The cohesion rule from the other side: a station
    /// nobody can hold within this distance is not a station, it is a bot wandering off.
    /// </summary>
    public const int MaxSpread = 5;

    /// <summary>
    /// Where each role stands once there is something to fight, measured in tiles from the creature itself.
    ///
    /// <para>
    /// <b>A rank is a distance from the enemy, and it was being measured from us.</b> Every station came off
    /// the anchor — the member in contact, or the leader while nobody had been hit — with the blades two
    /// tiles in front of it. So a company called by a healer stationed its blades two tiles ahead of the
    /// healer and twelve tiles short of the wraith, pressed a combatant none of them could touch, and broke
    /// off ninety seconds later with the target's health exactly where it started. The evening of 23.08.2026
    /// has it fifty-six times against nineteen kills, and the same wraith re-engaged every two minutes all
    /// night. Nothing in the squad was broken: it was laid out from the wrong origin, and the enemy is the
    /// only origin a fight has.
    /// </para>
    ///
    /// <para>
    /// Melee is <see cref="Contact"/> — one tile, meaning the eight tiles that touch the thing, one to a
    /// blade. The rest keep the ordering they always had: bows behind the blades, spells and bandages behind
    /// the bows, everybody else out of it.
    /// </para>
    /// </summary>
    public static int PressRingFor(BotRole role) =>
        role switch
        {
            BotRole.Melee => Contact,
            BotRole.Ranged => 5,
            BotRole.Caster => 7,
            BotRole.Medic => 7,
            _ => 9
        };

    /// <summary>A blade's own reach. One tile — the ring of eight that touches a creature.</summary>
    public const int Contact = 1;

    /// <summary>
    /// Where a member belongs in a fight, decided by what is in its hands rather than by what its class is
    /// called.
    ///
    /// <para>
    /// <b>A miner with a sword is a sword.</b> Both producing classes are issued a real melee weapon at birth
    /// — <c>BotArsenal.Melee(40.0)</c>, see BotCrafter and BotGatherer — and then the formation read their
    /// class name, filed them under Producer and stationed them <em>nine tiles</em> from the fight. So a
    /// company of five with two gatherers in it brought three blades to the ring and left two armed bots
    /// standing in a field watching. They can fight; they were never asked to.
    /// </para>
    ///
    /// <para>
    /// Only the producing roles are re-read, and only when something is actually held: an empty-handed
    /// gatherer stays out of it, which is the sensible half of what the old rule was trying to say. The reach
    /// of what it holds decides which rank — a bow would go to the archers' rank — because the ring a bot
    /// belongs in is a fact about its reach and never about its job.
    /// </para>
    ///
    /// <para>
    /// <b>A pickaxe counts, and that was decided rather than overlooked.</b> A digging tool was going to be
    /// excluded here — it is a tool, and a miner holding one is working, not soldiering. But a pickaxe and a
    /// hatchet are <c>BaseAxe</c> to the engine: they swing on the same timer as any axe and they do real
    /// damage. Patrick's ruling on being told that was to leave it alone. So the rule stays the simple one,
    /// and it is the honest one — if the thing in its hands can kill, the bot belongs where things are being
    /// killed.
    /// </para>
    /// </summary>
    public static BotRole RoleOf(IBotSquadMember member)
    {
        var role = member?.Class?.Role ?? BotRole.Melee;

        if (role != BotRole.Producer)
        {
            return role;
        }

        // Fists are a weapon to the engine and are not one here — see BotArms.Armed for the same test.
        var held = member.Self?.Weapon;

        if (held is null or Fists)
        {
            return role;
        }

        return held.MaxRange > Contact ? BotRole.Ranged : BotRole.Melee;
    }

    /// <summary>The eight tiles round a thing, clockwise from north.</summary>
    private static readonly (int X, int Y)[] Compass =
    [
        (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)
    ];

    /// <summary>
    /// The order the tiles round the enemy are tried in, as turns off the line our side is standing on:
    /// straight at it first, then out to either hand, and round the back last.
    /// </summary>
    private static readonly int[] Fan = [0, 1, -1, 2, -2, 3, -3, 4];

    /// <summary>Scratch for the share-out. One game thread, so one list is enough.</summary>
    private static readonly List<IBotSquadMember> _peers = [];

    /// <summary>
    /// The station this member should be holding.
    ///
    /// <para>
    /// Two checks before the answer is handed back, and both come from the first version's bill. The point
    /// must be somewhere a body can stand — worked out from the ground near the anchor's own height, never
    /// from a wide vertical window, because a wide window in a built-up world hands back a roof. And it must
    /// be somewhere this bot can actually walk to, checked with the same search that will later walk it: the
    /// first version's rally points were not checked, and landed inside buildings and behind the enemy.
    /// </para>
    /// </summary>
    public static Point3D StationFor(BotSquad squad, IBotSquadMember member)
    {
        if (squad == null || member?.Self == null)
        {
            return Point3D.Zero;
        }

        var map = squad.Map;

        if (map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        var anchor = squad.Anchor;
        var role = RoleOf(member);

        // Everyone of the same role, in serial order. Deterministic, so every member of the squad works out
        // the same file for the same bot without anybody being told.
        _peers.Clear();

        var members = squad.Members;

        for (var i = 0; i < members.Count; i++)
        {
            if (RoleOf(members[i]) == role)
            {
                _peers.Add(members[i]);
            }
        }

        _peers.Sort(static (a, b) => a.Self.Serial.Value.CompareTo(b.Self.Serial.Value));

        var file = _peers.IndexOf(member);

        if (file < 0)
        {
            return Point3D.Zero;
        }

        // A fight is laid out round the thing being fought. See PressRingFor for what that cost to learn.
        if (squad.Stance == BotSquadStance.Fighting && squad.Focus is { Deleted: false, Alive: true })
        {
            return PressStation(map, squad, member, role, file);
        }

        // 0, +1, -1, +2, -2 ... so the rank grows outwards from the axis rather than off to one side.
        var lateral = (file + 1) / 2 * (file % 2 == 0 ? 1 : -1) * FileSpacing;
        var ring = RingFor(role);

        var (fx, fy) = squad.Axis;

        // Ninety degrees off the axis. Integer arithmetic on a unit offset, so no trigonometry and no drift.
        var (rx, ry) = (-fy, fx);

        var x = anchor.X + fx * ring + rx * lateral;
        var y = anchor.Y + fy * ring + ry * lateral;

        return Reachable(map, x, y, anchor, member);
    }

    /// <summary>
    /// The station this member holds while the squad is fighting: a place at the creature, not a place in a
    /// line drawn from us.
    ///
    /// <para>
    /// <b>The blades get a tile each and no two of them get the same one.</b> A rank with a file spacing puts
    /// the second blade two tiles from the thing it is supposed to be hitting — which is one tile past the
    /// only range it has — so the man in the middle fought and the rest watched. Eight tiles touch a
    /// creature; the fan hands them out in a fixed order that starts on whichever side the squad is coming
    /// from, and the file comes off the serial ordering every member computes identically, so nobody is told
    /// anything and nobody is sent where somebody else is already going. It is also the answer to standing in
    /// a heap round a mob: a heap is what you get when everybody is sent to one tile.
    /// </para>
    ///
    /// <para>
    /// <b>No reachability search here, unlike a march, and that is the point of the difference.</b> Marching
    /// to an unreachable station is a bot standing in a field; walking at an unreachable enemy is a bot
    /// getting as close to it as the world permits, which is exactly what a partial path delivers and exactly
    /// what is wanted. It also keeps a fight from costing eight A* searches a member a beat.
    /// </para>
    /// </summary>
    private static Point3D PressStation(Map map, BotSquad squad, IBotSquadMember member, BotRole role, int file)
    {
        var at = squad.Focus.Location;
        var (ax, ay) = squad.Axis;

        // Rotated as the squad keeps failing to take up its stations, so a blade whose tile is behind a fence
        // tries the next one round rather than the same one for ever.
        var turn = file + squad.Attempt;

        // The compass line our side of the fight is on. Both ranks are laid out from it: the blades fan
        // around it onto the eight tiles that touch the creature, and the shooters go a quarter circle off it
        // so that their line to the target is not through the blades.
        var face = Bearing(ax, ay);

        if (role == BotRole.Melee)
        {
            for (var i = 0; i < Compass.Length; i++)
            {
                var (dx, dy) = Compass[((face + Fan[Math.Abs(turn + i) % Fan.Length]) % 8 + 8) % 8];

                if (Stand(map, at.X + dx, at.Y + dy, at, out var spot))
                {
                    return spot;
                }
            }

            return Point3D.Zero;
        }

        var ring = PressRingFor(role);

        // <b>Off to the flank, not behind the blades, and standing behind them was the whole trouble.</b> A
        // rank measured straight back down the line our side came in on puts every bow and every spell on
        // exactly the axis the melee ring is standing on — so the shot's path to the creature runs through
        // our own front rank, which is the one place an arrow must not go. Turned a quarter circle, the same
        // distance from the same creature becomes a clear line: the blades are in front of the thing and the
        // shooters are beside it. Alternating hands by file spreads them to both sides rather than stacking
        // one wing, and it costs nothing — the ring distance is unchanged, so nobody is further from the
        // fight than their weapon wants to be.
        var hand = turn % 2 == 0 ? 2 : -2;
        var (fx, fy) = Compass[((face + hand) % 8 + 8) % 8];

        // Files beyond the first pair fan further round rather than piling onto the two flanks.
        var wider = turn / 2;
        var (wx, wy) = Compass[((face + hand + (hand > 0 ? wider : -wider)) % 8 + 8) % 8];

        // Pulled in towards the fight a tile at a time when the ground behind will not hold anybody — never
        // pushed further out, because further out is out of the fight.
        for (var back = 0; ring - back >= 2; back++)
        {
            var r = ring - back;

            if (Stand(map, at.X + wx * r, at.Y + wy * r, at, out var spot))
            {
                return spot;
            }

            // The wider fan may be facing a wall. The plain flank is the fallback before giving ground.
            if (Stand(map, at.X + fx * r, at.Y + fy * r, at, out spot))
            {
                return spot;
            }
        }

        return Point3D.Zero;
    }

    /// <summary>Whether a body fits on this tile, at the height of whatever the squad is standing round.</summary>
    private static bool Stand(Map map, int x, int y, Point3D near, out Point3D spot)
    {
        if (BotStep.Ground(map, x, y, near.Z, BotStep.StandingReach, out var z))
        {
            spot = new Point3D(x, y, z);

            return true;
        }

        spot = Point3D.Zero;

        return false;
    }

    /// <summary>Which of the eight compass lines a unit offset is, as an index into <see cref="Compass"/>.</summary>
    private static int Bearing(int x, int y)
    {
        for (var i = 0; i < Compass.Length; i++)
        {
            if (Compass[i].X == x && Compass[i].Y == y)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Turns a proposed tile into one a bot can stand on and reach, walking it in towards the anchor if it
    /// cannot. Returns the anchor itself as the last resort — standing on top of the leader is untidy and
    /// better than standing in a wall.
    /// </summary>
    internal static Point3D Reachable(Map map, int x, int y, Point3D anchor, IBotSquadMember member)
    {
        var steps = Math.Max(Math.Abs(x - anchor.X), Math.Abs(y - anchor.Y));

        for (var back = 0; back <= steps; back++)
        {
            // Walk the candidate in towards the anchor a tile at a time.
            var cx = x + Math.Sign(anchor.X - x) * back;
            var cy = y + Math.Sign(anchor.Y - y) * back;

            if (!BotStep.Ground(map, cx, cy, anchor.Z, BotStep.StandingReach, out var z))
            {
                continue;
            }

            var candidate = new Point3D(cx, cy, z);

            // <b>What the shard has already proved, asked before a search is paid for.</b> A station on a
            // crypt roof is a station nobody can walk to, and the formation was handing one out every beat:
            // the member dropped it, the next beat computed the same tile, and the company stood there. On
            // 03.09.2026 that read in the log as "Alden 2 has dropped (1370, 1467, 30) because it is shut in
            // and this bot is outside it (station)" while the Baron's party sat still after a kill — three
            // roofs in that cluster had been filed as pockets an hour earlier.
            //
            // Free, and it does not disagree with the search below: a pocket is only filed when a search
            // walked ground to its edges. It simply knows sooner, and it goes on knowing after the tile that
            // was reachable when the station was chosen has been proved otherwise.
            // <b>Asked at the width the ledger actually files at, not at the tile.</b> With Exactly the sweep
            // is nought cells wide, so a station two tiles from a filed roof came back Unknown here and
            // Sealed to the walker a second later — the same question, two tolerances, two answers, and the
            // company standing between them. Within(2) is BotReach's own MaxSweep.
            if (BotReach.Ask(map, member.Self.Location, candidate, BotArrival.Within(2)) == BotReachVerdict.Sealed)
            {
                continue;
            }

            if (BotPath.CanReach(map, member.Self.Location, candidate, BotArrival.Exactly))
            {
                return candidate;
            }
        }

        return anchor;
    }

    /// <summary>
    /// Whether the asker has a better claim to a tile than the holder does.
    ///
    /// <para>
    /// This is the whole of the chokepoint problem, and it is a question about roles rather than about paths.
    /// Two trees with a gap between them, a mage standing in the gap, something hostile on the far side: the
    /// mage will be killed there in seconds and the blade in front of it would take minutes. So the gap
    /// belongs to the blade — not because anybody worked out that it is a chokepoint, but because the tile is
    /// nearer the threat than the mage's own station is, and ground nearer the threat belongs to whoever
    /// stands nearer the threat.
    /// </para>
    ///
    /// <para>
    /// Note what this does <em>not</em> do: a mage cannot move a blade out of the way. The blade is where it
    /// belongs.
    /// </para>
    /// </summary>
    public static bool OutranksFor(IBotSquadMember asker, IBotSquadMember holder) =>
        asker?.Class != null
        && holder?.Class != null
        && RingFor(asker.Class.Role) > RingFor(holder.Class.Role);
}
