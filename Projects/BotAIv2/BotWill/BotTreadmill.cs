using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

public static class BotTreadmill
{

    public static bool Running { get; set; } = true;

    public static int LosingRestMs { get; set; } = 15000;

    public static int InstantMs { get; set; } = 2000;

    public static int TwiceMs { get; set; } = 2000;

    public static int RestMs { get; set; } = 15000;

    public static long LosingRests { get; private set; }

    public static long InstantRests { get; private set; }

    public static long FlightsSpared { get; private set; }

    public static long RefusedLosing { get; private set; }

    public static long RefusedInstant { get; private set; }

    public static long StandsLetThrough { get; private set; }

    public static long FlightsAfterLosing { get; set; }

    private static readonly Dictionary<string, long> _restsByKind = new(StringComparer.Ordinal);

    private readonly struct Rest
    {
        public readonly long Until;

        public readonly bool Losing;

        public Rest(long until, bool losing)
        {
            Until = until;
            Losing = losing;
        }
    }

    private static readonly Dictionary<(Serial Bot, string Kind, long Target), Rest> _rests = [];

    private static readonly Dictionary<(Serial Bot, string Kind, long Target), long> _instants = [];

    private static readonly Dictionary<Serial, (long Tick, Serial Foe)> _lost = [];

    private static readonly List<(Serial Bot, string Kind, long Target)> _stale = [];

    private static long _sweptTick;

    private static bool _swept;

    private static long Target(BotDeed deed)
    {
        if (deed.Foe is { } foe)
        {
            return (long)(uint)foe.Serial.Value;
        }

        var where = deed.Where;
        var map = deed.Map?.MapID ?? 0;

        return (1L << 62) | ((long)(where.X & 0xFFFF) << 32) | ((long)(where.Y & 0xFFFF) << 16) | ((long)(where.Z & 0xFF) << 8) | (long)(map & 0xFF);
    }

    public static bool LosingJudgement(string why) => why != null && why.EndsWith(" was winning", StringComparison.Ordinal);

    public static void Failed(Mobile bot, BotDeed deed, string why, long heldMs, int made, double skill)
    {
        if (!Running || bot == null || deed == null || string.IsNullOrEmpty(deed.Kind))
        {
            return;
        }

        var now = Core.TickCount;

        Sweep(now);

        var key = (bot.Serial, deed.Kind, Target(deed));

        if (deed.Foe is { } foe && LosingJudgement(why) && LosingRestMs > 0)
        {
            _rests[key] = new Rest(now + LosingRestMs, true);
            _lost[bot.Serial] = (now, foe.Serial);
            LosingRests++;
            Count(deed.Kind);

            return;
        }

        if (heldMs > InstantMs || made > 0 || skill > 0 || RestMs <= 0)
        {
            return;
        }

        if (_instants.TryGetValue(key, out var last) && now - last <= TwiceMs)
        {
            _instants.Remove(key);

            if (deed is BotBolt)
            {
                FlightsSpared++;

                return;
            }

            _rests[key] = new Rest(now + RestMs, false);
            InstantRests++;
            Count(deed.Kind);

            return;
        }

        _instants[key] = now;
    }

    private static void Count(string kind) => _restsByKind[kind] = _restsByKind.GetValueOrDefault(kind) + 1;

    public static bool Resting(Mobile bot, BotDeed deed, out bool losing, out long leftMs)
    {
        losing = false;
        leftMs = 0;

        if (!Running || bot == null || deed == null || _rests.Count == 0 || string.IsNullOrEmpty(deed.Kind))
        {
            return false;
        }

        var key = (bot.Serial, deed.Kind, Target(deed));

        if (!_rests.TryGetValue(key, out var rest))
        {
            return false;
        }

        leftMs = rest.Until - Core.TickCount;

        if (leftMs <= 0)
        {
            _rests.Remove(key);
            leftMs = 0;

            return false;
        }

        losing = rest.Losing;

        if (losing && deed is BotRescue { Own: true, ToTheEnd: true })
        {
            StandsLetThrough++;

            return false;
        }

        if (losing)
        {
            RefusedLosing++;
        }
        else
        {
            RefusedInstant++;
        }

        return true;
    }

    public static bool Losing(Mobile bot)
    {
        if (!Running || bot == null || LosingRestMs <= 0 || !_lost.TryGetValue(bot.Serial, out var lost))
        {
            return false;
        }

        if (Core.TickCount - lost.Tick < LosingRestMs)
        {
            return true;
        }

        _lost.Remove(bot.Serial);

        return false;
    }

    public static void Forget(Mobile bot)
    {
        if (bot == null)
        {
            return;
        }

        _lost.Remove(bot.Serial);

        foreach (var key in _rests.Keys)
        {
            if (key.Bot == bot.Serial)
            {
                _stale.Add(key);
            }
        }

        foreach (var key in _instants.Keys)
        {
            if (key.Bot == bot.Serial)
            {
                _stale.Add(key);
            }
        }

        for (var i = 0; i < _stale.Count; i++)
        {
            _rests.Remove(_stale[i]);
            _instants.Remove(_stale[i]);
        }

        _stale.Clear();
    }

    private static void Sweep(long now)
    {
        if (_swept && now - _sweptTick < 60000)
        {
            return;
        }

        _swept = true;
        _sweptTick = now;

        foreach (var (key, rest) in _rests)
        {
            if (rest.Until - now <= 0)
            {
                _stale.Add(key);
            }
        }

        for (var i = 0; i < _stale.Count; i++)
        {
            _rests.Remove(_stale[i]);
        }

        _stale.Clear();

        foreach (var (key, tick) in _instants)
        {
            if (now - tick > TwiceMs)
            {
                _stale.Add(key);
            }
        }

        for (var i = 0; i < _stale.Count; i++)
        {
            _instants.Remove(_stale[i]);
        }

        _stale.Clear();

        if (_lost.Count > 1024)
        {
            _lost.Clear();
        }
    }

    public static bool Any => LosingRests + InstantRests + FlightsSpared > 0;

    public static string Describe()
    {
        var kinds = "";

        foreach (var (kind, n) in _restsByKind)
        {
            kinds += (kinds.Length == 0 ? "" : ", ") + $"{kind} {n}";
        }

        return $"{LosingRests} fights rested {LosingRestMs / 1000}s against the foe after a losing judgement (\"was winning\") and {InstantRests} deeds rested {RestMs / 1000}s against their target after failing within {InstantMs / 1000}s of being taken twice in {TwiceMs / 1000}s (by kind: {(kinds.Length == 0 ? "none" : kinds)}), "
            + $"{RefusedLosing} and {RefusedInstant} offers refused while those rests stood, {StandsLetThrough} fights to the end let through a losing judgement's rest, {FlightsAfterLosing} flights taken as on the failing rung after a losing judgement, {FlightsSpared} flights that failed at once twice and were not rested";
    }
}
