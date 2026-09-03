using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers whoever can mend the worst-hurt bot within sight — <b>itself included</b> — as ordinary work.
///
/// <para>
/// <b>Counting itself among the patients is what closes the gap in the ladder.</b> A bot drops onto
/// <c>Failing</c> at thirty-five per cent and looks after itself above all else; above that it is on the
/// ordinary rung, and without this it would carry on working at half health with nothing in the world offering
/// to patch it up. Now the same question — who here is worst off — answers both cases, and the <em>ladder</em>
/// keeps the ordering rather than a rule in this file: at thirty per cent nobody asks this proposer anything,
/// because the bot is already somewhere more urgent.
/// </para>
///
/// <para>
/// That ordering is a measured defect, not taste. The first version put the call for help <em>above</em>
/// failing health, so a bot on its last few points announced a company it could not join, found nobody able,
/// and posted the same call again dozens of times over. Looking after somebody else must never outrank looking
/// after yourself, and here it cannot.
/// </para>
///
/// <para>
/// <b>Spell or cloth, and the ability decides rather than the name.</b> A heal is two seconds and mana that
/// comes back on its own; a bandage is nine or ten and a thing that had to be bought — so a caster is simply
/// better at this, which is a fact about the two mechanics and not a rule about classes. A warrior with
/// bandages and sixty Healing patching up a miner is exactly as welcome.
/// </para>
///
/// <para>
/// <b>The patient has to be genuinely hurt.</b> That is the anti-exploit and it has to be here rather than in
/// the undertaking, because "cast heal on a healthy friend for ever" is the training dummy with a friend in
/// it — the exact shape the whole ledger is built to refuse. The engine says as much about cloth by itself; for
/// a spell this is the only place it gets said.
/// </para>
/// </summary>
public sealed class BotSurgeon : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSurgeon));

    /// <summary>How far a caster looks for somebody worth healing.</summary>
    public static int Reach { get; set; } = 20;

    private static bool _said;

    public string Name => "Surgeon";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        // <b>A healer may heal anybody, itself included, so long as nothing has picked the healer out.</b>
        // Ordered on 24.08.2026, and it is the sharper half of the same rule that stops bandaging under fire.
        // Mending is standing still: a cast is interrupted outright by a blow, and a bandage merely bleeds
        // its worth away per hit — so a healer with a zombie on it is not slow at healing, it is doing
        // nothing at all while being killed, and every beat spent trying is a beat not spent hitting back.
        // What is asked here is who has chosen the healer, not what happens to be nearby: a fight going on
        // ten tiles away is exactly when a healer is needed and is no reason to refuse.
        var onMe = BotThreat.Hunter(body, BotDefender.Reach);

        if (onMe != null)
        {
            BotCry.Raise(body, onMe);

            return null;
        }

        var patient = Worst(body, map);

        if (patient == null)
        {
            return null;
        }

        // Something to mend with, or there is nothing to offer. Spell first, because that is what the
        // undertaking will reach for first.
        var spell = BotMend.Spell(body, patient);

        if (spell < 0 && BotMend.Cloth(body) <= 0)
        {
            return null;
        }

        var onSelf = patient == body;

        if (!onSelf && !_said)
        {
            _said = true;

            logger.Information("{Name} has started patching up the rest of them", body.Name);
        }

        return new BotSalve(patient, map, onSelf, spell >= 0 ? SkillName.Magery : SkillName.Healing);
    }

    /// <summary>
    /// The worst-hurt of this population within reach, counting the asker, or null.
    ///
    /// Asked of the map rather than of a roster, the same way our side of a fight is counted: a hundred and
    /// fifty registry entries walked per bot per decision is what the first version did, and what is nearby
    /// costs what is nearby.
    /// </summary>
    private static Mobile Worst(Mobile bot, Map map)
    {
        Mobile worst = BotMend.Wants(bot) ? bot : null;
        var lowest = worst == null ? 1.0 : BotMend.Share(bot);

        foreach (var mobile in map.GetMobilesInRange<Mobile>(bot.Location, Reach))
        {
            // Being unreachable does not make anybody less hurt, which is why the worst-hurt rule kept
            // handing back the same patient after every refused road. See BotMend.Beyond — the note lapses in
            // ten seconds, so this is a pause rather than an abandonment.
            if (mobile == bot || mobile is not IBotAlly || !BotMend.Wants(mobile) || BotMend.OutOfReach(mobile))
            {
                continue;
            }

            var share = BotMend.Share(mobile);

            if (share >= lowest)
            {
                continue;
            }

            worst = mobile;
            lowest = share;
        }

        return worst;
    }

    /// <summary>Lets the note be made again after a world reload.</summary>
    public static void Forget() => _said = false;
}
