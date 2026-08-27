using System;

namespace Server.BotAI.V2;

/// <summary>
/// Getting away from whatever is killing it. The one piece of work in this project whose whole product is
/// that the bot still exists afterwards.
///
/// <para>
/// <b>The rung it answers has had a proposer for a while and it was only half the answer.</b>
/// <c>Failing</c> is the rung for a bot whose health is going, and the only thing offering work on it was
/// <see cref="BotMedic"/> — mending, standing still, where it stands. That is right when a bot is simply
/// hurt and wrong in the one case the rung exists for: three creatures on one bot, the bot at a third of
/// its health, winding a bandage that takes several seconds while all three go on hitting it. Watched from
/// a client it reads as a bot that has decided to die politely, and it is the same defect the ladder's own
/// notes describe twice — <em>standing still is not an option at any number</em> — reappearing as the thing
/// the rung does instead of standing still.
/// </para>
///
/// <para>
/// <b>It walks away from the worst of it and no further than that.</b> There is no clever route and there
/// must not be: a bot computing an escape path is a bot spending the population's whole path-search budget
/// at the moment it can least afford to stand about. Straight back along the line the creature came in on,
/// recomputed only on arrival, and towards home when straight back would leave the ground the population
/// lives on.
/// </para>
/// </summary>
public sealed class BotBolt : BotDeed
{
    /// <summary>The ledger's key.</summary>
    public const string Trade = "flee";

    /// <summary>
    /// What getting away is reckoned at per minute.
    ///
    /// <para>
    /// <b>Enormous, and it is not a thumb on the scale.</b> Everything on this shard is priced in
    /// gold-equivalent per minute so that wants can be compared, and what a death actually costs is the
    /// unbound half of the kit, the purse, the resurrection walk and the three minutes the ledger charges
    /// for a job that never finished. Against that, mending at thirty a minute is what it is worth and this
    /// is what this is worth; a number small enough to lose to a bandage would be a number that says a bot
    /// should stand and be killed tidily.
    /// </para>
    ///
    /// <para>
    /// It also has to survive its own measurement. The ledger blends this prior with what flight actually
    /// pays — which is nothing, for ever — so a place fled from a dozen times settles at roughly a seventh
    /// of what is written here. That floor is the number worth reading, not this one.
    /// </para>
    /// </summary>
    public static double Prior { get; set; } = 2000.0;

    /// <summary>How long getting clear is expected to take.</summary>
    public static double WorkMinutes { get; set; } = 0.5;

    /// <summary>
    /// How far a bot looks for what it is running from, and therefore how far counts as away.
    ///
    /// Fourteen tiles: outside a bow's ten and a caster's eight with enough margin that the thing has to
    /// actually follow to get back into range. One number for both questions on purpose — "what is after me"
    /// and "am I clear of it" are the same question asked twice, and answering them with two numbers is how
    /// this project keeps producing a bot that is neither fleeing nor fighting.
    /// </summary>
    public static int Watch { get; set; } = 14;

    /// <summary>
    /// How far to head in one go. Beyond the watch, so arriving means being clear rather than being asked
    /// again; short enough that the ground is still ground the bot knows.
    /// </summary>
    public static int Bound { get; set; } = 18;

    /// <summary>
    /// How long a flight may go on before it is given up as not working.
    ///
    /// <para>
    /// Half a minute. Something that keeps pace with a bot for half a minute is not going to be outrun, and
    /// a bot that runs for ever is a bot dragging a train of creatures across the whole population's ground.
    /// Giving up hands it back to the rung, which will offer mending — a bandage under fire is a poor answer
    /// and it is a better one than sprinting until dead.
    /// </para>
    /// </summary>
    public static int GiveUpMs { get; set; } = 30000;

    private readonly Map _map;

    private readonly Point3D _from;

    private Point3D _to;

    private long _begun;

    private bool _started;

    private int _legs;

    public BotBolt(Map map, Point3D from)
    {
        _map = map;
        _from = from;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    /// <summary>
    /// Where it was standing when it decided to run, never where it ends up.
    ///
    /// The ledger files outcomes by patch of ground and the fact worth filing is <em>this patch made me
    /// run</em>. Filing it under the safe place the bot reached would teach it that safety is dangerous.
    /// </summary>
    public override Point3D Where => _from;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    /// <summary>Nothing. Running teaches a bot nothing the engine will write on its sheet.</summary>
    public override SkillName? Trains => null;

    public override int Outlay => 0;

    /// <summary>
    /// Counted as coin, and it produces none.
    ///
    /// <para>
    /// A deliberate lie, and the alternative is worse. Work that pays in anything but money is discounted by
    /// how badly the bot needs money — and a bot that needs it badly enough discounts such work to nothing,
    /// which is a veto rather than a discount. Written honestly as zero, this would read "a bot with an empty
    /// purse may not run away", and the bots on this shard are born with an empty purse. What that factor is
    /// for is choosing between ways of earning, and there is no earning on this rung to choose between.
    /// </para>
    /// </summary>
    public override double Coin => 1.0;

    public override int Made => 0;

    public override string Stage => _to == Point3D.Zero ? "getting away" : $"getting away to {_to}";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal || !body.Alive)
        {
            return BotDoing.Failed("no body");
        }

