using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotQuadStore : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotQuadStore));

    private const int Shape = 2;

    private const int Oldest = 1;

    private static BotQuadStore _store;

    public static void Configure() => _store ??= new BotQuadStore();

    public BotQuadStore() : base("BotQuads", 13)
    {
    }

    public static int Restored { get; private set; }

    public override void Serialize(IGenericWriter writer)
    {
        var quads = BotQuad.All;

        writer.WriteEncodedInt(Shape);
        writer.WriteEncodedInt(quads.Count);

        foreach (var quad in quads)
        {
            writer.WriteEncodedInt(quad.Map?.MapID ?? -1);
            writer.WriteEncodedInt(quad.X);
            writer.WriteEncodedInt(quad.Y);
            writer.Write(quad.Safety);
            writer.WriteEncodedInt(quad.Passes);
            writer.WriteEncodedInt(quad.Blows);
            writer.WriteEncodedInt(quad.Deaths);
            writer.WriteEncodedInt(quad.RangersLost);
            writer.Write(quad.Trodden);
            writer.Write(quad.Swept);

            writer.Write(quad.HarrowedTick != 0);

            writer.WriteEncodedInt(quad.Levied);
            writer.WriteEncodedInt(quad.Wipes);
        }
    }

    public override void Deserialize(IGenericReader reader)
    {
        var shape = reader.ReadEncodedInt();

        if (shape < Oldest || shape > Shape)
        {
            logger.Warning(
                "The saved island is shape {Found} and this build reads {Oldest} to {Wanted}; it cannot be read, and the shard will stop on the engine's own prompt until Saves/BotQuads/BotQuads.bin is deleted",
                shape,
                Oldest,
                Shape
            );

            return;
        }

        var count = reader.ReadEncodedInt();

        for (var i = 0; i < count; i++)
        {
            var facet = reader.ReadEncodedInt();
            var x = reader.ReadEncodedInt();
            var y = reader.ReadEncodedInt();
            var safety = reader.ReadDouble();
            var passes = reader.ReadEncodedInt();
            var blows = reader.ReadEncodedInt();
            var deaths = reader.ReadEncodedInt();
            var rangers = reader.ReadEncodedInt();
            var trodden = reader.ReadBool();
            var swept = reader.ReadBool();
            var harrowed = reader.ReadBool();

            var levied = shape >= 2 ? reader.ReadEncodedInt() : 0;
            var wipes = shape >= 2 ? reader.ReadEncodedInt() : 0;

            BotQuad.Restore(
                facet,
                x,
                y,
                safety,
                passes,
                blows,
                deaths,
                rangers,
                trodden,
                swept,
                harrowed,
                levied,
                wipes
            );
        }

        Restored = count;

        logger.Information("The island was read back: {Count} quadrants the population had already walked", count);
    }
}
