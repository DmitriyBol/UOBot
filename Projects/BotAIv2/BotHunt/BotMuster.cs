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

    /// <summary>
    /// How far around itself a bot looks, both for the thing worth calling a company against and for the
    /// bots who would make it up.
    ///
    /// <para>
    /// One number for both, and it has to be one: the company is assembled in a single beat out of whoever
    /// is standing there, so a creature inside the reach and helpers outside it is a company of one.
    /// </para>
    ///
    /// <para>
    /// <b>Twelve tiles — about a screen — turned out to be the reason ogres were walked past.</b> A company
    /// is refused outright when no ally is inside this range, and that check happens before a single creature
    /// is examined, so a bot alone in a field never even asks what is standing in it. Once prowling started
    /// working properly the population spread out across three hundred tiles, and being alone became the
    /// normal state: nine hundred and eighty-one refusals in twenty minutes, every one of them silent about
    /// which gate closed. Widening this costs a longer walk to the fight and buys the fights themselves,
    /// which is the trade the whole subsystem exists to make.
    /// </para>
    /// </summary>
    public static int Reach { get; set; } = 28;

    /// <summary>
    /// How many others have to be free and able before it is worth calling at all.
    ///
    /// Two. One helper is not a company — it is two bots taking on something the arithmetic already said one
    /// bot must refuse, which is the same mistake with a witness.
    /// </summary>
    public static int Least { get; set; } = 2;

    private static bool _said;

    public string Name => "Muster";

    public BotStanding Rung => BotStanding.Free;

    /// <summary>
    /// Why a company was not called, counted case by case with no bucket called "other".
    ///
    /// <para>
    /// <b>Nought companies in an evening and nothing anywhere saying why.</b> Squads went from fifteen in
    /// twenty minutes to none, twice, and every explanation on offer was a guess: the population might be too
    /// scattered to gather two helpers, or there might be nothing big enough to need a company, or everybody
    /// might be too hurt. Those want opposite fixes and the log could not tell them apart, because a proposer
    /// that returns null is silent about which of its four gates closed. The counters below are that
    /// distinction, and the denominator — how many bots got as far as being asked — is what makes the rest
    /// of them mean anything.
    /// </para>
    /// </summary>
    public static long Asked { get; private set; }

    public static long Held { get; private set; }

    public static long Unfit { get; private set; }

    /// <summary>Nobody of ours was near enough to make a company at all.</summary>
    public static long Alone { get; private set; }

    /// <summary>Everything standing here is already one bot's work.</summary>
    public static long AllSmall { get; private set; }

    /// <summary>Something is here and it is beyond what everybody standing here could take together.</summary>
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

        // Squads have to actually be running. The module can be switched off, and a proposer that offered
        // companies into a subsystem that is not there would hand every bot an undertaking that fails on its
        // first beat.
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

        // The same fitness a lone hunt asks for. Being in company does not make a bot at half health any
        // more able to be in a fight, and the member that falls over first is the one that takes the squad
        // below the two it needs to exist.
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

        return new BotBand(quarry);
    }

    /// <summary>
    /// How many of ours are standing here, able to fight, and not already in a company.
    ///
    /// Counted from what is nearby rather than from a roster — the same rule the rest of this project
    /// follows, and for the same reason: asking the map costs what is nearby, while walking the population
    /// costs the population.
    /// </summary>
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

        // Said once, by name, because this is the first time on this shard that anything has proposed a
        // company at all — and "companies are never formed" and "companies are formed and do nothing" look
        // identical in a log that says neither.
        logger.Information(
            "{Name} is the first to call a company: {What} is beyond one bot and within reach of several",
            body.Name,
            quarry.Name
        );
    }

    /// <summary>Every gate, counted apart, against the number of bots that reached them.</summary>
    public static string Describe() =>
        Asked == 0
            ? "nobody has been offered a company yet"
            : $"{Asked} asked: {Called} called, {Alone} had nobody of ours near enough, {AllSmall} found only one-bot work, "
              + $"{AllTooBig} found something beyond all of us together, {NothingBig} found nothing hostile at all, "
              + $"{TooFewNear} found one but fewer than {Least} free, {Unfit} too hurt, {Held} already in one";

    /// <summary>Lets the line be said again after a world reload.</summary>
    public static void Forget()
    {
        _said = false;
        Asked = 0;
        Held = 0;
        Unfit = 0;
        Alone = 0;
        AllSmall = 0;
        AllTooBig = 0;
        NothingBig = 0;
        TooFewNear = 0;
        Called = 0;
    }
}
