using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotSpellsModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSpellsModule));

    public override string Name => "Spells";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Classes", "Will", "Shops", "Market"];

    public override void Start()
    {
        BotSpellsConfig.Load();

        Server.Engines.Craft.DefInscription.Knows = static (mobile, _) => mobile is BotMobile;

        BotGrimoire.Read();

        BotWill.Offer(new BotScribe());
        BotWill.Offer(new BotSeeker());

        BotWill.Offer(new BotArmoury());

        logger.Information(
            "Spells ready: {Grimoire}; a scribe buys {Batch} blanks at a time, writes {Margin} points below its own Inscribe and attempts every {Swing}ms",
            BotGrimoire.Describe(),
            BotInscribe.Batch,
            BotQuill.Margin,
            BotInscribe.SwingMs
        );
    }

    public override void Reset()
    {
        Server.Engines.Craft.DefInscription.Knows = null;
        BotScribe.Forget();
        BotScribes.Forget();
        BotSeeker.Forget();
        BotArmoury.Forget();
    }
}
