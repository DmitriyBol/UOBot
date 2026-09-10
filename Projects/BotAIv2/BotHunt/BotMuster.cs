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
    /// <para>
    /// <b>And twenty-eight became too narrow the same way twelve did, for the same reason, on 08.09.2026.</b>
    /// Widening the population's roaming that afternoon — the dart limit went from 240 tiles to 500 — spread
    /// it further again, and the refusals came back larger than before: 1306 in fifteen minutes, against the
    /// 981 in twenty that bought the last widening. Patrick watched five bots stand beside four orcs, each
    /// orc at 111 health against their 62, and nobody call anybody.
    /// </para>
    ///
    /// <para>
    /// <b>Tying it to <see cref="BotCry.Carries"/> was tried first and measured as not enough:</b> refusals
    /// went from 82% of real asks to 78%, and companies called from 2.7 a minute to 2.5. The two numbers
    /// look alike and are not. A cry is an emergency — forty tiles is twenty seconds of running, and help
    /// that arrives later arrives at a corpse. A muster is a plan: the fight has not started, nobody is
    /// dying, and a march is affordable.
    /// </para>
    ///
    /// <para>
    /// <b>So it is sized from the population's own spread instead of guessed.</b> Forty-nine bots roaming a
    /// circle of five hundred tiles is about sixteen thousand tiles each, which puts the nearest neighbour
    /// something like seventy tiles away; finding two of them needs roughly twice that. A hundred and twenty
    /// is a march of about a minute at walking pace, against fights that run for several.
    /// </para>
    /// </summary>
    public static int Reach { get; set; } = 120;

    /// <summary>
    /// How many others have to be free and able before it is worth calling at all.
    ///
    /// Two. One helper is not a company — it is two bots taking on something the arithmetic already said one
    /// bot must refuse, which is the same mistake with a witness.
    /// </summary>
    public static int Least { get; set; } = 2;

    /// <summary>
    /// The largest share of the population that may be standing in companies before no more are called.
    ///
    /// <para>
    /// <b>A company is bots not working, and until 08.09.2026 nothing capped how many.</b> Widening
    /// <see cref="Reach"/> to 120 that evening fixed the calling — refusals for want of a neighbour fell
    /// from 82% of real asks to 6% and companies called went from 40 to 96 — and immediately overshot: eight
    /// companies standing, 36 of 49 bots on the Bound rung, twelve holding nothing at all, and the shard's
    /// finishing rate down to 68%. A bot on that rung is not offered work of its own, so three quarters of
    /// the population had quietly stopped mining, sewing and trading.
    /// </para>
    ///
    /// <para>
    /// A third. Enough that a real threat always gets a company, few enough that the island goes on being
    /// worked while it does. This is the number to move if companies are wanted more or less often — moving
    /// Reach instead only changes how far away they find each other.
    /// </para>
    /// </summary>
    public static double MostBound { get; set; } = 0.34;

    /// <summary>Calls not made because too much of the population is already in a company.</summary>
    public static long Enough { get; private set; }

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

        // <b>Enough of the island is already under arms.</b> Every bot in a company is a bot the auction no
        // longer offers work to, so calling without a ceiling turns a population into an army that owns
        // nothing. See MostBound for the evening this was measured on.
        var alive = BotPopulation.Count;

        if (alive > 0 && BotSquads.Bound >= alive * MostBound)
        {
            Enough++;

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
            : $"{Asked} asked: {Called} called, {Enough} refused because {BotSquads.Bound} of us are already in one, {Alone} had nobody of ours near enough, {AllSmall} found only one-bot work, "
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
        Enough = 0;
        AllSmall = 0;
        AllTooBig = 0;
        NothingBig = 0;
        TooFewNear = 0;
        Called = 0;
    }
}
