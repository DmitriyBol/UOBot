using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;
using Server.Regions;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// The board of errands: what the watchers and the door ask of the population, for a price.
///
/// <para>
/// <b>Every mechanism the city had was an errand without a board.</b> A bounty on a square, a price on a head, a
/// standing order, a house call: each is "somebody wants this done and pays for it", each with its own list, its
/// own verb and its own taker. Patrick's order of 16.09.2026, evening: one board, with the power to post on it
/// given to the door (Argus and the debuggers) and to the marshal of events among them, paid from the city's
/// treasury; and any bot fit for the errand may take it (<see cref="BotQuester"/>) and is paid on the spot when it
/// is done. The guilds' masters do not post: Patrick took that back the same evening — they are ordinary bots.
/// </para>
///
/// <para>
/// Three kinds to begin with — kill so many of a creature near a place, bring so many of a thing to a place, scout a
/// place — because each maps onto work the population already knows how to do (<see cref="BotSlay"/>, a walk with a
/// pack, a walk). The reward is taken from the treasury the moment the errand is posted and held by the board, so
/// nothing is posted that cannot be paid, and a lapsed or withdrawn errand gives the money back. The engine's own
/// quests (Uzeraan, the Witch's apprentice, the Solen) are not here yet; when they are, they will be a further source
/// of errands on this board, not a second board.
/// </para>
///
/// <para>
/// Kept across restarts by <see cref="BotQuestStore"/>; a taker is not kept, so a restart puts every errand back on the
/// board with its count. "An offer is not an errand": an errand is taken in the deed's <c>Taken</c>, never in the
/// proposer, and a taken errand with no progress in <see cref="HoldMs"/> goes back on the board.
/// </para>
/// </summary>
public static class BotQuests
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotQuests));

    public static bool Running { get; set; } = true;

    public static int MostOpen { get; set; } = 12;

    public static int LapseMs { get; set; } = 7200000;

    public static int HoldMs { get; set; } = 1200000;

    public static int MostAmount { get; set; } = 200;

    public static int MostReward { get; set; } = 20000;

    public static int GatherTimesWorth { get; set; } = 10;

    public static int GatherLeastPerPiece { get; set; } = 20;

    public static long Capped { get; private set; }

    public static int KillRange { get; set; } = 120;

    public static int RetakeMs { get; set; } = 600000;

    public static int LookRange { get; set; } = 40;

    public static int EmptyMs { get; set; } = 180000;

    public static int MostLairs { get; set; } = 3;

    public static int MostLetGo { get; set; } = 3;

    public static int DeliverReach { get; set; } = 4;

    public static long Posted { get; private set; }

    public static long RefusedPosts { get; private set; }

    public static long Duplicates { get; private set; }

    public static long Taken { get; private set; }

    public static long Released { get; private set; }

    public static long Finished { get; private set; }

    public static long Lapsed { get; private set; }

    public static long Withdrawn { get; private set; }

    public static long Abandoned { get; private set; }

    public static long Paid { get; private set; }

    private static readonly List<BotQuest> _quests = [];

    private static int _nextId = 1;

    private static Clock _timer;

    public static IReadOnlyList<BotQuest> All => _quests;

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

    public static string PostByName(string kind, string what, int amount, int reward, Map map, int x, int y, string by)
    {
        var known = (kind ?? "").Trim().ToLowerInvariant() switch
        {
            "kill" => BotQuestKind.Kill,
            "gather" or "bring" => BotQuestKind.Gather,
            "scout" or "look" => BotQuestKind.Scout,
            _ => (BotQuestKind?)null
        };

        if (known == null)
        {
            RefusedPosts++;

            return $"the board takes errands to kill, gather or scout; \"{kind}\" is none of them.";
        }

        return Post(known.Value, what, amount, reward, map, x, y, by);
    }

    public static string Post(BotQuestKind kind, string what, int amount, int reward, Map map, int x, int y, string by)
    {
        if (!Running)
        {
            RefusedPosts++;

            return "the board is closed.";
        }

        if (map == null || map == Map.Internal)
        {
            RefusedPosts++;

            return "the board has no map to post on.";
        }

        if (Open() >= MostOpen)
        {
            RefusedPosts++;

            return $"the board holds at most {MostOpen} errands; wait for one to be done or withdraw one.";
        }

        if (reward <= 0 || reward > MostReward)
        {
            RefusedPosts++;

            return $"an errand pays between 1 and {MostReward}gp.";
        }

        Type type = null;
        var capped = "";

        if (kind != BotQuestKind.Scout)
        {
            if (amount <= 0 || amount > MostAmount)
            {
                RefusedPosts++;

                return $"an errand asks for between 1 and {MostAmount}.";
            }

            type = string.IsNullOrWhiteSpace(what) ? null : AssemblyHandler.FindTypeByName(what.Trim());

            if (kind == BotQuestKind.Kill && (type == null || !typeof(BaseCreature).IsAssignableFrom(type)))
            {
                RefusedPosts++;

                return $"the board knows no creature called {what}.";
            }

            if (kind == BotQuestKind.Gather && (type == null || !typeof(Item).IsAssignableFrom(type)))
            {
                RefusedPosts++;

                return $"the board knows no thing called {what}.";
            }

            for (var i = 0; i < _quests.Count; i++)
            {
                if (_quests[i].Kind == kind && _quests[i].Type == type)
                {
                    RefusedPosts++;
                    Duplicates++;

                    return $"that errand is already on the board: {_quests[i].Tell()}; wait for it to be done.";
                }
            }

            if (kind == BotQuestKind.Gather && GatherTimesWorth > 0)
            {
                var piece = Math.Max(GatherLeastPerPiece, BotAuction.Worth(type, 1) * GatherTimesWorth);
                var most = (int)Math.Min(MostReward, (long)piece * amount);

                if (reward > most)
                {
                    capped = $" (brought down from {reward}gp: a gathering errand pays at most {piece}gp a {type.Name})";
                    reward = most;
                    Capped++;
                }
            }
        }

        var where = Point3D.Zero;
        var lairs = 0;

        if (x >= 0 && y >= 0)
        {
            if (!Place(map, x, y, out where))
            {
                RefusedPosts++;

                return $"nothing can stand within {PlaceWithin} tiles of ({x}, {y}).";
            }

            if (kind == BotQuestKind.Scout && Region.Find(where, map)?.IsPartOf<TownRegion>() == true)
            {
                RefusedPosts++;

                return $"a scout is sent to ground outside the walls, and ({x}, {y}) is inside them. Leave the place out, and the board sends it to the nearest ground nobody has counted for {BotQuad.StaleMs / 3600000} hours.";
            }

            if (kind == BotQuestKind.Kill && type != null && BotLairs.Count(map, type) > 0)
            {
                var lair = BotLairs.Closest(map, type, where, out var reach);

                if (lair != Point3D.Zero && reach > KillRange)
                {
                    RefusedPosts++;

                    return $"no spawner keeps a {type.Name} within {KillRange} tiles of ({x}, {y}); the nearest keeps them round ({lair.X}, {lair.Y}), {Math.Max(Math.Abs(lair.X - x), Math.Abs(lair.Y - y))} tiles off. Post it there, or anywhere.";
                }
            }
        }
        else if (kind == BotQuestKind.Kill)
        {
            lairs = BotLairs.Count(map, type);

            if (lairs == 0)
            {
                RefusedPosts++;

                return $"no spawner on the island keeps a {type.Name}, so \"anywhere\" is nowhere a bot can walk to; give the errand a place.";
            }
        }
        else if (kind == BotQuestKind.Scout)
        {
            where = BotQuad.Stalest(map, BotPopulation.Where, ScoutWithin, null);

            if (where == Point3D.Zero)
            {
                RefusedPosts++;

                return $"no ground within {ScoutWithin} tiles of home has gone uncounted for {BotQuad.StaleMs / 3600000} hours, so there is nothing to scout.";
            }
        }
        else
        {
            var home = BotPopulation.Where;

            if (!Place(map, home.X, home.Y, out where))
            {
                RefusedPosts++;

                return "the errand needs a place, and nobody can stand at the population's home.";
            }
        }

        if (!BotCity.Escrow(reward))
        {
            RefusedPosts++;

            return $"the treasury holds {BotCity.Purse}gp and cannot hold {reward}gp for an errand.";
        }

        var quest = new BotQuest
        {
            Id = _nextId++,
            Kind = kind,
            What = type?.Name,
            Type = type,
            Amount = kind == BotQuestKind.Scout ? 1 : amount,
            Map = map,
            Where = where,
            Held = reward,
            By = by ?? "somebody",
            PostedTick = Core.TickCount
        };

        _quests.Add(quest);
        Posted++;

        logger.Information("{By} posted an errand on the board: {Errand}; {Purse}gp left in the treasury", quest.By, quest.Tell(), BotCity.Purse);

        return lairs > 0
            ? $"posted {quest.Tell()}, to be found at the nearest of {lairs} places on the island that keep it; {BotCity.Purse}gp left in the treasury."
            : $"posted {quest.Tell()}{capped}; {BotCity.Purse}gp left in the treasury.";
    }

    public static int PlaceWithin { get; set; } = 8;

    public static int ScoutWithin { get; set; } = 400;

    private static bool Place(Map map, int x, int y, out Point3D where)
    {
        for (var ring = 0; ring <= PlaceWithin; ring++)
        {
            for (var dx = -ring; dx <= ring; dx++)
            {
                for (var dy = -ring; dy <= ring; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring || !BotStep.Settle(map, x + dx, y + dy, out var z))
                    {
                        continue;
                    }

                    where = new Point3D(x + dx, y + dy, z);

                    return true;
                }
            }
        }

        where = Point3D.Zero;

        return false;
    }

    public static string Unpost(int id, string by)
    {
        var quest = Find(id);

        if (quest == null)
        {
            return $"there is no errand #{id} on the board.";
        }

        if (!quest.Open)
        {
            Release(quest, $"withdrawn by {by}");
        }

        Refund(quest);
        _quests.Remove(quest);
        Withdrawn++;

        logger.Information("{By} withdrew the errand {Errand}; its {Held}gp went back to the treasury", by, quest.Tell(), quest.Held);

        return $"withdrew {quest.Tell()}; {quest.Held}gp back in the treasury.";
    }

    public static BotQuest Find(int id)
    {
        for (var i = 0; i < _quests.Count; i++)
        {
            if (_quests[i].Id == id)
            {
                return _quests[i];
            }
        }

        return null;
    }

    public static bool Standing(BotQuest quest) => quest != null && _quests.Contains(quest);

    internal static bool Take(BotQuest quest, BotMobile bot)
    {
        if (quest == null || bot == null || !Standing(quest) || !quest.Open)
        {
            return false;
        }

        quest.Taker = bot;
        quest.TakenTick = Core.TickCount;
        Taken++;

        logger.Information("{Name} took the errand {Errand}", bot.Name, quest.Tell());

        return true;
    }

    internal static void Release(BotQuest quest, string why, bool empty = false)
    {
        if (quest == null || quest.Taker == null)
        {
            return;
        }

        var was = quest.Taker;

        quest.Taker = null;
        quest.LastTaker = was;
        quest.LetGoTick = Core.TickCount;
        Released++;

        if (!empty)
        {
            logger.Information("{Name} let the errand {Errand} go: {Why}; it is back on the board", was.Name, quest.Tell(), why);

            return;
        }

        quest.LetGo++;
        quest.LetGoBecause = why;

        if (quest.LetGo < MostLetGo || !Standing(quest))
        {
            logger.Information(
                "{Name} let the errand {Errand} go with nothing done for it, {LetGo} of {Most} takers so far: {Why}; it is back on the board",
                was.Name,
                quest.Tell(),
                quest.LetGo,
                MostLetGo,
                why
            );

            return;
        }

        Refund(quest);
        _quests.Remove(quest);
        Abandoned++;

        logger.Information(
            "The errand {Errand} came down after {LetGo} takers let it go with nothing done for it, the last of them {Name}: {Why}; its {Held}gp went back to the treasury",
            quest.Tell(),
            quest.LetGo,
            was.Name,
            why,
            quest.Held
        );
    }

    internal static void Touch(BotQuest quest, Mobile taker)
    {
        if (quest != null && taker != null && ReferenceEquals(quest.Taker, taker))
        {
            quest.TakenTick = Core.TickCount;
        }
    }

    internal static void Progress(BotQuest quest, int done)
    {
        if (quest == null || done <= 0)
        {
            return;
        }

        quest.Done += done;
        quest.TakenTick = Core.TickCount;
    }

    internal static void Finish(BotQuest quest, BotMobile bot)
    {
        if (quest == null || !Standing(quest))
        {
            return;
        }

        var held = quest.Held;

        var told = quest.Tell();

        if (bot is { Deleted: false } && held > 0)
        {
            var pack = bot.Backpack;

            if (pack != null)
            {
                pack.DropItem(new Gold(held));
            }
            else
            {
                Banker.Deposit(bot, held);
            }

            Paid += held;
            BotCity.Disbursed(held);
        }

        quest.Held = 0;
        _quests.Remove(quest);
        Finished++;

        logger.Information("{Name} finished the errand {Errand} and was paid {Gold}gp", bot?.Name ?? "nobody", told, held);
    }

    private static void Refund(BotQuest quest)
    {
        if (quest.Held > 0)
        {
            BotCity.Refund(quest.Held);
        }
    }

    private static int Open()
    {
        var open = 0;

        for (var i = 0; i < _quests.Count; i++)
        {
            if (_quests[i].Open)
            {
                open++;
            }
        }

        return open;
    }

    private static void Lapse()
    {
        var now = Core.TickCount;

        for (var i = _quests.Count - 1; i >= 0; i--)
        {
            var quest = _quests[i];

            if (quest.Open)
            {
                if (now - quest.PostedTick < LapseMs)
                {
                    continue;
                }

                Refund(quest);
                _quests.RemoveAt(i);
                Lapsed++;

                logger.Information("The errand {Errand} lapsed after {Hours} hours untaken; its {Held}gp went back to the treasury", quest.Tell(), LapseMs / 3600000, quest.Held);

                continue;
            }

            if (quest.Taker is not { Deleted: false, Alive: true })
            {
                Release(quest, "the taker is dead or gone");

                continue;
            }

            if (now - quest.TakenTick >= HoldMs)
            {
                Release(quest, $"no progress in {HoldMs / 60000} minutes");
            }
        }
    }

    public static string Tell()
    {
        if (_quests.Count == 0)
        {
            return "nothing on the board";
        }

        using var say = ValueStringBuilder.Create(512);

        for (var i = 0; i < _quests.Count; i++)
        {
            if (i > 0)
            {
                say.Append("; ");
            }

            say.Append(_quests[i].Tell());
        }

        return say.ToString();
    }

    public static string Describe() =>
        !Running
            ? "the board is closed"
            : $"{Posted} errands posted ({RefusedPosts} refused, {Duplicates} of them for standing on the board already), {Taken} taken, {Released} let go, {Finished} finished for {Paid}gp, {Lapsed} lapsed, {Withdrawn} withdrawn, {Abandoned} taken down after {MostLetGo} takers did nothing for them; {BotQuester.Describe()}; lairs: {BotLairs.Describe()}; on the board: {Tell()}";

    internal static void Save(IGenericWriter writer)
    {
        var now = Core.TickCount;

        writer.WriteEncodedInt(_nextId);
        writer.WriteEncodedInt(_quests.Count);

        for (var i = 0; i < _quests.Count; i++)
        {
            var quest = _quests[i];

            writer.WriteEncodedInt(quest.Id);
            writer.WriteEncodedInt((int)quest.Kind);
            writer.Write(quest.What ?? "");
            writer.WriteEncodedInt(quest.Amount);
            writer.WriteEncodedInt(quest.Done);
            writer.Write(quest.Map);
            writer.Write(quest.Where);
            writer.WriteEncodedInt(quest.Held);
            writer.Write(quest.By ?? "");
            writer.Write(Math.Max(0L, now - quest.PostedTick));
        }
    }

    internal static int Load(IGenericReader reader)
    {
        var now = Core.TickCount;

        _quests.Clear();
        _nextId = Math.Max(1, reader.ReadEncodedInt());

        var count = reader.ReadEncodedInt();

        for (var i = 0; i < count; i++)
        {
            var id = reader.ReadEncodedInt();
            var kind = (BotQuestKind)reader.ReadEncodedInt();
            var what = reader.ReadString();
            var amount = reader.ReadEncodedInt();
            var done = reader.ReadEncodedInt();
            var map = reader.ReadMap();
            var where = reader.ReadPoint3D();
            var held = reader.ReadEncodedInt();
            var by = reader.ReadString();
            var age = reader.ReadLong();
            var type = string.IsNullOrEmpty(what) ? null : AssemblyHandler.FindTypeByName(what);

            if (map == null || map == Map.Internal || held <= 0 || (kind != BotQuestKind.Scout && type == null))
            {
                continue;
            }

            _quests.Add(
                new BotQuest
                {
                    Id = id,
                    Kind = kind,
                    What = type?.Name,
                    Type = type,
                    Amount = Math.Max(1, amount),
                    Done = Math.Max(0, done),
                    Map = map,
                    Where = where,
                    Held = held,
                    By = string.IsNullOrEmpty(by) ? "somebody" : by,
                    PostedTick = now - Math.Max(0L, age)
                }
            );

            BotCity.Held(held);
        }

        return _quests.Count;
    }

    public static void Wipe()
    {
        for (var i = 0; i < _quests.Count; i++)
        {
            Refund(_quests[i]);
        }

        _quests.Clear();
    }

    public static void Forget()
    {
        Posted = 0;
        RefusedPosts = 0;
        Duplicates = 0;
        Taken = 0;
        Released = 0;
        Finished = 0;
        Lapsed = 0;
        Withdrawn = 0;
        Abandoned = 0;
        Paid = 0;
    }

    private sealed class Clock : Timer
    {
        public Clock() : base(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60))
        {
        }

        protected override void OnTick()
        {
            if (Running)
            {
                Lapse();
            }
        }
    }
}
