using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotClaimStore : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotClaimStore));

    private const int Shape = 2;

    private const int Oldest = 1;

    private static BotClaimStore _store;

    public static void Configure() => _store ??= new BotClaimStore();

    public BotClaimStore() : base("BotClaims", 14)
    {
    }

    public static int Restored { get; private set; }

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(Shape);

        var held = 0;

        foreach (var _ in BotClaim.Owned())
        {
            held++;
        }

        writer.WriteEncodedInt(held);

        foreach (var (key, guild, bought) in BotClaim.Owned())
        {
            writer.WriteEncodedInt(key.Map);
            writer.WriteEncodedInt(key.X);
            writer.WriteEncodedInt(key.Y);
            writer.Write(guild);
            writer.Write(bought);
        }

        var making = 0;

        foreach (var _ in BotClaim.Bids)
        {
            making++;
        }

        var owed = BotClaim.Owed;

        writer.WriteEncodedInt(making + owed.Count);

        foreach (var bid in BotClaim.Bids)
        {
            writer.WriteEncodedInt(bid.Key.Map);
            writer.WriteEncodedInt(bid.Key.X);
            writer.WriteEncodedInt(bid.Key.Y);
            writer.Write(bid.Guild);
            writer.WriteEncodedInt((int)bid.Want);
            writer.Write(bid.From ?? "");
            writer.WriteEncodedInt(bid.Escrow);
        }

        for (var i = 0; i < owed.Count; i++)
        {
            writer.WriteEncodedInt(owed[i].Key.Map);
            writer.WriteEncodedInt(owed[i].Key.X);
            writer.WriteEncodedInt(owed[i].Key.Y);
            writer.Write(owed[i].Guild);
            writer.WriteEncodedInt((int)BotClaim.Kind.Settle);
            writer.Write("");
            writer.WriteEncodedInt(owed[i].Coin);
        }

        var rests = 0;

        foreach (var _ in BotClaim.Rests())
        {
            rests++;
        }

        writer.WriteEncodedInt(rests);

        foreach (var (guild, key, failures, ago) in BotClaim.Rests())
        {
            writer.Write(guild);
            writer.WriteEncodedInt(key.Map);
            writer.WriteEncodedInt(key.X);
            writer.WriteEncodedInt(key.Y);
            writer.WriteEncodedInt(failures);
            writer.Write(ago);
        }
    }

    public override void Deserialize(IGenericReader reader)
    {
        var shape = reader.ReadEncodedInt();

        if (shape < Oldest || shape > Shape)
        {
            logger.Warning(
                "The saved claims are shape {Found} and this build reads {Oldest} to {Wanted}; they cannot be read, and the shard will stop on the engine's own prompt until Saves/BotClaims/BotClaims.bin is deleted",
                shape,
                Oldest,
                Shape
            );

            return;
        }

        var held = reader.ReadEncodedInt();

        for (var i = 0; i < held; i++)
        {
            var facet = reader.ReadEncodedInt();
            var x = reader.ReadEncodedInt();
            var y = reader.ReadEncodedInt();
            var guild = reader.ReadString();
            var bought = reader.ReadBool();

            BotClaim.Restore(facet, x, y, guild, bought);
        }

        var making = reader.ReadEncodedInt();
        var back = 0;
        var held2 = 0;

        for (var i = 0; i < making; i++)
        {
            var facet = reader.ReadEncodedInt();
            var x = reader.ReadEncodedInt();
            var y = reader.ReadEncodedInt();
            var guild = reader.ReadString();
            var want = reader.ReadEncodedInt();
            var from = reader.ReadString();
            var escrow = shape >= 2 ? reader.ReadEncodedInt() : 0;

            if (BotClaim.Reopen(facet, x, y, guild, (BotClaim.Kind)want, from, escrow))
            {
                back++;
                held2 += escrow;
            }
        }

        var rests = shape >= 2 ? reader.ReadEncodedInt() : 0;

        for (var i = 0; i < rests; i++)
        {
            var guild = reader.ReadString();
            var facet = reader.ReadEncodedInt();
            var x = reader.ReadEncodedInt();
            var y = reader.ReadEncodedInt();
            var failures = reader.ReadEncodedInt();
            var ago = reader.ReadLong();

            BotClaim.Rest(guild, (facet, x, y), failures, ago);
        }

        Restored = held;

        logger.Information(
            "The register of ground was read back: {Held} squares belong to somebody, {Back} claims that were still being made have their five minutes again ({Escrow}gp held for them), and {Rests} failed musters still rest their squares",
            held,
            back,
            held2,
            rests
        );
    }
}
