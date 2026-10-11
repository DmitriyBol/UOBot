using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// The events of the world (07.10.2026, Patrick's five ideas approved at 00:10: "let's build them tonight, and keep watching the
/// key numbers"): an invasion out of a dungeon onto a town's road, a fair once a day, caravans between the towns, the night, and an
/// hourly rumour. This class is what the five share: the switch, the clock that turns them, the voice that announces them, the
/// one way coin is handed to a bot, and the <c>Events:</c> line.
///
/// <para>
/// <b>Nothing here mints.</b> An invasion's reward and a caravan's pay are held out of the city's own purse the moment they are
/// posted (<see cref="BotCity.Escrow"/>) and paid out of that hold or handed back; a lottery is tickets in and the same coin out
/// to one winner; a rumour's chest is filled from the crown's purse (the revels' own, through <see cref="CrownTake"/>) and goes
/// back there if nobody finds it; the fair's lower prices are less coin destroyed at an NPC's counter, not coin made. Every coin
/// that moves is counted on the <c>Events:</c> line, and a restart hands back whatever was held (<see cref="BotEventsStore"/>).
/// </para>
///
/// <para>
/// <b>The minds' assembly speaks for the towns.</b> Argus's herald announces through <see cref="Herald"/>, the championship is
/// held at the fair by <c>BotTourney</c> reading <see cref="BotFair.TownName"/>, and the crown's purse is reached through
/// <see cref="CrownTake"/> and <see cref="CrownGive"/>. All three are filled from <c>mindedBots/debugger/BotEventsArgus</c>; with
/// the debugger off, the announcements still go to the world channel and the log, and rumours are not spread.
/// </para>
/// </summary>
public static class BotSeasons
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSeasons));

    public static bool Running { get; set; } = true;

    public static int EveryMs { get; set; } = 5000;

    public static int SayEveryMs { get; set; } = 300000;

    public static int CityReserve { get; set; } = 1000;

    public static int Hue { get; set; } = 0x35;

    public static Action<Map, Point3D, string> Herald { get; set; }

    public static Func<int, int> CrownTake { get; set; }

    public static Action<int> CrownGive { get; set; }

    public static Func<string, string> CallTourney { get; set; }

    public static long Announced { get; private set; }

    public static long PaidOut { get; private set; }

    public static long Unpayable { get; private set; }

    private static Timer _clock;

    private static Timer _summary;

    public static void Start()
    {
        Stop();

        _clock = Timer.DelayCall(
            TimeSpan.FromMilliseconds(Math.Max(1000, EveryMs)),
            TimeSpan.FromMilliseconds(Math.Max(1000, EveryMs)),
            Tick
        );

        _summary = Timer.DelayCall(
            TimeSpan.FromMilliseconds(Math.Max(10000, SayEveryMs)),
            TimeSpan.FromMilliseconds(Math.Max(10000, SayEveryMs)),
            () => logger.Information("Events: {What}", Describe())
        );
    }

    public static void Stop()
    {
        _clock?.Stop();
        _clock = null;
        _summary?.Stop();
        _summary = null;
    }

    private static void Tick()
    {
        if (!Running || !BotTowns.Ready)
        {
            return;
        }

        var now = Core.TickCount;

        Guard("night", () => BotNight.Beat(now));
        Guard("invasion", () => BotInvasion.Beat(now));
        Guard("fair", () => BotFair.Beat(now));
        Guard("caravan", () => BotCaravans.Beat(now));
        Guard("rumour", () => BotRumour.Beat(now));
    }

    private static readonly Dictionary<string, long> _faults = [];

    private static void Guard(string what, Action beat)
    {
        try
        {
            beat();
        }
        catch (Exception e)
        {
            _faults[what] = _faults.TryGetValue(what, out var n) ? n + 1 : 1;

            if (_faults[what] <= 3 || _faults[what] % 100 == 0)
            {
                logger.Error(e, "The {What} beat failed ({Times} times so far)", what, _faults[what]);
            }
        }
    }

    public static void Announce(string what, Map map, Point3D at, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        Announced++;

        logger.Information("Events: {What}: {Line}", what, line);
        BotEvents.Post("event", "the town crier", null, null, at.X, at.Y, line, what, null);

        var herald = Herald;

        if (herald != null)
        {
            try
            {
                herald(map, at, line);

                return;
            }
            catch (Exception e)
            {
                logger.Warning("The herald could not call the {What}: {Message}", what, e.Message);
            }
        }

        if (Network.NetState.Instances.Count > 0)
        {
            World.Broadcast(Hue, true, line);
        }
    }

    public static void Say(Mobile who, string channel, string line) => BotVoice.Say(who, channel, line, true);

    public static bool Pay(Mobile who, int gold)
    {
        if (gold <= 0)
        {
            return true;
        }

        if (who is not { Deleted: false })
        {
            Unpayable += gold;

            return false;
        }

        if (who.FindBankNoCreate() == null)
        {
            _ = who.BankBox;
        }

        if (Banker.Deposit(who, gold))
        {
            PaidOut += gold;

            return true;
        }

        if (who.Backpack is { } pack && pack.TryDropItem(who, new Gold(gold), false))
        {
            PaidOut += gold;

            return true;
        }

        Unpayable += gold;

        return false;
    }

    public static int Hold(int asked, int least)
    {
        if (!BotCity.Running || asked <= 0)
        {
            return 0;
        }

        var can = Math.Min(asked, BotCity.Purse - Math.Max(0, CityReserve));

        if (can < Math.Max(1, least) || !BotCity.Escrow(can))
        {
            return 0;
        }

        Held += can;

        return can;
    }

    public static bool Disburse(Mobile who, int gold)
    {
        if (gold <= 0 || !Pay(who, gold))
        {
            return false;
        }

        BotCity.Disbursed(gold);
        Released += gold;

        return true;
    }

    public static void Return(int gold)
    {
        if (gold <= 0)
        {
            return;
        }

        BotCity.Refund(gold);
        Returned += gold;
    }

    public static long Held { get; private set; }

    public static long Released { get; private set; }

    public static long Returned { get; private set; }

    internal static void HeldBefore(int gold)
    {
        if (gold > 0)
        {
            BotCity.Held(gold);
            Held += gold;
        }
    }

    public static bool Open(Map map, int x, int y, out Point3D at) => Why(map, x, y, out at) == null;

    public static string Why(Map map, int x, int y, out Point3D at)
    {
        at = Point3D.Zero;

        if (map == null || map == Map.Internal)
        {
            return "no map";
        }

        if (!BotStep.Settle(map, x, y, out var z))
        {
            return "no ground to stand on";
        }

        var spot = new Point3D(x, y, z);

        if (BotReachMap.Class(map, x, y) is var kind && kind != BotReachClass.Unknown && kind != BotReachClass.Open)
        {
            return $"the reach map reads it {kind}";
        }

        if (Region.Find(spot, map)?.IsPartOf<GuardedRegion>() == true)
        {
            return "guarded ground";
        }

        if (BotBarred.Holds(map, spot))
        {
            return "barred ground";
        }

        if (BotDungeon.Under(spot))
        {
            return "a dungeon's ground";
        }

        if (BotZones.LevelAt(map, x, y) is BotZoneLevel.Deadly)
        {
            return "a deadly zone";
        }

        if (!map.CanFit(x, y, z, 16, false, false))
        {
            return "too cramped to stand";
        }

        at = spot;

        return null;
    }

    public static string Bearing(BotTowns.Town town, Point3D at)
    {
        if (town == null)
        {
            return $"at ({at.X}, {at.Y})";
        }

        var dx = at.X - town.Square.X;
        var dy = at.Y - town.Square.Y;
        var far = (int)Math.Sqrt(dx * dx + dy * dy);
        var ns = dy < -far / 3 ? "north" : dy > far / 3 ? "south" : "";
        var ew = dx > far / 3 ? "east" : dx < -far / 3 ? "west" : "";
        var way = ns.Length > 0 && ew.Length > 0 ? $"{ns}-{ew}" : ns + ew;

        return far < 12 ? $"in {town.Name}" : $"{far} tiles {(way.Length > 0 ? way : "out")} of {town.Name}";
    }

    public static bool Fighter(Mobile m) =>
        m is BotMobile { Deleted: false, Alive: true } bot && bot.Map != null && bot.Map != Map.Internal
        && bot is IBotAlly { AbleToFight: true } && !BotTrading.Neutral(bot) && bot.HitsMax > 0
        && bot.Hits >= bot.HitsMax * BotHunter.FitAt;

    public static string Describe() =>
        !Running
            ? "the events are switched off (BotSeasons.Running)"
            : $"invasions: {BotInvasion.Describe()}; fairs: {BotFair.Describe()}; caravans: {BotCaravans.Describe()}; night: {BotNight.Describe()}; rumours: {BotRumour.Describe()}; "
              + $"the city's purse: {Held}gp held for events, {Released} paid out of the hold, {Returned} handed back, {Held - Released - Returned} held now; {PaidOut}gp handed to bots in all (lottery and rumours included), {Unpayable} that could not be put anywhere; {Announced} announcements";

    public static void Forget()
    {
        Announced = 0;
        PaidOut = 0;
        Unpayable = 0;
        Held = 0;
        Released = 0;
        Returned = 0;
        _faults.Clear();
    }

    public static int Wipe()
    {
        var before = Returned;

        BotInvasion.Stand();
        BotCaravans.Stand();
        BotFair.Stand();

        var crown = BotRumour.Stand();
        var back = (int)Math.Min(int.MaxValue, Returned - before + crown);

        BotInvasion.Forget();
        BotCaravans.Forget();
        BotCaravans.For = null;
        BotFair.Forget();
        BotFair.Asked = null;
        BotFair.LastUtc = DateTime.MinValue;
        BotFair.Turn = 0;
        BotNight.Forget();
        BotRumour.Forget();
        Forget();

        return back;
    }
}
