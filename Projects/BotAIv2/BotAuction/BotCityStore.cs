using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Keeps the city's treasury, its standing orders and its bounties across restarts.
///
/// <para>
/// <b>Every restart emptied the treasury back to its opening sum.</b> On 16.09.2026 the purse stood full at 20000gp
/// with 10000 of it taxed off the guilds' ground; the 18:17 restart opened it at 1000 again, and every standing order
/// and bounty the watchers had put up through the door was gone with it. The ground's reputation, what pays where and
/// the murderers' clocks all survive a restart — see <see cref="BotQuadStore"/>, <see cref="BotCommonsStore"/> and
/// <see cref="BotOutlawStore"/> — and the city's money is the same kind of fact.
/// </para>
///
/// <para>
/// A bounty on ground is written with its age rather than its tick, because a tick count belongs to the process that
/// made it; read back as "set that long ago", so it lapses when it would have. A standing order is written by the
/// thing's name and looked up again on the way in, and dropped if this build knows no such thing.
/// </para>
/// </summary>
public sealed class BotCityStore : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCityStore));

    private const int Shape = 1;

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

        if (shape != Shape)
        {
            logger.Warning(
                "The saved treasury is shape {Found} and this build reads {Wanted}; it cannot be read, and the shard will stop on the engine's own prompt until Saves/BotCity/BotCity.bin is deleted",
                shape,
                Shape
            );

            return;
        }

        var (purse, orders, bounties, heads) = BotCity.Load(reader);

        Restored = purse;

        logger.Information(
            "The treasury was read back: {Purse}gp, {Orders} standing orders, {Bounties} bounties on ground and {Heads} on heads, carried on from where they stood",
            purse,
            orders,
            bounties,
            heads
        );
    }
}
