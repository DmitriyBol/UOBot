using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// What a bot is built of, as far as a fight is concerned: enough to say whether a measurement made last hour still
/// describes it. See <see cref="BotProving.Fresh"/>.
/// </summary>
public readonly record struct BotBuild(int Skills, int Stats, int Armour, string Weapon, int Damage)
{
    private static readonly SkillName[] Fighting =
    [
        SkillName.Swords, SkillName.Macing, SkillName.Fencing, SkillName.Archery, SkillName.Wrestling,
        SkillName.Tactics, SkillName.Anatomy, SkillName.Healing, SkillName.Parry,
        SkillName.Magery, SkillName.EvalInt, SkillName.Meditation, SkillName.MagicResist
    ];

    public static BotBuild Of(Mobile m)
    {
        if (m == null)
        {
            return default;
        }

        var skills = 0.0;

        for (var i = 0; i < Fighting.Length; i++)
        {
            skills += m.Skills[Fighting[i]]?.Base ?? 0.0;
        }

        BaseWeapon blade = null;
        BaseRanged bow = null;

        Consider(m.Weapon as BaseWeapon, ref blade, ref bow);

        if (m.Backpack != null)
        {
            foreach (var weapon in m.Backpack.FindItemsByType<BaseWeapon>())
            {
                Consider(weapon, ref blade, ref bow);
            }
        }

        var damage = Math.Max(blade == null ? 0 : Average(blade), bow == null ? 0 : Average(bow));
        var arms = (blade?.GetType().Name ?? "Fists") + (bow == null ? "" : $"+{bow.GetType().Name}");

        return new BotBuild(
            (int)skills,
            m.RawStr + m.RawDex + m.RawInt,
            (int)m.ArmorRating,
            arms,
            damage
        );
    }

    private static int Average(BaseWeapon weapon) => (weapon.MinDamage + weapon.MaxDamage) / 2;

    private static void Consider(BaseWeapon weapon, ref BaseWeapon blade, ref BaseRanged bow)
    {
        if (weapon == null || weapon.Deleted || weapon is Fists)
        {
            return;
        }

        if (weapon is BaseRanged ranged)
        {
            if (bow == null || Average(ranged) > Average(bow))
            {
                bow = ranged;
            }

            return;
        }

        if (blade == null || Average(weapon) > Average(blade))
        {
            blade = weapon;
        }
    }

    public override string ToString() => $"{Skills} in the fighting skills, {Stats} of stats, armour {Armour}, {Weapon} ({Damage})";
}

