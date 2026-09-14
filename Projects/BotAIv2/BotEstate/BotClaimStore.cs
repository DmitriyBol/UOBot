using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Keeps who owns which square of the island across restarts.
///
/// <para>
/// <b>Patrick's order of 10.09.2026: "притязания надо записывать, ибо за них боты платят".</b> Every other
/// board on this shard is cleared on a world reload and that is right — a market stall, a want, an errand
/// and a squad are all about a moment. Ground is not. A guild spends four free claims and then five thousand
/// gold a square, stands three of its members in a field for five minutes to earn each one, and until this
/// existed the whole register was thrown away by the next rebuild. That is not a board going stale, it is
/// the shard taking something a guild bought.
/// </para>
///
/// <para>
/// <b>Names and numbers only, exactly as <see cref="BotQuadStore"/> is.</b> A holding is a facet id, two
/// square coordinates, a guild's name and whether it was paid for. Nothing here points at a mobile, an item
/// or a serial, so it outlives a world the rest of the shard cannot.
/// </para>
///
/// <para>
/// <b>A claim still running is written too, and it comes back with its clock reset.</b> The money went when
/// the claim was declared, so a restart in the middle of a muster would otherwise charge a guild five
/// thousand gold for nothing. Restoring the remaining seconds would be worse than useless — the population
/// is rebuilt scattered and thirty seconds is not enough for anybody to walk anywhere — so what is returned
/// is the attempt, whole. The tick it began at is deliberately not written: a tick count means nothing in
/// the next process, and on some hosts it is the machine's uptime.
/// </para>
/// </summary>
public sealed class BotClaimStore : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotClaimStore));

    private const int Shape = 1;

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

        writer.WriteEncodedInt(making);

        foreach (var bid in BotClaim.Bids)
        {
            writer.WriteEncodedInt(bid.Key.Map);
            writer.WriteEncodedInt(bid.Key.X);
            writer.WriteEncodedInt(bid.Key.Y);
            writer.Write(bid.Guild);
            writer.WriteEncodedInt((int)bid.Want);
            writer.Write(bid.From ?? "");
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

        for (var i = 0; i < making; i++)
        {
            var facet = reader.ReadEncodedInt();
            var x = reader.ReadEncodedInt();
            var y = reader.ReadEncodedInt();
            var guild = reader.ReadString();
            var want = reader.ReadEncodedInt();
            var from = reader.ReadString();

            if (BotClaim.Reopen(facet, x, y, guild, (BotClaim.Kind)want, from))
            {
                back++;
            }
        }

        Restored = held;

        logger.Information(
            "The register of ground was read back: {Held} squares belong to somebody, and {Back} claims that were still being made have their five minutes again",
            held,
            back
        );
    }
}
