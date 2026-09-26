using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Keeps the guilds' widenings across restarts. See <see cref="BotGuilds.WidenBy"/>: a guild paid 10000gp for ten more
/// places, and a restart that forgot it would put the guild back at fifteen with twenty-five members in it. Keyed by the
/// guild's name, which is how the rest of the estate finds a guild again after a boot. A shape this build cannot read is
/// dropped whole rather than guessed at.
/// </summary>
public sealed class BotGuildWidenStore : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotGuildWidenStore));

    private const int Shape = 1;

    private static BotGuildWidenStore _store;

    public static void Configure() => _store ??= new BotGuildWidenStore();

    public BotGuildWidenStore() : base("BotGuildWiden", 19)
    {
    }

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(Shape);
        BotGuilds.SaveWidenings(writer);
    }

    public override void Deserialize(IGenericReader reader)
    {
        var shape = reader.ReadEncodedInt();

        if (shape != Shape)
        {
            logger.Warning("The saved guild widenings are shape {Found} and this build reads {Wanted}; every guild stands at its ceiling", shape, Shape);

            return;
        }

        var widened = BotGuilds.LoadWidenings(reader);

        logger.Information("Guild widenings read back: {Widened} guilds hold more than the ceiling", widened);
    }
}
