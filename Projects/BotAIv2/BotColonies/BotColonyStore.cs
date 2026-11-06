using System;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotColonyStore : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotColonyStore));

    private const int Shape = 1;

    private const int Oldest = 1;

    private static BotColonyStore _store;

    [CallPriority(41)]
    public static void Configure()
    {
        if (BotCore.Enabled)
        {
            _store ??= new BotColonyStore();
        }
    }

    public BotColonyStore() : base("BotColonies", 16)
    {
    }

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(Shape);

        var colonies = BotColonies.ForSave();

        writer.WriteEncodedInt(colonies.Count);

        for (var i = 0; i < colonies.Count; i++)
        {
            var c = colonies[i];

            writer.Write(c.Guild ?? "");
            writer.Write(c.Island ?? "");
            writer.WriteEncodedInt(c.Map?.MapID ?? 0);
            writer.Write(c.Site.X);
            writer.Write(c.Site.Y);
            writer.Write(c.Site.Z);
            writer.Write((uint)c.StoreSerial);
            writer.Write(c.FoundedAt.Ticks);
            writer.Write(c.Claimed);
            writer.Write(c.OreIn);
            writer.Write(c.IngotsIn);
            writer.Write(c.LogsIn);
            writer.Write(c.ColonistMs);
            writer.Write(c.Runs);
            writer.Write(c.CargoUnits);
            writer.Write(c.CargoGold);
            writer.Write(c.ChestGold);
            writer.Write(c.CrewGold);
            writer.Write(c.CaptainGold);
            writer.Write(c.Plundered);
        }
    }

    public override void Deserialize(IGenericReader reader)
    {
        var shape = reader.ReadEncodedInt();

        if (shape < Oldest || shape > Shape)
        {
            logger.Warning(
                "The saved colonies are shape {Found} and this build reads {Oldest} to {Wanted}; they cannot be read, and the shard will stop on the engine's own prompt until Saves/BotColonies/BotColonies.bin is deleted",
                shape,
                Oldest,
                Shape
            );

            return;
        }

        var many = reader.ReadEncodedInt();

        for (var i = 0; i < many; i++)
        {
            var colony = new BotColony
            {
                Guild = reader.ReadString(),
                Island = reader.ReadString(),
                Map = Map.Maps[Math.Clamp(reader.ReadEncodedInt(), 0, Map.Maps.Length - 1)]
            };

            var x = reader.ReadInt();
            var y = reader.ReadInt();
            var z = reader.ReadInt();

            colony.Site = new Point3D(x, y, z);
            colony.StoreSerial = (Serial)reader.ReadUInt();
            colony.FoundedAt = new DateTime(reader.ReadLong(), DateTimeKind.Utc);
            colony.Claimed = reader.ReadBool();
            colony.OreIn = reader.ReadLong();
            colony.IngotsIn = reader.ReadLong();
            colony.LogsIn = reader.ReadLong();
            colony.ColonistMs = reader.ReadLong();
            colony.Runs = reader.ReadLong();
            colony.CargoUnits = reader.ReadLong();
            colony.CargoGold = reader.ReadLong();
            colony.ChestGold = reader.ReadLong();
            colony.CrewGold = reader.ReadLong();
            colony.CaptainGold = reader.ReadLong();
            colony.Plundered = reader.ReadLong();
            colony.Last = "read back from the save";

            if (!string.IsNullOrEmpty(colony.Guild))
            {
                BotColonies.Restore(colony);
            }
        }

        if (many > 0)
        {
            logger.Information("{Many} colonies read back off the save", many);
        }
    }
}
