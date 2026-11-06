using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

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
