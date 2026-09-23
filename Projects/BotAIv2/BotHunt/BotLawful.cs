using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The law-abiding set on a murderer they can take between them.
///
/// <para>
/// <b>Patrick's order of 17.09.2026: law-abiding bots fight murderers when they can take them.</b> Until then a red
/// was hunted only by the Baron's patrol (<see cref="BotManhunt"/>), raised when the Baron happened to choose it, and
/// hit back at by whoever it set on (<c>BotDefender</c>); everybody else walked past. Elspeth murdered Joss beside the
/// home at 19:21:47 on build 110, prowled red for nine minutes, and nothing but the guards of Britain ever went for her.
/// </para>
///
/// <para>
/// Every <see cref="SweepMs"/> each red outside a guarded town is looked at. The law-abiding within <see cref="Sight"/>
/// of it are taken strongest first until their strength together, with whoever is fighting it already, comes to
/// <see cref="Margin"/> times the red's (<see cref="BotThreat.Power"/>), at most <see cref="MostHands"/> of them, and
/// then they are pressed against it. If they would not come to that between them nobody is pressed — "if they can take
/// it" — and that is counted and said. The fight is the patrol's (<see cref="BotBrawl.Manhunt"/>): a red that falls to
/// it is caught and goes to a cell (<see cref="BotOutlaw.Fell"/>), and it ends when the red is down, in a cell, or no
/// longer red.
/// </para>
///
/// <para>
/// Law-abiding means not red and not in a cell, fit (<see cref="BotHunter.FitAt"/>), its own master (free or at its own
/// work: a company's bot follows its company, a hurt or hunted one has enough on its hands), in no fight already, not a
/// healer — healers stand by the ones who go — and not the Baron, whose patrol is its own.
/// </para>
/// </summary>
public static class BotLawful
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotLawful));

    public static bool Running { get; set; } = true;

    public static int SweepMs { get; set; } = 5000;

    public static int Sight { get; set; } = 18;

    public static double Margin { get; set; } = 1.5;

    public static int MostHands { get; set; } = 4;

    public static int SayEveryMs { get; set; } = 120000;

    public static long Looked { get; private set; }

    public static long Unseen { get; private set; }

    public static long Outmatched { get; private set; }

    public static long Enough { get; private set; }

    public static long Pressed { get; private set; }

    public static long Hidden { get; private set; }

    private static long _sweptTick;

    private static readonly List<BotMobile> _reds = [];

    private static readonly List<BotMobile> _hands = [];

    private static readonly Dictionary<Serial, long> _saidTick = [];

    public static void Beat(long now)
    {
        if (!Running || now - _sweptTick < SweepMs)
        {
            return;
        }

        _sweptTick = now;
        _reds.Clear();
        BotOutlaw.Outlaws(_reds);

        for (var i = 0; i < _reds.Count; i++)
        {
            Look(_reds[i], now);
        }
    }

    private static void Look(BotMobile red, long now)
    {
        var map = red.Map;

        if (!red.Alive || map == null || map == Map.Internal || BotOutlaw.Guarded(map, red.X, red.Y, red.Z))
        {
            return;
        }

        if (red.Hidden)
        {
            Hidden++;

            return;
        }

        Looked++;

        var power = BotThreat.Now(red);
        var against = power * Margin;
        var on = 0.0;
        var inSight = 0;

        _hands.Clear();

        foreach (var other in map.GetMobilesInRange<BotMobile>(red.Location, Sight))
        {
            if (other == red || other is not { Deleted: false, Alive: true, Fallen: false })
            {
                continue;
            }

            if (Against(other, red))
            {
                on += BotThreat.Now(other);
                inSight++;

                continue;
            }

            if (Lawful(other))
            {
                _hands.Add(other);
                inSight++;
            }
        }

        if (inSight > 0 && BotOutlaw.Tell(red, _hands.Count > 0 ? _hands[0].Name : "those already on it"))
        {
            logger.Information(
                "{Red} the {Crime} was seen at ({X}, {Y}) by {Count} law-abiding, and the Baron knows of it now",
                red.Name,
                BotOutlaw.IsRed(red) ? "murderer" : "wanted thief",
                red.X,
                red.Y,
                inSight
            );
        }

        if (on >= against)
        {
            Enough++;

            return;
        }

        if (_hands.Count == 0)
        {
            Unseen++;

            return;
        }

        _hands.Sort((a, b) => BotThreat.Now(b).CompareTo(BotThreat.Now(a)));

        var together = on;
        var taking = 0;

        while (taking < _hands.Count && taking < MostHands && together < against)
        {
            together += BotThreat.Now(_hands[taking]);
            taking++;
        }

        if (together < against)
        {
            Outmatched++;

            if (!_saidTick.TryGetValue(red.Serial, out var said) || now - said >= SayEveryMs)
            {
                _saidTick[red.Serial] = now;

                logger.Information(
                    "{Red} the {Crime} at ({X}, {Y}) is in sight of {Count} law-abiding, {Together:F0} of strength with the strongest {Taking} against its {Power:F0}; nobody sets on it",
                    red.Name,
                    BotOutlaw.IsRed(red) ? "murderer" : "wanted thief",
                    red.X,
                    red.Y,
                    inSight,
                    together,
                    taking,
                    power
                );
            }

            return;
        }

        List<string> names = [];

        for (var i = 0; i < taking; i++)
        {
            var hand = _hands[i];
            var trains = hand.Bond?.Weapon?.Skill ?? SkillName.Wrestling;

            if (BotWill.Press(hand, new BotBrawl(red, BotBrawl.Manhunt, trains, BotManhunt.Over), $"{red.Name} the outlaw in sight"))
            {
                names.Add(hand.Name);
            }
        }

        Pressed += names.Count;

        if (names.Count > 0)
        {
            logger.Information(
                "{Count} law-abiding set on {Red} the murderer at ({X}, {Y}): {Names}, {Together:F0} of strength with any already on it against its {Power:F0}",
                names.Count,
                red.Name,
                red.X,
                red.Y,
                string.Join(", ", names),
                together,
                power
            );
        }
    }

    private static bool Against(BotMobile other, BotMobile red) =>
        other.Resolve?.Deed switch
        {
            BotManhunt hunt => ReferenceEquals(hunt.Foe, red),
            BotBrawl { Kind: BotBrawl.Manhunt } brawl => ReferenceEquals(brawl.Foe, red),
            _ => ReferenceEquals(other.Combatant, red) && !BotOutlaw.Outlaw(other)
        };

    private static bool Lawful(BotMobile other)
    {
        if (BotUnderworld.Member(other))
        {
            return false;
        }

        if (other.Class is not { } klass || klass.Role == BotRole.Medic || klass.Unpaid || other.Squad != null)
        {
            return false;
        }

        if (BotOutlaw.Outlaw(other) || BotOutlaw.Jailed(other) || BotDuel.Duelling(other))
        {
            return false;
        }

        if (other.HitsMax <= 0 || other.Hits < other.HitsMax * BotHunter.FitAt)
        {
            return false;
        }

        if (other.Resolve?.Deed is BotBrawl or BotManhunt or BotSentence)
        {
            return false;
        }

        return BotLadder.Standing(other) is BotStanding.Free or BotStanding.Busy;
    }

    public static string Describe() =>
        !Running
            ? "the law-abiding never set on a murderer of their own accord"
            : $"{Looked} looks at a red outside the towns: {Pressed} law-abiding pressed against one, {Enough} with enough on it already, {Outmatched} with nobody in sight who could take it together, {Unseen} with nobody law-abiding in sight, {Hidden} with the red hidden";

    public static void Forget()
    {
        Looked = 0;
        Unseen = 0;
        Outmatched = 0;
        Enough = 0;
        Pressed = 0;
        Hidden = 0;
        _sweptTick = 0;
        _reds.Clear();
        _hands.Clear();
        _saidTick.Clear();
    }
}
