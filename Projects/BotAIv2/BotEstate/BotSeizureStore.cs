using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotSeizureStore : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSeizureStore));

    private const int Shape = 1;

    private const int Oldest = 1;

    private static BotSeizureStore _store;

    public static void Configure() => _store ??= new BotSeizureStore();

    public BotSeizureStore() : base("BotSeizures", 16)
    {
    }

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(Shape);

        List<(string Guild, long Ago)> kept = [];

        foreach (var row in BotSeizure.Memory())
        {
            kept.Add(row);
        }

        writer.WriteEncodedInt(kept.Count);

        for (var i = 0; i < kept.Count; i++)
        {
            writer.Write(kept[i].Guild);
            writer.Write(kept[i].Ago);
        }
    }

    public override void Deserialize(IGenericReader reader)
    {
        var shape = reader.ReadEncodedInt();

        if (shape < Oldest || shape > Shape)
        {
            logger.Warning(
                "The saved seizures are shape {Found} and this build reads {Oldest} to {Wanted}; they cannot be read, and the shard will stop on the engine's own prompt until Saves/BotSeizures/BotSeizures.bin is deleted",
                shape,
                Oldest,
                Shape
            );

            return;
        }

        var many = reader.ReadEncodedInt();

        for (var i = 0; i < many; i++)
        {
            var guild = reader.ReadString();
            var ago = reader.ReadLong();

            BotSeizure.Restore(guild, ago);
        }

        if (many > 0)
        {
            logger.Information("{Many} seizures read back off the save", many);
        }
    }
}
