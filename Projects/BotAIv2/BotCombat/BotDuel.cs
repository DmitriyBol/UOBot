using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Two of ours set against each other on purpose, and the rule that says when it is over.
///
/// <para>
/// <b>Patrick's order of 15.09.2026: Argus holds a championship between the strongest bots, one against one.</b>
/// The engine lets one player strike another in Felucca, but it charges for it: the striker is flagged criminal,
/// guards come, and the population's own reflexes read every blow as an attack. A duel is the one arrangement under
/// which two bots may hit each other with none of that — the pair is written here, <c>BotMobile.IsHarmfulCriminal</c>
/// reads it and answers no, and the two brawls pressed on them ask this class every beat whether the duel is done.
/// </para>
///
/// <para>
/// <b>Nobody dies for a title.</b> A duel is over when either duellist is down to <see cref="YieldAt"/> of its health,
/// or the clock runs out, and the one with the larger share standing wins. The bodies are the shard's fighters and
/// they have work to go back to; a championship that killed fifteen of them would be a plague beast with a trophy.
/// </para>
/// </summary>
public static class BotDuel
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDuel));

    public static double YieldAt { get; set; } = 0.35;

    public static int CapMs { get; set; } = 120000;

    public static long Begun { get; private set; }

    public static long Yielded { get; private set; }

    public static long Called { get; private set; }

    public static long Deaths { get; private set; }

    private sealed class Bout
    {
        public BotMobile A;

        public BotMobile B;

        public long Began;

        public BotMobile Winner;

        public string Why;
    }

    private static readonly Dictionary<Serial, Bout> _bouts = [];

    public static bool Between(Mobile a, Mobile b) =>
        a != null && b != null && _bouts.TryGetValue(a.Serial, out var bout)
        && (ReferenceEquals(bout.A, b) || ReferenceEquals(bout.B, b));

    public static bool Duelling(Mobile m) => m != null && _bouts.ContainsKey(m.Serial);

    public static bool Begin(BotMobile a, BotMobile b, SkillName trainsA, SkillName trainsB)
    {
        if (a is not { Deleted: false, Alive: true } || b is not { Deleted: false, Alive: true } || a == b
            || Duelling(a) || Duelling(b))
        {
            return false;
        }

        var bout = new Bout { A = a, B = b, Began = Core.TickCount };

        _bouts[a.Serial] = bout;
        _bouts[b.Serial] = bout;

        var pressedA = BotWill.Press(a, new BotBrawl(b, BotBrawl.Duel, trainsA, Over), $"a duel against {b.Name}");
        var pressedB = BotWill.Press(b, new BotBrawl(a, BotBrawl.Duel, trainsB, Over), $"a duel against {a.Name}");

        if (!pressedA || !pressedB)
        {
            _bouts.Remove(a.Serial);
            _bouts.Remove(b.Serial);

            if (pressedA)
            {
                BotWill.Abandon(a, "the duel could not be set up", unreached: false);
            }

            if (pressedB)
            {
                BotWill.Abandon(b, "the duel could not be set up", unreached: false);
            }

            return false;
        }

        Begun++;

        logger.Information("{A} and {B} are set against each other in a duel", a.Name, b.Name);

        return true;
    }

    private static double Share(Mobile m) =>
        m is { Deleted: false, Alive: true, HitsMax: > 0 } ? Math.Clamp(m.Hits / (double)m.HitsMax, 0.0, 1.0) : 0.0;

    private static string Over(IBotWilful bot, BotBrawl brawl)
    {
        if (bot?.Self is not { } self || !_bouts.TryGetValue(self.Serial, out var bout))
        {
            return "the duel is over";
        }

        if (bout.Winner != null)
        {
            return bout.Why;
        }

        var a = Share(bout.A);
        var b = Share(bout.B);
        var deadA = bout.A is not { Deleted: false, Alive: true };
        var deadB = bout.B is not { Deleted: false, Alive: true };

        if (deadA || deadB)
        {
            Deaths++;
            Decide(bout, deadA ? bout.B : bout.A, deadA ? bout.A : bout.B, "the other fell");
        }
        else if (a <= YieldAt || b <= YieldAt)
        {
            Yielded++;
            Decide(bout, a >= b ? bout.A : bout.B, a >= b ? bout.B : bout.A, "the other yielded");
        }
        else if (Core.TickCount - bout.Began >= CapMs)
        {
            Called++;
            Decide(bout, a >= b ? bout.A : bout.B, a >= b ? bout.B : bout.A, "the clock ran out");
        }

        return bout.Winner == null ? null : bout.Why;
    }

    private static void Decide(Bout bout, BotMobile winner, BotMobile loser, string how)
    {
        bout.Winner = winner;
        bout.Why = $"{winner?.Name} won the duel: {how}";

        Disarm(bout.A);
        Disarm(bout.B);

        logger.Information(
            "{Winner} beat {Loser} in a duel ({How}) after {Seconds}s, standing at {WinnerShare:P0} against {LoserShare:P0}",
            winner?.Name,
            loser?.Name,
            how,
            (Core.TickCount - bout.Began) / 1000,
            Share(winner),
            Share(loser)
        );
    }

    private static void Disarm(Mobile m)
    {
        if (m is { Deleted: false })
        {
            m.Combatant = null;
            m.Warmode = false;
        }
    }

    public static bool Call(Mobile m, string why)
    {
        if (m == null || !_bouts.TryGetValue(m.Serial, out var bout) || bout.Winner != null)
        {
            return false;
        }

        var a = Share(bout.A);
        var b = Share(bout.B);

        Called++;
        Decide(bout, a >= b ? bout.A : bout.B, a >= b ? bout.B : bout.A, why ?? "it was called");

        return true;
    }

    public static BotMobile Winner(Mobile m) =>
        m != null && _bouts.TryGetValue(m.Serial, out var bout) ? bout.Winner : null;

    public static bool Decided(Mobile m) => Winner(m) != null;

    public static void End(Mobile a, Mobile b)
    {
        if (a != null)
        {
            _bouts.Remove(a.Serial);
        }

        if (b != null)
        {
            _bouts.Remove(b.Serial);
        }
    }

    public static void Forget()
    {
        _bouts.Clear();
        Begun = 0;
        Yielded = 0;
        Called = 0;
        Deaths = 0;
    }

    public static string Describe() =>
        Begun == 0
            ? "no duel has been fought"
            : $"{Begun} duels fought, {Yielded} decided by a yield at {YieldAt:P0}, {Called} by the clock at {CapMs / 1000}s, {Deaths} with a death in them; {_bouts.Count / 2} standing";
}
