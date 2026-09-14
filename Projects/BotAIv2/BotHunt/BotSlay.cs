using System;
using Server.Logging;
using System.Collections.Generic;
using Server.Items;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Close, fight, go through what is left. The fighter's chain, and the first work in this project that brings
/// new gold into the world.
///
/// <para>
/// <b>Its stages are its own business, and the third one is the point of it.</b> A hunt that ends when the
/// thing falls over leaves a bot standing next to everything it earned. Mining learned this first — an
/// undertaking that ends at the vein leaves a bot underground holding rock — and it is the same lesson: the
/// work is not the fight, it is the fight <em>and getting paid for it</em>.
/// </para>
///
/// <para>
/// <b>The fighting itself is entirely the engine's.</b> Setting <c>Combatant</c> starts a server-side timer
/// that swings the weapon, rolls to hit, applies damage and wears the blade down — no client involved and
/// nothing simulated. So this file never decides a blow. What it decides is when to stop, and that is the one
/// judgement it must not get wrong.
/// </para>
///
/// <para>
/// <b>It runs away, and it has to, because nothing above it will.</b> The rung for a bot that is losing has no
/// proposer, so the brain's answer to failing health is to hold on to whatever the bot is already doing — and
/// what this bot is already doing is being killed. The first version's sharpest lesson was that flight must
/// outrank everything social; here it also has to outrank the undertaking's own stubbornness. Giving up marks
/// the place with caution, which is exactly the right record: this patch of ground kills me.
/// </para>
/// </summary>
public sealed class BotSlay : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSlay));

    public const string Trade = "hunt";

    public static double Prior { get; set; } = 60.0;

    public static double WorkMinutes { get; set; } = 3.0;

    public static double FleeAt { get; set; } = 0.4;

    public static int Leash { get; set; } = 50;

    public static long Slipped { get; private set; }

    private Point3D _began;

    private bool _begun;

    public static double FillFraction { get; set; } = 0.8;

    public static int OddsMs { get; set; } = 1000;

    public static int NoProgressMs { get; set; } = 45000;

    public static int CapMs { get; set; } = 150000;

    public static int BlindMs { get; set; } = 6000;

    public static int StillMs { get; set; } = 1000;

    public static int KiteSlackMs { get; set; } = 700;

    private const int Eye = 14;

    public static int TooClose { get; set; } = 3;

    public static int KiteWithin { get; set; } = 5;

    private static int Standoff(int reach) => Math.Max(1, reach - 2);

    private enum Leg
    {
        Close,
        Fight,
        Spoils
    }

    private readonly BaseCreature _quarry;

    private readonly Map _map;

    private readonly Point3D _found;

    private readonly SkillName _trains;

    private Leg _leg;

    private Point3D _fell;

    private int _made;

    private int _taken;

    private int _coins;

    private int _casts;

    private int _hides;

    private Point3D _backing;

    private bool _aiming;

    private bool _cast;

    private long _castTick;

    private long _oddsTick;

    private bool _weighed;

    private bool _complained;

    private int _lowest = int.MaxValue;

    private long _seenTick;

    private long _armedTick;

    private long _progressTick;

    private long _tookTick;

    public BotSlay(BaseCreature quarry, SkillName trains)
    {
        _quarry = quarry;
        _map = quarry.Map;
        _found = quarry.Location;
        _fell = quarry.Location;
        _trains = trains;

        _tookTick = Core.TickCount;
        _progressTick = Core.TickCount;
        _seenTick = Core.TickCount;
        _armedTick = Core.TickCount - BotArms.EveryMs;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _found;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => _trains;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override int Made => _made;

    public override bool Pressing(IBotWilful bot)
    {
        var body = bot?.Self;

        return body != null && !Down() && body.InRange(_quarry.Location, BotQuarry.Notice);
    }

    public override string Stage => _leg switch
    {
        Leg.Close => $"after {_quarry?.Name ?? "something"}",
        Leg.Fight => _casts > 0
            ? $"fighting {_quarry?.Name ?? "something"} ({_casts} spells)"
            : $"fighting {_quarry?.Name ?? "something"}",
        _ => _taken > 0 ? $"took {_taken} things and {_coins}gp" : "going through the corpse"
    };

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        return _leg switch
        {
            Leg.Close => Closing(bot, body),
            Leg.Fight => Fighting(bot, body),
            _ => Looting(bot, body)
        };
    }

    private BotDoing Closing(IBotWilful bot, Mobile body)
    {
        if (Down())
        {
            _leg = Leg.Spoils;

            return Looting(bot, body);
        }

        if (Core.TickCount - _tookTick >= CapMs)
        {
            BotQuarry.Crowd(_quarry);

            return BotDoing.Failed($"could not catch {_quarry.Name}");
        }

        if (!_begun)
        {
            _begun = true;
            _began = body.Location;
        }

        var drawn = Math.Max(Math.Abs(body.X - _began.X), Math.Abs(body.Y - _began.Y));

        if (drawn > Leash)
        {
            BotQuarry.Crowd(_quarry);
            Slipped++;

            return BotDoing.Failed($"{_quarry.Name} drew it {drawn} tiles and was let go");
        }

        _fell = _quarry.Location;

        var reach = Math.Max(body.Weapon?.MaxRange ?? 1, BotStrike.Can(body) ? BotStrike.Range : 0);
        var standoff = Standoff(reach);

        if (!body.InRange(_quarry.Location, standoff))
        {
            return BotDoing.Walk(_map, _quarry, BotArrival.Within(standoff), $"after {_quarry.Name}");
        }

        _seenTick = Core.TickCount;
        _leg = Leg.Fight;

        return Fighting(bot, body);
    }

    private BotDoing Fighting(IBotWilful bot, Mobile body)
    {
        if (Down())
        {
            _leg = Leg.Spoils;

            return Looting(bot, body);
        }

        if (_quarry.Hits < _lowest)
        {
            _lowest = _quarry.Hits;
            _progressTick = Core.TickCount;
        }

        if (Core.TickCount - _armedTick >= BotArms.EveryMs)
        {
            _armedTick = Core.TickCount;

            BotArms.Check(body, bot?.Class);
        }

        var sighted = body.InLOS(_quarry);
        var lawful = body.CanBeHarmful(_quarry, false);

        if (sighted && lawful)
        {
            _seenTick = Core.TickCount;
        }
        else if (Core.TickCount - _seenTick >= BlindMs)
        {
            body.Combatant = null;
            body.Warmode = false;

            var apart = (int)body.GetDistanceToSqrt(_quarry.Location);

            var why = (sighted, lawful) switch
            {
                (false, false) => "out of sight and the engine refuses it too",
                (false, true)  => "nothing of it in sight",
                _              => "in plain sight and the engine refuses the blow"
            };

            BotQuarry.Shun(_quarry);

            return BotDoing.Failed($"cannot land a blow on {_quarry.Name} at {apart} tiles: {why}");
        }
        else
        {
            if (!body.InRange(_quarry.Location, 1))
            {
                return BotDoing.Walk(_map, _quarry, BotArrival.Within(1), $"looking for a line on {_quarry.Name}");
            }
        }

        var stalled = Core.TickCount - _progressTick >= NoProgressMs;

        if (stalled || Core.TickCount - _tookTick >= CapMs)
        {
            body.Combatant = null;
            body.Warmode = false;

            var left = _quarry.HitsMax > 0 ? _lowest * 100 / _quarry.HitsMax : 0;

            if (stalled)
            {
                BotQuarry.Crowd(_quarry);

                return BotDoing.Failed($"{_quarry.Name} would not go down: {left}% of it left and not a scratch in {NoProgressMs / 1000}s");
            }

            return BotDoing.Failed($"ran out of time on {_quarry.Name} with {left}% of it left");
        }

        if (body.HitsMax > 0 && body.Hits < body.HitsMax * FleeAt)
        {
            body.Combatant = null;
            body.Warmode = false;

            return BotDoing.Failed($"{_quarry.Name} was winning");
        }

        if (!_weighed || Core.TickCount - _oddsTick >= OddsMs)
        {
            _weighed = true;
            _oddsTick = Core.TickCount;

            if (BotThreat.Decide(body, BotMobile.NoticeRange) == BotStand.Outmatched)
            {
                body.Combatant = null;
                body.Warmode = false;

                BotQuarry.Crowd(_quarry);

                return BotDoing.Failed($"too many of them around {_quarry.Name}");
            }
        }

        _fell = _quarry.Location;

        var casting = BotStrike.Can(body);

        var armed = casting && BotStrike.Best(body) >= 0;

        var reach = Math.Max(body.Weapon?.MaxRange ?? 1, armed ? BotStrike.Range : 0);
        var standoff = Standoff(reach);

        if (!body.InRange(_quarry.Location, reach))
        {
            _leg = Leg.Close;

            return BotDoing.Walk(_map, _quarry, BotArrival.Within(standoff), $"after {_quarry.Name}");
        }

        if (Kiting(body, reach))
        {
            var opening = Away(body, _quarry, reach);

            if (opening != Point3D.Zero)
            {
                body.Warmode = true;
                body.Combatant = _quarry;

                BotQuarry.Claim(body, _quarry);

                return BotDoing.Walk(_map, opening, BotArrival.Within(1), $"keeping {_quarry.Name} at arm's length");
            }
        }

        var dry = casting && !armed;

        var yield = dry ? Standoff(BotStrike.Range) : standoff;

        var yields = casting ? dry : standoff > TooClose;

        if (yields && yield > TooClose && body.InRange(_quarry.Location, TooClose))
        {
            var closing = BotArms.Suit(body, _quarry, TooClose);

            if (closing)
            {
                _backing = Point3D.Zero;

                Closed++;
            }
            else
            {
                if (_backing != Point3D.Zero
                    && (body.InRange(_backing, 1) || !Utility.InRange(_backing, _quarry.Location, yield)))
                {
                    _backing = Point3D.Zero;
                }

                var back = _backing != Point3D.Zero ? _backing : Away(body, _quarry, yield);

                if (back != Point3D.Zero)
                {
                    _backing = back;

                    body.Warmode = true;
                    body.Combatant = _quarry;

                    BotQuarry.Claim(body, _quarry);

                    return BotDoing.Walk(_map, back, BotArrival.Within(1), $"backing off {_quarry.Name}");
                }
            }
        }

        body.Warmode = true;
        body.Combatant = _quarry;

        if (casting)
        {
            if (_aiming && body.Target != null)
            {
                _aiming = false;

                if (BotStrike.Aim(body, _quarry))
                {
                    _casts++;
                }

                return BotDoing.Work($"casting at {_quarry.Name}");
            }

            if (body.Spell != null)
            {
                return BotDoing.Work("casting");
            }

            if (_cast && Core.TickCount - _castTick < BotStrike.CastMs)
            {
                return BotDoing.Work($"between spells at {_quarry.Name}");
            }

            var spell = BotStrike.Best(body);

            if (spell >= 0 && BotStrike.Begin(body, spell))
            {
                _aiming = true;
                _cast = true;
                _castTick = Core.TickCount;

                return BotDoing.Work($"casting at {_quarry.Name}");
            }

            if (!_complained)
            {
                _complained = true;

                logger.Information(
                    "{Name} could not throw a spell at {What}: {Why} — best {Spell}, {Mana} of {Pool} mana, book holds {Known}",
                    body.Name,
                    _quarry.Name,
                    BotStrike.Why(body),
                    spell,
                    body.Mana,
                    body.ManaMax,
                    BotGrimoire.Count(body)
                );
            }
        }

        BotQuarry.Claim(body, _quarry);

        return BotDoing.Work($"fighting {_quarry.Name}");
    }

    private BotDoing Looting(IBotWilful bot, Mobile body)
    {
        body.Combatant = null;
        body.Warmode = false;

        if (!body.InRange(_fell, BotQuarry.LootReach))
        {
            return BotDoing.Walk(_map, _fell, BotArrival.Within(BotQuarry.LootReach), "to the corpse");
        }

        if (!BotQuarry.Ours(body, _quarry))
        {
            return BotDoing.Done($"{_quarry?.Name} was somebody else's fight");
        }

        var corpse = BotQuarry.Remains(_map, _fell, _quarry);

        if (corpse == null)
        {
            return BotDoing.Done("nothing left on it");
        }

        _hides = Skin(body, corpse);

        var squad = (bot as IBotSquadMember)?.Squad;

        if (squad != null)
        {
            _taken = BotSpoils.Share(squad, (IBotSquadMember)bot, corpse);

            BotQuarry.Release(_quarry);

            return BotDoing.Done($"{_quarry?.Name} split {_taken} ways with the squad");
        }

        Take(bot, body, corpse);

        BotQuarry.Paid(_quarry?.GetType(), _coins);

        BotQuarry.Release(_quarry);

        return BotDoing.Done(
            (_casts > 0, _hides > 0) switch
            {
                (true, true) =>
                    $"{_taken} things and {_coins}gp off {_quarry?.Name}, {_hides} leather, {_casts} spells thrown",
                (true, false) => $"{_taken} things and {_coins}gp off {_quarry?.Name}, {_casts} spells thrown",
                (false, true) => $"{_taken} things and {_coins}gp off {_quarry?.Name}, {_hides} leather",
                (false, false) => $"{_taken} things and {_coins}gp off {_quarry?.Name}"
            }
        );
    }

    public static int Skin(Mobile body, Corpse corpse)
    {
        if (corpse.Carved || corpse.IsCriminalAction(body))
        {
            return 0;
        }

        var blade = Blade(body);

        if (blade == null)
        {
            return 0;
        }

        corpse.Carve(body, blade);

        var shears = body.Backpack?.FindItemByType<Scissors>();

        if (shears == null)
        {
            return 0;
        }

        var cut = 0;

        List<Item> lying = [.. corpse.Items];

        for (var i = 0; i < lying.Count; i++)
        {
            if (lying[i] is not (BaseHides and IScissorable hide))
            {
                continue;
            }

            var was = lying[i].Amount;

            if (hide.Scissor(body, shears))
            {
                cut += was;
            }
        }

        return cut;
    }

    private static Item Blade(Mobile body)
    {
        if (body.Weapon is Item held and (BaseKnife or BaseSword))
        {
            return held;
        }

        var pack = body.Backpack;

        if (pack == null)
        {
            return null;
        }

        List<Item> carried = pack.Items;

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (item is BaseKnife or BaseSword && !item.Deleted)
            {
                return item;
            }
        }

        return null;
    }

    private void Take(IBotWilful bot, Mobile body, Corpse corpse)
    {
        var (taken, coins, made) = Rifle(bot, body, corpse);

        _taken += taken;
        _coins += coins;
        _made += made;
    }

    public static (int Taken, int Coins, int Made) Rifle(IBotWilful bot, Mobile body, Corpse corpse) =>
        Rifle(bot, body, corpse, item => corpse.CheckLoot(body, item));

    public static (int Taken, int Coins, int Made) Rifle(
        IBotWilful bot, Mobile body, Container from, Func<Item, bool> may
    )
    {
        var taken = 0;
        var coins = 0;
        var made = 0;

        var pack = body.Backpack;

        if (pack == null)
        {
            return (0, 0, 0);
        }

        var ceiling = BotLadder.Ceiling(body) * FillFraction;

        List<Item> lying = [.. from.Items];

        for (var i = 0; i < lying.Count; i++)
        {
            var item = lying[i];

            if (item == null || item.Deleted || !item.Movable || may?.Invoke(item) == false)
            {
                continue;
            }

            if (item is Gold coin)
            {
                coins += coin.Amount;

                pack.DropItem(coin);

                continue;
            }

            if (BotLadder.Load(body) >= ceiling)
            {
                break;
            }

            pack.DropItem(item);

            taken++;

            if (item.GetType() == bot.Bond?.Weapon?.Ammunition)
            {
                continue;
            }

            if (BotOven.Spares(body, item))
            {
                continue;
            }

            if (BotFletching.Spares(body, item))
            {
                continue;
            }

            var floor = BotShops.Buyer(body, item, out var offered) != null ? offered : 1;
            var worth = BotAuction.Worth(item.GetType(), Math.Max(1, floor));

            if (BotAuction.List(bot, item, worth) != null)
            {
                made += worth * (item.Amount > 0 ? item.Amount : 1);
            }
        }

        return (taken, coins, made);
    }

    public override bool Bend(IBotWilful bot)
    {
        BotQuarry.Shun(_quarry);

        return false;
    }

    public override void Drop(IBotWilful bot)
    {
        BotQuarry.Release(_quarry);

        if (bot?.Self is BotMobile { Class.Closes: true } closer)
        {
            closer.Draw(melee: false);
        }
    }

    public static long Asked { get; private set; }

    public static long Handless { get; private set; }

    public static long Distant { get; private set; }

    public static long Rushed { get; private set; }

    public static long Kited { get; private set; }

    public static long Longest { get; private set; }

    public static long Closed { get; private set; }

    public static string Bows() =>
        Asked == 0
            ? "nobody has been offered a kite"
            : $"{Asked} asked: {Handless} were melee and have no kite to give, {Kited} gave ground, {Distant} were far enough off to simply shoot, {Rushed} had the shot too near, {Closed} drew steel instead — longest clock seen {Longest}ms against {StillMs + KiteSlackMs}ms needed; "
              + $"{Steady} of them had been standing still long enough for the engine to loose an arrow, longest stillness seen {Stillest}ms against the {StillMs}ms it wants";

    public static void ForgetBows()
    {
        Asked = 0;
        Handless = 0;
        Distant = 0;
        Rushed = 0;
        Kited = 0;
        Longest = 0;
        Closed = 0;
        Stillest = 0;
        Steady = 0;
    }

    public static long Stillest { get; private set; }

    public static long Steady { get; private set; }

    private bool Kiting(Mobile body, int reach)
    {
        Asked++;

        var still = Core.TickCount - body.LastMoveTime;

        if (still > Stillest)
        {
            Stillest = still;
        }

        if (still >= StillMs)
        {
            Steady++;
        }

        if (reach <= 1 || (body.Weapon?.MaxRange ?? 1) <= 1)
        {
            Handless++;

            return false;
        }

        if (!body.InRange(_quarry.Location, KiteWithin))
        {
            Distant++;

            return false;
        }

        var left = body.NextCombatTime - Core.TickCount;

        if (left > Longest)
        {
            Longest = left;
        }

        if (left <= StillMs + KiteSlackMs)
        {
            Rushed++;

            return false;
        }

        Kited++;

        return true;
    }

    private static Point3D Away(Mobile body, Mobile from, int want)
    {
        var map = body.Map;

        if (map == null)
        {
            return Point3D.Zero;
        }

        var dx = body.X - from.X;
        var dy = body.Y - from.Y;

        if (dx == 0 && dy == 0)
        {
            return Point3D.Zero;
        }

        var step = Math.Max(Math.Abs(dx), Math.Abs(dy));

        var mark = from.Location;

        mark.Z += Eye;

        for (var d = want; d >= 1; d--)
        {
            var x = from.X + dx * d / step;
            var y = from.Y + dy * d / step;

            if (!BotStep.Settle(map, x, y, out var z))
            {
                continue;
            }

            if (map.LineOfSight(new Point3D(x, y, z + Eye), mark))
            {
                return new Point3D(x, y, z);
            }
        }

        return Point3D.Zero;
    }

    private bool Down() =>
        _quarry == null || _quarry.Deleted || !_quarry.Alive || _quarry.Map != _map;
}
