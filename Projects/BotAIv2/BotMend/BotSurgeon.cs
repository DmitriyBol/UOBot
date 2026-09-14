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

    private static Mobile Worst(Mobile bot, Map map)
    {
        Mobile worst = BotMend.Wants(bot) ? bot : null;
        var lowest = worst == null ? 1.0 : BotMend.Share(bot);

        foreach (var mobile in map.GetMobilesInRange<Mobile>(bot.Location, Reach))
        {
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

    public static void Forget() => _said = false;
}
