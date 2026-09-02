using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Targeting;

namespace Server.BotAI.V2;

/// <summary>
/// The King's Rangers: one standing company of five, raised once and replaced only when it is wiped out.
///
/// <para>
/// <b>A fixture rather than a population, and that is why they are kept here instead of in the class mix.</b>
/// <c>BotPopulation</c> raises a proportion of each trade and lets the auction sort out what any of them
/// does; these five are one unit with one duty, they are always exactly five, and losing one of them is not
/// answered by raising another — the company goes on with four until it is destroyed, and then the whole of
/// it comes back together. That is a different lifecycle from anything else on the shard and it has its own
/// keeper.
/// </para>
///
/// <para>
/// <b>Two hours between a wipe and the next company, by order.</b> Long enough that losing them matters —
/// the map stops being filled in and everybody can see it — and not so long that a bad afternoon costs a
/// whole session. Measured from the death of the last of them rather than the first, because a company that
/// lost four and held is a company that is still working.
/// </para>
///
/// <para>
/// <b>They exist only where Britain does.</b> See <see cref="BotRangers.Mainland"/>: their whole duty is the
/// island the capital stands on, so a quadrant across water is not theirs to walk however unknown it is. It
/// is a rectangle rather than a real coastline because a rectangle is a fact anybody can check on a map, and
/// a wrong one is a line in a config file rather than a rebuild.
/// </para>
/// </summary>
public static class BotRangers
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRangers));

    /// <summary>
    /// Whether the crown keeps a company of rangers at all.
    ///
    /// <para>
    /// <b>Off since 02.09.2026, by instruction.</b> They were five bots outside the population's own
    /// economy — raised apart, never revived, walking their own ground on their own clock — and they showed
    /// up in every measurement of the shard as five bots doing something nobody else did. The code is left
    /// standing and this line is the whole of the switch.
    /// </para>
    /// </summary>
    public static bool Enabled { get; set; }

    /// <summary>
    /// How long after the last of them falls before the next company is raised.
    ///
    /// Two hours, by order. See the class note: measured from the death of the last, not the first.
    /// </summary>
    public static int MournMs { get; set; } = 7200000;

    /// <summary>
    /// The island Britain stands on, as a rectangle of tiles.
    ///
    /// <para>
    /// The Britannian mainland runs from the northern mountains above Minoc down past Trinsic, and from the
    /// western shore at Yew across to Vesper and the eastern coast. Skara Brae, Moonglow, Magincia and Ocllo
    /// are all islands of their own and all fall outside it. The bounds are deliberately generous inland and
    /// tight at the coasts: a quadrant wrongly included is a walk that fails at the water's edge and is
    /// counted, while one wrongly excluded is ground nobody will ever be sent to and nothing will ever say so.
    /// </para>
    /// </summary>
    public static Rectangle2D Mainland { get; set; } = new(0, 0, 3200, 3500);

    /// <summary>
    /// How far a ranger notices something hostile, in tiles.
    ///
    /// <para>
    /// Eighteen, by order: what a person sees on their own screen. The population's own <c>NoticeRange</c> is
    /// ten — about a bowshot — which is right for a bot that has a trade to get back to and wrong for a
    /// company whose entire product is knowing what lives on the ground it is crossing. A ranger that walks
    /// past something at twelve tiles has failed the errand it is on.
    /// </para>
    /// </summary>
    public static int Sight { get; set; } = 18;

    /// <summary>The five, in the order they were raised. Dead ones are dropped as they are noticed.</summary>
    private static readonly List<BotMobile> _company = [];

    /// <summary>
    /// How many of each sort one company holds. Five bodies, by order.
    ///
    /// <para>
    /// Named rather than constructed, and looked up in <c>BotClasses</c>. Every bot of a class shares one
    /// instance of it, and that instance is the one the registry filled in — a freshly constructed class has
    /// never had <c>Reset</c> called on it, so its skills, kit and stipend are all empty. The shard says so
    /// plainly when it happens: "holding its trade already: nothing at all, which is a defect".
    /// </para>
    /// </summary>
    private static readonly (string Klass, string Called)[] Muster5 =
    [
        ("Ranger Warrior", "Ser Alric"),
        ("Ranger Warrior", "Ser Bruin"),
        ("Ranger Archer", "Fletcher Wyn"),
        ("Ranger Mage", "Magister Coll"),
        ("Ranger Healer", "Sister Maren")
    ];

    private static long _fellTick;

    private static bool _mourning;

    /// <summary>Companies raised, all told, and how many were lost.</summary>
    public static long Raised { get; private set; }

    public static long Lost { get; private set; }

    /// <summary>The living rangers. Never more than five.</summary>
    public static IReadOnlyList<BotMobile> Company => _company;

    /// <summary>How many are still on their feet.</summary>
    public static int Standing
    {
        get
        {
            var up = 0;

            for (var i = 0; i < _company.Count; i++)
            {
                if (Alive(_company[i]))
                {
                    up++;
                }
            }

            return up;
        }
    }

    /// <summary>
    /// Whether the surgeon is the only one of them left standing.
    ///
    /// <para>
    /// <b>The round ends there, by order, and the reasoning is that a healer alone is not a company.</b> He
    /// fights only what reaches him, carries no armour worth the name and exists to keep four other bots
    /// upright; walking him on into unread ground by himself is walking him into the thing that killed the
    /// other four. Ending the round does not save him from anything already on him — it stops him being
    /// sent somewhere new.
    /// </para>
    /// </summary>
    public static bool SurgeonAlone()
    {
        var up = 0;
        var healer = false;

        for (var i = 0; i < _company.Count; i++)
        {
            var ranger = _company[i];

            if (!Alive(ranger))
            {
                continue;
            }

            up++;

            if (ranger.Class is BotRangerHealer)
            {
                healer = true;
            }
        }

        return up == 1 && healer;
    }

    /// <summary>Whether a bot is one of the crown's rangers.</summary>
    public static bool Ours(Mobile body) => body is BotMobile { Class: BotRanger };

    /// <summary>Whether this ground is the island the rangers may walk.</summary>
    public static bool Theirs(Map map, Point3D where) =>
        map != null && map == BotPopulation.Home && Mainland.Contains(new Point2D(where.X, where.Y));

    /// <summary>
    /// Looked at on the population's own clock. Raises a company when there is none and the mourning is over.
    /// </summary>
    public static void Tick()
    {
        if (!Enabled)
        {
            return;
        }

        Prune();

        if (_company.Count > 0)
        {
            _mourning = false;

            // Kept together on every beat rather than only when they are raised: a member can be knocked out
            // of the squad by anything — a death, a rescue that pulled it into another company, a squad beat
            // that dissolved one — and a ranger walking on its own is a ranger about to be killed on its own.
            Muster();

            // The surgeon alone is not a company. The round stops; he is not sent anywhere new.
            if (SurgeonAlone())
            {
                Ground = Point3D.Zero;

                return;
            }

            March();
            Fight();

            return;
        }

        // <b>The first company is raised at once, not mourned for two hours.</b> "No company standing" and
        // "the company was destroyed" are the same state to look at and completely different facts, and the
        // mourning clock belongs only to the second. Written without this distinction the shard came up, saw
        // no rangers, started grieving for a company that had never existed, and would have waited two hours
        // to raise the first one. This is the second time today a first tick has quietly started a clock
        // instead of doing the thing — the pin writer did the same an hour ago.
        if (Raised == 0)
        {
            Raise();

            return;
        }

        var now = Core.TickCount;

        // The whole company is gone. The clock starts on the tick that noticed, and is compared by
        // subtraction against a stamp that was itself a real reading — these counters can start enormous.
        if (!_mourning)
        {
            _mourning = true;
            _fellTick = now;

            Lost++;

            logger.Warning(
                "The King's Rangers have been destroyed. The crown will raise another company in {Wait} minutes",
                MournMs / 60000
            );

            return;
        }

        if (now - _fellTick < MournMs)
        {
            return;
        }

        Raise();
    }

    /// <summary>Raises a fresh company of five where the population lives.</summary>
    public static int Raise()
    {
        _mourning = false;

        var made = 0;

        for (var i = 0; i < Muster5.Length; i++)
        {
            var (name, called) = Muster5[i];
            var klass = BotClasses.Find(name);

            if (klass == null)
            {
                logger.Error("There is no class called {Name}, so no ranger of that sort was raised", name);

                continue;
            }

            // <b>Named by the crown, never dealt a name off the population's roll.</b> The pool is the town's
            // own list handed out in order, so a company drawn from it comes out as "Kerrin 2" and "Lysa 2" —
            // which reads to anybody watching as two ordinary bots with duplicate names. These five are not
            // of the population and should not be able to be mistaken for it from a client, a log line or a
            // dashboard row.
            var bot = BotPopulation.Raise(klass, called);

            if (bot == null)
            {
                continue;
            }

            _company.Add(bot);
            made++;
        }

        if (made == 0)
        {
            // Nowhere to put them, which BotPopulation.Raise has already said in its own words. The mourning
            // clock is restarted so this is retried rather than hammered every beat.
            _mourning = true;
            _fellTick = Core.TickCount;

            return 0;
        }

        Raised++;

        Muster();

        logger.Information(
            "The King's Rangers ride out: {Count} of them — two in the line, a bow, a staff and a surgeon",
            made
        );

        return made;
    }

    /// <summary>
    /// Puts the company into one standing squad: the first warrior leads, everybody else falls in.
    ///
    /// <para>
    /// <b>The squad is raised once and kept, and every defect this class produced came from not doing
    /// that.</b> It was left to the errand to call a company together each time, which meant the company was
    /// re-formed on every sweep, dissolved by every skirmish, and re-formed again — so three of them could
    /// each believe they led it, a fight could outlive the sweep and strand all five, and a squad with
    /// nothing to fight could quietly disband itself while its members stood in a field. These five are one
    /// unit for their whole lives. Their squad should be too.
    /// </para>
    ///
    /// <para>
    /// Charged, permanently: that flag is what stops <c>BotSquad.Quiet</c> letting a company go for having
    /// had nothing to fight lately, which is the ordinary and correct rule for a warband raised against one
    /// troll and exactly wrong for a standing patrol whose whole value is being somewhere before anything
    /// happens.
    /// </para>
    /// </summary>
    public static void Muster()
    {
        IBotSquadMember leader = null;

        // The leader is the first living warrior — the one built to stand in front. Falling back to whoever
        // is left keeps a company of two working when the line is dead.
        for (var i = 0; i < _company.Count; i++)
        {
            if (Alive(_company[i]) && _company[i].Class is BotRangerWarrior && _company[i] is IBotSquadMember warrior)
            {
                leader = warrior;

                break;
            }
        }

        if (leader == null)
        {
            for (var i = 0; i < _company.Count; i++)
            {
                if (Alive(_company[i]) && _company[i] is IBotSquadMember any)
                {
                    leader = any;

                    break;
                }
            }
        }

        if (leader == null)
        {
            return;
        }

        var squad = leader.Squad;

        // Somebody else's company, or none at all: the leader takes one of its own.
        if (squad == null || !ReferenceEquals(squad.Leader, leader))
        {
            BotSquads.Leave(leader);

            squad = BotSquads.Form(leader);
        }

        if (squad == null)
        {
            return;
        }

        // Room for all five. The shard's ordinary ceiling is five including the leader, and this company is
        // exactly that — said out loud rather than relied on, because a sixth ranger would otherwise be
        // silently left out of its own company.
        squad.Ceiling = Math.Max(squad.Ceiling, _company.Count);
        squad.Charged = true;

        for (var i = 0; i < _company.Count; i++)
        {
            var ranger = _company[i];

            if (!Alive(ranger) || ranger is not IBotSquadMember member || ReferenceEquals(member, leader))
            {
                continue;
            }

            if (ReferenceEquals(member.Squad, squad))
            {
                continue;
            }

            BotSquads.Leave(member);
            BotSquads.Join(squad, member);
        }
    }

    /// <summary>Where the company is walking now, or nothing while it has no orders.</summary>
    public static Point3D Ground { get; private set; }

    /// <summary>Squares this company has read, and squares it stepped past as unreachable.</summary>
    public static long Read { get; private set; }

    public static long Baulked { get; private set; }

    private static long _orderedTick;

    /// <summary>Squares missed in a row. Three of them is a company that is stuck rather than unlucky.</summary>
    private static int _baulks;

    /// <summary>How many unreachable squares in a row mean the company should walk back out.</summary>
    public static int Lost3 { get; set; } = 3;

    /// <summary>
    /// How long one square is allowed to take before it is written off as unreachable.
    ///
    /// Three minutes. A quadrant is thirty tiles and the company runs; anything beyond this is a hill it
    /// cannot climb, a lake, or a walled yard, and the answer to all three is the next square.
    /// </summary>
    public static int LegMs { get; set; } = 180000;

    /// <summary>
    /// The company's own orders, given directly and on its own clock.
    ///
    /// <para>
    /// <b>No auction, no proposer, no errand.</b> Every version of this that went through the decision layer
    /// failed the same way — a skirmish is worth more per minute than a patrol, so the patrol was outbid and
    /// discarded, and afterwards the company had nothing in hand and stood still. This walks them the way an
    /// order walks soldiers: the leader is pointed at a square, the squad's own formation brings the other
    /// four along, and nothing may take the order away.
    /// </para>
    ///
    /// <para>
    /// Fighting is not handled here at all and must not be: it is a reflex on the bot itself — see
    /// <c>BotMobile.Watch</c>, which takes a target for the whole company the moment one of them sees
    /// something hostile, and puts the fight *on top of* the march rather than instead of it. That is the
    /// division that took all evening to find. Orders here, fighting there, and neither can erase the other.
    /// </para>
    /// </summary>
    private static void March()
    {
        var leader = Leader();

        if (leader?.Self is not BotMobile body || body.Map == null || body.Map == Map.Internal)
        {
            Ground = Point3D.Zero;

            return;
        }

        var squad = leader.Squad;
        var now = Core.TickCount;

        // Standing in the square it was sent to: read it, and take the next one.
        if (Ground != Point3D.Zero && body.InRange(Ground, BotQuad.Side / 2))
        {
            BotQuad.Swept(body.Map, body.Location);
            Read++;
            _baulks = 0;
            Ground = Point3D.Zero;
        }

        // Three minutes on one leg and no nearer. The square is real and unreachable, which is a fact about
        // the island worth keeping — marked read so it is never offered again — and the round goes on.
        if (Ground != Point3D.Zero && now - _orderedTick >= LegMs)
        {
            BotQuad.Seen(body.Map, Ground);
            Baulked++;
            _baulks++;
            Ground = Point3D.Zero;
        }

        // <b>Three squares running that could not be reached means the company is not choosing badly — it is
        // somewhere it cannot get out of.</b> A cave is the case that taught this: the frontier is answered
        // from where the leader stands, so a company inside one is offered the unread ground deeper inside
        // it, fails to reach that too, and works its way further in for the rest of the session. Walking back
        // to where the population lives is the one order that is always reachable from anywhere they can have
        // walked to, and it puts them somewhere the frontier means something again.
        if (_baulks >= Lost3)
        {
            _baulks = 0;
            Ground = Point3D.Zero;
            _orderedTick = now;

            logger.Warning(
                "The King's Rangers could not reach {Count} squares running and are walking back out to {Where}",
                Lost3,
                BotPopulation.Where
            );

            body.Journey.Begin(body.Map, BotPopulation.Where, BotArrival.Within(BotQuad.Side), "back out of it");

            return;
        }

        if (Ground == Point3D.Zero)
        {
            Ground = BotQuad.Frontier(
                body.Map,
                body.Location,
                Roam,
                at => Theirs(body.Map, at) && BotReach.Ask(body.Map, body.Location, at, BotArrival.Within(BotQuad.Side / 3)) != BotReachVerdict.Sealed
            );

            if (Ground == Point3D.Zero)
            {
                return;
            }

            _orderedTick = now;

            logger.Information(
                "The King's Rangers are ordered to ({X}, {Y}); {Read} squares read, {Baulked} stepped past",
                Ground.X,
                Ground.Y,
                Read,
                Baulked
            );
        }

        // A fight outranks the march for as long as it lasts, and the squad is already anchored on the thing
        // being fought. Re-pointing the leader now would drag the formation off its own target.
        if (squad is { Stance: BotSquadStance.Fighting })
        {
            return;
        }

        // The order, and only when it is not already the order: BotJourney compares by arrival distance, so
        // an order rewritten every beat is a route that restarts every beat. This project has paid for that.
        if (!body.Journey.Active || body.Journey.Target != Ground)
        {
            body.Journey.Begin(body.Map, Ground, BotArrival.Within(BotQuad.Side / 3), "the King's orders");
        }
    }

    /// <summary>
    /// The company's own fighting, on the company's own beat.
    ///
    /// <para>
    /// <b>Casting and mending live inside errands on this shard, and the rangers have no errands.</b> A mage
    /// throws spells because <c>BotSlay</c> tells it to, and BotSlay arrives from the auction — which these
    /// five were deliberately taken out of. So the moment they became their own thing, the mage stopped
    /// casting entirely and stood in the line swinging a staff, and the surgeon stopped mending. Their two
    /// duties are scouting and fighting; the second one has to be written here, beside the first.
    /// </para>
    ///
    /// <para>
    /// <b>Reagents are never the reason a ranger fails to cast.</b> The quartermaster keeps all eight
    /// stocked — see BotQuartermaster — so what decides a cast is mana and the engine's own rules, which is
    /// the way it should read from outside: a mage that is out of mana is resting, not out of shopping.
    /// </para>
    /// </summary>
    private static void Fight()
    {
        for (var i = 0; i < _company.Count; i++)
        {
            var ranger = _company[i];

            if (!Alive(ranger) || ranger.Paralyzed)
            {
                continue;
            }

            // <b>A finished cast asks for a target and waits for one, and nothing here was answering.</b>
            // BotStrike.Begin only starts the spell; the engine then puts a target cursor on the bot and the
            // caster stands there holding it until somebody points it. That is what "he casts Por Corp Wis
            // and freezes" was — the spell was fine, the mana was fine, and the mage was waiting to be told
            // what to hit for the rest of the session. Answered before anything else on the beat, because a
            // bot holding a cursor can do nothing else at all.
            if (ranger.Target != null)
            {
                Aim(ranger);

                continue;
            }

            if (ranger.Spell != null)
            {
                continue;
            }

            // The surgeon first, and he is asked about everybody rather than about himself: keeping the
            // other four upright is the whole of his office, and a healer who only ever heals himself is
            // four bots' worth of funeral. Whoever is worst hurt, and only if the wound is worth a cast.
            if (ranger.Class is BotRangerHealer)
            {
                Mend(ranger);

                continue;
            }

            if (ranger.Class is not BotRangerMage)
            {
                continue;
            }

            var quarry = ranger.Combatant as Mobile;

            if (quarry is not { Deleted: false, Alive: true } || quarry.Map != ranger.Map)
            {
                continue;
            }

            if (!ranger.InRange(quarry.Location, CastRange))
            {
                continue;
            }

            // Strongest the pool will pay for, downwards: lightning while there is mana for it, magic arrow
            // at the bottom rather than reaching for a stick. See BotStrike.Best.
            Tried++;

            var spell = BotStrike.Best(ranger);

            if (spell < 0)
            {
                // Named rather than silent, because "the mage will not cast" turned out to be three separate
                // things wearing one silence: no book, no mana, and no reagents. Best walks the ladder and
                // returns -1 for all three alike, so the counter has to ask the questions itself.
                if (BotGrimoire.Count(ranger) == 0)
                {
                    Bookless++;
                }
                else
                {
                    Spent++;
                }

                continue;
            }

            if (BotStrike.Begin(ranger, spell))
            {
                Cast++;
            }
            else
            {
                Balked++;
            }
        }
    }

    /// <summary>
    /// Points a finished cast at whatever it was meant for: the quarry for an attack, the patient for a heal.
    ///
    /// <para>
    /// Cancelled rather than left standing when there is nothing to point it at. A cursor with no target is
    /// a bot that has stopped existing as far as everything else is concerned — it will not walk, will not
    /// swing and will not cast again — so an unanswerable cursor has to be thrown away rather than kept.
    /// </para>
    /// </summary>
    private static void Aim(BotMobile ranger)
    {
        var at = ranger.Class is BotRangerHealer ? Worst(ranger) : ranger.Combatant as Mobile;

        if (at is { Deleted: false, Alive: true } && at.Map == ranger.Map && BotStrike.Aim(ranger, at))
        {
            Aimed++;

            return;
        }

        ranger.Target?.Cancel(ranger, TargetCancelType.Canceled);
        Cancelled++;
    }

    /// <summary>Casts pointed at something, and cursors thrown away for want of anything to point at.</summary>
    public static long Aimed { get; private set; }

    public static long Cancelled { get; private set; }

    /// <summary>Whoever in the company is worst hurt and within reach, or nothing.</summary>
    private static BotMobile Worst(BotMobile surgeon)
    {
        BotMobile worst = null;
        var lowest = 1.0;

        for (var i = 0; i < _company.Count; i++)
        {
            var ranger = _company[i];

            if (!Alive(ranger) || ranger.HitsMax <= 0 || ranger.Map != surgeon.Map)
            {
                continue;
            }

            var share = ranger.Hits / (double)ranger.HitsMax;

            if (share >= Hurt || share >= lowest || !surgeon.InRange(ranger.Location, CastRange))
            {
                continue;
            }

            lowest = share;
            worst = ranger;
        }

        return worst;
    }

    /// <summary>The surgeon's beat: whoever in the company is worst hurt, if a cast would help.</summary>
    private static void Mend(BotMobile surgeon)
    {
        var worst = Worst(surgeon);

        if (worst == null)
        {
            return;
        }

        var spell = BotMend.Spell(surgeon, worst);

        if (spell >= 0)
        {
            BotMend.Begin(surgeon, spell);
        }
    }

    /// <summary>Casts attempted, got off, and the three ways they fail.</summary>
    public static long Tried { get; private set; }

    public static long Cast { get; private set; }

    /// <summary>The mage has no spellbook at all. Should never happen: the crown issues one.</summary>
    public static long Bookless { get; private set; }

    /// <summary>A book, and nothing in it the mana or the reagents will pay for.</summary>
    public static long Spent { get; private set; }

    /// <summary>The engine refused the cast outright.</summary>
    public static long Balked { get; private set; }

    /// <summary>How far a ranger will cast. The engine's own line of sight for a spell.</summary>
    public static int CastRange { get; set; } = 12;

    /// <summary>The share of health below which the surgeon reaches for a spell.</summary>
    public static double Hurt { get; set; } = 0.85;

    /// <summary>How far the company will look for its next square.</summary>
    public static int Roam { get; set; } = 500;

    /// <summary>Whoever leads the company, or nothing.</summary>
    private static IBotSquadMember Leader()
    {
        for (var i = 0; i < _company.Count; i++)
        {
            if (Alive(_company[i]) && _company[i] is IBotSquadMember member && member.Squad?.Leader == member)
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>Drops the dead and the deleted. A company of four is still a company.</summary>
    private static void Prune()
    {
        for (var i = _company.Count - 1; i >= 0; i--)
        {
            if (!Alive(_company[i]))
            {
                _company.RemoveAt(i);
            }
        }
    }

    private static bool Alive(BotMobile bot) =>
        bot is { Deleted: false, Alive: true } && bot.Map != null && bot.Map != Map.Internal;

    public static string Describe()
    {
        if (!Enabled)
        {
            return "the crown keeps no rangers";
        }

        if (_company.Count == 0)
        {
            var waited = _mourning ? Math.Max(0, (MournMs - (Core.TickCount - _fellTick)) / 60000) : 0;

            return Raised == 0
                ? "the King's Rangers have not been raised yet"
                : $"the King's Rangers are dead; another company in {waited} minutes ({Raised} raised, {Lost} lost)";
        }

        var where = Ground == Point3D.Zero ? "nowhere" : $"({Ground.X}, {Ground.Y})";

        return $"the King's Rangers: {Standing} of {_company.Count} on their feet, {Read} squares read and {Baulked} stepped past, "
               + $"now ordered to {where} ({Raised} companies raised, {Lost} lost); "
               + $"the mage: {Tried} looks, {Cast} spells thrown, {Aimed} aimed, {Cancelled} cursors thrown away, "
               + $"{Spent} had nothing the mana would pay for, {Bookless} had no book, {Balked} refused by the engine";
    }

    public static void Forget()
    {
        _company.Clear();
        _mourning = false;
        Raised = 0;
        Lost = 0;
        Read = 0;
        Baulked = 0;
        Tried = 0;
        Cast = 0;
        Bookless = 0;
        Spent = 0;
        Balked = 0;
        Aimed = 0;
        Cancelled = 0;
        Ground = Point3D.Zero;
    }
}
