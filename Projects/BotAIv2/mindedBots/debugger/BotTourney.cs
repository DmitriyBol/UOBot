using System;
using System.Collections.Generic;
using System.IO;
using Server.BotAI.V2;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.Mind;

/// <summary>
/// The championship: once a week Argus calls the strongest of the population to a ring, sets them against each other
/// one pair at a time, heals them between fights, and crowns the one left standing.
///
/// <para>
/// <b>Patrick's order of 15.09.2026.</b> Thirty entrants, one against one, the winner goes on; the champion's name
/// turns yellow and it is handed a weapon or a piece of armour fit for its class. Held once a week, and by hand
/// through the door (<c>do tourney</c>) whenever somebody wants to see one.
/// </para>
///
/// <para>
/// <b>Driven from the watcher's own beat, one pair at a time, and nothing here holds the game loop.</b> Each beat
/// looks at one thing: whether the next pair should be called, whether the pair in the ring has been decided, or
/// whether the pause between fights is over. The duel itself is <see cref="BotDuel"/> — the pair, the rule, and the
/// two brawls pressed on the fighters — and the fighters' own bodies do the fighting. Argus's part is the summons,
/// the healing, the bracket and the prize: exactly a referee's.
/// </para>
///
/// <para>
/// <b>Only the pair in the ring is summoned.</b> The other entrants go on with their day and are lifted to the ring
/// when their turn comes, so the shard is not stood still for half an hour with thirty bots in a field; a loser walks
/// home from the ring like anybody sent anywhere. Bots in a company are not called — a company owns where its members
/// stand — and neither is anybody with a class paid nothing, which is the Baron.
/// </para>
/// </summary>
public static class BotTourney
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTourney));

    public static bool Running { get; set; } = true;

    public static long EveryMs { get; set; } = 3600000L;

    public static long LeastGapMs { get; set; } = 3600000L;

    public static bool Ready => Running && _stage == Stage.Idle && (DateTime.Now - _held).TotalMilliseconds >= LeastGapMs;

    public static long TooSoon { get; private set; }

    public static int Entrants { get; set; } = 30;

    public static Point3D Ring { get; set; } = new(1458, 1500, 0);

    public static int Apart { get; set; } = 3;

    public static int BetweenMs { get; set; } = 8000;

    public static int SlackMs { get; set; } = 30000;

    public static long Held { get; private set; }

    public static int RepeatPurse { get; set; } = 1000;

    public static long Repeats { get; private set; }

    public static long Duplicates { get; private set; }

    private static bool _swept;

    public static long Called { get; private set; }

    public static long Fights { get; private set; }

    public static long Byes { get; private set; }

    public static long Unset { get; private set; }

    public static long CalledOff { get; private set; }

    public static string Champion { get; private set; }

    private enum Stage
    {
        Idle,
        Calling,
        Fighting,
        Pausing
    }

    private static Stage _stage = Stage.Idle;

    private static readonly List<BotMobile> _round = [];

    private static readonly List<BotMobile> _next = [];

    private static BotMobile _a;

    private static BotMobile _b;

    private static long _stageTick;

    private static int _roundNo;

    private static int _entered;

    private static DateTime _held = DateTime.MinValue;

    private static long _hueTick;

    private static string _by;

    public static bool InProgress => _stage != Stage.Idle;

    private static string File => Path.Combine(Core.BaseDirectory, "Saves", "BotTourney", "tourney.txt");

    public static void Load()
    {
        _held = DateTime.Now;
        Champion = null;

        try
        {
            if (!System.IO.File.Exists(File))
            {
                return;
            }

            foreach (var line in System.IO.File.ReadAllLines(File))
            {
                var eq = line.IndexOf('=');

                if (eq <= 0)
                {
                    continue;
                }

                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();

                if (key == "held" && DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var held))
                {
                    _held = held;
                }
                else if (key == "champion" && value.Length > 0)
                {
                    Champion = value;
                }
            }

            logger.Information("The championship was last held {Held}; the champion is {Champion}", _held, Champion ?? "nobody");
        }
        catch (Exception e)
        {
            logger.Warning("The championship's record could not be read: {Message}", e.Message);
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, $"held={_held:O}{Environment.NewLine}champion={Champion}{Environment.NewLine}");
        }
        catch (Exception e)
        {
            logger.Warning("The championship's record could not be written: {Message}", e.Message);
        }
    }

    public static bool Due(DateTime now) => Running && (now - _held).TotalMilliseconds >= EveryMs;

    public static string Start(string by)
    {
        if (!Running)
        {
            return "championships are switched off (BotTourney.Running).";
        }

        if (_stage != Stage.Idle)
        {
            return $"a championship is already running: round {_roundNo}, {_round.Count + _next.Count} still in it.";
        }

        if (by != "the keyboard" && (DateTime.Now - _held).TotalMilliseconds < LeastGapMs)
        {
            TooSoon++;

            return $"too soon: the last championship ended {(DateTime.Now - _held).TotalMinutes:F0} minutes ago, and they are held once an hour.";
        }

        var fighters = Fighters();

        if (fighters.Count < 2)
        {
            return $"only {fighters.Count} fit fighters could be called; a championship wants two at least.";
        }

        _round.Clear();
        _next.Clear();
        _round.AddRange(fighters);
        _roundNo = 1;
        Called++;
        _entered = fighters.Count;
        _by = by;
        _stage = Stage.Calling;
        _stageTick = Core.TickCount;

        logger.Information(
            "Tourney: a championship is called by {By} with {Count} entrants, the strongest first: {Names}",
            by,
            fighters.Count,
            string.Join(", ", fighters.ConvertAll(f => $"{f.Name} ({BotThreat.Power(f):F0})"))
        );

        return $"the championship is on: {fighters.Count} entrants, {fighters[0].Name} the strongest at {BotThreat.Power(fighters[0]):F0}. Watch the log for \"Tourney:\".";
    }

    public static string Stop()
    {
        if (_stage == Stage.Idle)
        {
            return "no championship is running.";
        }

        Release();
        _round.Clear();
        _next.Clear();
        _stage = Stage.Idle;

        logger.Information("Tourney: the championship was called off");

        return "the championship is called off.";
    }

    private static List<BotMobile> Fighters()
    {
        var bots = BotPopulation.Bots;
        List<BotMobile> fit = [];

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is not { Deleted: false, Alive: true, Fallen: false } || bot.Class is not { } klass)
            {
                continue;
            }

            if (klass.Role == BotRole.Producer || klass.Unpaid || bot.Squad != null || bot.Murderer || BotDuel.Duelling(bot))
            {
                continue;
            }

            if (bot.Map == null || bot.Map == Map.Internal)
            {
                continue;
            }

            fit.Add(bot);
        }

        fit.Sort((x, y) => BotThreat.Power(y).CompareTo(BotThreat.Power(x)));

        if (fit.Count > Entrants)
        {
            fit.RemoveRange(Entrants, fit.Count - Entrants);
        }

        return fit;
    }

    public static void Beat(long now)
    {
        if (!Running)
        {
            return;
        }

        if (Champion != null && now - _hueTick >= 60000)
        {
            _hueTick = now;
            Crowned(Find(Champion));
            Regalia();
        }

        switch (_stage)
        {
            case Stage.Idle:
                if (Due(DateTime.Now))
                {
                    Start("the calendar");
                }

                return;

            case Stage.Calling:
                Call(now);

                return;

            case Stage.Fighting:
                Judge(now);

                return;

            case Stage.Pausing:
                if (now - _stageTick >= BetweenMs)
                {
                    _stage = Stage.Calling;
                    _stageTick = now;
                }

                return;
        }
    }

    private static void Call(long now)
    {
        _round.RemoveAll(m => m is not { Deleted: false, Alive: true });

        if (_round.Count == 0)
        {
            if (_next.Count == 0)
            {
                logger.Information("Tourney: nobody is left standing; no champion");
                _stage = Stage.Idle;

                return;
            }

            if (_next.Count == 1)
            {
                Crown(_next[0]);

                return;
            }

            _round.AddRange(_next);
            _next.Clear();
            _roundNo++;

            logger.Information("Tourney: round {Round} with {Count} left", _roundNo, _round.Count);
        }

        if (_round.Count == 1)
        {
            Byes++;
            _next.Add(_round[0]);
            _round.RemoveAt(0);

            logger.Information("Tourney: {Name} has a bye", _next[^1].Name);

            return;
        }

        _a = _round[0];
        _b = _round[1];
        _round.RemoveRange(0, 2);

        var map = BotPopulation.Home ?? Map.Felucca;

        if (!Summon(_a, map, Ring.X - Apart, Ring.Y) || !Summon(_b, map, Ring.X + Apart, Ring.Y))
        {
            Unset++;
            logger.Warning("Tourney: {A} or {B} could not be put in the ring; the first goes on by default", _a.Name, _b.Name);
            _next.Add(_a);
            _a = null;
            _b = null;

            return;
        }

        Heal(_a);
        Heal(_b);

        if (!BotDuel.Begin(_a, _b, Trains(_a), Trains(_b)))
        {
            Unset++;
            logger.Warning("Tourney: {A} and {B} could not be set against each other; the first goes on by default", _a.Name, _b.Name);
            _next.Add(_a);
            _a = null;
            _b = null;

            return;
        }

        Fights++;
        _stage = Stage.Fighting;
        _stageTick = now;

        logger.Information(
            "Tourney: round {Round}, {A} ({PowerA:F0}) against {B} ({PowerB:F0}) in the ring at {X},{Y}",
            _roundNo,
            _a.Name,
            BotThreat.Power(_a),
            _b.Name,
            BotThreat.Power(_b),
            Ring.X,
            Ring.Y
        );
    }

    private static void Judge(long now)
    {
        if (_a == null || _b == null)
        {
            _stage = Stage.Calling;

            return;
        }

        var winner = BotDuel.Winner(_a);

        if (winner == null && now - _stageTick >= BotDuel.CapMs + SlackMs)
        {
            CalledOff++;
            BotDuel.Call(_a, "the referee called it");
            winner = BotDuel.Winner(_a);
        }

        if (winner == null)
        {
            return;
        }

        var loser = ReferenceEquals(winner, _a) ? _b : _a;

        BotDuel.End(_a, _b);
        Heal(_a);
        Heal(_b);

        _next.Add(winner);

        logger.Information("Tourney: {Winner} goes on, {Loser} is out", winner.Name, loser?.Name);

        _a = null;
        _b = null;
        _stage = Stage.Pausing;
        _stageTick = now;
    }

    private static void Crown(BotMobile winner)
    {
        Sweep();

        Held++;
        _held = DateTime.Now;
        Champion = winner?.Name;
        _stage = Stage.Idle;

        Uncrown(Champion);
        Crowned(winner);

        var prize = Prize(winner);

        Save();

        logger.Information(
            "Tourney: {Name} the {Class} is the champion of the shard, from {Entered} entrants over {Rounds} rounds, called by {By}; the prize is {Prize}",
            winner?.Name,
            winner?.Class?.Name,
            _entered,
            _roundNo,
            _by,
            prize
        );

        winner?.Say($"I am the champion of this island, and my name is {winner.Name}!");
    }

    public static void Forget()
    {
        Champion = null;
        _held = DateTime.Now;
        _swept = false;
        Uncrown(null);
        Save();
    }

    private static void Uncrown(string except)
    {
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is { Deleted: false } && bot.NameHue == BotMobile.ChampionHue
                && !string.Equals(bot.Name, except, StringComparison.OrdinalIgnoreCase))
            {
                bot.NameHue = -1;
            }
        }
    }

    private static void Crowned(BotMobile bot)
    {
        if (bot is { Deleted: false } && bot.NameHue != BotMobile.ChampionHue)
        {
            bot.NameHue = BotMobile.ChampionHue;
            bot.InvalidateProperties();
        }
    }

    private static string Prize(BotMobile bot)
    {
        var pack = bot?.Backpack;

        if (bot == null || pack == null)
        {
            return "nothing, there was no pack to put it in";
        }

        var total = 0;

        for (var i = 0; i < Draws.Length; i++)
        {
            total += Draws[i].Weight;
        }

        var roll = Utility.Random(total);
        var drawn = Draws.Length - 1;

        for (var i = 0; i < Draws.Length; i++)
        {
            if (roll < Draws[i].Weight)
            {
                drawn = i;

                break;
            }

            roll -= Draws[i].Weight;
        }

        var draw = Draws[drawn];

        Drawn[drawn]++;

        string won;

        if (draw.Level > 0)
        {
            var top = draw.Level == TopLevel;
            var role = bot.Class?.Role ?? BotRole.Melee;

            won = role is BotRole.Caster or BotRole.Medic
                ? (top ? Robe(bot, pack) : null) ?? Leather(bot, pack, draw.Level) ?? (top ? Steed(bot, pack) : null)
                : Weapon(bot, pack, draw.Level) ?? Armour(bot, pack, draw.Level) ?? (top ? Steed(bot, pack) : null);

            if (won == null)
            {
                Repeats++;
                Pay(bot, pack, RepeatPurse);

                won = $"{RepeatPurse}gp, holding every prize of that rung its kind may choose";
            }
        }
        else if (draw.Gold > 0)
        {
            Pay(bot, pack, draw.Gold);

            won = $"{draw.Gold}gp";
        }
        else
        {
            won = Supplies(bot, pack);
        }

        return $"{won} (the draw fell on {draw.Name}, {draw.Weight} in {total})";
    }

    private const int TopLevel = 5;

    private static readonly (int Weight, int Level, int Gold, string Name)[] Draws =
    [
        (1, 5, 0, "the top: vanquishing or invulnerability"),
        (2, 4, 0, "power or fortification"),
        (3, 3, 0, "force or hardening"),
        (5, 2, 0, "might or guarding"),
        (8, 1, 0, "ruin or defense"),
        (13, 0, 0, "a pack of supplies"),
        (21, 0, 1000, "a purse of 1000"),
        (34, 0, 500, "a purse of 500"),
        (55, 0, 250, "a purse of 250")
    ];

    private static readonly long[] Drawn = new long[9];

    private static readonly string[] WeaponWords = ["", "ruin", "might", "force", "power", "vanquishing"];

    private static readonly string[] ArmourWords = ["", "defense", "guarding", "hardening", "fortification", "invulnerability"];

    public static int SupplyBandages { get; set; } = 40;

    public static int SupplyHeals { get; set; } = 3;

    public static int SupplyCures { get; set; } = 2;

    public static int SupplyReagents { get; set; } = 20;

    public static int SupplyAmmo { get; set; } = 150;

    private static string Supplies(BotMobile bot, Container pack)
    {
        var said = $"a pack of supplies: {SupplyBandages} bandages, {SupplyHeals} heal and {SupplyCures} cure potions";

        pack.TryDropItem(bot, new Bandage(SupplyBandages), false);
        pack.TryDropItem(bot, new HealPotion { Amount = SupplyHeals }, false);
        pack.TryDropItem(bot, new CurePotion { Amount = SupplyCures }, false);

        if (bot.Class?.Role is BotRole.Caster or BotRole.Medic || bot.Skills.Magery.Base >= 30.0)
        {
            Item[] herbs =
            [
                new BlackPearl(SupplyReagents), new Bloodmoss(SupplyReagents), new Garlic(SupplyReagents), new Ginseng(SupplyReagents),
                new MandrakeRoot(SupplyReagents), new Nightshade(SupplyReagents), new SpidersSilk(SupplyReagents), new SulfurousAsh(SupplyReagents)
            ];

            for (var i = 0; i < herbs.Length; i++)
            {
                pack.TryDropItem(bot, herbs[i], false);
            }

            said += $", {SupplyReagents} of each reagent";
        }

        if (bot.Bond?.Weapon is { Weapon: { } kind } && typeof(BaseRanged).IsAssignableFrom(kind)
            && kind.CreateInstance<Item>() is BaseRanged bow)
        {
            var ammo = bow.AmmoType;

            bow.Delete();

            if (ammo != null && ammo.CreateInstance<Item>() is { } shot)
            {
                shot.Amount = SupplyAmmo;
                pack.TryDropItem(bot, shot, false);

                said += $", {SupplyAmmo} {ammo.Name.ToLowerInvariant()}s";
            }
        }

        return said;
    }

    public static int RobeInt { get; set; } = 15;

    public const int RobeHue = 0x0555;

    public const int SteedHue = 0x0501;

    private const string RobeMod = "champion's robe";

    private static string Weapon(BotMobile bot, Container pack, int level)
    {
        if (bot.Class?.Role is not (BotRole.Melee or BotRole.Ranged) || bot.Bond?.Weapon is not { Weapon: { } kind })
        {
            return null;
        }

        var owned = Owned(bot, kind);

        for (var i = 0; i < owned.Count; i++)
        {
            if (owned[i] is BaseWeapon held && (int)held.DamageLevel >= level)
            {
                return null;
            }
        }

        if (kind.CreateInstance<Item>() is not BaseWeapon copy)
        {
            return null;
        }

        copy.DamageLevel = (WeaponDamageLevel)level;
        copy.DurabilityLevel = (WeaponDurabilityLevel)level;

        var said = Give(bot, pack, copy, $"{kind.Name} of {WeaponWords[level]}, bound");

        if (said != null)
        {
            Replace(bot, owned);
        }

        return said;
    }

    private static void Replace(BotMobile bot, List<Item> lesser)
    {
        for (var i = 0; i < lesser.Count; i++)
        {
            if (lesser[i] is { Deleted: false } old)
            {
                bot.Bond?.Items.Remove(old.Serial);
                old.Delete();
            }
        }
    }

    private static string Armour(BotMobile bot, Container pack, int level)
    {
        Layer[] order = [Layer.InnerTorso, Layer.Pants, Layer.Arms, Layer.Gloves, Layer.Neck, Layer.Helm];

        for (var i = 0; i < order.Length; i++)
        {
            if (bot.FindItemOnLayer(order[i]) is not BaseArmor worn || (int)worn.ProtectionLevel >= level)
            {
                continue;
            }

            var given = Piece(bot, pack, worn.GetType(), level);

            if (given != null)
            {
                return given;
            }
        }

        return null;
    }

    private static string Piece(BotMobile bot, Container pack, Type type, int level)
    {
        var owned = Owned(bot, type);

        for (var i = 0; i < owned.Count; i++)
        {
            if (owned[i] is BaseArmor held && (int)held.ProtectionLevel >= level)
            {
                return null;
            }
        }

        if (type.CreateInstance<Item>() is not BaseArmor copy)
        {
            return null;
        }

        copy.ProtectionLevel = (ArmorProtectionLevel)level;
        copy.Durability = (ArmorDurabilityLevel)level;

        var said = Give(bot, pack, copy, $"{type.Name} of {ArmourWords[level]}, bound");

        if (said != null)
        {
            Replace(bot, owned);
        }

        return said;
    }

    private static string Leather(BotMobile bot, Container pack, int level)
    {
        Type[] set = [typeof(LeatherChest), typeof(LeatherLegs), typeof(LeatherArms), typeof(LeatherGloves), typeof(LeatherGorget), typeof(LeatherCap)];

        for (var i = 0; i < set.Length; i++)
        {
            var given = Piece(bot, pack, set[i], level);

            if (given != null)
            {
                return given;
            }
        }

        return null;
    }

    private static string Robe(BotMobile bot, Container pack)
    {
        if (RobeOf(bot) != null)
        {
            return null;
        }

        var robe = new Robe(RobeHue) { Name = "a robe of the arcane mind" };
        var said = Give(bot, pack, robe, $"a robe of the arcane mind (+{RobeInt} Intelligence while worn), bound");

        if (said != null && !robe.Deleted)
        {
            if (bot.FindItemOnLayer(Layer.OuterTorso) is { } over)
            {
                pack.DropItem(over);
            }

            bot.EquipItem(robe);
            Regalia(bot);
        }

        return said;
    }

    private static Robe RobeOf(BotMobile bot)
    {
        if (bot?.Bond == null)
        {
            return null;
        }

        if (bot.FindItemOnLayer(Layer.OuterTorso) is Robe { Hue: RobeHue } worn && BotBinding.IsBound(worn, bot.Bond))
        {
            return worn;
        }

        if (bot.Backpack is not { } pack)
        {
            return null;
        }

        foreach (var item in pack.FindItemsByType(typeof(Robe), true))
        {
            if (item is Robe { Hue: RobeHue } robe && BotBinding.IsBound(robe, bot.Bond))
            {
                return robe;
            }
        }

        return null;
    }

    private static string Steed(BotMobile bot, Container pack)
    {
        if (bot.Mount is BotChampionSteed || pack.FindItemByType<BotChampionSteed>() != null)
        {
            return null;
        }

        var (regular, mounted, kind) = bot.Class?.Role switch
        {
            BotRole.Caster => (0x25A0, 0x3E9C, "kirin"),
            BotRole.Medic  => (0x25CE, 0x3E9B, "unicorn"),
            BotRole.Melee  => (0x2619, 0x3E98, "swamp dragon"),
            BotRole.Ranged => (0x2135, 0x3EAC, "ostard"),
            _              => (0x2615, 0x3E9A, "ridgeback")
        };

        var steed = new BotChampionSteed
        {
            RegularID = regular,
            MountedID = mounted,
            Hue = SteedHue,
            Name = $"{bot.Name}'s {kind}"
        };

        if (!pack.TryDropItem(bot, steed, false))
        {
            steed.Delete();

            return null;
        }

        if (pack.FindItemByType<BotSteed>() is { } bought && bought is not BotChampionSteed)
        {
            bought.Delete();
            Banker.Deposit(bot, BotSteed.Price);
        }

        return $"a golden {kind} of its own";
    }

    private static string Give(BotMobile bot, Container pack, Item prize, string said)
    {
        if (!pack.TryDropItem(bot, prize, false))
        {
            prize.Delete();

            return null;
        }

        BotBinding.Bind(prize, bot.Bond);

        return said;
    }

    private static void Regalia(BotMobile bot)
    {
        if (bot is not { Deleted: false })
        {
            return;
        }

        var wearing = bot.FindItemOnLayer(Layer.OuterTorso) is Robe { Hue: RobeHue } robe && BotBinding.IsBound(robe, bot.Bond);
        var lent = bot.GetStatMod(RobeMod);

        if (wearing && lent == null)
        {
            bot.AddStatMod(new StatMod(StatType.Int, RobeMod, RobeInt, TimeSpan.Zero));
        }
        else if (!wearing && lent != null)
        {
            bot.RemoveStatMod(RobeMod);
        }
    }

    private static void Regalia()
    {
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false } bot && (bot.GetStatMod(RobeMod) != null || RobeOf(bot) != null))
            {
                Regalia(bot);
            }
        }
    }

    private static bool IsPrize(Item item) =>
        item is BaseWeapon { DamageLevel: not WeaponDamageLevel.Regular } or BaseArmor { ProtectionLevel: not ArmorProtectionLevel.Regular };

    private static List<Item> Owned(BotMobile bot, Type type)
    {
        List<Item> owned = [];

        if (bot?.Bond == null || type == null)
        {
            return owned;
        }

        for (var i = 0; i < bot.Items.Count; i++)
        {
            var item = bot.Items[i];

            if (item.GetType() == type && IsPrize(item) && BotBinding.IsBound(item, bot.Bond))
            {
                owned.Add(item);
            }
        }

        if (bot.Backpack is { } pack)
        {
            foreach (var item in pack.FindItemsByType(type, true))
            {
                if (IsPrize(item) && BotBinding.IsBound(item, bot.Bond) && !owned.Contains(item))
                {
                    owned.Add(item);
                }
            }
        }

        return owned;
    }

    private static void Pay(BotMobile bot, Container pack, int amount)
    {
        if (!Banker.Deposit(bot, amount))
        {
            pack?.TryDropItem(bot, new Gold(amount), false);
        }
    }

    private static void Sweep()
    {
        if (_swept)
        {
            return;
        }

        _swept = true;

        var exchanged = Sweep(BotPopulation.Bots) + Sweep(BotPopulation.Away);

        if (exchanged > 0)
        {
            logger.Information("Tourney: {Count} duplicate prizes the population held were exchanged for prizes of their owners' choosing", exchanged);
        }
    }

    private static int Sweep(IReadOnlyList<BotMobile> bots)
    {
        var exchanged = 0;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is not { Deleted: false, Bond: { } bond } bot)
            {
                continue;
            }

            List<Type> seen = [];

            foreach (var serial in new List<Serial>(bond.Items))
            {
                if (World.FindItem(serial) is not { Deleted: false } item || !IsPrize(item) || seen.Contains(item.GetType()))
                {
                    continue;
                }

                seen.Add(item.GetType());

                var owned = Owned(bot, item.GetType());

                for (var k = 1; k < owned.Count; k++)
                {
                    bond.Items.Remove(owned[k].Serial);
                    owned[k].Delete();
                    Duplicates++;
                    exchanged++;

                    logger.Information("Tourney: {Name} exchanged a duplicate {Kind} for {Prize}", bot.Name, item.GetType().Name, Prize(bot));
                }
            }
        }

        return exchanged;
    }

    private static bool Summon(BotMobile bot, Map map, int x, int y)
    {
        if (bot is not { Deleted: false, Alive: true } || map == null)
        {
            return false;
        }

        for (var attempt = 0; attempt < 32; attempt++)
        {
            var spread = attempt < 16 ? 3 : 7;
            var sx = x + Utility.RandomMinMax(-spread, spread);
            var sy = y + Utility.RandomMinMax(-spread, spread);

            if (!map.CanSpawnMobile(sx, sy, Ring.Z - 12, Ring.Z + 12, false, false, out var z))
            {
                continue;
            }

            if (!map.LineOfSight(new Point3D(sx, sy, z + 10), new Point3D(Ring.X, Ring.Y, Ring.Z + 10)))
            {
                continue;
            }

            bot.MoveToWorld(new Point3D(sx, sy, z), map);
            bot.Journey?.Finish();
            bot.Combatant = null;
            bot.Warmode = false;

            return true;
        }

        return false;
    }

    private static void Heal(BotMobile bot)
    {
        if (bot is not { Deleted: false, Alive: true })
        {
            return;
        }

        if (bot.Poisoned)
        {
            bot.CurePoison(bot);
        }

        bot.Hits = bot.HitsMax;
        bot.Stam = bot.StamMax;
        bot.Mana = bot.ManaMax;
    }

    private static void Release()
    {
        if (_a != null && _b != null)
        {
            BotDuel.Call(_a, "the championship was called off");
            BotDuel.End(_a, _b);
        }

        Heal(_a);
        Heal(_b);
        _a = null;
        _b = null;
    }

    private static SkillName Trains(BotMobile bot) => bot?.Bond?.Weapon?.Skill ?? SkillName.Wrestling;

    private static BotMobile Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false } bot && string.Equals(bot.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return bot;
            }
        }

        return null;
    }

    public static string Describe()
    {
        var stage = _stage switch
        {
            Stage.Idle     => "no championship running",
            Stage.Calling  => $"calling the next pair of round {_roundNo}",
            Stage.Fighting => $"round {_roundNo}: {_a?.Name} against {_b?.Name} in the ring",
            _              => "between fights"
        };

        return $"the championship: {stage}; champion {Champion ?? "nobody"}; {Held} held, {Fights} fights, {Byes} byes, {Unset} pairs passed over, {CalledOff} called by the referee, {Repeats} champions paid in gold for holding every prize and {Duplicates} duplicate prizes exchanged; draws since the boot {string.Join("/", Drawn)} (top first), {TooSoon} calls refused inside the hour; {BotDuel.Describe()}; next by the calendar {(_held + TimeSpan.FromMilliseconds(EveryMs)):yyyy-MM-dd HH:mm}";
    }
}
