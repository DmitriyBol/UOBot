using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Meetings: one guild's envoy walks to another guild's town, or to its own town's hall, and a word is given there before a
/// witness — war, peace, trade, standing orders, an alliance, or a task from the town.
///
/// <para>
/// <b>Patrick's order of 29.09.2026, evening.</b> "Guilds may declare war on each other only if they meet in the main building
/// of a town. One of the debuggers is invited as the witness: he becomes visible and hears both sides. The bots can come to an
/// agreement or start a war. The meeting must be on the guild's own ground: if The Blade wants war with The Needle and The Needle
/// is in Cove, The Blade's envoy must go to Cove and talk to them there. This removes lightning decisions and gives the guilds
/// time for their work." And the same way: alliances, orders, trade and safety for each other; and a guild's dealings with its
/// town, whose voice gives it a task.
/// </para>
///
/// <para>
/// <b>What it replaces.</b> <c>BotRegard</c> declared a war the moment an opinion crossed <c>Enmity</c> and the war's rules let
/// it, and made an alliance the moment both opinions crossed <c>Alliance</c>. Both now call a meeting instead
/// (<see cref="Grieve"/>, <see cref="Befriend"/>), and the war or the alliance comes of the meeting or does not. Two more topics
/// are called by this file's own look: a guild another thinks ill of goes to ask for peace before the grievance reaches the war
/// line (<see cref="WaryShare"/>), and two guilds on good terms go to agree trade (<see cref="Cordial"/>). Wars already standing
/// are left alone.
/// </para>
///
/// <para>
/// <b>A meeting is walked, waited for and heard; nothing in it is instant.</b> An envoy (any member; a guild of one sends its
/// founder) takes the errand (<see cref="BotEnvoy"/>) and walks to the place (<see cref="Seat"/>); the host guild is asked to
/// send somebody once the envoy is near; a witness is summoned from the debugger's squad through <see cref="Summon"/>; the
/// lines are said a few seconds apart; the duke's word is asked of the model through <see cref="Judge"/> and, if it does not
/// come in <see cref="JudgeWaitMs"/>, given by rule (<see cref="BotDuke.Rule"/>). Every stage has its own clock, so a meeting
/// that cannot happen lapses and says why, and the errands that served it end honestly — finished when the word was given,
/// failed when it lapsed.
/// </para>
/// </summary>
public static class BotParley
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotParley));

    public static bool Running { get; set; } = true;

    public static bool Active { get; internal set; }

    public static int MostMeetings { get; set; } = 2;

    public static int MostAudiences { get; set; } = 2;

    public static int BeatMs { get; set; } = 2000;

    public static int LookMs { get; set; } = 60000;

    public static int OfferMs { get; set; } = 1200000;

    public static int MarchMs { get; set; } = 1800000;

    public static int ClaimMs { get; set; } = 180000;

    public static int HostCallTiles { get; set; } = 150;

    public static int HostWaitMs { get; set; } = 240000;

    public static int WitnessWaitMs { get; set; } = 180000;

    public static int LineMs { get; set; } = 5000;

    public static int JudgeWaitMs { get; set; } = 120000;

    public static int LongestMs { get; set; } = 5400000;

    public static int MeetEveryMs { get; set; } = 7200000;

    public static int RetryMs { get; set; } = 1800000;

    public static int ArriveTiles { get; set; } = 4;

    public static double WaryShare { get; set; } = 0.5;

    public static double Cordial { get; set; } = 25.0;

    public static double CallOdds { get; set; } = 0.2;

    public static int SeatTownTiles { get; set; } = 300;

    public static bool MeetAtSeat { get; set; } = false;

    public static Dictionary<string, int[]> Halls { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string JudgeModel { get; set; }

    public static string JudgeKeepAlive { get; set; } = "10s";

    public static int JudgeTimeoutMs { get; set; } = 90000;

    public static Func<BotHearing, Mobile> Summon { get; set; }

    public static Action<BotHearing> Dismiss { get; set; }

    public static Func<BotHearing, Action<BotVerdict>, bool> Judge { get; set; }

    public static long Called { get; private set; }

    private static readonly long[] _calledBy = new long[5];

    public static long Heard { get; private set; }

    public static long Witnessed { get; private set; }

    public static long Unwitnessed { get; private set; }

    public static long HostsAbsent { get; private set; }

    public static long NoEnvoy { get; private set; }

    public static long Lost { get; private set; }

    public static long TooLong { get; private set; }

    public static long Vanished { get; private set; }

    public static long Pending { get; private set; }

    public static long Standing { get; private set; }

    public static long Queued { get; private set; }

    public static long Unreachable { get; private set; }

    public static long Wars { get; internal set; }

    public static long Forbidden { get; internal set; }

    public static long Alliances { get; internal set; }

    public static long Late { get; private set; }

    public static long Unread { get; private set; }

    private static readonly List<BotHearing> _meetings = [];

    private static readonly List<string> _recent = [];

    private static readonly Dictionary<(string A, string B), long> _clocks = [];

    private static readonly Dictionary<string, Point3D> _halls = new(StringComparer.OrdinalIgnoreCase);

    private static int _nextId;

    private static long _lookedTick;

    private static bool _everLooked;

    public static IReadOnlyList<BotHearing> Meetings => _meetings;

    public static IReadOnlyList<string> Recent => _recent;

    private static (string A, string B) Pair(string one, string other) =>
        string.CompareOrdinal(one, other) <= 0 ? (one, other) : (other, one);

    public static Point3D Hall(BotTowns.Town town)
    {
        if (town == null)
        {
            return Point3D.Zero;
        }

        if (_halls.TryGetValue(town.Name, out var known))
        {
            return known;
        }

        var at = Point3D.Zero;
        var map = BotPopulation.Home;

        if (map != null && map != Map.Internal && Halls != null && Halls.TryGetValue(town.Name, out var set) && set is { Length: >= 2 }
            && BotTowns.Place(map, set[0], set[1], 4, out var spot))
        {
            at = spot;
        }

        if (at == Point3D.Zero)
        {
            at = BotSettle.Of(town)?.Hearth ?? town.Square;
        }

        if (at == Point3D.Zero)
        {
            at = town.Square;
        }

        if (!BotGates.Ready)
        {
            return at;
        }

        at = Doorstep(map, town, at);

        _halls[town.Name] = at;

        return at;
    }

    public static int DoorstepTiles { get; set; } = 16;

    public static long Doorsteps { get; private set; }

    public static long Doorless { get; private set; }

    private static Point3D Doorstep(Map map, BotTowns.Town town, Point3D at)
    {
        if (map == null || at == Point3D.Zero || town.Square == Point3D.Zero || BotGates.Joined(map, at, town.Square))
        {
            return at;
        }

        for (var r = 1; r <= DoorstepTiles; r++)
        {
            for (var dx = -r; dx <= r; dx++)
            {
                for (var dy = -r; dy <= r; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                    {
                        continue;
                    }

                    if (!BotStep.Settle(map, at.X + dx, at.Y + dy, out var z) || !map.CanSpawnMobile(at.X + dx, at.Y + dy, z))
                    {
                        continue;
                    }

                    var spot = new Point3D(at.X + dx, at.Y + dy, z);

                    if (!BotGates.Joined(map, spot, town.Square))
                    {
                        continue;
                    }

                    Doorsteps++;

                    logger.Information(
                        "{Town}'s hall at {From} stands where no road from the town reaches; its meetings are held at {To}, {Tiles} tiles off",
                        town.Name,
                        at,
                        spot,
                        r
                    );

                    return spot;
                }
            }
        }

        Doorless++;

        logger.Information(
            "{Town}'s hall at {From} stands where no road from the town reaches, and nothing within {Tiles} tiles does",
            town.Name,
            at,
            DoorstepTiles
        );

        return at;
    }

    public static bool Seat(Guild guild, out BotTowns.Town town, out Point3D at)
    {
        town = null;
        at = Point3D.Zero;

        var map = BotPopulation.Home;

        if (guild == null || map == null || map == Map.Internal || !BotTowns.Surveyed)
        {
            return false;
        }

        var seat = BotSeat.Of(guild.Name);

        if (seat != Point3D.Zero)
        {
            var near = BotTowns.Nearest(seat);

            if (near != null && Utility.InRange(near.Square, seat, SeatTownTiles))
            {
                town = near;
                at = MeetAtSeat && near.Bounds.Contains(seat) && BotStep.Settle(map, seat.X, seat.Y, out var inside)
                    ? new Point3D(seat.X, seat.Y, inside)
                    : Hall(near);

                return at != Point3D.Zero;
            }

            if (BotStep.Settle(map, seat.X, seat.Y, out var z))
            {
                at = new Point3D(seat.X, seat.Y, z);

                return true;
            }
        }

        town = LivesIn(guild) ?? BotSettle.HomeTown ?? BotTowns.Nearest(BotPopulation.Where);
        at = Hall(town);

        return at != Point3D.Zero;
    }

    public static BotTowns.Town LivesIn(Guild guild)
    {
        if (guild?.Members == null)
        {
            return null;
        }

        BotTowns.Town best = null;
        var most = 0;
        Dictionary<BotTowns.Town, int> counts = [];

        for (var i = 0; i < guild.Members.Count; i++)
        {
            if (guild.Members[i] is not BotMobile { Deleted: false } member || BotResidence.Of(member) is not { } town)
            {
                continue;
            }

            var n = counts.GetValueOrDefault(town) + 1;

            counts[town] = n;

            if (n > most)
            {
                most = n;
                best = town;
            }
        }

        return best;
    }

    public static bool Grieve(Guild mine, Guild theirs, string why) => Ask(BotTopic.Grievance, mine, theirs, why);

    public static bool Befriend(Guild mine, Guild theirs, string why) => Ask(BotTopic.Friendship, mine, theirs, why);

    private static bool Ask(BotTopic topic, Guild mine, Guild theirs, string why)
    {
        if (!Running || !Active)
        {
            return false;
        }

        if (mine == null || theirs == null || mine == theirs || mine.Name == BotUnderworld.GuildName || theirs.Name == BotUnderworld.GuildName)
        {
            return true;
        }

        if (Between(mine.Name, theirs.Name) != null)
        {
            Pending++;

            return true;
        }

        if (!BotTowns.Ready)
        {
            Queued++;

            return true;
        }

        if (_clocks.TryGetValue(Pair(mine.Name, theirs.Name), out var until) && Core.TickCount - until < 0)
        {
            Standing++;

            return true;
        }

        if (!Room || Busy(mine.Name) || Busy(theirs.Name))
        {
            Queued++;

            return true;
        }

        Call(topic, mine, theirs, why);

        return true;
    }

    public static BotHearing Between(string one, string other)
    {
        for (var i = 0; i < _meetings.Count; i++)
        {
            var m = _meetings[i];

            if (m.Between && (m.Guest == one && m.Host == other || m.Guest == other && m.Host == one))
            {
                return m;
            }
        }

        return null;
    }

    public static bool Busy(string guild)
    {
        for (var i = 0; i < _meetings.Count; i++)
        {
            var m = _meetings[i];

            if (m.Between && (m.Guest == guild || m.Host == guild))
            {
                return true;
            }
        }

        return false;
    }

    public static bool Treating(string guild, BotTowns.Town town)
    {
        for (var i = 0; i < _meetings.Count; i++)
        {
            var m = _meetings[i];

            if (!m.Between && m.Guest == guild && ReferenceEquals(m.Town, town))
            {
                return true;
            }
        }

        return false;
    }

    public static bool Treating(string guild)
    {
        for (var i = 0; i < _meetings.Count; i++)
        {
            if (!_meetings[i].Between && _meetings[i].Guest == guild)
            {
                return true;
            }
        }

        return false;
    }

    public static bool Room => Running && Count(true) < MostMeetings;

    public static bool AudienceRoom => BotBurgh.Running && Count(false) < MostAudiences;

    private static int Count(bool between)
    {
        var n = 0;

        for (var i = 0; i < _meetings.Count; i++)
        {
            if (_meetings[i].Between == between)
            {
                n++;
            }
        }

        return n;
    }

    private static BotHearing Call(BotTopic topic, Guild guest, Guild host, string why)
    {
        if (!Seat(host, out var town, out var at))
        {
            Unreachable++;
            _clocks[Pair(guest.Name, host.Name)] = Core.TickCount + RetryMs;

            return null;
        }

        var map = BotPopulation.Home;

        Seat(guest, out _, out var from);

        if (from != Point3D.Zero && !BotGates.Joined(map, from, at))
        {
            Unreachable++;
            _clocks[Pair(guest.Name, host.Name)] = Core.TickCount + RetryMs;

            logger.Information(
                "{Guest} would meet {Host} over {Topic}, but no walk joins where it lives to {Town}; nothing is called for {Minutes} minutes",
                guest.Name,
                host.Name,
                BotDuke.Word(topic),
                town?.Name ?? "their seat",
                RetryMs / 60000
            );

            return null;
        }

        var m = Open(topic, guest.Name, host.Name, town, map, at, why);

        logger.Warning(
            "{Guest} calls a meeting with {Host} over {Topic} ({Why}): its envoy is to walk to {Town} at {X},{Y}, and nothing is decided until it has been heard",
            guest.Name,
            host.Name,
            BotDuke.Word(topic),
            why,
            town?.Name ?? "their seat",
            at.X,
            at.Y
        );

        return m;
    }

    public static BotHearing Audience(Guild guild, BotTowns.Town town, string why)
    {
        if (guild == null || town == null || !AudienceRoom)
        {
            return null;
        }

        var at = Hall(town);

        if (at == Point3D.Zero)
        {
            return null;
        }

        var m = Open(BotTopic.Town, guild.Name, null, town, BotPopulation.Home, at, why);

        logger.Information(
            "{Guild} sends an envoy to {Town}'s hall at {X},{Y} to hear what the town wants of it ({Why})",
            guild.Name,
            town.Name,
            at.X,
            at.Y,
            why
        );

        return m;
    }

    private static BotHearing Open(BotTopic topic, string guest, string host, BotTowns.Town town, Map map, Point3D at, string why)
    {
        var now = Core.TickCount;
        var m = new BotHearing
        {
            Id = ++_nextId,
            Topic = topic,
            Guest = guest,
            Host = host,
            Town = town,
            Map = map,
            Place = at,
            Why = why,
            Stage = BotHearingStage.Called,
            CalledTick = now,
            StageTick = now
        };

        _meetings.Add(m);
        Called++;
        _calledBy[(int)topic]++;

        BotEvents.Post("parley", guest, null, guest, at.X, at.Y, $"{guest} calls a meeting {(host != null ? $"with {host}" : $"with the town of {town?.Name}")} over {BotDuke.Word(topic)}: {why}", null, null);

        return m;
    }

    public static BotHearing Wanting(BotMobile bot, out bool asHost)
    {
        asHost = false;

        if (_meetings.Count == 0 || bot?.Guild is not Guild guild)
        {
            return null;
        }

        var now = Core.TickCount;

        for (var i = 0; i < _meetings.Count; i++)
        {
            var m = _meetings[i];

            if (m.Stage >= BotHearingStage.Hearing || m.Turned.Contains(bot.Serial))
            {
                continue;
            }

            if (m.Guest == guild.Name && m.Stage <= BotHearingStage.Walking && !Held(m.Envoy, m.EnvoyTick, now))
            {
                return m;
            }

            if (m.Between && m.Host == guild.Name && m.HostWanted && !Held(m.HostRep, m.HostTick, now))
            {
                asHost = true;

                return m;
            }
        }

        return null;
    }

    private static bool Held(BotMobile who, long tick, long now) =>
        who is { Deleted: false, Alive: true } && now - tick < ClaimMs;

    public static void Took(BotHearing m, BotMobile bot, bool asHost)
    {
        if (m == null || bot == null)
        {
            return;
        }

        var now = Core.TickCount;

        if (asHost)
        {
            m.HostRep = bot;
            m.HostTick = now;

            return;
        }

        m.Envoy = bot;
        m.EnvoyTick = now;

        if (m.Stage == BotHearingStage.Called)
        {
            m.Stage = BotHearingStage.Walking;
            m.StageTick = now;
        }

        logger.Information(
            "{Name} the {Class} of {Guild} sets out as envoy to {Where} over {Topic}",
            bot.Name,
            bot.Class?.Name,
            m.Guest,
            m.Between ? $"{m.Host} at {m.Town?.Name ?? "their seat"}" : $"the town of {m.Town?.Name}",
            BotDuke.Word(m.Topic)
        );
    }

    public static void Still(BotHearing m, BotMobile bot, bool asHost)
    {
        if (m == null || bot == null)
        {
            return;
        }

        if (asHost && ReferenceEquals(m.HostRep, bot))
        {
            m.HostTick = Core.TickCount;
        }
        else if (!asHost && ReferenceEquals(m.Envoy, bot))
        {
            m.EnvoyTick = Core.TickCount;
        }
    }

    public static void Let(BotHearing m, BotMobile bot, bool asHost, bool couldNotReach)
    {
        if (m == null || bot == null)
        {
            return;
        }

        if (couldNotReach)
        {
            m.Turned.Add(bot.Serial);
        }

        if (asHost)
        {
            if (ReferenceEquals(m.HostRep, bot))
            {
                m.HostRep = null;
            }

            return;
        }

        if (!ReferenceEquals(m.Envoy, bot))
        {
            return;
        }

        m.Envoy = null;

        if (m.Stage is BotHearingStage.Walking or BotHearingStage.Gathered)
        {
            m.Stage = BotHearingStage.Called;
            m.StageTick = Core.TickCount;
        }
    }

    public static bool There(BotHearing m, Mobile who) =>
        who is { Deleted: false, Alive: true } && who.Map == m.Map && who.InRange(m.Place, ArriveTiles);

    public static void Beat()
    {
        var now = Core.TickCount;

        if (!_everLooked || now - (_lookedTick + LookMs) >= 0)
        {
            var since = _everLooked ? now - _lookedTick : LookMs;

            _everLooked = true;
            _lookedTick = now;

            if (Running)
            {
                Look();
            }

            BotBurgh.Look(since);
        }

        for (var i = _meetings.Count - 1; i >= 0; i--)
        {
            var m = _meetings[i];

            try
            {
                Step(m, now);
            }
            catch (Exception e)
            {
                logger.Error(e, "Meeting #{Id} between {Guest} and {Host} threw at {Stage}; it is ended", m.Id, m.Guest, m.Host ?? m.Town?.Name, m.Stage);
                Lost++;
                End(m, $"it threw: {e.Message}", false);
            }

            if (m.Stage == BotHearingStage.Over)
            {
                _meetings.RemoveAt(i);
            }
        }
    }

    private static void Step(BotHearing m, long now)
    {
        if (m.Stage == BotHearingStage.Over)
        {
            return;
        }

        if (BotGuilds.Named(m.Guest) == null || m.Between && BotGuilds.Named(m.Host) == null)
        {
            Vanished++;
            End(m, "a guild is gone", false);

            return;
        }

        if (now - m.CalledTick >= LongestMs && m.Stage < BotHearingStage.Hearing)
        {
            TooLong++;
            End(m, $"it stood {LongestMs / 60000} minutes without being heard", false);

            return;
        }

        if (m.Envoy != null && !Held(m.Envoy, m.EnvoyTick, now) && m.Stage < BotHearingStage.Hearing)
        {
            m.Envoy = null;
            m.Stage = BotHearingStage.Called;
            m.StageTick = now;
        }

        switch (m.Stage)
        {
            case BotHearingStage.Called:
                {
                    if (now - m.StageTick >= OfferMs)
                    {
                        NoEnvoy++;
                        End(m, $"nobody of {m.Guest} would go in {OfferMs / 60000} minutes", false);
                    }

                    return;
                }
            case BotHearingStage.Walking:
                {
                    if (m.Between && !m.HostWanted && m.Envoy is { } walker && walker.Map == m.Map
                        && walker.InRange(m.Place, HostCallTiles))
                    {
                        m.HostWanted = true;
                    }

                    if (There(m, m.Envoy))
                    {
                        m.Stage = BotHearingStage.Gathered;
                        m.StageTick = now;
                        m.ArrivedTick = now;
                        m.HostWanted = m.Between;
                    }

                    return;
                }
            case BotHearingStage.Gathered:
                {
                    Gather(m, now);

                    return;
                }
            case BotHearingStage.Hearing:
                {
                    if (now - m.SaidTick < LineMs)
                    {
                        return;
                    }

                    if (m.Said < m.Lines.Count)
                    {
                        Speak(m, m.Lines[m.Said].Who, m.Lines[m.Said].Line);
                        m.Said++;
                        m.SaidTick = now;

                        return;
                    }

                    m.Stage = BotHearingStage.Awaiting;
                    m.StageTick = now;

                    return;
                }
            case BotHearingStage.Awaiting:
                {
                    if (m.Verdict == null && (m.ModelFailed || !m.Asked || now - m.StageTick >= JudgeWaitMs))
                    {
                        var because = !m.Asked ? "the model was not asked" : m.ModelFailed ? "the model gave no answer that could be read" : $"no answer in {JudgeWaitMs / 1000}s";

                        m.Verdict = m.Between ? BotDuke.Rule(m, because) : BotTownTask.Rule(m, because);
                        m.JudgedBy = $"the rule ({because})";
                        BotDuke.Given(false);
                    }

                    if (m.Verdict != null)
                    {
                        Pronounce(m, now);
                    }

                    return;
                }
            case BotHearingStage.Verdict:
                {
                    if (now - m.SaidTick < LineMs)
                    {
                        return;
                    }

                    if (m.Said < m.Lines.Count)
                    {
                        Speak(m, m.Lines[m.Said].Who, m.Lines[m.Said].Line);
                        m.Said++;
                        m.SaidTick = now;

                        return;
                    }

                    End(m, m.Ending, true);

                    return;
                }
        }
    }

    private static void Gather(BotHearing m, long now)
    {
        if (!There(m, m.Envoy))
        {
            m.Stage = BotHearingStage.Walking;
            m.StageTick = now;

            return;
        }

        if (m.Witness == null && !m.Unwitnessed)
        {
            var body = Summon?.Invoke(m);

            if (body != null)
            {
                m.Witness = body;
                m.WitnessName = body.Name;
                Witnessed++;
            }
            else if (Summon == null || now - m.ArrivedTick >= WitnessWaitMs)
            {
                m.Unwitnessed = true;
                Unwitnessed++;
            }
        }

        var hostReady = !m.Between || There(m, m.HostRep);

        if (!hostReady && now - m.ArrivedTick >= HostWaitMs)
        {
            m.HostAbsent = true;
            HostsAbsent++;
            hostReady = true;
        }

        if (!hostReady || m.Witness == null && !m.Unwitnessed)
        {
            return;
        }

        Open(m, now);
    }

    private static void Open(BotHearing m, long now)
    {
        m.Stage = BotHearingStage.Hearing;
        m.StageTick = now;
        m.HearingTick = now;
        m.Said = 0;
        m.SaidTick = now - LineMs;

        if (m.Between)
        {
            m.Allowed = BotDuke.Allowed(m.Topic, BotGuilds.Named(m.Guest), BotGuilds.Named(m.Host));
            BotDuke.Hearing(m);
            m.Brief = BotDuke.Brief(m);
        }
        else
        {
            BotTownTask.Hearing(m);
        }

        m.Asked = Judge != null && m.Allowed is { Length: > 0 } && Judge(m, v => Answered(m, v));

        logger.Information(
            "Meeting #{Id}: {Guest} and {Other} are heard at {Town} before {Witness}; the word may be {Allowed}, and is asked of {Who}",
            m.Id,
            m.Guest,
            m.Host ?? $"the town of {m.Town?.Name}",
            m.Town?.Name ?? "their seat",
            m.WitnessName ?? "no witness",
            string.Join(", ", m.Allowed ?? []),
            m.Asked ? "the model" : "the rule"
        );
    }

    private static void Answered(BotHearing m, BotVerdict v)
    {
        if (m.Stage > BotHearingStage.Awaiting || m.Verdict != null)
        {
            Late++;

            return;
        }

        if (v == null || v.Outcome == null || m.Allowed == null || Array.IndexOf(m.Allowed, v.Outcome) < 0 || string.IsNullOrWhiteSpace(v.Speech))
        {
            Unread++;
            m.ModelFailed = true;

            return;
        }

        m.Verdict = v;
        m.JudgedBy = "the model";
        BotDuke.Given(true);
    }

    private static void Pronounce(BotHearing m, long now)
    {
        var v = m.Verdict;

        if (m.Between)
        {
            BotDuke.Apply(m);
        }
        else
        {
            BotBurgh.Give(m);
        }

        m.Stage = BotHearingStage.Verdict;
        m.StageTick = now;
        m.Lines.Clear();
        m.Said = 0;
        m.SaidTick = now - LineMs;

        var fill = BotDuke.Fill(m);
        var speaker = m.Witness != null ? BotSpeaker.Witness : BotSpeaker.Envoy;

        if (!m.Between && !string.IsNullOrWhiteSpace(v.Report))
        {
            Words(m, speaker, v.Report, fill);
        }

        Words(m, speaker, v.Speech, fill);

        if (m.Between)
        {
            m.Lines.Add((BotSpeaker.Envoy, BotDuke.Phrase($"parley:end:{m.Applied ?? BotDuke.Nothing}", m.Envoy, fill)));
        }
        else
        {
            m.Lines.Add((BotSpeaker.Envoy, BotDuke.Phrase("parley:town-accept", m.Envoy, fill)));
        }

        logger.Warning(
            "Meeting #{Id}: {Guest} and {Other} at {Town} before {Witness} — the word, by {By}, is {Outcome}: \"{Speech}\" ({Reason}); {Ending}",
            m.Id,
            m.Guest,
            m.Host ?? $"the town of {m.Town?.Name}",
            m.Town?.Name ?? "their seat",
            m.WitnessName ?? "no witness",
            m.JudgedBy,
            v.Outcome,
            v.Speech,
            v.Reason,
            m.Ending
        );
    }

    private static void Words(BotHearing m, BotSpeaker who, string text, Dictionary<string, string> fill)
    {
        var speech = who == BotSpeaker.Envoy && m.Witness == null
            ? BotDuke.Phrase(m.Between ? "parley:relay" : "parley:relay-town", m.Envoy, new Dictionary<string, string>(fill) { ["speech"] = text })
            : text;

        var most = Math.Max(40, BotVoice.MostLetters - 10);

        while (speech.Length > most)
        {
            var cut = speech.LastIndexOf(". ", most, StringComparison.Ordinal);

            if (cut < most / 3)
            {
                cut = speech.LastIndexOf(' ', most);
            }

            if (cut <= 0)
            {
                break;
            }

            m.Lines.Add((who, speech[..(cut + 1)].Trim()));
            speech = speech[(cut + 1)..].Trim();

            if (m.Lines.Count > 8)
            {
                return;
            }
        }

        if (speech.Length > 0)
        {
            m.Lines.Add((who, speech));
        }
    }

    private static void Speak(BotHearing m, BotSpeaker who, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        Mobile body = who switch
        {
            BotSpeaker.Witness => m.Witness,
            BotSpeaker.Host => m.HostRep,
            _ => m.Envoy
        };

        if (body is { Deleted: false } && body.Map == m.Map && body.InRange(m.Place, ArriveTiles * 3))
        {
            BotVoice.Say(body, "local", line, true);

            return;
        }

        BotEvents.Post("parley", body?.Name ?? who.ToString(), null, m.Guest, m.Place.X, m.Place.Y, line, "unsaid", null);
    }

    private static void End(BotHearing m, string ending, bool heard)
    {
        var now = Core.TickCount;

        m.Stage = BotHearingStage.Over;
        m.OverTick = now;
        m.Heard = heard;
        m.Ending = ending ?? (heard ? "heard" : "lapsed");

        if (m.Witness != null)
        {
            try
            {
                Dismiss?.Invoke(m);
            }
            catch (Exception e)
            {
                logger.Warning("The witness of meeting #{Id} could not be sent back: {Message}", m.Id, e.Message);
            }
        }

        if (heard)
        {
            Heard++;
        }

        if (m.Between)
        {
            var agreed = heard && m.Applied != null && m.Applied != BotDuke.Nothing;

            _clocks[Pair(m.Guest, m.Host)] = now + (agreed ? MeetEveryMs : RetryMs);
        }
        else
        {
            BotBurgh.Ended(m);
        }

        var said = $"#{m.Id} {m.Guest} {(m.Between ? $"and {m.Host}" : $"at {m.Town?.Name}")} over {BotDuke.Word(m.Topic)}: {m.Ending}"
            + (heard ? $" (by {m.JudgedBy}, before {m.WitnessName ?? "no witness"})" : "");

        _recent.Add(said);

        if (_recent.Count > 8)
        {
            _recent.RemoveAt(0);
        }

        if (!heard)
        {
            logger.Information("Meeting #{Id} lapsed: {Said}", m.Id, said);
        }

        BotEvents.Post("parley", m.Guest, null, m.Guest, m.Place.X, m.Place.Y, said, heard ? "heard" : "lapsed", null);
    }

    private static void Look()
    {
        BotPact.Sweep();

        if (!Room)
        {
            return;
        }

        var wary = BotRegard.Enmity * Math.Clamp(WaryShare, 0.0, 1.0);
        var now = Core.TickCount;

        foreach (var mine in BotGuilds.Standing)
        {
            if (mine == null || mine.Disbanded || mine.Name == BotUnderworld.GuildName || Busy(mine.Name))
            {
                continue;
            }

            foreach (var theirs in BotGuilds.Standing)
            {
                if (theirs == null || theirs == mine || theirs.Disbanded || theirs.Name == BotUnderworld.GuildName || mine.IsWar(theirs)
                    || Busy(theirs.Name))
                {
                    continue;
                }

                if (_clocks.TryGetValue(Pair(mine.Name, theirs.Name), out var until) && now - until < 0)
                {
                    continue;
                }

                var theyOfUs = BotRegard.Of(theirs.Name, mine.Name);
                var weOfThem = BotRegard.Of(mine.Name, theirs.Name);

                if (theyOfUs <= wary && theyOfUs > BotRegard.Enmity && !BotPact.Holds(mine.Name, theirs.Name, BotPact.Kind.Peace)
                    && Utility.RandomDouble() < CallOdds)
                {
                    Call(BotTopic.Safety, mine, theirs, $"{theirs.Name} thinks {theyOfUs:F0} of it, and war is declared at {BotRegard.Enmity:F0}");

                    return;
                }

                if (weOfThem >= Cordial && theyOfUs >= Cordial && !mine.IsAlly(theirs) && !BotPact.Holds(mine.Name, theirs.Name, BotPact.Kind.Trade)
                    && !BotPact.Holds(mine.Name, theirs.Name, BotPact.Kind.Orders) && (mine.Members?.Count ?? 0) <= (theirs.Members?.Count ?? 0)
                    && Utility.RandomDouble() < CallOdds)
                {
                    Call(BotTopic.Commerce, mine, theirs, $"each thinks well of the other ({weOfThem:F0} and {theyOfUs:F0})");

                    return;
                }
            }
        }
    }

    public static string ByHand(string tail)
    {
        var parts = (tail ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            return "meet wants a guild and another guild or a town, and may name a topic: meet The Blade ; The Needle ; grievance — or meet The Needle ; Cove";
        }

        var guest = BotGuilds.Find(parts[0]);

        if (guest == null)
        {
            return $"no guild is called \"{parts[0]}\".";
        }

        var host = BotGuilds.Find(parts[1]);

        if (host == null)
        {
            var town = BotTowns.Find(parts[1]);

            if (town == null)
            {
                return $"\"{parts[1]}\" is neither a guild nor a town.";
            }

            if (!AudienceRoom)
            {
                return BotBurgh.Running ? $"{MostAudiences} audiences with towns stand already." : "the towns' dealings are switched off (BotBurgh.Running).";
            }

            if (Treating(guest.Name))
            {
                return $"{guest.Name} has an envoy out to a town already.";
            }

            var audience = Audience(guest, town, "called at the keyboard");

            return audience == null ? "the audience could not be called." : $"called: {audience.Tell()}";
        }

        if (!Room)
        {
            return Running ? $"{MostMeetings} meetings between guilds stand already." : "meetings are switched off (BotParley.Running).";
        }

        if (Between(guest.Name, host.Name) != null || Busy(guest.Name) || Busy(host.Name))
        {
            return "one of those guilds stands in a meeting already.";
        }

        var topic = parts.Length > 2 && Enum.TryParse<BotTopic>(parts[2], true, out var named) && named != BotTopic.Town ? named : BotTopic.Grievance;
        var m = Call(topic, guest, host, "called at the keyboard");

        return m == null ? "the meeting could not be called: no walk joins them, or the host has no town." : $"called: {m.Tell()}";
    }

    public static string Tell()
    {
        using var say = Server.Text.ValueStringBuilder.Create(512);

        say.Append(_meetings.Count == 0 ? "no meetings stand" : $"{_meetings.Count} meetings stand: ");

        for (var i = 0; i < _meetings.Count; i++)
        {
            say.Append(i > 0 ? "; " : "");
            say.Append(_meetings[i].Tell());
        }

        say.Append(". Lately: ");
        say.Append(_recent.Count == 0 ? "nothing" : string.Join("; ", _recent));
        say.Append($". {BotPact.Tell()}. {BotBurgh.Tell()}");

        return say.ToString();
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "meetings are off: wars and alliances are made the moment an opinion crosses its line";
        }

        using var say = Server.Text.ValueStringBuilder.Create(1024);

        say.Append($"{_meetings.Count} meetings standing");

        for (var i = 0; i < _meetings.Count; i++)
        {
            say.Append(i == 0 ? " (" : "; ");
            say.Append(_meetings[i].Tell());
            say.Append(i == _meetings.Count - 1 ? ")" : "");
        }

        say.Append(
            $"; {Called} called ({_calledBy[(int)BotTopic.Grievance]} over a grievance, {_calledBy[(int)BotTopic.Safety]} to ask for peace, {_calledBy[(int)BotTopic.Friendship]} to ally, {_calledBy[(int)BotTopic.Commerce]} to trade, {_calledBy[(int)BotTopic.Town]} to a town), "
        );
        say.Append(
            $"{Heard} heard ({Witnessed} before a witness, {Unwitnessed} with none free, {HostsAbsent} with the host absent), {NoEnvoy} lapsed for want of an envoy, {TooLong} for taking longer than {LongestMs / 60000} minutes, {Vanished} for a guild gone, {Lost} for a fault, {Unreachable} not called for no walk to the host; "
        );
        say.Append(
            $"grievances at the war line: {Pending} found a meeting already called, {Standing} the duke's word still standing ({MeetEveryMs / 60000} minutes after an agreement, {RetryMs / 60000} after nothing), {Queued} no room; "
        );
        say.Append($"{BotDuke.Describe()}, {Late} of the model's answers late and {Unread} unreadable; {Wars} wars declared at a meeting, {Forbidden} refused by the war's rules at the word, {Alliances} alliances; ");
        say.Append($"{BotEnvoy.Describe()}; {BotHerald.Describe()}; {Doorsteps} halls met at their doorstep for standing where no road reaches, {Doorless} with none found; {BotPact.Describe()}; {BotGrievances.Describe()}");

        return say.ToString();
    }

    public static IEnumerable<(string A, string B, long LeftMs)> Clocks()
    {
        var now = Core.TickCount;

        foreach (var (pair, until) in _clocks)
        {
            var left = until - now;

            if (left > 0)
            {
                yield return (pair.A, pair.B, left);
            }
        }
    }

    public static void RestoreClock(string a, string b, long leftMs)
    {
        if (!string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && a != b && leftMs > 0)
        {
            _clocks[Pair(a, b)] = Core.TickCount + leftMs;
        }
    }

    public static void Forget()
    {
        for (var i = 0; i < _meetings.Count; i++)
        {
            if (_meetings[i].Witness != null)
            {
                try
                {
                    Dismiss?.Invoke(_meetings[i]);
                }
                catch (Exception)
                {
                }
            }
        }

        _meetings.Clear();
        _recent.Clear();
        _halls.Clear();
        Array.Clear(_calledBy);
        Called = 0;
        Heard = 0;
        Witnessed = 0;
        Unwitnessed = 0;
        HostsAbsent = 0;
        NoEnvoy = 0;
        Lost = 0;
        TooLong = 0;
        Vanished = 0;
        Pending = 0;
        Standing = 0;
        Queued = 0;
        Unreachable = 0;
        Wars = 0;
        Forbidden = 0;
        Alliances = 0;
        Late = 0;
        Unread = 0;
        _everLooked = false;
        BotDuke.Forget();
    }

    public static void Wipe()
    {
        Forget();
        _clocks.Clear();
    }
}
