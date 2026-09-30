using System;
using System.Collections.Generic;
using System.Diagnostics;
using Server.Guilds;
using Server.Items;
using Server.Logging;
using Server.Misc;
using Server.Mobiles;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// An autonomous inhabitant of the shard. <b>The object all seven other subsystems were written against
/// and waiting for.</b>
///
/// <para>
/// <b>It derives from <see cref="PlayerMobile"/> rather than <c>BaseCreature</c>, and that single choice is
/// what makes this project's measure of work possible.</b> Every player system applies without being
/// reimplemented: use-based skill gain, fame and karma, banking, criminal flags, a lootable corpse. Skill
/// gain is the point — the whole decision layer values work by the skill it produces, and skill here is the
/// engine raising a number after its own check, not us deciding a number should go up. On a creature there
/// would be nothing to measure and the metric would have to be invented.
/// </para>
///
/// <para>
/// The cost is that nothing drives it: <see cref="PlayerMobile"/> has no think loop. <see cref="BotBeat"/>
/// supplies one, and <see cref="Beat"/> below is the whole of what a bot does per turn — three calls, in an
/// order that matters.
/// </para>
///
/// <para>
/// <b>Nothing here holds a <c>NetState</c>.</b> The engine tolerates that, and one consequence is load
/// bearing: <see cref="Mobile.Move"/> skips its movement throttle when the net state is null, so a bot's
/// pace is set purely by how often it is beaten. Content that assumes a connected client is the standing
/// hazard of this approach, and the reason the population starts small.
/// </para>
///
/// <para>
/// <b>Saved bots are not reused.</b> The world save will contain these — they are Mobiles like any other —
/// so the population is purged and rebuilt on every world load. See
/// <see cref="BotPopulation.PurgeSaved"/> for why that is the honest choice rather than laziness: half the
/// state a bot needs lives in objects that would have to be rebuilt anyway, and a kit handed out twice is a
/// bot with two of everything.
/// </para>
/// </summary>
public class BotMobile : PlayerMobile, IBotWilful, IBotAside
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMobile));

    public static double FitFraction { get; set; } = 0.25;

    public static int NoticeRange { get; set; } = 10;

    public static int ChampionHue { get; set; } = 0x35;

    public static bool Runs { get; set; }

    public BotMobile(Serial serial) : base(serial)
    {
    }

    public BotMobile()
    {
    }

    public Mobile Self => this;

    public BotClass Class { get; private set; }

    public BotJourney Journey { get; } = new();

    public BotWalkResult LastWalk { get; private set; }

    private (int Map, int X, int Y) _quadWas;

    private Point3D _quadAt;

    private bool _quadSeen;

    private bool _quadClean;

    public BotResolve Resolve { get; } = new();

    public BotSquad Squad { get; set; }

    public BotBond Bond { get; private set; }

    public string Was { get; private set; }

    public string WasDoing { get; private set; }

    public Map WasDoingOn { get; private set; }

    public Point3D WasDoingAt { get; private set; }

    public bool AbleToFight =>
        !Deleted && Alive && Map != null && Map != Map.Internal && Hits >= HitsMax * FitFraction;

    public long DueTick { get; internal set; }

    public bool Scheduled { get; internal set; }

    public bool Fallen { get; private set; }

    public static long BackAsGhost { get; private set; }

    public bool ReviveComplained { get; set; }

    public Corpse Remains { get; set; }

    public int Refusals { get; set; }

    public bool Tired { get; set; }

    public DateTime WellRestedUntil { get; set; }

    public BotResidence.Record Residence { get; set; }

    public long FellTick { get; private set; }

    public double Progress
    {
        get
        {
            var klass = Class;

            if (klass == null)
            {
                return 0.0;
            }

            var wanted = 0.0;
            var reached = 0.0;
            var wants = klass.Skills;

            for (var i = 0; i < wants.Count; i++)
            {
                var (skill, target) = wants[i];

                if (target <= 0.0)
                {
                    continue;
                }

                wanted += target;
                reached += Math.Min(target, Skills[skill].Base);
            }

            var weapon = Bond?.Weapon;

            if (weapon != null && weapon.Value.Target > 0.0)
            {
                wanted += weapon.Value.Target;
                reached += Math.Min(weapon.Value.Target, Skills[weapon.Value.Skill].Base);
            }

            return wanted <= 0.0 ? 0.0 : reached / wanted;
        }
    }

    public double Mood
    {
        get
        {
            if (Class is { Grieves: true })
            {
                return Grief();
            }

            var urges = Resolve.Urges;

            return Math.Clamp(1.0 - (urges.Boredom + urges.Need) / 2.0, 0.0, 1.0);
        }
    }

    private double Grief()
    {
        var map = Map;

        if (map == null || map == Map.Internal)
        {
            return 1.0;
        }

        var standing = BotPeril.Unavenged(map, Location, BotPopulation.Roam);

        return Math.Clamp(1.0 - standing / (double)Math.Max(1, GriefAt), 0.0, 1.0);
    }

    public static int GriefAt { get; set; } = 4;

    public void Become(BotClass klass, string name, bool female) => Become(klass, name, female, null);

    public BotPeoples.People People => BotPeoples.Of(Name);

    public void Become(BotClass klass, string name, bool female, BotPeoples.People people)
    {
        if (klass == null)
        {
            return;
        }

        Player = true;

        Class = klass;

        Name = name;
        Female = female;

        Race = people?.Race ?? Race.Human;
        Body = female ? Race.FemaleBody : Race.MaleBody;
        Hue = Race.RandomSkinHue();
        HairItemID = Race.RandomHair(female);
        HairHue = Race.RandomHairHue();

        if (people is { Beards: true } && !female)
        {
            FacialHairItemID = Race.RandomFacialHair(false);
            FacialHairHue = HairHue;
        }

        AddItem(new Backpack { Movable = false });

        Build(klass);
        Learn(klass);
        LearnCommon();

        BotGuilds.Enrol(this);

        Bond = BotOutfit.Give(this, klass);

        Clothe();
        LearnWeapon();

        Hits = HitsMax;
        Stam = StamMax;
        Mana = ManaMax;

        if (klass.Seasoned)
        {
            var sb = ValueStringBuilder.Create(160);

            try
            {
                Recite(ref sb, klass);

                logger.Information(
                    "{Name} was raised a seasoned {Class}, holding its trade already: {Skills}",
                    Name,
                    klass.Name,
                    sb.ToString()
                );
            }
            finally
            {
                sb.Dispose();
            }
        }
    }

    private void Recite(ref ValueStringBuilder sb, BotClass klass)
    {
        var wanted = klass.Skills;
        var said = 0;

        for (var i = 0; i < wanted.Count; i++)
        {
            var skill = Skills[wanted[i].Skill];

            if (skill == null)
            {
                continue;
            }

            if (said++ > 0)
            {
                sb.Append(", ");
            }

            sb.Append(skill.Info.Name);
            sb.Append(' ');
            sb.Append(skill.Base, "F1");
        }

        if (said == 0)
        {
            sb.Append("nothing at all, which is a defect");
        }
    }

    private void Unfixate()
    {
        if (Resolve.Deed != null || Combatant is not Mobile foe)
        {
            return;
        }

        var reach = Math.Max(1, Weapon?.MaxRange ?? 1);

        if (foe is { Deleted: false, Alive: true } && foe.Map == Map && InRange(foe.Location, reach))
        {
            return;
        }

        Combatant = null;
        Warmode = false;
        Unfixated++;
    }

    public static long Unfixated { get; private set; }

    public void Beat()
    {
        if (Deleted || !Alive || Map == null || Map == Map.Internal)
        {
            return;
        }

        BotPurse.Bank(this);

        BotStipend.Keep(this);

        BotAuction.Fetch(this);

        BotStores.Keep(this);

        BotHaggle.Keep(this);

        BotMeal.Keep(this);

        BotInns.Regen(this);

        BotPets.Keep(this);

        BotTidy.Keep(this);

        BotStable.Keep(this);
        BotStable.Ride(this);

        Dress();

        Trickle();

        Gasp();

        Unfixate();

        Rank();

        var began = System.Diagnostics.Stopwatch.GetTimestamp();

        BotWill.Decide(this);

        var decided = System.Diagnostics.Stopwatch.GetTimestamp();

        BotWill.Spent(decided - began);

        var result = BotWalk.Advance(this, Journey, Running);

        LastWalk = result;

        BotWalk.Spent(System.Diagnostics.Stopwatch.GetTimestamp() - decided);

        Cross();

        BotWill.Note(this, result);

        switch (result)
        {
            case BotWalkResult.Refused:
            case BotWalkResult.GaveUp:
                {
                    if (++Refusals >= BotPopulation.StrandedLimit)
                    {
                        BotPopulation.Rescue(this);
                    }

                    break;
                }
            case BotWalkResult.Stepped:
            case BotWalkResult.Arrived:
            case BotWalkResult.Improvised:
                {
                    Refusals = 0;

                    break;
                }
        }
    }

    private void Trickle()
    {
        var klass = Class;

        if (klass == null || Mana >= ManaMax)
        {
            return;
        }

        if (Core.TickCount - _trickledTick < BotClass.ManaTrickleIntervalMs)
        {
            return;
        }

        _trickledTick = Core.TickCount;

        var given = klass.ManaTrickle(Weapon is BaseStaff);

        if (given > 0)
        {
            Mana = Math.Min(ManaMax, Mana + given);
        }
    }

    private long _trickledTick;

    public static double Critical { get; set; } = 0.15;

    public static long Gasps { get; private set; }

    public static long Drank { get; private set; }

    public static long Dry { get; private set; }

    public static long Refused { get; private set; }

    public static long Freed { get; private set; }

    public static void ForgetGasps()
    {
        Gasps = 0;
        Drank = 0;
        Dry = 0;
        Refused = 0;
        Freed = 0;
    }

    public static string DescribeGasps() =>
        Gasps == 0
            ? "nobody has been under a sixth of their health"
            : $"{Gasps} times a bot fell under {Critical:P0} health: {Drank} bottles swallowed ({Freed} of them after putting the weapon away), {Dry} found no bottle in the pack, {Refused} were refused by the engine";

    private bool _gasping;

    private void Gasp()
    {
        if (HitsMax <= 0 || Hits > HitsMax * Critical)
        {
            _gasping = false;

            return;
        }

        if (!_gasping)
        {
            _gasping = true;
            Gasps++;
        }

        var bottle = BotMend.Bottle(Backpack, BotPotionKind.Heal);

        if (bottle == null)
        {
            Dry++;

            return;
        }

        if (!BotMend.Swallow(this, bottle))
        {
            if (Stow() && BotMend.Swallow(this, bottle))
            {
                Drank++;
                Freed++;

                return;
            }

            Refused++;

            return;
        }

        Drank++;

        logger.Information(
            "{Name} was at {Share:P0} and drank a bottle where it stood",
            Name,
            Hits / (double)HitsMax
        );
    }

    public override bool IsHarmfulCriminal(Mobile target) =>
        !BotDuel.Between(this, target) && base.IsHarmfulCriminal(target);

    public override void OnDamage(int amount, Mobile from, bool willKill)
    {
        base.OnDamage(amount, from, willKill);

        if (from == null || from == this || Deleted || !Alive || willKill)
        {
            return;
        }

        BotWill.Hurt(this);
        BotSquads.Note(this, from);
        BotVoice.Struck(this, from);

        BotPeril.Struck(Map, Location);

        BotQuad.Struck(Map, Location, false);

        _quadClean = false;

        BotStable.Throw(this);

        Resolve.Bruise(Core.TickCount);

        Warmode = true;
        Combatant = from;

        if (BotLadder.Failing(this))
        {
            return;
        }

        if (Resolve.Deed is BotBolt)
        {
            return;
        }

        var stand = BotThreat.Decide(this, NoticeRange);

        if (stand == BotStand.Nothing)
        {
            return;
        }

        if (stand != BotStand.Fight)
        {
            return;
        }

        Mobile quarry = BotThreat.Strongest(this, NoticeRange) ?? from;

        Engage(quarry);
    }

    private void Engage(Mobile quarry)
    {
        if (quarry is not { Deleted: false, Alive: true } || quarry.Map != Map)
        {
            return;
        }

        Combatant = quarry;
        Warmode = true;

        if (!ReferenceEquals(Journey.Current?.Follow, quarry))
        {
            Journey.Interrupt(Map, quarry, BotArrival.Beside, "fight");
        }
    }

    private bool Stow()
    {
        var pack = Backpack;

        if (pack == null)
        {
            return false;
        }

        var freed = false;

        for (var i = 0; i < TwoHands.Length; i++)
        {
            var held = FindItemOnLayer(TwoHands[i]);

            if (held == null)
            {
                continue;
            }

            if (pack.TryDropItem(this, held, false))
            {
                freed = true;
            }
        }

        return freed;
    }

    private static readonly Layer[] TwoHands = [Layer.OneHanded, Layer.TwoHanded];

    public override void AddNameProperties(IPropertyList list)
    {
        if (Guild is not Guild guild || string.IsNullOrEmpty(guild.Abbreviation))
        {
            base.AddNameProperties(list);

            return;
        }

        var title = PropertyTitle && !string.IsNullOrEmpty(Title) ? $" {Title}" : " ";

        list.Add(1050045, $"[{guild.Abbreviation.FixHtmlFormattable()}] \t{Name ?? " "}\t{ApplyNameSuffix(title)}");

        if (DisplayGuildTitle)
        {
            list.Add(guild.Name.FixHtml());
        }
    }

    public override bool Move(Direction d) => BotOutlaw.Steps(this, d) && base.Move(d);

    public override void OnDeath(Container c)
    {
        base.OnDeath(c);

        var fellAt = Location;
        var fellMap = Map;

        (Resolve?.Deed as BotBolt)?.Died(this, fellAt);

        Fallen = true;
        FellTick = Core.TickCount;
        ReviveComplained = false;

        BotResidence.Fell(this, Location);

        Remains = c as Corpse;

        BotOutlaw.Fell(this, LastKiller);

        logger.Information(
            "{Name} the {Class} was killed at {Where}; it should rise again in {Wait}s",
            Name,
            Class?.Name,
            Location,
            BotPopulation.ReviveMs / 1000
        );

        BotBinding.TrimAmmunition(this, Bond, c);

        BotWill.Died(this);

        var felledBy = LastKiller;

        BotBarred.Fell(fellMap, fellAt, felledBy, $"{Name} the {Class?.Name}");

        BotEvents.Post(
            "death",
            this,
            $"killed by {felledBy?.Name ?? "something"} at ({fellAt.X}, {fellAt.Y})",
            null,
            BotJson.Object(
                w =>
                {
                    w.WriteString("killer", felledBy?.Name);
                    w.WriteString("killerKind", felledBy?.GetType().Name);
                    w.WriteString("class", Class?.Name);
                }
            )
        );

        BotVoice.Died(this, felledBy);

        BotKept.Fell(this, fellMap, fellAt);

        BotZones.Fell(fellMap, fellAt);

        BotSquads.Leave(this);

        if ((c as Corpse)?.Killer is BotMobile { Deleted: false } slayer && slayer != this)
        {
            BotRegard.Killed((slayer.Guild as Guilds.Guild)?.Name, (Guild as Guilds.Guild)?.Name);

            BotClaim.Bled(
                Map,
                Location,
                (slayer.Guild as Guilds.Guild)?.Name,
                (Guild as Guilds.Guild)?.Name
            );
        }

        if ((c as Corpse)?.Killer is BotMobile { Deleted: false } killer && killer != this)
        {
            BotQuad.FellToBot();
        }
        else
        {
            BotPeril.Fell(Map, Location);

            BotQuad.Fell(Map, Location, Class is BotBaron ? BotQuad.BaronWorth : BotQuad.DeathWorth);
        }

        Journey.Finish();

        Warmode = false;
    }

    private void Cross()
    {
        if (Map == null || Map == Map.Internal || Deleted)
        {
            return;
        }

        BotQuad.Look(this);

        var now = BotQuad.Key(Map, Location);

        if (!_quadSeen)
        {
            _quadSeen = true;
            _quadWas = now;
            _quadAt = Location;
            _quadClean = true;

            BotQuad.Crossed(Map, Location, Location);

            return;
        }

        if (now == _quadWas)
        {
            return;
        }

        if (_quadClean)
        {
            BotQuad.Crossed(Map, _quadAt, Location);
        }
        else
        {
            BotQuad.Seen(Map, Location);
        }

        _quadWas = now;
        _quadAt = Location;
        _quadClean = true;
    }

    public static double RunAbove { get; set; } = 0.2;

    public bool Running =>
        Runs && StamMax > 0 && Stam > StamMax * RunAbove && (Resolve.Deed?.Hurries ?? true) && !Hidden;

    private const int RankEveryMs = 30000;

    private bool _ranked;

    private long _rankedTick;

    private void Rank()
    {
        var now = Core.TickCount;

        if (_ranked && now - _rankedTick < RankEveryMs)
        {
            return;
        }

        _ranked = true;
        _rankedTick = now;

        BotRank = Titles.GetSkillTitle(this, Earned());
    }

    public string BotRank { get; private set; }

    private Skill Earned()
    {
        Skill best = null;

        for (var i = 0; i < Skills.Length; i++)
        {
            var skill = Skills[i];

            if (skill == null || Granted(skill.SkillName))
            {
                continue;
            }

            if (best == null || skill.BaseFixedPoint > best.BaseFixedPoint)
            {
                best = skill;
            }
        }

        return best;
    }

    public static bool Granted(SkillName skill)
    {
        for (var i = 0; i < Common.Length; i++)
        {
            if (Common[i].Skill == skill)
            {
                return true;
            }
        }

        return false;
    }

    public bool Minded { get; set; }

    public bool Herbed { get; set; }

    public long HerbTick { get; set; }

    public bool Crafted { get; set; }

    public long CraftTick { get; set; }

    public override void ClearHands()
    {
    }

    private static bool _saidDisarmed;

    public static int SayEveryMs { get; set; } = 60000;

    private static bool _saidWorn;

    private static long _wornTick;

    public override void OnItemRemoved(Item item)
    {
        base.OnItemRemoved(item);

        if (Deleted || !Alive || World.Loading)
        {
            return;
        }

        if (_saidDisarmed || item is not BaseWeapon || item.Layer is not (Layer.OneHanded or Layer.TwoHanded))
        {
            return;
        }

        _saidDisarmed = true;

        logger.Information(
            "{Name} has had its {Item} taken out of its hands. Whatever did it is here: {Where}",
            Name,
            item.GetType().Name,
            new StackTrace(false)
        );
    }

    private static bool _saidWorse;

    public override void OnItemAdded(Item item)
    {
        base.OnItemAdded(item);

        if (_saidWorse || Deleted || !Alive || World.Loading || item is not BaseWeapon weapon
            || weapon.Layer is not (Layer.OneHanded or Layer.TwoHanded) || Rank(weapon) != 2)
        {
            return;
        }

        var pack = Backpack;

        if (pack == null)
        {
            return;
        }

        var bar = Worth(weapon) * WeaponMargin;
        var carried = pack.Items;

        for (var i = 0; i < carried.Count; i++)
        {
            if (carried[i] is not BaseWeapon other || (other is BaseRanged) != (weapon is BaseRanged)
                || Rank(other) != 2 || !Suits(other) || Worth(other) <= bar)
            {
                continue;
            }

            _saidWorse = true;

            logger.Information(
                "{Name} has had a {Worse} put in its hands with a better {Better} in its pack. Whatever did it is here: {Where}",
                Name,
                weapon.GetType().Name,
                other.GetType().Name,
                new StackTrace(false)
            );

            return;
        }
    }

    public override void OnAfterResurrect()
    {
        base.OnAfterResurrect();

        Fallen = false;

        BotBinding.Restore(this, Bond);

        Rearm();

        Hits = HitsMax;
        Stam = StamMax;
        Mana = ManaMax;
    }

    public override void OnAfterDelete()
    {
        base.OnAfterDelete();

        BotWill.Forget(this);
        BotSquads.Leave(this);
        BotPopulation.Forget(this);

        Journey.Finish();

        Bond = null;
        Class = null;
        Squad = null;
    }

    public bool StepAsideFor(Mobile asker)
    {
        if (asker == null || Deleted || !Alive || !BotSquads.ShouldYield(this, asker))
        {
            return false;
        }

        var away = BotSquads.YieldAwayFrom(this, asker);

        Direction = away;

        return Move(away);
    }

    private void Build(BotClass klass)
    {
        var str = Math.Max(1, klass.Str);
        var dex = Math.Max(1, klass.Dex);
        var pow = Math.Max(1, klass.Int);

        var cap = StatCap;
        var total = str + dex + pow;

        if (cap > 0 && total > cap)
        {
            var scale = cap / (double)total;

            str = Math.Max(1, (int)(str * scale));
            dex = Math.Max(1, (int)(dex * scale));
            pow = Math.Max(1, (int)(pow * scale));
        }

        RawStr = str;
        RawDex = dex;
        RawInt = pow;
    }

    private void Learn(BotClass klass)
    {
        var wanted = klass.Skills;

        if (wanted == null || wanted.Count == 0)
        {
            return;
        }

        List<(SkillName Skill, double Target, int Declared)> ordered = [];

        for (var i = 0; i < wanted.Count; i++)
        {
            ordered.Add((wanted[i].Skill, wanted[i].Target, i));
        }

        ordered.Sort(
            static (a, b) =>
            {
                var byTarget = b.Target.CompareTo(a.Target);

                return byTarget != 0 ? byTarget : a.Declared.CompareTo(b.Declared);
            }
        );

        for (var i = 0; i < ordered.Count; i++)
        {
            var (skill, target, _) = ordered[i];

            if (klass.Seasoned)
            {
                Skills[skill].Base = Math.Clamp(target * klass.Seasoning, 0.0, target);

                continue;
            }

            var allowance = i < StartingAllowance.Length ? StartingAllowance[i] : 0.0;

            Skills[skill].Base = Math.Min(target, allowance);
        }
    }

    private static readonly double[] StartingAllowance = [50.0, 30.0, 20.0];

    private void LearnCommon()
    {
        for (var i = 0; i < Common.Length; i++)
        {
            var (skill, value) = Common[i];
            var held = Skills[skill];

            if (held != null && value > held.Base)
            {
                held.Base = Math.Clamp(value, 0.0, 100.0);
            }
        }
    }

    public static readonly (SkillName Skill, double Value)[] Common =
    [
        (SkillName.ItemID, 100.0)
    ];

    private void LearnWeapon()
    {
        var weapon = Bond?.Weapon;

        if (weapon == null)
        {
            return;
        }

        var chosen = weapon.Value;

        Skills[chosen.Skill].Base = Class is { Seasoned: true } klass
            ? Math.Clamp(chosen.Target * klass.Seasoning, 0.0, chosen.Target)
            : Math.Min(chosen.Target, StartingAllowance[0]);
    }

    public static int DressEveryMs { get; set; } = 15000;

    public static long LeftToTheFight { get; private set; }

    private bool _dressed;

    private long _dressedTick;

    internal bool Appraised;

    internal long AppraisedTick;

    private void Dress()
    {
        var now = Core.TickCount;

        if (!_dressed)
        {
            _dressed = true;
            _dressedTick = now;

            return;
        }

        if (now - _dressedTick < DressEveryMs)
        {
            return;
        }

        _dressedTick = now;

        Rearm();
    }

    private void Clothe()
    {
        Wear(new Shirt(Utility.RandomDyedHue()));
        Wear(new LongPants(Utility.RandomDyedHue()));
        Wear(new Boots());
    }

    public int Rearm()
    {
        var pack = Backpack;

        if (pack == null || Deleted || !Alive)
        {
            return 0;
        }

        if (Spell != null)
        {
            return 0;
        }

        var worn = 0;
        var refused = 0;

        HashSet<Layer> taken = [];

        for (var i = 0; i < Items.Count; i++)
        {
            taken.Add(Items[i].Layer);
        }

        var held = FindItemOnLayer(Layer.TwoHanded) ?? FindItemOnLayer(Layer.OneHanded);

        using var put = ValueStringBuilder.Create();
        using var left = ValueStringBuilder.Create();

        List<Item> carried = [.. pack.Items];

        Unhand(taken, carried);

        Rewield(taken, carried);

        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < carried.Count; i++)
            {
                var item = carried[i];

                if (item is not (BaseWeapon or BaseArmor or BaseClothing) || item.Deleted || item.Parent != pack)
                {
                    continue;
                }

                if (item.Layer == Layer.TwoHanded != (pass == 0))
                {
                    continue;
                }

                if (taken.Contains(item.Layer) && !Upgrade(item.Layer, carried, pass))
                {
                    continue;
                }

                if (item.Layer == Layer.OneHanded && FindItemOnLayer(Layer.TwoHanded) is BaseWeapon)
                {
                    continue;
                }

                if (item is BaseWeapon && item.Layer == Layer.TwoHanded && taken.Contains(Layer.OneHanded))
                {
                    continue;
                }

                var choice = Pick(carried, item.Layer, pass);

                if (choice == null)
                {
                    continue;
                }

                if (EquipItem(choice))
                {
                    worn++;
                    taken.Add(choice.Layer);

                    if (put.Length > 0)
                    {
                        put.Append(", ");
                    }

                    put.Append(choice.GetType().Name);

                    continue;
                }

                refused++;

                if (left.Length > 0)
                {
                    left.Append(", ");
                }

                left.Append(choice.GetType().Name);
            }
        }

        BotArms.Dressing(worn, refused);

        var now = Core.TickCount;

        if ((worn > 0 || refused > 0) && (!_saidWorn || now - _wornTick >= SayEveryMs))
        {
            _saidWorn = true;
            _wornTick = now;

            logger.Information(
                "{Name} was holding {Held} and put on {Put}{Left}",
                Name,
                held?.GetType().Name ?? "nothing",
                worn > 0 ? put.ToString() : "nothing",
                refused > 0 ? $" and could not wear {left.ToString()}" : ""
            );
        }

        return worn;
    }

    private void Unhand(HashSet<Layer> taken, List<Item> carried)
    {
        if (!taken.Contains(Layer.OneHanded) || taken.Contains(Layer.TwoHanded))
        {
            return;
        }

        if (Combatant is { Deleted: false, Alive: true })
        {
            LeftToTheFight++;

            return;
        }

        var wanted = Pick(carried, Layer.TwoHanded, 0);

        if (wanted == null)
        {
            return;
        }

        var held = FindItemOnLayer(Layer.OneHanded);

        if (held == null || Rank(held) >= Rank(wanted))
        {
            return;
        }

        var pack = Backpack;

        if (pack == null || !pack.TryDropItem(this, held, false))
        {
            return;
        }

        taken.Remove(Layer.OneHanded);

        logger.Information(
            "{Name} put {Held} away so that both hands were free for {Wanted}",
            Name,
            held.GetType().Name,
            wanted.GetType().Name
        );
    }

    public static double WeaponMargin { get; set; } = 1.05;

    public static long Rewielded { get; private set; }

    public static int RewieldRestMs { get; set; } = 300000;

    public static long Reverted { get; private set; }

    private Serial _rewielded;

    private long _rewieldedTick;

    private void Rewield(HashSet<Layer> taken, List<Item> carried)
    {
        var held = FindItemOnLayer(Layer.TwoHanded) as BaseWeapon ?? FindItemOnLayer(Layer.OneHanded) as BaseWeapon;

        if (held == null || Rank(held) != 2)
        {
            return;
        }

        var ranged = held is BaseRanged;
        var was = Worth(held);
        var bar = was * WeaponMargin;
        BaseWeapon better = null;

        for (var i = 0; i < carried.Count; i++)
        {
            if (carried[i] is not BaseWeapon weapon || weapon.Deleted || weapon.Parent != Backpack)
            {
                continue;
            }

            if (weapon is BaseRanged != ranged || Rank(weapon) != 2 || !Suits(weapon))
            {
                continue;
            }

            if (weapon.Layer == Layer.TwoHanded && held.Layer == Layer.OneHanded && taken.Contains(Layer.TwoHanded))
            {
                continue;
            }

            var worth = Worth(weapon);

            if (worth <= bar)
            {
                continue;
            }

            better = weapon;
            bar = worth;
        }

        if (better == null)
        {
            return;
        }

        if (better.Serial == _rewielded && Core.TickCount - _rewieldedTick < RewieldRestMs)
        {
            Reverted++;

            return;
        }

        var pack = Backpack;

        if (pack == null || !pack.TryDropItem(this, held, false))
        {
            return;
        }

        if (!EquipItem(better))
        {
            EquipItem(held);

            return;
        }

        taken.Remove(held.Layer);
        taken.Add(better.Layer);
        Rewielded++;
        _rewielded = better.Serial;
        _rewieldedTick = Core.TickCount;

        logger.Information(
            "{Name} put {Old} away for {New}, worth {Worth:F0} to it against {Was:F0} by damage, skill and swing{Kept}",
            Name,
            held.GetType().Name,
            better.GetType().Name,
            bar,
            was,
            BotBinding.IsBound(held, Bond) ? "; the old one is bound and stays in the pack" : ""
        );
    }

    private bool OwnKind(Item item)
    {
        if (item is not BaseWeapon weapon || Bond?.Weapon is not { } own || Class is not { StaffManaTrickle: 0 })
        {
            return false;
        }

        if (weapon.Skill != own.Skill)
        {
            return false;
        }

        return weapon is BaseRanged bow ? bow.AmmoType == own.Ammunition : own.Ammunition == null;
    }

    private double Worth(BaseWeapon weapon)
    {
        if (weapon == null)
        {
            return 0.0;
        }

        var delay = weapon.GetDelay(this).TotalSeconds;

        if (delay <= 0.0)
        {
            return 0.0;
        }

        var damage = (weapon.MinDamage + weapon.MaxDamage) / 2.0;
        var level = (int)weapon.DamageLevel;

        if (!Core.AOS && level > 0)
        {
            damage += Core.T2A ? 2 * level - 1 : level;
        }

        var tactics = Skills.Tactics.Base;

        if (weapon.UseSkillMod && weapon.AccuracySkill == SkillName.Tactics)
        {
            tactics += 5 * (int)weapon.AccuracyLevel;
        }

        damage += damage * ((tactics - 50.0) / 100.0);

        var modifiers = Str / 5.0 / 100.0 + Skills.Anatomy.Value / 5.0 / 100.0 + weapon.VirtualDamageBonus / 100.0;

        if (Core.UOR && weapon.Type == WeaponType.Axe)
        {
            modifiers += Skills.Lumberjacking.Value / 5.0 / 100.0;
        }

        if (weapon.Quality != WeaponQuality.Regular)
        {
            modifiers += ((int)weapon.Quality - 1) * 0.2;
        }

        damage += damage * modifiers;

        if (weapon.MaxHitPoints > 0 && weapon.HitPoints < weapon.MaxHitPoints)
        {
            damage *= (50.0 + 50.0 * weapon.HitPoints / weapon.MaxHitPoints) / 100.0;
        }

        var lands = Math.Max(0.1, Skills[weapon.Skill].Value + 50.0);

        return damage * lands / delay;
    }

    private Item Pick(List<Item> carried, Layer layer, int pass)
    {
        Item best = null;
        var bestRank = -1;
        var bestScore = 0.0;

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (item is not (BaseWeapon or BaseArmor or BaseClothing) || item.Deleted || item.Parent != Backpack)
            {
                continue;
            }

            if (item.Layer != layer || item.Layer == Layer.TwoHanded != (pass == 0))
            {
                continue;
            }

            if (!Suits(item))
            {
                Misfits++;

                continue;
            }

            var rank = Rank(item);

            if (rank < 0)
            {
                continue;
            }

            var score = Score(item);

            if (rank < bestRank || rank == bestRank && score <= bestScore)
            {
                continue;
            }

            best = item;
            bestRank = rank;
            bestScore = score;
        }

        return best;
    }

    private double Score(Item item) => item is BaseWeapon weapon ? Worth(weapon) : Guards(item);

    public bool Suits(Item item) =>
        item switch
        {
            BaseArmor armour =>
                (Female ? armour.AllowFemaleWearer : armour.AllowMaleWearer)
                && Str >= armour.StrRequirement
                && Dex >= armour.DexRequirement
                && Int >= armour.IntRequirement,
            BaseClothing clothing =>
                (Female ? clothing.AllowFemaleWearer : clothing.AllowMaleWearer)
                && Str >= clothing.StrRequirement,
            BaseWeapon weapon => Str >= weapon.StrRequirement,
            _ => true
        };

    public static long Misfits { get; private set; }

    private static double Guards(Item item) => item is BaseArmor armour ? armour.ArmorRating : 0.0;

    private bool Upgrade(Layer where, List<Item> carried, int pass)
    {
        if (where is Layer.OneHanded or Layer.TwoHanded)
        {
            return false;
        }

        var worn = FindItemOnLayer(where);

        if (worn is not (BaseArmor or BaseClothing))
        {
            return false;
        }

        var choice = Pick(carried, where, pass);

        if (choice == null || Guards(choice) <= Guards(worn))
        {
            return false;
        }

        var pack = Backpack;

        if (pack == null || !pack.TryDropItem(this, worn, false))
        {
            return false;
        }

        logger.Information(
            "{Name} took off {Old} for {New}, which stops {Guard:F0} against {Was:F0}",
            Name,
            worn.GetType().Name,
            choice.GetType().Name,
            Guards(choice),
            Guards(worn)
        );

        return true;
    }

    private int Rank(Item item)
    {
        var kind = item.GetType();

        if (Bond?.Weapon?.Weapon == kind)
        {
            return 2;
        }

        var tools = BotOutfit.ToolsFor(Class);

        for (var i = 0; i < tools.Count; i++)
        {
            if (tools[i] == kind)
            {
                return -1;
            }
        }

        return OwnKind(item) ? 2 : 1;
    }

    public bool Draw(bool melee)
    {
        var pack = Backpack;

        if (pack == null || Deleted || !Alive)
        {
            return false;
        }

        var held = Weapon as Item;

        if (held != null && held.Parent != this)
        {
            held = null;
        }

        if (held is BaseWeapon inHand && inHand is BaseRanged != melee)
        {
            return true;
        }

        BaseWeapon wanted = null;

        List<Item> carried = [.. pack.Items];

        var bestRank = 0;
        var bestWorth = 0.0;

        for (var i = 0; i < carried.Count; i++)
        {
            if (carried[i] is not BaseWeapon weapon || weapon is BaseRanged == melee || !Suits(weapon))
            {
                continue;
            }

            if (weapon is BaseRanged shooter && (shooter.AmmoType == null || pack.GetAmount(shooter.AmmoType) <= 0))
            {
                continue;
            }

            var rank = Rank(weapon);

            if (rank <= 0)
            {
                continue;
            }

            var worth = Worth(weapon);

            if (rank < bestRank || rank == bestRank && worth <= bestWorth)
            {
                continue;
            }

            bestRank = rank;
            bestWorth = worth;
            wanted = weapon;
        }

        if (wanted == null)
        {
            return false;
        }

        if (held != null && !pack.TryDropItem(this, held, false))
        {
            return false;
        }

        var shield = wanted.Layer == Layer.TwoHanded ? FindItemOnLayer(Layer.TwoHanded) : null;

        if (shield != null && !pack.TryDropItem(this, shield, false))
        {
            if (held != null)
            {
                EquipItem(held);
            }

            return false;
        }

        if (EquipItem(wanted))
        {
            return true;
        }

        if (shield != null)
        {
            EquipItem(shield);
        }

        if (held != null)
        {
            EquipItem(held);
        }

        return false;
    }

    private void Wear(Item item)
    {
        if (item == null)
        {
            return;
        }

        if (Bond != null)
        {
            BotBinding.Bind(item, Bond);
        }

        if (EquipItem(item))
        {
            return;
        }

        var pack = Backpack;

        if (pack == null)
        {
            item.Delete();

            return;
        }

        pack.DropItem(item);
    }

    public override string ToString() =>
        Class == null ? base.ToString() : $"{Name} the {Class.Name}";

    public override void Serialize(IGenericWriter writer)
    {
        base.Serialize(writer);

        writer.Write(1);

        writer.Write(Class?.Name ?? "");

        writer.Write(Bond != null);
        Bond?.Save(writer);

        Resolve.Ledger.Save(writer);

        var deed = Resolve.Deed;

        writer.Write(deed?.Kind ?? "");
        writer.Write(deed?.Map ?? Map.Internal);
        writer.Write(deed?.Where ?? Point3D.Zero);
    }

    public override void Deserialize(IGenericReader reader)
    {
        base.Deserialize(reader);

        var version = reader.ReadInt();

        if (version < 1)
        {
            return;
        }

        var was = reader.ReadString();

        Was = string.IsNullOrWhiteSpace(was) ? null : was;

        if (reader.ReadBool())
        {
            var bond = new BotBond();

            bond.Load(reader);
            Bond = bond;
        }

        Resolve.Ledger.Load(reader);

        var doing = reader.ReadString();

        WasDoing = string.IsNullOrWhiteSpace(doing) ? null : doing;
        WasDoingOn = reader.ReadMap();
        WasDoingAt = reader.ReadPoint3D();
    }

    public bool Revive(BotClass klass, bool away = false)
    {
        if (klass == null || Deleted || Backpack == null)
        {
            return false;
        }

        if (!Alive)
        {
            Fallen = true;
            FellTick = Core.TickCount;
            ReviveComplained = false;
            BackAsGhost++;
        }

        Player = true;

        if (!away && (Map == null || Map == Map.Internal) && LogoutMap != null && LogoutMap != Map.Internal)
        {
            MoveToWorld(LogoutLocation, LogoutMap);
        }

        if (away ? LogoutMap == null || LogoutMap == Map.Internal : Map == null || Map == Map.Internal)
        {
            return false;
        }

        Class = klass;

        Bond ??= BotOutfit.Give(this, klass);

        if (Guild == null)
        {
            BotGuilds.Enrol(this);
        }

        return true;
    }
}
