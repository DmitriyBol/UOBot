using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// Who has murdered lately, who is in a cell for it, and the clock that lets both go.
///
/// <para>
/// <b>Patrick's order of 15.09.2026: a bot may set on another bot and rob it.</b> To rob it has to kill, and the killer
/// goes red; it is red for an hour, friendly to other reds and an enemy to everybody else; the Baron may raise a patrol
/// against it instead of hunting, and a red the Baron's patrol kills is caught and put in a cell at the bottom of the
/// map, one bot to a cell. The thought of robbing is a very rare one.
/// </para>
///
/// <para>
/// <b>Red is the engine's own word.</b> A killer's <c>Kills</c> is set to the engine's murderer threshold, so its name
/// turns red by the engine's notoriety, the guards of any town treat it as the engine treats reds, and the count here
/// only decides when the hour is up. Nothing is invented about what red means; what is added is the hour, the cell,
/// and the reading of "red" by the population's own rules — the robber's choice of victim, the Baron's patrol, and
/// the town the red keeps out of.
/// </para>
///
/// <para>
/// <b>The cells are the engine's jail.</b> The same ten points <c>JailSystem</c> uses, inside the jail region where
/// nothing may be harmed and nobody may travel out; a bot put there holds a sentence, which is a piece of work that
/// does nothing until the clock lets it go, and then it is lifted back to its guild's seat with a clean name.
/// </para>
/// </summary>
public static class BotOutlaw
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotOutlaw));

    public static bool Running { get; set; } = true;

    public static int RedMs { get; set; } = 3600000;

    public static int JailMs { get; set; } = 3600000;

    public static int EveryMs { get; set; } = 1000;

    public static int RedKills { get; set; } = 5;

    public static readonly Point3D[] Cells =
    [
        new(5276, 1164, 0), new(5286, 1164, 0), new(5296, 1164, 0), new(5306, 1164, 0),
        new(5276, 1174, 0), new(5286, 1174, 0), new(5296, 1174, 0), new(5306, 1174, 0),
        new(5283, 1184, 0), new(5304, 1184, 0)
    ];

    public static long Murders { get; private set; }

    public static long Robbed { get; private set; }

    public static long Caught { get; private set; }

    public static long Released { get; private set; }

    public static long Redeemed { get; private set; }

    public static long Unavenged { get; private set; }

    public static bool Seizes { get; set; } = true;

    public static int BankFine { get; set; } = 1000;

    public static bool TakesCorpse { get; set; } = true;

    public static bool TakesBond { get; set; } = true;

    public static long Seized { get; private set; }

    public static long SeizedGold { get; private set; }

    public static long SeizedThings { get; private set; }

    public static long SeizedBank { get; private set; }

    public static long SeizedCorpse { get; private set; }

    public static long Stripped { get; private set; }

    public static long BaronsOwn { get; private set; }

    public static long Shared { get; private set; }

    public static int BloodReach { get; set; } = 24;

    public static long Kept { get; private set; }

    public static long Overfull { get; private set; }

    private sealed class Record
    {
        public BotMobile Bot;

        public string Name;

        public long RedUntil;

        public long JailedUntil;

        public int Cell = -1;

        public string CaughtBy;

        public bool Known;

        public string KnownBy;

        public bool Wanted;

        public long WantedUntil;
    }

    private static readonly Dictionary<Serial, Record> _records = [];

    private static readonly List<Mobile> _hands = [];

    private static readonly List<KeyValuePair<Serial, Record>> _walking = [];

    public static long Rebound { get; private set; }

    private static BotMobile Named(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false } bot && bot.Name == name)
            {
                return bot;
            }
        }

        return null;
    }

    private static Timer _timer;

    public static void Start()
    {
        Stop();

        _timer = new Clock();
        _timer.Start();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    internal static void Save(IGenericWriter writer)
    {
        var now = Core.TickCount;
        var kept = 0;

        foreach (var (_, r) in _records)
        {
            if (r.Bot is { Deleted: false })
            {
                kept++;
            }
        }

        writer.WriteEncodedInt(kept);

        foreach (var (_, r) in _records)
        {
            if (r.Bot is not { Deleted: false })
            {
                continue;
            }

            writer.Write(r.Bot);
            writer.Write(Math.Max(0L, r.RedUntil - now));
            writer.Write(Math.Max(0L, r.JailedUntil - now));
            writer.WriteEncodedInt(r.Cell);
            writer.Write(r.CaughtBy ?? "");
            writer.Write(r.Wanted ? Math.Max(0L, r.WantedUntil - now) : 0L);
            writer.Write(r.Known);
            writer.Write(r.KnownBy ?? "");
        }
    }

    internal static (int Reds, int Jailed) Load(IGenericReader reader, int shape)
    {
        var now = Core.TickCount;
        var count = reader.ReadEncodedInt();
        var reds = 0;
        var jailed = 0;

        for (var i = 0; i < count; i++)
        {
            var bot = reader.ReadEntity<BotMobile>();
            var redLeft = reader.ReadLong();
            var jailLeft = reader.ReadLong();
            var cell = reader.ReadEncodedInt();
            var caughtBy = reader.ReadString();

            var wantedLeft = 0L;
            var known = true;
            string knownBy = null;

            if (shape >= 2)
            {
                wantedLeft = reader.ReadLong();
                known = reader.ReadBool();
                knownBy = reader.ReadString();
            }

            if (bot is not { Deleted: false })
            {
                continue;
            }

            var r = new Record
            {
                Bot = bot,
                Name = bot.Name,
                RedUntil = now + Math.Max(0L, redLeft),
                JailedUntil = now + Math.Max(0L, jailLeft),
                Cell = cell,
                CaughtBy = string.IsNullOrEmpty(caughtBy) ? null : caughtBy,
                Known = known,
                KnownBy = string.IsNullOrEmpty(knownBy) ? null : knownBy,
                Wanted = wantedLeft > 0,
                WantedUntil = now + Math.Max(0L, wantedLeft)
            };

            _records[bot.Serial] = r;

            if (cell >= 0)
            {
                jailed++;
            }
            else
            {
                reds++;
            }

            if ((cell >= 0 || redLeft > 0) && bot.Kills < RedKills)
            {
                bot.Kills = RedKills;
            }
        }

        return (reds, jailed);
    }

    public static bool IsRed(Mobile m)
    {
        if (m == null || !_records.TryGetValue(m.Serial, out var r))
        {
            return false;
        }

        return r.Cell < 0 && r.RedUntil - Core.TickCount > 0;
    }

    public static bool Jailed(Mobile m) =>
        m != null && _records.TryGetValue(m.Serial, out var r) && r.Cell >= 0 && r.JailedUntil - Core.TickCount > 0;

    public static Point3D CellOf(Mobile m) =>
        m != null && _records.TryGetValue(m.Serial, out var r) && r.Cell >= 0 ? Cells[r.Cell] : Point3D.Zero;

    public static int LeftInCell(Mobile m) =>
        m != null && _records.TryGetValue(m.Serial, out var r) && r.Cell >= 0
            ? (int)Math.Max(0, (r.JailedUntil - Core.TickCount) / 60000)
            : 0;

    public static int RedCount()
    {
        var n = 0;

        foreach (var (_, r) in _records)
        {
            if (r.Cell < 0 && r.RedUntil - Core.TickCount > 0 && r.Bot is { Deleted: false })
            {
                n++;
            }
        }

        return n;
    }

    public static long Told { get; private set; }

    public static bool Tell(BotMobile red, string by)
    {
        if (red == null || !_records.TryGetValue(red.Serial, out var r) || r.Known || r.Cell >= 0)
        {
            return false;
        }

        r.Known = true;
        r.KnownBy = by;
        Told++;

        return true;
    }

    public static int WantedMs { get; set; } = 1800000;

    public static long WantedPosted { get; private set; }

    public static bool IsWanted(Mobile m) =>
        m != null && _records.TryGetValue(m.Serial, out var r) && r.Cell < 0 && r.Wanted && r.WantedUntil - Core.TickCount > 0;

    public static bool Outlaw(Mobile m) => IsRed(m) || IsWanted(m);

    public static bool Want(BotMobile bot, string by, string why)
    {
        if (!Running || bot is not { Deleted: false } || Jailed(bot))
        {
            return false;
        }

        var now = Core.TickCount;

        if (!_records.TryGetValue(bot.Serial, out var r))
        {
            _records[bot.Serial] = r = new Record { Bot = bot, Name = bot.Name, RedUntil = now };
        }

        var again = r.Wanted && r.WantedUntil - now > 0;

        r.Wanted = true;
        r.WantedUntil = now + WantedMs;
        r.Known = true;
        r.KnownBy ??= by;
        bot.Criminal = true;
        WantedPosted++;

        if (!again)
        {
            logger.Information(
                "{Name} is wanted for {Why}, on the word of {By}: grey to everybody for {Minutes} minutes",
                bot.Name,
                why,
                by,
                WantedMs / 60000
            );
        }

        return true;
    }

    public static void Outlaws(List<BotMobile> into)
    {
        var now = Core.TickCount;

        foreach (var (_, r) in _records)
        {
            if (r.Cell < 0 && (r.RedUntil - now > 0 || r.Wanted && r.WantedUntil - now > 0) && r.Bot is { Deleted: false } bot)
            {
                into.Add(bot);
            }
        }
    }

    public static bool Known(Mobile m) => m != null && _records.TryGetValue(m.Serial, out var r) && r.Known;

    public static void Reds(List<BotMobile> into)
    {
        foreach (var (_, r) in _records)
        {
            if (r.Cell < 0 && r.RedUntil - Core.TickCount > 0 && r.Bot is { Deleted: false } bot)
            {
                into.Add(bot);
            }
        }
    }

    public static int JailedCount()
    {
        var n = 0;

        foreach (var (_, r) in _records)
        {
            if (r.Cell >= 0 && r.Bot is { Deleted: false })
            {
                n++;
            }
        }

        return n;
    }

    public static void Murdered(BotMobile killer, BotMobile victim)
    {
        if (!Running || killer is not { Deleted: false })
        {
            return;
        }

        if (!_records.TryGetValue(killer.Serial, out var r))
        {
            _records[killer.Serial] = r = new Record { Bot = killer, Name = killer.Name };
        }

        r.RedUntil = Core.TickCount + RedMs;

        if (killer.Kills < RedKills)
        {
            killer.Kills = RedKills;
        }

        Murders++;

        BotInquest.Murder(killer, victim);

        logger.Information(
            "{Killer} the {Class} murdered {Victim} at ({X}, {Y}) and is red for {Minutes} minutes",
            killer.Name,
            killer.Class?.Name,
            victim?.Name,
            victim?.X ?? killer.X,
            victim?.Y ?? killer.Y,
            RedMs / 60000
        );
    }

    public static void Took(int coins)
    {
        if (coins > 0)
        {
            Robbed += coins;
        }
    }

    public static void Fell(BotMobile fallen, Mobile killer)
    {
        if (!Running || fallen == null || !Outlaw(fallen))
        {
            return;
        }

        var patrol = Hunting(fallen);

        if (killer is not BotMobile by)
        {
            if (patrol != null)
            {
                logger.Information(
                    "{Name}, red, fell to {Killer} while {Patrol}'s patrol stood against it; the patrol has the catch",
                    fallen.Name,
                    killer?.Name ?? "something",
                    patrol.Name
                );

                Jail(fallen, patrol);

                return;
            }

            Unavenged++;

            logger.Information("{Name}, red, was killed by {Killer} and is not caught by it", fallen.Name, killer?.Name ?? "something");

            return;
        }

        Blood(fallen, by);

        var hunter = by.Class is BotBaron || by.Resolve?.Deed is BotBrawl { Kind: BotBrawl.Manhunt } || by.Resolve?.Deed is BotManhunt;

        if (!hunter && patrol != null)
        {
            logger.Information(
                "{Name}, red, fell to {Killer} while {Patrol}'s patrol stood against it; the patrol has the catch",
                fallen.Name,
                by.Name,
                patrol.Name
            );

            Jail(fallen, patrol);

            return;
        }

        if (!hunter)
        {
            Unavenged++;

            logger.Information("{Name}, red, was killed by {Killer}, who was not on a patrol; it stays red", fallen.Name, by.Name);

            return;
        }

        Jail(fallen, by);
    }

    private static void Blood(BotMobile fallen, BotMobile by)
    {
        var price = BotUnderworld.Price(fallen);

        if (price <= 0)
        {
            return;
        }

        if (by.Class is BotBaron)
        {
            BaronsOwn++;

            logger.Information("{Name} was worth {Gold}gp, and the Baron took it himself: nobody is paid", fallen.Name, price);

            return;
        }

        _hands.Clear();

        var squad = BotSquads.Of(by);

        if (squad != null)
        {
            var company = squad.Members;

            for (var i = 0; i < company.Count; i++)
            {
                if (company[i]?.Self is { Deleted: false, Alive: true } member && member.Map == fallen.Map
                    && member.InRange(fallen.Location, BloodReach))
                {
                    _hands.Add(member);
                }
            }
        }

        if (_hands.Count == 0)
        {
            _hands.Add(by);
        }

        Shared += _hands.Count > 1 ? 1 : 0;

        if (BotCity.Blood(fallen, _hands, price) > 0)
        {
            BotUnderworld.Sold(fallen);
        }
    }

    private static void Seize(BotMobile bot)
    {
        if (!Seizes || bot is not { Deleted: false })
        {
            return;
        }

        var took = 0;
        var coins = bot.Backpack is { } pack ? Empty(pack, ref took) : 0;

        if (TakesCorpse && bot.Remains is { Deleted: false } corpse)
        {
            coins += Empty(corpse, ref took);
            bot.Remains = null;
            SeizedCorpse++;
        }

        var kit = TakesBond ? BotBinding.Forfeit(bot, bot.Bond) : 0;

        if (kit > 0)
        {
            Stripped++;
        }

        var fine = BankFine > 0 ? Math.Min(BankFine, Mobiles.Banker.GetBalance(bot)) : 0;

        if (fine > 0 && !Mobiles.Banker.Withdraw(bot, fine))
        {
            fine = 0;
        }

        BotCity.Tax(coins + fine);
        SeizedGold += coins;
        SeizedBank += fine;
        SeizedThings += took;

        if (coins <= 0 && fine <= 0 && took <= 0 && kit <= 0)
        {
            return;
        }

        Seized++;

        logger.Information(
            "{Name} was stripped at the cell door: {Gold}gp off the body, {Fine}gp out of its bank, {Things} things and {Kit} of its own kit forfeit",
            bot.Name,
            coins,
            fine,
            took,
            kit
        );
    }

    private static int Empty(Container box, ref int took)
    {
        var coins = box.GetAmount(typeof(Gold));

        if (coins > 0 && !box.ConsumeTotal(typeof(Gold), coins))
        {
            coins = 0;
        }

        List<Item> forfeit = [];

        for (var i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is { Deleted: false } item)
            {
                forfeit.Add(item);
            }
        }

        for (var i = 0; i < forfeit.Count; i++)
        {
            took++;
            forfeit[i].Delete();
        }

        return coins;
    }

    private static BotMobile Hunting(BotMobile red)
    {
        var bots = BotPopulation.Bots;
        BotMobile posse = null;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is not { Deleted: false } || bot == red)
            {
                continue;
            }

            switch (bot.Resolve?.Deed)
            {
                case BotManhunt hunt when ReferenceEquals(hunt.Foe, red):
                    return bot;

                case BotBrawl { Kind: BotBrawl.Manhunt } brawl when ReferenceEquals(brawl.Foe, red):
                    posse ??= bot;

                    break;
            }
        }

        return posse;
    }

    public static bool Jail(BotMobile bot, Mobile by)
    {
        if (!Running || bot is not { Deleted: false })
        {
            return false;
        }

        var cell = FreeCell();

        if (cell < 0)
        {
            Overfull++;

            logger.Warning("{Name} was caught by {By} but every cell is taken; it stays red", bot.Name, by?.Name);

            return false;
        }

        if (!_records.TryGetValue(bot.Serial, out var r))
        {
            _records[bot.Serial] = r = new Record { Bot = bot, Name = bot.Name };
        }

        r.Cell = cell;
        r.Wanted = false;
        r.JailedUntil = Core.TickCount + JailMs;
        r.CaughtBy = by?.Name;
        Caught++;
        BotUnderworld.Jailed(bot);

        Seize(bot);

        BotCity.Collect(bot, by);

        logger.Information(
            "{Name} was caught by {By} and is sentenced to cell {Cell} at ({X}, {Y}) for {Minutes} minutes",
            bot.Name,
            by?.Name,
            cell,
            Cells[cell].X,
            Cells[cell].Y,
            JailMs / 60000
        );

        return true;
    }

    private static int FreeCell()
    {
        for (var i = 0; i < Cells.Length; i++)
        {
            var taken = false;

            foreach (var (_, r) in _records)
            {
                if (r.Cell == i && r.Bot is { Deleted: false })
                {
                    taken = true;

                    break;
                }
            }

            if (!taken)
            {
                return i;
            }
        }

        return -1;
    }

    public static BotMobile AtLarge(Map map, Point3D from, int range)
    {
        BotMobile best = null;
        var bestAway = double.MaxValue;

        foreach (var (_, r) in _records)
        {
            var bot = r.Bot;

            if (bot is not { Deleted: false, Alive: true } || bot.Map != map || r.Cell >= 0
                || r.RedUntil - Core.TickCount <= 0 && !(r.Wanted && r.WantedUntil - Core.TickCount > 0))
            {
                continue;
            }

            if (!r.Known || bot.Hidden)
            {
                continue;
            }

            var away = Math.Max(Math.Abs(bot.X - from.X), Math.Abs(bot.Y - from.Y));

            if (away > range || away >= bestAway)
            {
                continue;
            }

            best = bot;
            bestAway = away;
        }

        return best;
    }

    public static BotMobile Assailant(Mobile body, int range)
    {
        var map = body?.Map;

        if (!Running || map == null || map == Map.Internal)
        {
            return null;
        }

        foreach (var other in map.GetMobilesInRange<BotMobile>(body.Location, range))
        {
            if (other != body && other.Alive && Assailing(other, body))
            {
                return other;
            }
        }

        return null;
    }

    public static bool Assailing(BotMobile other, Mobile victim) =>
        other?.Resolve?.Deed is BotRob rob && ReferenceEquals(rob.Foe, victim)
        || ReferenceEquals(other?.Combatant, victim) && Outlaw(other);

    public static bool Keeps(Mobile body, Map map, Point3D where)
    {
        if (!Running || map == null || where == Point3D.Zero || !Outlaw(body))
        {
            return false;
        }

        if (Region.Find(where, map)?.GetRegion<GuardedRegion>() is { } guarded && !guarded.IsDisabled())
        {
            Kept++;

            return true;
        }

        if (body.Map == map && Crosses(map, body.Location, where))
        {
            Routed++;

            return true;
        }

        return false;
    }

    public static int Stride { get; set; } = 12;

    public static long Routed { get; private set; }

    public static bool WalksRound { get; set; } = true;

    public static long Detoured { get; private set; }

    public static long Stopped { get; private set; }

    private static readonly Dictionary<Serial, long> _stoppedSaid = [];

    public static bool Guarded(Map map, int x, int y, int z = 0) =>
        map != null && Region.Find(new Point3D(x, y, z), map)?.GetRegion<GuardedRegion>() is { } guarded && !guarded.IsDisabled();

    public static bool KeptRound(Mobile bot) => Running && WalksRound && Outlaw(bot);

    public static BotAvoid Road(Mobile bot, Map map, Point3D target, BotAvoid avoid)
    {
        if (bot == null || map == null || !KeptRound(bot) || Guarded(map, bot.X, bot.Y, bot.Z) || Guarded(map, target.X, target.Y, target.Z))
        {
            return avoid;
        }

        Detoured++;

        return avoid.Towns(map);
    }

    public static bool Steps(Mobile bot, Direction d)
    {
        if (bot?.Map is not { } map || map == Map.Internal || !KeptRound(bot) || Guarded(map, bot.X, bot.Y, bot.Z))
        {
            return true;
        }

        int x = bot.X, y = bot.Y;
        global::Server.Movement.Movement.Offset(d, ref x, ref y);

        if (!Guarded(map, x, y, bot.Z))
        {
            return true;
        }

        Stopped++;

        var now = Core.TickCount;

        if (!_stoppedSaid.TryGetValue(bot.Serial, out var said) || now - said >= 60000)
        {
            _stoppedSaid[bot.Serial] = now;

            logger.Information(
                "{Name}, red, was refused a step into {Town} at ({X}, {Y})",
                bot.Name,
                Region.Find(new Point3D(x, y, bot.Z), map)?.GetRegion<GuardedRegion>()?.Name ?? "a guarded town",
                x,
                y
            );
        }

        return false;
    }

    private static bool Crosses(Map map, Point3D from, Point3D to)
    {
        var steps = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)) / Math.Max(1, Stride);

        for (var i = 1; i < steps; i++)
        {
            var x = from.X + (to.X - from.X) * i / steps;
            var y = from.Y + (to.Y - from.Y) * i / steps;

            if (Region.Find(new Point3D(x, y, map.GetAverageZ(x, y)), map)?.GetRegion<GuardedRegion>() is { } guarded && !guarded.IsDisabled())
            {
                return true;
            }
        }

        return false;
    }

    private static void Tick()
    {
        if (!Running || _records.Count == 0)
        {
            return;
        }

        var now = Core.TickCount;
        List<Serial> gone = null;
        List<(Serial Was, Record Record)> rebound = null;

        _walking.Clear();

        foreach (var pair in _records)
        {
            _walking.Add(pair);
        }

        for (var w = 0; w < _walking.Count; w++)
        {
            var (serial, r) = _walking[w];

            var bot = r.Bot;

            if (bot is not { Deleted: false })
            {
                var again = Named(r.Name);

                if (again == null)
                {
                    if (BotPopulation.Count > 0)
                    {
                        (gone ??= []).Add(serial);
                    }

                    continue;
                }

                r.Bot = again;
                bot = again;
                (rebound ??= []).Add((serial, r));
                Rebound++;

                if (r.Cell < 0 && r.RedUntil - now > 0 && again.Kills < RedKills)
                {
                    again.Kills = RedKills;
                }
            }

            if (r.Cell >= 0)
            {
                if (now - r.JailedUntil >= 0)
                {
                    Release(bot, r);
                    (gone ??= []).Add(serial);
                }
                else if (bot.Alive && !InCell(bot, r))
                {
                    Place(bot, r);
                }

                continue;
            }

            if (r.Wanted && now - r.WantedUntil >= 0)
            {
                r.Wanted = false;

                logger.Information("{Name} is no longer wanted", bot.Name);
            }
            else if (r.Wanted && bot.Alive && !bot.Criminal)
            {
                bot.Criminal = true;
            }

            if (now - r.RedUntil >= 0 && bot.Kills >= RedKills)
            {
                bot.Kills = 0;
                Redeemed++;

                logger.Information("{Name}'s hour is up; it is no longer red", bot.Name);
            }

            if (now - r.RedUntil >= 0 && !r.Wanted)
            {
                (gone ??= []).Add(serial);
            }
        }

        if (rebound != null)
        {
            for (var i = 0; i < rebound.Count; i++)
            {
                _records.Remove(rebound[i].Was);
                _records[rebound[i].Record.Bot.Serial] = rebound[i].Record;
            }
        }

        if (gone != null)
        {
            for (var i = 0; i < gone.Count; i++)
            {
                _records.Remove(gone[i]);
            }
        }
    }

    private static bool InCell(BotMobile bot, Record r) =>
        bot.Map == (BotPopulation.Home ?? Map.Felucca) && Utility.InRange(bot.Location, Cells[r.Cell], 3);

    private static void Place(BotMobile bot, Record r)
    {
        var map = BotPopulation.Home ?? Map.Felucca;
        var cell = Cells[r.Cell];

        bot.Combatant = null;
        bot.Warmode = false;
        bot.MoveToWorld(cell, map);
        bot.Journey?.Discard();

        if (bot.Resolve?.Deed is not BotSentence)
        {
            BotWill.Press(bot, new BotSentence(map, cell), $"caught by {r.CaughtBy ?? "a patrol"}");
        }
    }

    private static void Release(BotMobile bot, Record r)
    {
        bot.Kills = 0;
        Released++;

        var map = BotPopulation.Home ?? Map.Felucca;
        var home = BotSeat.Home(bot);

        if (bot.Alive)
        {
            bot.MoveToWorld(home, map);
            bot.Journey?.Discard();
        }

        logger.Information("{Name} is let out of cell {Cell} after {Minutes} minutes and put down at ({X}, {Y})", bot.Name, r.Cell, JailMs / 60000, home.X, home.Y);
    }

    public static string Describe() =>
        !Running
            ? "nobody robs, nobody is red and nobody is jailed"
            : $"{Murders} murders, {Robbed}gp robbed, {Caught} murderers caught by a patrol and {Released} let out, {Redeemed} went unpunished until the hour ran out, {Unavenged} were killed by something that was no patrol, {BaronsOwn} were the Baron's own kill and paid nobody, {Seized} catches stripped {SeizedGold}gp off the body, {SeizedBank}gp out of the bank and {SeizedThings} things into the treasury, {SeizedCorpse} corpses emptied where they lay and {Stripped} kits forfeit, {Shared} prices of blood split between a company; "
              + $"{RedCount()} red now and {JailedCount()} in cells of {Cells.Length}; {Told} made known to the Baron, {WantedPosted} made wanted; {Kept} errands kept a red out of a guarded town and {Routed} off a road through one, {Detoured} red roads drawn round the towns and {Stopped} red steps refused at a ward's edge; {Overfull} catches found every cell taken; {Rebound} records moved onto a bot's new body after a boot";

    public static void Forget()
    {
        _records.Clear();
        Murders = 0;
        Robbed = 0;
        Caught = 0;
        Released = 0;
        Redeemed = 0;
        Unavenged = 0;
        BaronsOwn = 0;
        Seized = 0;
        SeizedGold = 0;
        SeizedThings = 0;
        SeizedBank = 0;
        SeizedCorpse = 0;
        Stripped = 0;
        Shared = 0;
        Kept = 0;
        Routed = 0;
        Told = 0;
        WantedPosted = 0;
        Detoured = 0;
        Stopped = 0;
        _stoppedSaid.Clear();
        Overfull = 0;
        Rebound = 0;
    }

    private sealed class Clock : Timer
    {
        public Clock() : base(TimeSpan.FromMilliseconds(Math.Max(250, EveryMs)), TimeSpan.FromMilliseconds(Math.Max(250, EveryMs)))
        {
        }

        protected override void OnTick()
        {
            BotRobber.Beat(Core.TickCount);
            BotLawful.Beat(Core.TickCount);
            BotInquest.Beat(Core.TickCount);
            BotUnderworld.Beat(Core.TickCount);
            BotLair.Watch(Core.TickCount);
            BotFence.Beat(Core.TickCount);
            Tick();
        }
    }
}

