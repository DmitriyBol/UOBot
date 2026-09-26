using System.Diagnostics;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// What the island's spawners keep alive near a place, reckoned as a fight and set beside what the square asks and what a
/// bot brought there — counted and written down, and nothing refused. §5 S8 of DECISIONS.md, in shadow.
///
/// <para>
/// <b>The ground map counts heads, and only as fresh as the last look.</b> A square asks strength of whoever goes there
/// (<see cref="BotQuad.Muscle(Map, Point3D)"/>) from its earned reading less a tenth for each hostile creature somebody
/// standing in it counted in the last five minutes (<see cref="BotQuad.MobWorth"/>, <see cref="BotQuad.SightMs"/>). So an
/// ogre counts as a mongbat, and a square nobody has stood in lately counts nothing at all: the three spawners east of
/// Britain at (1969, 1409), (1986, 1463) and (1983, 1517) keep ettins, gargoyles, gazers, ogres, trolls and water elementals,
/// seven of each, with a walking range of thirty, and at 23:01 on 16.09.2026 the square at (1998, 1494) read +0.08 and asked
/// nothing while seven bots died there in two minutes. The spawners are known at every boot (<see cref="BotLairs"/>), what
/// they keep alive is in their entries, and <see cref="BotThreat.Power"/> speaks the units a square asks in.
/// </para>
///
/// <para>
/// <b>Measured before it is enforced, because it would move the fear of the whole map at once.</b> Every prowl, hunt and
/// sweep refusal reads the same number. Decided by Claude on Patrick's word of 17.09.2026 ("as you consider necessary"):
/// first how often the dead fell where the spawners kept more than they brought while the square asked no more, and how
/// much of the island such a rule would close to a bot alone; then whether to make it a wall.
/// </para>
/// </summary>
public static class BotKept
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotKept));

    public static bool Running { get; set; } = true;

    public static int Sight { get; set; } = 12;

    public static double LoneBot { get; set; } = 1116;

    public static int SurveyEveryMs { get; set; } = 1800000;

    public static long Deaths { get; private set; }

    public static long Outmatched { get; private set; }

    public static long Unwarned { get; private set; }

    private static long _surveyedTick;

    private static bool _surveyed;

    private static string _survey = "the island not yet reckoned";

    public static void Fell(BotMobile bot)
    {
        if (!Running || bot?.Map == null || bot.Map == Map.Internal)
        {
            return;
        }

        var map = bot.Map;
        var where = bot.Location;
        var strength = BotThreat.Power(bot);
        var company = 1;

        if (bot is IBotSquadMember { Squad: not null } member)
        {
            var members = member.Squad.Members;

            for (var i = 0; i < members.Count; i++)
            {
                if (members[i]?.Self is not { Deleted: false, Alive: true } self || self == bot)
                {
                    continue;
                }

                if (members[i] is IBotAlly { AbleToFight: true })
                {
                    strength += BotThreat.Power(self);
                    company++;
                }
            }
        }

        var asked = BotQuad.Muscle(map, where);
        var fight = BotLairs.MetFight(map, where, Sight, out var kept, out var wide);

        Deaths++;

        if (fight > strength)
        {
            Outmatched++;

            if (asked <= strength)
            {
                Unwarned++;
            }
        }

        var killer = bot.LastKiller;

        logger.Information(
            "Kept: {Name} fell at ({X}, {Y}) with {Strength:F0} of strength in a company of {Company}, killed by {Killer}; the square asked {Asked:F0}, and the spawners near keep {Kept} alive, {Fight:F0} to meet within sight and {Wide:F0} over all their ground",
            bot.Name,
            where.X,
            where.Y,
            strength,
            company,
            killer switch
            {
                null => "nobody known",
                BotMobile other => $"{other.Name}, one of ours ({BotThreat.Power(other):F0})",
                _ => $"{killer.GetType().Name} ({BotThreat.Power(killer):F0})"
            },
            asked,
            kept,
            fight,
            wide
        );
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "what the spawners keep is not reckoned (BotKept.Running)";
        }

        if (!_surveyed || Core.TickCount - _surveyedTick >= SurveyEveryMs)
        {
            _surveyed = true;
            _surveyedTick = Core.TickCount;
            _survey = Survey();
        }

        return $"what the spawners keep, in shadow: {Deaths} deaths reckoned, {Outmatched} where it came to more than the dead bot's company brought and {Unwarned} of those where the square asked no more than it brought; {_survey}";
    }

    private static string Survey()
    {
        var map = BotPopulation.Home;

        if (map == null || map == Map.Internal)
        {
            return "the island not yet reckoned";
        }

        var watch = Stopwatch.StartNew();
        var squares = 0;
        var wideLone = 0;
        var lone = 0;
        var company = 0;
        var twice = 0;
        var unasked = 0;
        var worst = 0.0;
        var worstAt = Point2D.Zero;
        var full = LoneBot * BotSquad.MaxSize;

        foreach (var quad in BotQuad.All)
        {
            if (quad.Map != map || quad.Deep || quad.Townbound)
            {
                continue;
            }

            squares++;

            var middle = quad.Middle;
            var at = new Point3D(middle.X, middle.Y, 0);
            var fight = BotLairs.MetFight(map, at, Sight, out _, out var wide);

            if (wide > LoneBot)
            {
                wideLone++;
            }

            if (fight <= LoneBot)
            {
                continue;
            }

            lone++;

            if (fight > full)
            {
                company++;
            }

            if (fight > 2 * full)
            {
                twice++;
            }

            if (BotQuad.Muscle(map, at) < fight)
            {
                unasked++;
            }

            if (fight > worst)
            {
                worst = fight;
                worstAt = middle;
            }
        }

        return $"of {squares} squares outside the walls and the dungeons, {lone} have more than a lone bot of {LoneBot:F0} to meet within {Sight} tiles of what the spawners keep, {company} more than a company of {BotSquad.MaxSize}, {twice} more than two, and {unasked} of them ask less than that now ({wideLone} over all the spawners' ground); the most at ({worstAt.X}, {worstAt.Y}), {worst:F0} (reckoned in {watch.ElapsedMilliseconds}ms)";
    }

    public static void Forget()
    {
        Deaths = 0;
        Outmatched = 0;
        Unwarned = 0;
        _surveyed = false;
    }
}
