using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers the maker of a guild a dungeon its band could actually come back out of.
///
/// <para>
/// <b>Only a guild's maker, by order.</b> A delve is called by the head of a band, which on this shard is
/// the crafter every guild is founded on and the one bot in it with a mind. It is the same office that
/// charges the band with a trade and decides who is in it — see <c>BotCharter</c> and <c>BotRoster</c> —
/// and a delve is the third thing that office can do.
/// </para>
///
/// <para>
/// <b>Which dungeon is a measurement, not a preference.</b> Patrick's order names the reason in one line:
/// a demon is plainly not a harpy. Every dungeon has been measured — see <see cref="BotDungeon"/>, which
/// builds one of each creature its spawners name and reads its strength off the engine — and what this
/// does with those numbers is pick the richest one whose <i>worst</i> inhabitant the party can still
/// answer. The worst rather than the average, because a party is killed by the strongest thing it meets
/// and not by the mean of what lives there.
/// </para>
///
/// <para>
/// <b>And the strength is the party's, not the leader's.</b> Asked of the bodies that would actually be
/// called up — the fighters standing within reach of the muster, best first, as many as would go down —
/// because a leader is a crafter and a crafter's own strength says nothing about what its band can take.
/// </para>
///
/// <para>
/// <b>Every refusal has a name.</b> A delve needs a maker, one not already leading a company, one fit to
/// walk into a cave, a surveyed dungeon and a band strong enough for one. Which of those was missing is
/// the only question worth being able to answer on an evening when nobody went anywhere.
/// </para>
/// </summary>
public sealed class BotDelver : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDelver));

    public static double Odds { get; set; } = 1.5;

    public static int Reach { get; set; } = 200;

    public static int HeadsPerParty { get; set; } = 20;

    public static int BetweenMs { get; set; } = 1800000;

    public string Name => "Delver";

    public BotStanding Rung => BotStanding.Free;

    public static long Asked { get; private set; }

    public static long NotALeader { get; private set; }

    public static long Held { get; private set; }

    public static long Unfit { get; private set; }

    public static long Resting { get; private set; }

    public static long TooSoon { get; private set; }

    public static long Unsurveyed { get; private set; }

    public static long Outmatched { get; private set; }

    public static long Crowded { get; private set; }

    public static long Offered { get; private set; }

    private static readonly Dictionary<string, long> _last = [];

    private static readonly Dictionary<string, long> _claims = [];

    private static bool _said;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (body is not BotMobile who || who.Guild is not Guild guild || !ReferenceEquals(BotGuilds.Maker(guild), who))
        {
            NotALeader++;

            return null;
        }

        Asked++;

        if (BotDelve.Resting(body))
        {
            Resting++;

            return null;
        }

        if (_last.TryGetValue(guild.Name, out var when) && Core.TickCount - (when + BetweenMs) < 0)
        {
            TooSoon++;

            return null;
        }

        if (!BotSquads.Running)
        {
            return null;
        }

        if (bot is not IBotSquadMember { Squad: null })
        {
            Held++;

            return null;
        }

        if (body.HitsMax <= 0 || body.Hits < body.HitsMax * BotHunter.FitAt)
        {
            Unfit++;

            return null;
        }

        BotDungeon.Survey(map);

        var strength = Band(map, body);
        var deep = Deepest(strength);

        if (deep == null)
        {
            if (BotDungeon.Rooms == 0)
            {
                Unsurveyed++;
            }
            else if (Deepest(strength, alone: false, full: true) != null)
            {
                Crowded++;
            }
            else
            {
                Outmatched++;
            }

            return null;
        }

        _last[guild.Name] = Core.TickCount;
        _claims[deep.Name] = Core.TickCount;
        Offered++;

        Once(body, deep, strength);

        return new BotDelve(map, deep);
    }

    private static double Band(Map map, Mobile leader)
    {
        _might.Clear();

        foreach (var mobile in map.GetMobilesInRange<Mobile>(leader.Location, Reach))
        {
            if (mobile == leader || mobile is not BotMobile other)
            {
                continue;
            }

            if (other is not IBotAlly { AbleToFight: true } || other.Class is not { } klass)
            {
                continue;
            }

            if (klass.Role == BotRole.Producer || other.Squad != null || BotDelveParty.Delving(other))
            {
                continue;
            }

            _might.Add(BotThreat.Power(other));
        }

        _might.Sort(static (a, b) => b.CompareTo(a));

        var total = 0.0;
        var counted = Math.Min(_might.Count, Math.Max(1, BotDelve.Company - 1));

        for (var i = 0; i < counted; i++)
        {
            total += _might[i];
        }

        return total;
    }

    private static readonly List<double> _might = [];

    private static BotDungeon.Deep Deepest(double strength)
    {
        var empty = Deepest(strength, alone: true);

        return empty ?? Deepest(strength, alone: false);
    }

    private static BotDungeon.Deep Deepest(double strength, bool alone, bool full = false)
    {
        BotDungeon.Deep best = null;

        var all = BotDungeon.All;

        for (var i = 0; i < all.Count; i++)
        {
            var deep = all[i];

            if (!deep.Ready || strength < deep.Worst * Odds)
            {
                continue;
            }

            if (!full)
            {
                var parties = BotDelveParty.Inside(deep) + (Spoken(deep) ? 1 : 0);

                if (parties >= (alone ? 1 : Holds(deep)))
                {
                    continue;
                }
            }

            if (best == null || deep.Power > best.Power)
            {
                best = deep;
            }
        }

        return best;
    }

    private static int Holds(BotDungeon.Deep deep) =>
        Math.Max(1, deep.Heads / Math.Max(1, HeadsPerParty));

    private static bool Spoken(BotDungeon.Deep deep) =>
        _claims.TryGetValue(deep.Name, out var when) && Core.TickCount - (when + BotDelve.MusterMs) < 0;

    private static void Once(Mobile body, BotDungeon.Deep deep, double strength)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information(
            "{Name} has been offered the first delve on this shard: {Deep}, worth {Power} a head and {Worst} at worst, against {Strength} of band",
            body.Name,
            deep.Name,
            deep.Power.ToString("F0"),
            deep.Worst.ToString("F0"),
            strength.ToString("F0")
        );
    }

    public static string Describe() =>
        Asked == 0
            ? $"no guild's maker has ever been offered a delve ({NotALeader} answers went to bots that lead no band)"
            : $"{Asked} times a maker was asked: {Offered} were offered a dungeon, {Held} were already leading a company, "
            + $"{Unfit} were too hurt, {Resting} came too soon after a call that failed, {TooSoon} too soon after their band's last delve, "
            + $"{Unsurveyed} found nothing surveyed, {Crowded} found every dungeon they could survive already full, "
            + $"{Outmatched} found nothing their band could come back out of at ×{Odds:F2} the worst of it; "
            + $"{BotDungeon.Describe()}; {BotDelve.Describe()}";

    public static void Forget()
    {
        _said = false;
        Asked = 0;
        NotALeader = 0;
        Held = 0;
        Unfit = 0;
        Resting = 0;
        TooSoon = 0;
        Unsurveyed = 0;
        Outmatched = 0;
        Crowded = 0;
        Offered = 0;
        _last.Clear();
        _claims.Clear();
        _might.Clear();

        BotDelve.Forget();
        BotDungeon.Forget();
        BotHalls.Forget();
    }
}
