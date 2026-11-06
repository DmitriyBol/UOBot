using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotCrimeStore : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCrimeStore));

    private const int Shape = 1;

    private static BotCrimeStore _store;

    public static void Configure() => _store ??= new BotCrimeStore();

    public BotCrimeStore() : base("BotCrime", 16)
    {
    }

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(Shape);
        BotCrime.Save(writer);
    }

    public override void Deserialize(IGenericReader reader)
    {
        var shape = reader.ReadEncodedInt();

        if (shape != Shape)
        {
            logger.Warning(
                "The saved books on crime are shape {Found} and this build reads {Wanted}; they cannot be read, and the shard will stop on the engine's own prompt until Saves/BotCrime/BotCrime.bin is deleted",
                shape,
                Shape
            );

            return;
        }

        var count = BotCrime.Load(reader);

        if (count > 0)
        {
            logger.Information("The books on crime were read back: {Count} names on record", count);
        }
    }
}