        var now = Core.TickCount;

        if (!_started)
        {
            _started = true;
            _begun = now;
        }

        // Said every beat, not once. Every blow that lands re-points the bot at whatever hit it — see
        // BotMobile.OnDamage — so a flight that dropped its combatant once would be swinging again a
        // fraction of a second later, and a bot that is swinging is a bot standing still.
        body.Combatant = null;
        body.Warmode = false;

        var worst = BotThreat.Strongest(body, Watch);

        if (worst == null)
        {
            return BotDoing.Done(_legs > 0 ? $"clear of it after {_legs} legs" : "nothing following");
        }

        if (now - _begun >= GiveUpMs)
        {
            // Not a failure of the ground: the place did not do this, the thing that kept pace did. Finished
            // rather than failed, so the patch is not marked with caution for it.
            return BotDoing.Done($"could not shake {worst.Name}");
        }

        // Recomputed on arrival rather than every beat, and that is the whole of the cost control here.
        // Aiming at a point that moves with the creature would buy a fresh path search on every step, per
        // fleeing bot, at exactly the moment the population has several of them.
        if (_to == Point3D.Zero || body.InRange(_to, 1))
        {
            _to = Retreat(_map, body, worst);
            _legs++;
        }

        if (_to == Point3D.Zero)
        {
            // Nowhere behind it holds a body. Standing and fighting is the honest fallback, and the rung will
            // offer a bandage in the same breath.
            return BotDoing.Failed($"cornered by {worst.Name}");
        }

        return BotDoing.Walk(_map, _to, BotArrival.Within(1), $"away from {worst.Name}");
    }

    /// <summary>The way out turned out not to exist. Try the other way — towards home — once, then give up.</summary>
    public override bool Bend(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null)
        {
            return false;
        }

        var home = Homeward(_map, body);

        if (home == Point3D.Zero || home == _to)
        {
            return false;
        }

        _to = home;

        return true;
    }

    /// <summary>
    /// Somewhere roughly <see cref="Bound"/> tiles from the threat, on the far side of the bot from it, or
    /// nothing at all when there is nowhere.
    ///
    /// <para>
    /// <b>Public and static so that the proposer can ask it before offering flight, and that is the whole
    /// point of it being here.</b> A bot with nowhere to run failed this undertaking the instant it took it
    /// on, and was offered it again in the same beat — Cedric failed to flee a harpy eighty-one times in
    /// thirty-six seconds on 26.08.2026, which is the shard's oldest defect shape: a piece of work that is
    /// taken up and refused, over and over, because the refusal is invisible to whatever offers it. The
    /// answer is not to price flight lower — being cornered is not a reason to run <em>less</em> keenly — it
    /// is for nobody to offer running to somebody who cannot run.
    /// </para>
    ///
    /// Falls back to heading home when straight back would put the bot off the ground the population lives
    /// on — which is not a nicety. The far edge of the roam is where the terrain that strands bots is, and a
    /// flight that ends in being carried back by the rescue has traded one emergency for another.
    /// </summary>
    public static Point3D Retreat(Map map, Mobile body, Mobile from)
    {
        if (map == null || map == Map.Internal || body == null || from == null)
        {
            return Point3D.Zero;
        }

        var dx = body.X - from.X;
        var dy = body.Y - from.Y;
        var step = Math.Max(Math.Abs(dx), Math.Abs(dy));

        if (step > 0)
        {
            var x = body.X + dx * Bound / step;
            var y = body.Y + dy * Bound / step;

            if (BotStep.Settle(map, x, y, out var z))
            {
                var back = new Point3D(x, y, z);

                if (BotPopulation.Within(map, back))
                {
                    return back;
                }
            }
        }

        return Homeward(map, body);
    }

    /// <summary>A leg of the way home, or home itself when it is nearer than a leg.</summary>
    private static Point3D Homeward(Map map, Mobile body)
    {
        var home = BotPopulation.Where;
        var dx = home.X - body.X;
        var dy = home.Y - body.Y;
        var step = Math.Max(Math.Abs(dx), Math.Abs(dy));

        if (step <= 0)
        {
            return Point3D.Zero;
        }

        if (step <= Bound)
        {
            return BotStep.Settle(map, home.X, home.Y, out var atHome)
                ? new Point3D(home.X, home.Y, atHome)
                : Point3D.Zero;
        }

        var x = body.X + dx * Bound / step;
        var y = body.Y + dy * Bound / step;

        return BotStep.Settle(map, x, y, out var z) ? new Point3D(x, y, z) : Point3D.Zero;
    }
}
