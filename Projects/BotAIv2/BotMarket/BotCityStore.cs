using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotCityStore : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCityStore));

    private const int Shape = 2;

    private static BotCityStore _store;

    public static void Configure() => _store ??= new BotCityStore();

    public BotCityStore() : base("BotCity", 16)
    {
    }

    public static int Restored { get; private set; }

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(Shape);
        BotCity.Save(writer);
    }

    public override void Deserialize(IGenericReader reader)
    {
        var shape = reader.ReadEncodedInt();

        if (shape < 1 || shape > Shape)
        {
            logger.Warning(
                "The saved treasury is shape {Found} and this build reads {Wanted}; it cannot be read, and the shard will stop on the engine's own prompt until Saves/BotCity/BotCity.bin is deleted",
                shape,
                Shape
            );

            return;
        }

        var (purse, orders, bounties, heads) = BotCity.Load(reader, shape);

        Restored = purse;

        logger.Information(
            "The treasury was read back (shape {Shape}): {Purse}gp, {Orders} standing orders, {Bounties} bounties on ground and {Heads} on heads, {Kinds} prices on kinds with {HeadPurse}gp in the heads' purse, carried on from where they stood",
            shape,
            purse,
            orders,
            bounties,
            heads,
            BotCity.KindPrices,
            BotCity.HeadPurse
        );
    }
}
