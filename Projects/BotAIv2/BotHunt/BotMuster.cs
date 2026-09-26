using Server.Logging;

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

    public static double MostBound { get; set; } = 0.34;

    public static long Enough { get; private set; }

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

        var alive = BotPopulation.Count;

        if (alive > 0 && BotSquads.Bound >= alive * MostBound)
        {
            Enough++;

            return null;
        }

        if (body.HitsMax <= 0 || body.Hits < body.HitsMax * BotHunter.FitAt)
        {
            Unfit++;

            return null;
        }

        var quarry = BotQuarry.Company(body, Reach, out var why);

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

        if (Free(body, Reach) < Least)
        {
            TooFewNear++;

            return null;
        }

        Called++;

        Once(body, quarry);

        return new BotBand(quarry, BotThreat.OurPower(body, Reach));
    }

    private static int Free(Mobile body, int range)
    {
        var map = body.Map;
        var free = 0;

        foreach (var mobile in map.GetMobilesInRange<Mobile>(body.Location, range))
        {
            if (mobile == body || mobile is not IBotSquadMember { Squad: null })
            {
                continue;
            }

            if (mobile is IBotAlly { AbleToFight: true })
            {
                free++;
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
            : $"{Asked} asked: {Called} called, {Enough} refused because {BotSquads.Bound} of us are already in one, {Alone} had nobody of ours near enough, {AllSmall} found only one-bot work, "
              + $"{AllTooBig} found something beyond all of us together, {NothingBig} found nothing hostile at all, "
              + $"{TooFewNear} found one but fewer than {Least} free, {Unfit} too hurt, {Held} already in one";

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
        Called = 0;
    }
}