/// <summary>
/// Sitting in a cell: a piece of work that does nothing until the clock lets the bot go. Pressed by
/// <see cref="BotOutlaw"/>, and the only thing a jailed bot holds.
/// </summary>
public sealed class BotSentence : BotDeed
{
    public const string Trade = "sentence";

    private readonly Map _map;

    private readonly Point3D _cell;

    public BotSentence(Map map, Point3D cell)
    {
        _map = map;
        _cell = cell;
    }

    public override string Kind => Trade;

    public override bool Committed => true;

    public override bool Steadfast => true;

    public override bool Summons => true;

    public override bool Braves => true;

    public override bool Unpaid => true;

    public override bool Still => true;

    public override Map Map => _map;

    public override Point3D Where => _cell;

    public override double Expects => 1.0;

    public override double Minutes => BotOutlaw.JailMs / 60000.0;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override int Made => 0;

    public override string Stage => "in a cell";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null)
        {
            return BotDoing.Failed("no body");
        }

        if (!BotOutlaw.Jailed(body))
        {
            return BotDoing.Done("let out");
        }

        return BotDoing.Work($"in a cell, {BotOutlaw.LeftInCell(body)} min left");
    }
}

/// <summary>
/// The Baron's patrol against a murderer: the Baron goes after the red, and presses the nearest few fit fighters into a
/// posse against the same one. A red the posse kills is caught — see <see cref="BotOutlaw.Fell"/>.
/// </summary>
public sealed class BotManhunt : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotManhunt));

    public const string Trade = "manhunt";

    public static int Posse { get; set; } = 4;

    public static int Reach { get; set; } = 60;

    public static long Raised { get; private set; }

    public static long Pressed { get; private set; }

    private readonly BotMobile _red;

    private readonly BotBrawl _fight;

    public BotManhunt(BotMobile red, SkillName trains)
    {
        _red = red;
        _fight = new BotBrawl(red, BotBrawl.Manhunt, trains, Over);
    }

    public static string Over(IBotWilful bot, BotBrawl brawl)
    {
        var red = brawl?.Foe as BotMobile;

        if (red is not { Deleted: false, Alive: true })
        {
            return $"{red?.Name ?? "the murderer"} is down";
        }

        if (BotOutlaw.Jailed(red))
        {
            return $"{red.Name} is in a cell";
        }

        return BotOutlaw.Outlaw(red) ? null : $"{red.Name} is no longer red or wanted";
    }

    public override string Kind => Trade;

    public override bool Committed => true;

    public override bool Steadfast => true;

    public override bool Braves => true;

    public override bool Unpaid => true;

    public override Mobile Foe => _red;

    public override Map Map => _fight.Map;

    public override Point3D Where => _fight.Where;

    public override double Expects => BotBrawl.Prior;

    public override double Minutes => BotBrawl.WorkMinutes;

    public override SkillName? Trains => _fight.Trains;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override int Made => 0;

    public override string Stage => $"patrol against {_red?.Name}: {_fight.Stage}";

    public override void Taken(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (body == null || map == null || map == Map.Internal || _red == null)
        {
            return;
        }

        Raised++;

        var pressed = 0;
        List<BotMobile> near = [];

        foreach (var other in map.GetMobilesInRange<BotMobile>(body.Location, Reach))
        {
            if (other == body || other == _red || !other.Alive || other.Squad != null || other.Class is not { } klass
                || klass.Role is BotRole.Producer or BotRole.Medic || klass.Unpaid || BotOutlaw.Outlaw(other)
                || BotDuel.Duelling(other) || other.Resolve?.Deed is BotBrawl || BotUnderworld.Member(other))
            {
                continue;
            }

            near.Add(other);
        }

        near.Sort((x, y) => x.GetDistanceToSqrt(body).CompareTo(y.GetDistanceToSqrt(body)));

        for (var i = 0; i < near.Count && pressed < Posse; i++)
        {
            var hand = near[i];
            var trains = hand.Bond?.Weapon?.Skill ?? SkillName.Wrestling;

            if (BotWill.Press(hand, new BotBrawl(_red, BotBrawl.Manhunt, trains, Over), $"the Baron's patrol against {_red.Name}"))
            {
                pressed++;
            }
        }

        Pressed += pressed;

        logger.Information(
            "{Baron} raises a patrol of {Count} against {Red} the murderer, red at ({X}, {Y})",
            body.Name,
            pressed,
            _red.Name,
            _red.X,
            _red.Y
        );
    }

    public override BotDoing Advance(IBotWilful bot) => _fight.Advance(bot);

    public override bool Bend(IBotWilful bot) => _fight.Bend(bot);

    public override void Drop(IBotWilful bot) => _fight.Drop(bot);

    public static string Describe() => $"{Raised} patrols raised against murderers, {Pressed} fighters pressed into them; {BotLawful.Describe()}; {BotInquest.Describe()}";

    public static void Forget()
    {
        Raised = 0;
        Pressed = 0;
    }
}
