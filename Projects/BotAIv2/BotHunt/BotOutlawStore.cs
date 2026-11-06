using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotOutlawStore : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotOutlawStore));

    private const int Shape = 2;

    private static BotOutlawStore _store;

    public static void Configure() => _store ??= new BotOutlawStore();

    public BotOutlawStore() : base("BotOutlaws", 15)
    {
    }

    public static int Restored { get; private set; }

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(Shape);
        BotOutlaw.Save(writer);
    }

    public override void Deserialize(IGenericReader reader)
    {
        var shape = reader.ReadEncodedInt();

        if (shape is < 1 or > Shape)
        {
            logger.Warning(
                "The saved outlaws are shape {Found} and this build reads {Wanted}; they cannot be read, and the shard will stop on the engine's own prompt until Saves/BotOutlaws/BotOutlaws.bin is deleted",
                shape,
                Shape
            );

            return;
        }

        var (reds, jailed) = BotOutlaw.Load(reader, shape);

        Restored = reds + jailed;

        logger.Information("The outlaws were read back: {Reds} red and {Jailed} in the cells, their clocks carried on from where they stood", reds, jailed);
    }
}
