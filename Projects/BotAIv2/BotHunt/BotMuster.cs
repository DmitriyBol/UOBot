using System.Collections.Generic;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a bot the chance to call a company against something it must otherwise walk past.
///
/// <para>
/// <b>It answers the question the hunter is not allowed to ask.</b> <see cref="BotHunter"/> judges a quarry
/// against what one bot can do alone, because a claim sends exactly one bot — and that is right, and it was
/// paid for in five deaths in nine minutes when the sum counted company that never came. The cost of being
/// right about it is a population that spends its evenings on rats while everything worth killing stands in
/// the next field. This is the other half: not "can I take that", but "could we".
/// </para>
///
/// <para>
/// <b>Nothing is announced and nobody is asked.</b> The bots who come are simply put in the company by the
/// one that called it, in the same beat, which is what the squad's own note means by the collective mind
/// being arithmetic rather than messages. The first version's alternative is on record — a bot posted a
/// call for help, the call found nobody able, it disbanded in the same tick, and the bot posted it again,
/// dozens of times over.
/// </para>
/// </summary>
public sealed class BotMuster : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMuster));

    public static int Reach { get; set; } = 120;

    public static int Least { get; set; } = 2;

    public static double MostBound { get; set; } = 0.5;

    public static long Enough { get; private set; }

    public static long VeteransPastCeiling { get; private set; }

    private static bool _said;

    public string Name => "Muster";

    public BotStanding Rung => BotStanding.Free;

    public static long Asked { get; private set; }

    public static long Held { get; private set; }

    public static long Unfit { get; private set; }

    public static long Alone { get; private set; }

    public static long AllSmall { get; private set; }

    public static long AllTooBig { get; private set; }

    public static long NothingBig { get; private set; }

    public static long TooFewNear { get; private set; }

    public static long Called { get; private set; }

    public static int UrgeMs { get; set; } = 15000;

    public static long UrgedBands { get; private set; }

    public static long UrgedAlone { get; private set; }

    private static readonly Dictionary<Serial, (BaseCreature Quarry, long Tick)> _urged = [];

    public static void Urge(Mobile body, BaseCreature quarry)
    {
        if (body == null || quarry == null)
        {
            return;
        }

        _urged[body.Serial] = (quarry, Core.TickCount);
    }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (!BotSquads.Running)
        {
            return null;
        }

        Asked++;

        if (bot is not IBotSquadMember { Squad: null })
        {
            Held++;

            return null;
        }

        if (BotTrading.Passes(body))
        {
            return null;
        }

        var alive = BotPopulation.Count;

        if (alive > 0 && BotSquads.Bound >= alive * MostBound)
        {
            if (!BotTraits.Veteran(body))
            {
                Enough++;

                return null;
            }

            VeteransPastCeiling++;
        }

        if (body.HitsMax <= 0 || body.Hits < body.HitsMax * BotHunter.FitAt)
        {
            Unfit++;

            return null;
        }

        if (_urged.TryGetValue(body.Serial, out var urge) && Core.TickCount - urge.Tick < UrgeMs)
        {
            _urged.Remove(body.Serial);

            if (urge.Quarry is { Deleted: false, Alive: true } foe && foe.Map == map && body.InRange(foe, Reach * 2) && !BotQuarry.Shunned(foe))
            {
                if (Free(body, Reach, out var levy) >= Least)
                {
                    if (Judged && (levy + BotThreat.Power(body)) * BotThreat.Tolerance < BotBand.Asked(map, foe, body))
                    {
                        Short++;

                        return null;
                    }

                    if (Judged && BotSquad.BeyondFor(body, foe, levy + BotThreat.Power(body)))
                    {
                        Priced++;

                        return null;
                    }

                    UrgedBands++;
                    Called++;
                    Once(body, foe);

                    return new BotBand(foe, BotThreat.OurPower(body, Reach));
                }

                UrgedAlone++;
            }
        }

        var quarry = BotQuarry.Company(body, Reach, BotSquad.ChoiceOdds, out var why);

        if (quarry == null)
        {
            switch (why)
            {
                case BotQuarry.CompanyRefusal.Alone:
                    Alone++;

                    break;

                case BotQuarry.CompanyRefusal.AllSmall:
                    AllSmall++;

                    break;

                case BotQuarry.CompanyRefusal.AllTooBig:
                    AllTooBig++;

                    break;

                default:
                    NothingBig++;

                    break;
            }

            return null;
        }

        if (Free(body, Reach, out var brought) < Least)
        {
            TooFewNear++;

            return null;
        }

        if (Judged && (brought + BotThreat.Power(body)) * BotThreat.Tolerance < BotBand.Asked(map, quarry, body))
        {
            Short++;

            return null;
        }

        if (Judged && BotSquad.BeyondFor(body, quarry, brought + BotThreat.Power(body)))
        {
            Priced++;

            return null;
        }

        Called++;

        Once(body, quarry);

        return new BotBand(quarry, BotThreat.OurPower(body, Reach));
    }

    public static bool Judged { get; set; } = true;

    public static long Short { get; private set; }

    public static long Priced { get; private set; }

    public static long HostileNear { get; private set; }

    private static int Free(Mobile body, int range, out double strength)
    {
        var map = body.Map;
        var free = 0;

        strength = 0.0;

        foreach (var mobile in map.GetMobilesInRange<Mobile>(body.Location, range))
        {
            if (mobile == body || mobile is not IBotSquadMember { Squad: null })
            {
                continue;
            }

            if (mobile is IBotAlly { AbleToFight: true } && (mobile is not IBotWilful wilful || BotSquads.Pressable(wilful))
                && !BotTrading.Neutral(mobile))
            {
                if (BotRegard.Hostile(body, mobile))
                {
                    HostileNear++;

                    continue;
                }

                free++;
                strength += BotThreat.Power(mobile);
            }
        }

        return free;
    }

    private static void Once(Mobile body, Mobile quarry)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information(
            "{Name} is the first to call a company: {What} is beyond one bot and within reach of several",
            body.Name,
            quarry.Name
        );
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been offered a company yet"
            : $"{Asked} asked: {Called} called ({UrgedBands} of them on a foe a bot had just found too many round, {UrgedAlone} such urges with too few free near), {Enough} refused because {BotSquads.Bound} of us are already in one, {Alone} had nobody of ours near enough, {AllSmall} found only one-bot work, "
              + $"{AllTooBig} found something beyond all of us together, {NothingBig} found nothing hostile at all, "
              + $"{TooFewNear} found one but fewer than {Least} free, {Short} found one the free near fell short of (judged before the call{(Judged ? "" : ", off")}), {Priced} found one the company's own price put past them, {HostileNear} hostile bots near a caller left out of the count, {Unfit} too hurt, {Held} already in one";

    public static void Forget()
    {
        _said = false;
        Asked = 0;
        Held = 0;
        Unfit = 0;
        Alone = 0;
        Enough = 0;
        AllSmall = 0;
        AllTooBig = 0;
        NothingBig = 0;
        TooFewNear = 0;
        Short = 0;
        Priced = 0;
        HostileNear = 0;
        Called = 0;
    }
}
