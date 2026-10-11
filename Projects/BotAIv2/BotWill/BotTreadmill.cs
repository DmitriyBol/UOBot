using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>
/// The same deed against the same thing, failing the moment it is taken, is not offered to that bot again for a few seconds.
///
/// <para>
/// <b>Orlando's treadmill (07.10.2026, loops18).</b> Build 418, 11:47:34-11:47:42: Orlando the WarriorMage took "rescue: hitting
/// back at a troll" 39 times in eight seconds, five a second, and every one failed at once "a troll was winning", 0 coin. Since
/// react14 a fight that ends is decided again at the clock's next look (<c>BotWill.Settle</c> hurries it), so a deed that fails
/// on its first beat is offered and failed at the clock's pace. The breaker (<see cref="BotBreaker"/>) never rests a defence,
/// on Patrick's "it must hit back in a fight", and its six-in-five-minutes count is far too slow for a loop of ten a second.
/// The cause of that loop is mended where it starts (<c>BotDefender</c> read the standing flight share, the fight the bot's own,
/// leaned by its nature: a cautious bot between 40 and 44 % of its health was offered a fight that gave itself up on its first
/// beat). This is the rule under it, for every deed, so the next loop of this shape stops at its second turn.
/// </para>
///
/// <para>
/// <b>Two rules, keyed on the bot, the kind of work and its target</b> (what it fights when it fights something, the place
/// otherwise). (1) A fight that ends on a <em>losing judgement</em> ("X was winning": the bot's health under its own flight share)
/// rests that fight against that foe for <see cref="LosingRestMs"/> at once — fight what you beat, leave what you do not — and
/// for those seconds the flight is what the Hunted rung offers (<c>BotFugitive</c>, its early flight judged as on the failing
/// rung). The fight carried on to the end (<c>BotDefender.Stand</c>, which no losing judgement ends) is not rested by it: a bot
/// with no flight on offer runs or fights, never neither (react14). (2) Any deed that fails inside <see cref="InstantMs"/> of
/// being taken, having made nothing, twice inside <see cref="TwiceMs"/> against the same target, is rested for
/// <see cref="RestMs"/>. A flight never is: running away is never rested (<see cref="BotBreaker"/>), and a cornered flight
/// has its own rest (<c>BotFugitive.CorneredRestMs</c>).
/// </para>
///
/// <para>
/// <b>A rested offer is not a failure.</b> It is refused in the appraisal like the breaker's rests, so the hurried decision asks
/// the same question and cannot pass it; the work in hand is never refused. Everything is counted in the Will line.
/// </para>
/// </summary>
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
