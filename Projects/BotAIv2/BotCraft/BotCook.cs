using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotCook : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCook));

    private static bool _saidNoSystem;

    public static long Asked { get; private set; }

    public static long NoKit { get; private set; }

    public static long NoMeat { get; private set; }

    public static long Unskilled { get; private set; }

    public static long NoFire { get; private set; }

    public static long Offered { get; private set; }

    public string Name => "Cook";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (BotOven.Kit(body) == null)
        {
            NoKit++;

            return null;
        }

        Asked++;

        if (BotOven.System == null)
        {
            if (!_saidNoSystem)
            {
                _saidNoSystem = true;

                logger.Error("The cooking system does not exist yet, so nobody can cook");
            }

            return null;
        }

        var raw = BotOven.Larder(body, out _);

        if (raw == null)
        {
            NoMeat++;

            return null;
        }

        var recipe = BotOven.Choose(body, raw);

        if (recipe?.ItemType == null)
        {

            Unskilled++;

            return null;
        }

        var here = BotOven.AtAHearth(body);
        var hearth = here ? body.Location : BotGround.Hearth(bot, body.Location);

        if (!here && hearth == Point3D.Zero)
        {
            NoFire++;

            return null;
        }

        Offered++;

        return new BotBake(map, hearth, raw, recipe, recipe.ItemType);
    }

    public static string Describe() =>
        Asked == 0
            ? $"nobody has been offered the skillet ({NoKit} answers went to bots with none)"
            : $"{Asked} asked to cook: {Offered} put something on, {NoMeat} had no meat worth cooking, "
              + $"{Unskilled} had meat but no recipe their skill would carry, {NoFire} had both and no fire "
              + $"they could get to (of {BotGround.Hearths.Count} known), {BotBake.Hopped} sent on to another fire from a cold or roadless one; {BotOven.Spared} stacks of raw meat "
              + $"kept back off a corpse for their own pan against {BotOven.Sold} sold on past the cap; {BotMeal.Describe()}";

    public static void Forget()
    {
        _saidNoSystem = false;
        Asked = 0;
        NoKit = 0;
        NoMeat = 0;
        Unskilled = 0;
        NoFire = 0;
        Offered = 0;
        BotOven.Forget();
    }
}
