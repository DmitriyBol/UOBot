using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotCommonsStore : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCommonsStore));

    private const int Shape = 1;

    private static BotCommonsStore _store;

    public static void Configure() => _store ??= new BotCommonsStore();

    public BotCommonsStore() : base("BotCommons", 14)
    {
    }

    public static int Restored { get; private set; }

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(Shape);
        BotCommons.Save(writer);
    }

    public override void Deserialize(IGenericReader reader)
    {
        var shape = reader.ReadEncodedInt();

        if (shape != Shape)
        {
            logger.Warning(
                "The saved commons are shape {Found} and this build reads {Wanted}; they cannot be read, and the shard will stop on the engine's own prompt until Saves/BotCommons/BotCommons.bin is deleted",
                shape,
                Shape
            );

            return;
        }

        var (patches, trades, seams) = BotCommons.Load(reader);

        Restored = patches;

        logger.Information(
            "The commons were read back: {Patches} patches, {Trades} trades and {Seams} seams of what pays where, faded from when they were written",
            patches,
            trades,
            seams
        );
    }
}
