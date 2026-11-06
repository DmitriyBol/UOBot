using Server.Logging;

namespace Server.BotAI.V2;

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