/// <summary>
/// One fight on the proving ground: a bot's double against one creature, refereed to the end and read.
///
/// <para>
/// <b>Patrick's order of 26.09.2026: "the bots dress, train and grow stronger, and nobody goes past the Orc Caves. I want
/// Argus to spawn a copy of the bot and a creature in Green Acres and judge the bot's strength — counting only the weapon
/// and the hit points is not enough; the skills, the weapon's properties and everything else must count."</b> The number
/// the shard judged every fight by was <c>BotThreat.Power</c> — health times the weapon's average hit, plus half the
/// Magery — which cannot see armour, hit chance, swing speed, a bandage, a bottle or a poisoned blade. The only
/// measurement that sees all of them at once is the fight itself, so this holds one.
/// </para>
///
/// <para>
/// <b>The double fights the way the bot does, with the bot's own hands.</b> The swinging is the engine's (Combatant and
/// Warmode start its timer on the weapon's own delay); the spells are <see cref="BotStrike"/>'s ladder, thrown and aimed as
/// <c>BotSlay</c> throws them; the bandage, the bottle and the healing spell are <see cref="BotMend"/>'s, at the same
/// shares of health the population reaches for them. What it does not do is decide: it never flees, never goes home to
/// bank, never takes a better offer. It stands and fights until one of them is down, which is the question.
/// </para>
///
/// <para>
/// <b>What is read, and why it is one number.</b> A fight is two rates racing: how fast the bot takes the creature apart,
/// and how fast the creature takes the bot apart net of what the bot heals. The ratio of the two is R — how many of these
/// creatures the bot's health would pay for, one after another: <c>R = (share of the creature killed) × HitsMax / (damage
/// taken − health given back)</c>. A win for thirty net damage on a hundred hit points is R 3.3; a death with the creature at
/// 60% is R 0.4. R is measured against a creature whose strength on the shard's old scale is known
/// (<see cref="BotDungeon.Might"/>), so R × that is the bot's strength on the same scale — the one every dungeon's worst
/// inhabitant is already ranked by — with everything the formula could not see folded in by the fight itself. A caster's
/// mana is the other half of its health, and for one it is read the same way (<see cref="BotProving"/>).
/// </para>
/// </summary>
public sealed class BotTrial
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTrial));

    public BotTrial(BotMobile bot, string kind, int ring, Map map, Point3D middle, string by)
    {
        Original = bot;
        Bot = bot?.Name;
        Class = bot?.Class?.Name;
        Role = bot?.Class?.Role ?? BotRole.Melee;
        Kind = kind;
        Ring = ring;
        Map = map;
        Middle = middle;
        By = by;
        Build = BotBuild.Of(bot);
        PowerThen = BotThreat.Power(bot);
        Might = BotDungeon.Might(kind);
    }

    public BotMobile Original { get; }

    public string Bot { get; }

    public string Class { get; }

    public BotRole Role { get; }

    public string Kind { get; }

    public int Ring { get; }

    public Map Map { get; }

    public Point3D Middle { get; }

    public string By { get; }

    public BotBuild Build { get; }

    public double PowerThen { get; }

    public double Might { get; }

    public BotStandIn Double { get; private set; }

    public string Held { get; private set; }

    public BaseCreature Foe { get; private set; }

    public long Began { get; private set; }

    public string Verdict { get; private set; }

    public double Killed { get; private set; }

    public int HitsMax { get; private set; }

    public int ManaMax { get; private set; }

    public int Dealt { get; private set; }

    public int Mended { get; private set; }

    public int Spent { get; private set; }

    public int Casts => _driver?.Casts ?? 0;

    public int Heals => _driver?.Heals ?? 0;

    public int Bandages => _driver?.Bandages ?? 0;

    public int Bottles => _driver?.Bottles ?? 0;

    public int Steps => _driver?.Steps ?? 0;

    public double Seconds { get; private set; }

    public double Net { get; private set; }

    public double R { get; private set; }

    public double Strength => R * Might;

    public string Refused { get; private set; }

    private int _foeLast;

    private int _manaLast;

    private long _changedTick;

    private int _hitsLast;

    private BotDriver _driver;

    private int _heldBeats;

    private Type _ammo;

    private int _ammoStart = -1;

    public int Shots { get; private set; } = -1;

    public double HeldSeconds => _heldBeats * (double)BotProving.TickMs / 1000.0;

    private bool _tracing;

    private long _traceTick;

    private void Trace(BotStandIn me, BaseCreature foe, long now)
    {
        if (!_tracing || me is not { Deleted: false } || foe is not { Deleted: false } || now - _traceTick < 1000)
        {
            return;
        }

        _traceTick = now;

        logger.Information(
            "Proving trace: {Bot} +{Seconds}s holding {Weapon} ({Ammo} left), swing in {Swing}ms, still {Still}ms, {Distance} tiles, sees {Sees}, in sight {Los}, paralysed {Paralysed}, frozen {Frozen}, combatant {Combatant}, warmode {Warmode}, hits {Hits}; the {Kind} {FoeHits}/{FoeMax}, its combatant {FoeCombatant}, casting {Casting}",
            Bot,
            (now - Began) / 1000,
            me.Weapon?.GetType().Name ?? "nothing",
            _ammo == null ? -1 : me.Backpack?.GetAmount(_ammo) ?? 0,
            me.NextCombatTime - now,
            now - me.LastMoveTime,
            (int)me.GetDistanceToSqrt(foe),
            me.CanSee(foe),
            me.InLOS(foe),
            me.Paralyzed,
            me.Frozen,
            me.Combatant == foe ? "the foe" : me.Combatant?.Name ?? "none",
            me.Warmode,
            me.Hits,
            Kind,
            foe.Hits,
            foe.HitsMax,
            foe.Combatant == me ? "the double" : foe.Combatant?.Name ?? "none",
            foe.Spell?.GetType().Name ?? "no"
        );
    }

    private Point3D Side(int sign) =>
        new(Middle.X + sign * BotProving.Apart, Middle.Y, Middle.Z);

    public bool Begin()
    {
        if (Original is not { Deleted: false } || Map == null || Map == Map.Internal)
        {
            Refused = "the bot is gone";

            return false;
        }

        if (Might <= 0.0)
        {
            Refused = $"{Kind} is nothing the engine can build";

            return false;
        }

        var type = AssemblyHandler.FindTypeByName(Kind);

        if (type == null || !typeof(BaseCreature).IsAssignableFrom(type))
        {
            Refused = $"{Kind} is not a creature";

            return false;
        }

        BaseCreature foe;

        try
        {
            foe = Activator.CreateInstance(type) as BaseCreature;
        }
        catch (Exception e)
        {
            Refused = $"{Kind} would not be built: {e.GetType().Name}";

            return false;
        }

        if (foe == null)
        {
            Refused = $"{Kind} would not be built";

            return false;
        }

        var me = BotStandIn.Copy(Original, Map, Side(-1));

        if (me == null)
        {
            foe.Delete();
            Refused = "the double could not be made";

            return false;
        }

        foe.Home = Middle;
        foe.RangeHome = BotProving.Leash;
        foe.MoveToWorld(Side(1), Map);

        Double = me;
        Foe = foe;
        Held = me.Holding();

        if (me.Weapon is BaseRanged { AmmoType: { } ammo })
        {
            _ammo = ammo;
            _ammoStart = me.Backpack?.GetAmount(ammo) ?? 0;

            if (BotProving.TraceRanged > 0)
            {
                BotProving.TraceRanged--;
                _tracing = true;
                _traceTick = Core.TickCount - 1000;
            }
        }
        HitsMax = me.HitsMax;
        ManaMax = me.ManaMax;
        _foeLast = foe.Hits;
        _manaLast = me.Mana;
        _hitsLast = me.Hits;
        Began = Core.TickCount;
        _changedTick = Began;
        _driver = new BotDriver(me, Began);

        me.Direction = me.GetDirectionTo(foe);
        me.Warmode = true;
        me.Combatant = foe;
        foe.Warmode = true;
        foe.Combatant = me;

        return true;
    }

    public bool Drive()
    {
        if (Verdict != null)
        {
            return true;
        }

        var me = Double;
        var foe = Foe;
        var now = Core.TickCount;

        Read(me, foe, now);

        if (foe is not { Deleted: false } || !foe.Alive)
        {
            return Decide("won");
        }

        if (me is not { Deleted: false } || !me.Alive)
        {
            return Decide("lost");
        }

        if (now - Began >= BotProving.CapMs)
        {
            return Decide("called at the cap");
        }

        if (now - _changedTick >= BotProving.StillMs)
        {
            return Decide("called: nobody hurt anybody");
        }

        _driver.Answer(foe, now);

        return false;
    }

    private void Read(BotStandIn me, BaseCreature foe, long now)
    {
        if (foe is { Deleted: false })
        {
            var hits = foe.Alive ? foe.Hits : 0;

            if (hits < _foeLast)
            {
                Dealt += _foeLast - hits;
                _changedTick = now;
            }
            else if (hits > _foeLast)
            {
                Mended += hits - _foeLast;
            }

            _foeLast = hits;
        }

        if (me is { Deleted: false })
        {
            if (me.Paralyzed || me.Frozen)
            {
                _heldBeats++;
            }

            if (_ammo != null && me.Alive)
            {
                Shots = Math.Max(0, _ammoStart - (me.Backpack?.GetAmount(_ammo) ?? 0));
            }

            Trace(me, foe, now);

            if (me.Mana < _manaLast)
            {
                Spent += _manaLast - me.Mana;
            }

            _manaLast = me.Mana;

            var hits = me.Alive ? me.Hits : 0;

            if (hits < _hitsLast)
            {
                _changedTick = now;
            }

            _hitsLast = hits;
        }
    }

    private bool Decide(string verdict)
    {
        Verdict = verdict;
        Seconds = (Core.TickCount - Began) / 1000.0;

        var foe = Foe;

        Killed = foe is not { Deleted: false } || !foe.Alive
            ? 1.0
            : foe.HitsMax <= 0
                ? 0.0
                : Math.Clamp(1.0 - foe.Hits / (double)foe.HitsMax, 0.0, 1.0);

        var me = Double;
        var taken = me?.Taken ?? 0;
        var healed = me?.Healed ?? 0;

        Net = Math.Max(taken - healed, BotProving.NetFloor * Math.Max(1, HitsMax));

        var health = Killed * Math.Max(1, HitsMax) / Net;

        var mana = double.MaxValue;

        if (Role == BotRole.Caster && ManaMax > 0 && Spent >= ManaMax * BotProving.ManaShare)
        {
            mana = Killed * ManaMax / Spent;
        }

        R = Math.Min(BotProving.RMax, Math.Min(health, mana));

        return true;
    }

    public void End()
    {
        var map = Map;

        if (Double is { Deleted: false } me)
        {
            me.Combatant = null;
            me.Warmode = false;
        }

        if (Foe is { Deleted: false } foe)
        {
            foe.Combatant = null;
            foe.Delete();
        }

        Double?.Delete();

        if (map == null || map == Map.Internal)
        {
            return;
        }

        BotProving.Sweep(map, Middle, BotProving.Leash + BotProving.Apart + 4);
    }

    public string Say()
    {
        var what = Verdict == "won"
            ? $"won in {Seconds:F0}s"
            : Verdict == "lost"
                ? $"lost in {Seconds:F0}s with the {Kind} at {1.0 - Killed:P0}"
                : $"{Verdict} after {Seconds:F0}s with the {Kind} at {1.0 - Killed:P0}";

        return $"{Bot} the {Class} ({Build}; the double held {Held}, {Double?.Draws ?? 0} weapon changes) against the {Kind} ({Might:F0} of might): {what}, taking {Double?.Taken ?? 0} and healing "
            + $"{Double?.Healed ?? 0} of {HitsMax}, {Dealt} dealt; {Casts} spells, {Heals} healing spells, {Bandages} bandages, "
            + $"{Bottles} bottles, {Spent} mana; R {R:F2}, which is {Strength:F0} of strength against the {Power:F0} the old formula gives it; "
            + $"the double took {Steps} steps{(Shots >= 0 ? $", fired {Shots}" : "")}{(HeldSeconds >= 1.0 ? $" and stood paralysed {HeldSeconds:F0}s" : "")}";
    }

    private double Power => PowerThen;
}
