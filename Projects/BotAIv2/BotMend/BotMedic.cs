using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a bot the chance to look after itself, and it is the only thing on the rung that says so.
///
/// <para>
/// <b>This fills the rung the ladder has always had and never had anything on.</b> <c>Failing</c> exists
/// because a bot whose health is going does not weigh options — but with no proposer answering it, the brain's
/// only available answer was to hold on to whatever the bot was already doing. That was harmless while nothing
/// on the shard fought. The day hunting arrived it became "go back to the skeleton", and the first version
/// spent a night proving where that ends: four hundred and forty-three deaths, a hundred and four of them one
/// bot getting up in the same tile every half minute.
/// </para>
///
/// <para>
/// <b>Above everything, including helping anybody else.</b> That ordering is a measured defect, not taste: the
/// first version put the call for help above failing health, so a bot on its last few points announced a
/// company it could not join, found nobody able to come, and posted the same call again — dozens of times over.
/// Flight and self-repair outrank the social.
/// </para>
/// </summary>
public sealed class BotMedic : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMedic));

    private static bool _saidNoMeans;

    public string Name => "Medic";

    public BotStanding Rung => BotStanding.Failing;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive || !BotMend.Wants(body))
        {
            return null;
        }

        Asked++;

        if (BotThreat.Anything(body, BotMend.Peril))
        {
            Hostile++;

            var foe = BotThreat.Hunter(body, BotMend.Peril);

            if (foe != null)
            {
                BotCry.Raise(body, foe);
            }

            return null;
        }

        var spell = BotMend.Spell(body, body);

        if (spell >= 0)
        {
            Spelled++;

            return new BotSalve(body, map, onSelf: true, SkillName.Magery);
        }

        if (BotMend.Cloth(body) > 0)
        {
            Bandaged++;

            return new BotSalve(body, map, onSelf: true, SkillName.Healing);
        }

        Dry++;

        Missing(body);

        return null;
    }

    public static long Asked { get; private set; }

    public static long Hostile { get; private set; }

    public static long Spelled { get; private set; }

    public static long Bandaged { get; private set; }

    public static long Dry { get; private set; }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been hurt and left to look after itself"
            : $"{Asked} looked after themselves: {Spelled} had the mana, {Bandaged} had the cloth, "
              + $"{Hostile} had something standing over them, {Dry} were hurt and safe with neither";

    public static void ForgetCounts()
    {
        Asked = 0;
        Hostile = 0;
        Spelled = 0;
        Bandaged = 0;
        Dry = 0;
    }

    private static void Missing(Mobile body)
    {
        if (_saidNoMeans)
        {
            return;
        }

        _saidNoMeans = true;

        logger.Error(
            "{Name} is hurt and has neither the mana, the herbs nor the cloth to do anything about it",
            body.Name
        );
    }

    public static void Forget() => _saidNoMeans = false;
}
