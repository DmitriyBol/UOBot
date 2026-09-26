using System.Collections.Generic;
using Server.Guilds;

namespace Server.BotAI.V2;

/// <summary>
/// One guild, one officer, one errand at a time — and the difference between having been offered an errand
/// and actually being on one.
///
/// <para>
/// <b>The four officers each kept this rule and each kept it the same way wrong.</b> A proposer that offered
/// a guild errand wrote a claim on the guild for the length of the whole errand — three or four minutes —
/// and then handed the errand to an auction that was free to prefer something else. When it did, the guild
/// was locked out of that officer for four minutes having done nothing at all, and nothing anywhere said so:
/// the claim looks identical whether a bot is walking to a plot or the offer was thrown away a second after
/// it was made.
/// </para>
///
/// <para>
/// <b>Measured on the one that matters most.</b> In the session of 09.09.2026 at 01:59 the steward offered a
/// hall five times and three were raised — so two guilds spent three minutes each sealed off from the most
/// valuable errand on the shard because of an offer nobody took. <c>BotHall.Prior</c> carries a note about
/// exactly this pressure from the other side: it was raised from ninety to four hundred because offers were
/// not being taken, which treats the symptom. This is the cause.
/// </para>
///
/// <para>
/// So there are two lengths and not one. <see cref="Offering"/> is written when the offer is made and lasts
/// a few seconds — long enough that two bots asked in the same handful of beats are not both sent after the
/// same hall, short enough that a lost auction costs the guild almost nothing. <see cref="Hold"/> is written
/// by the errand itself on every beat it is alive, and it lasts as long as the officer says. An errand that
/// is really running therefore keeps its guild to itself; an offer that was refused lets go by itself.
/// </para>
/// </summary>
public static class BotOffice
{
    public static int OfferedMs { get; set; } = 12000;

    public static long Lapsed { get; private set; }

    public static long Kept { get; private set; }

    private static readonly Dictionary<(string Office, string Guild), long> _claims = [];

    private static readonly Dictionary<(string Office, string Guild), long> _pending = [];

    private static readonly List<(string Office, string Guild)> _stale = [];

    public static bool Busy(string office, Guild guild)
    {
        if (guild == null)
        {
            return false;
        }

        Sweep();

        return _claims.TryGetValue((office, guild.Name), out var until) && Core.TickCount - until < 0;
    }

    public static void Offering(string office, Guild guild)
    {
        if (guild == null)
        {
            return;
        }

        var key = (office, guild.Name);
        var now = Core.TickCount;

        _claims[key] = now + OfferedMs;
        _pending[key] = now;
    }

    public static void Hold(string office, Guild guild, int claimMs)
    {
        if (guild == null)
        {
            return;
        }

        var key = (office, guild.Name);

        _claims[key] = Core.TickCount + claimMs;

        if (_pending.Remove(key))
        {
            Kept++;
        }
    }

    public static void Release(string office, Guild guild)
    {
        if (guild == null)
        {
            return;
        }

        var key = (office, guild.Name);

        _claims.Remove(key);
        _pending.Remove(key);
    }

    private static void Sweep()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var now = Core.TickCount;

        _stale.Clear();

        foreach (var (key, made) in _pending)
        {
            if (now - (made + OfferedMs) >= 0)
            {
                _stale.Add(key);
            }
        }

        for (var i = 0; i < _stale.Count; i++)
        {
            _pending.Remove(_stale[i]);
            Lapsed++;
        }
    }

    public static string Describe() =>
        $"{Kept} guild errands were taken up out of the offers made and {Lapsed} offers lapsed unclaimed";

    public static void Forget()
    {
        _claims.Clear();
        _pending.Clear();
        _stale.Clear();
        Lapsed = 0;
        Kept = 0;
    }
}
