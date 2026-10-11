using System.Collections.Generic;
using Server.Logging;
using Server.Guilds;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a member of a guild at war somebody of the enemy to fight.
///
/// <para>
/// <b>The call comes first and the sighting second, and that order is the whole of "call your
/// guildmates".</b> A member asks what its guild is already on before it asks what it can see: if the guild
/// has a call standing and the enemy is within <c>BotFeud.Answer</c>, that is who it goes to, wherever it
/// happens to be looking. Only a bot whose guild is on nobody starts a quarrel of its own, and starting one
/// raises the call the others will answer. Without that ordering five members find five different enemies
/// and a war is five duels.
/// </para>
///
/// <para>
/// <b>Only classes that fight.</b> A tailor sent at an armed warrior is a tailor killed for nothing, and
/// the population pays for the resurrection and the lost afternoon. The class's own roll says whether it
/// carries a weapon; that is the same question <c>BotHarrow</c> asks of a company, asked here of one bot.
/// </para>
/// </summary>
public sealed class BotFeuder : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotFeuder));

    public static int Watch { get; set; } = 20;

    public static double Fit { get; set; } = 0.5;

    public static long Defended { get; private set; }

    public static long Asked { get; private set; }

    public static long Offered { get; private set; }

    public static long Peaceful { get; private set; }

    public static long Unseen { get; private set; }

    public static long Unfit { get; private set; }

    public static long Unarmed { get; private set; }

    public static long Unsupplied { get; private set; }

    public static long Sheltered { get; private set; }

    public static long Sallied { get; private set; }

    public static long Summoned { get; private set; }

    public static long Mustered { get; private set; }

    public string Name => "feuder";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        if (!BotEstate.Running || !BotFeud.Running || !BotRegard.Running || !BotRegard.Warring)
        {
            return null;
        }

        if (bot?.Self is not BotMobile { Deleted: false, Alive: true } body || body.Map == null ||
            body.Map == Map.Internal)
        {
            return null;
        }

        if (body.Guild is not Guild ours)
        {
            return null;
        }

        if (BotUnderworld.Band(ours))
        {
            return null;
        }

        Asked++;

        if (BotUnderworld.Near(body, Watch) is { } bandit)
        {
            var why = BanditLeft(bot, body, bandit);

            if (why == null)
            {
                Bandits++;
                Offered++;

                return new BotQuarrel(bandit, ours.Name);
            }

            _banditsLeft[why] = _banditsLeft.GetValueOrDefault(why) + 1;

            if (bot is IBotSquadMember { Squad: not null })
            {
                return null;
            }
        }

        if (!AnyWar(ours))
        {
            Peaceful++;

            return null;
        }

        if (body.Hits < body.HitsMax * Fit)
        {
            Unfit++;

            return null;
        }

        var kit = bot.Class?.Kit;

        if (kit == null || kit.Melee.Count == 0 && kit.Ranged.Count == 0)
        {
            Unarmed++;

            return null;
        }

        var threat = BotFeud.Threat(ours);

        var summoned = string.Equals(BotRest.CalledBy(body), ours.Name, System.StringComparison.OrdinalIgnoreCase);

        if (summoned)
        {
            Summoned++;
        }

        var mustered = !summoned && BotWar.Mustered(body);

        if (mustered)
        {
            Mustered++;
        }

        var anywhere = summoned || mustered;

        if (threat != null && threat != body && (anywhere || body.InRange(threat.Location, BotFeud.Defend)))
        {
            if (BotFeud.Quarrel(body, threat))
            {
                BotFeud.Stood();
                Defended++;
                Offered++;

                return BotFeud.Rally(bot, ours, threat, true) ?? Alone(bot, new BotQuarrel(threat, ours.Name, true));
            }

            Sheltered++;
        }

        if (!BotProvision.Fit(body, out _))
        {
            Unsupplied++;

            return null;
        }

        var called = BotFeud.On(ours);

        if (called != null && called != body && (anywhere || body.InRange(called.Location, BotFeud.Answer)))
        {
            if (!BotFeud.Quarrel(body, called))
            {
                Sheltered++;

                return null;
            }

            BotFeud.Came();
            Offered++;

            return BotFeud.Rally(bot, ours, called, false) ?? Alone(bot, new BotQuarrel(called, ours.Name));
        }

        BotMobile enemy = null;
        var sheltered = false;

        foreach (var near in body.GetMobilesInRange<BotMobile>(Watch))
        {
            if (near == body || near.Deleted || !near.Alive || near.Guild is not Guild theirs || theirs == ours)
            {
                continue;
            }

            if (theirs.Name == BotUnderworld.GuildName)
            {
                continue;
            }

            if (!BotRegard.AtWar(ours, theirs))
            {
                continue;
            }

            if (!BotFeud.Quarrel(body, near))
            {
                sheltered = true;

                continue;
            }

            enemy = near;

            break;
        }

        if (enemy == null && !sheltered && BotFeud.Sortie(ours))
        {
            var foe = BotFeud.Foe(ours, body.Location, body.Map);

            if (foe != null && BotFeud.Quarrel(body, foe))
            {
                BotFeud.Call(ours, foe);
                BotFeud.Sortied();
                Sallied++;
                Offered++;

                return BotFeud.Rally(bot, ours, foe, false) ?? Alone(bot, new BotQuarrel(foe, ours.Name));
            }
        }

        if (enemy == null)
        {
            if (sheltered)
            {
                Sheltered++;
            }
            else
            {
                Unseen++;
            }

            return null;
        }

        BotFeud.Call(ours, enemy);
        Offered++;

        return BotFeud.Rally(bot, ours, enemy, false) ?? Alone(bot, new BotQuarrel(enemy, ours.Name));
    }

    private static BotDeed Alone(IBotWilful bot, BotQuarrel quarrel) =>
        bot is IBotSquadMember { Squad: not null } ? null : quarrel;

    private static bool AnyWar(Guild ours)
    {
        if (ours == null)
        {
            return false;
        }

        var engine = Warring(ours);
        var ledger = BotWar.Fighting(ours.Name) > 0;

        if (engine != ledger)
        {
            Disagreeing++;

            var now = Core.TickCount;

            if (now - (_disagreedTick + DisagreeSayMs) >= 0)
            {
                _disagreedTick = now;

                logger.Warning(
                    "{Guild}: the ledger says {Ledger} and the engine says {Engine}; the engine lists {Count} enemies",
                    ours.Name,
                    ledger ? "at war" : "at peace",
                    engine ? "at war" : "at peace",
                    ours.Enemies?.Count ?? 0
                );
            }
        }

        return engine || ledger;
    }

    private static bool Warring(Guild ours)
    {
        var enemies = ours.Enemies;

        if (enemies == null)
        {
            return false;
        }

        for (var i = 0; i < enemies.Count; i++)
        {
            if (enemies[i] is { } enemy && enemy.Name != BotUnderworld.GuildName)
            {
                return true;
            }
        }

        return false;
    }

    private static string BanditLeft(IBotWilful bot, BotMobile body, BotMobile bandit)
    {
        if (bot is IBotSquadMember { Squad: not null })
        {
            return "the fighter is in a company";
        }

        if (body.Hits < body.HitsMax * Fit)
        {
            return "the fighter too hurt";
        }

        if (BotThreat.Now(body) < BotThreat.Power(bandit) * BanditOdds)
        {
            return "the bandit the stronger";
        }

        if (!BotFeud.Quarrel(body, bandit))
        {
            return "a guarded town or no lawful blow";
        }

        if (!BanditStands)
        {
            return null;
        }

        if (bandit.Squad != null)
        {
            return "the bandit in a company";
        }

        if (bandit.Journey.Moving && !body.InRange(bandit.Location, BanditBeside))
        {
            return "the bandit walking";
        }

        if (bandit.Resolve?.Deed is { Steadfast: true })
        {
            return "the bandit at steadfast work";
        }

        if (body.Map is { } map && BotReach.Ask(map, body.Location, bandit.Location, BotArrival.Within(BotQuarrel.Reach)) == BotReachVerdict.Sealed)
        {
            return "no way to the bandit";
        }

        return null;
    }

    public static bool BanditStands { get; set; } = true;

    public static int BanditBeside { get; set; } = 2;

    private static readonly Dictionary<string, long> _banditsLeft = new(System.StringComparer.Ordinal);

    private static string DescribeBanditsLeft()
    {
        if (_banditsLeft.Count == 0)
        {
            return "none";
        }

        var said = "";

        foreach (var (why, n) in _banditsLeft)
        {
            said += (said.Length == 0 ? "" : ", ") + $"{why} {n}";
        }

        return said;
    }

    public static double BanditOdds { get; set; } = 0.8;

    public static long Bandits { get; private set; }

    public static long Disagreeing { get; private set; }

    private static long _disagreedTick;

    public static int DisagreeSayMs { get; set; } = 60000;

    public static string Describe() =>
        Asked == 0
            ? "nobody has been looked at for a quarrel"
            : $"the feuder looked {Asked} times and sent {Offered} ({Defended} of them to defend their own ground, {Bandits} against a bandit of The Shadow in sight; bandits in sight not set on: {DescribeBanditsLeft()}): {Peaceful} guilds were at war with nobody, "
            + $"{Unseen} saw no enemy within {Watch} tiles or the call, {Sallied} went out after one nobody had seen, {Summoned} looks at members called back from rest, {Mustered} at members drawn in from beyond their reach because their guild was behind in its war, {Sheltered} were kept apart by a town, "
            + $"{Unfit} were too hurt to start one, {Unarmed} carry no weapon, {Unsupplied} kept off somebody else's ground for want of supplies, {Disagreeing} found the ledger and the engine disagreeing about the war; {BotQuarrel.Describe()}; {BotFeud.Describe()}";

    public static void Forget()
    {
        _banditsLeft.Clear();
        Asked = 0;
        Offered = 0;
        Defended = 0;
        Sallied = 0;
        Summoned = 0;
        Mustered = 0;
        Disagreeing = 0;
        Peaceful = 0;
        Unseen = 0;
        Unfit = 0;
        Unsupplied = 0;
        Sheltered = 0;
        Unarmed = 0;
    }
}
