using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Magic as a trade and as an appetite, as a module.
///
/// <para>
/// <see cref="BotPhase.World"/>, and it requires <c>Classes</c>, <c>Will</c>, <c>Shops</c> and
/// <c>Auction</c> — the last two because both ends of this subsystem are somebody else's counter: paper and
/// herbs come off a shelf, and what is written goes to the bots' own market or to a want standing on it.
/// </para>
///
/// <para>
/// <b>This is the module that gives the market a buyer that is not us.</b> Mining put metal out that nothing
/// needed; sewing bought its cloth off a shelf. A caster's book is the first appetite in this population that
/// only another bot can satisfy, because the engine's shopkeepers stop at the third circle — so the first
/// closed loop of the shard runs through here: a shopkeeper's herbs become a scribe's scroll, and a scribe's
/// scroll becomes another caster's spell, at a price the two of them settle between themselves.
/// </para>
/// </summary>
public sealed class BotSpellsModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSpellsModule));

    public override string Name => "Spells";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Classes", "Will", "Shops", "Auction"];

    public override void Start()
    {
        BotSpellsConfig.Load();

        // Needs the world, because it reads the answer off one of each scroll rather than guessing it.
        BotGrimoire.Read();

        BotWill.Offer(new BotScribe());
        BotWill.Offer(new BotSeeker());

        // Scrolls for everybody else. Until this, the only bot that ever asked for one was a mage filling a
        // book — one want per spell, once, for ever — so the scribes had no customers and the board of what
        // the population needs stayed empty. See BotArmoury.
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
        BotScribe.Forget();
        BotSeeker.Forget();
        BotArmoury.Forget();
    }
}
