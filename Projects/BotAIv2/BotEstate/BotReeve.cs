using Server.Guilds;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The guild's own company for the guild's own ground.
///
/// <para>
/// <b>A claim that only ever cost money meant nothing once it was paid for.</b> A guild bought a square, bots
/// died in it, the square went dire, and the Baron came to clear it — or did not, the island having worse
/// squares — while the guild that held it went on hunting elsewhere. Patrick's fourth Quad idea of 16.09.2026,
/// taken that evening: when a square a guild holds goes dire, the guild raises its own company onto it, led by
/// its own leader, and the Baron leaves such ground to the guild while it has somebody to raise one.
/// </para>
///
/// <para>
/// The errand is the Baron's <see cref="BotHarrow"/>, unchanged: a muster, a march, the square walked and its
/// spawn killed, the company paid what the city put on it. What differs is the office: any fit fighter of the
/// guild — armed, no novice — rather than the Baron, and only the guild's own squares rather than the island's
/// worst. Not the leader classes: the first cut asked for <see cref="BotClass.Leads"/>, and the 92 window read "53
/// guild leaders asked: 53 in no guild" — the Captain is nobody's member, and the guilds' masters are crafters, made
/// ordinary bots by Patrick the same evening. Reckoned by the harrow's own prior, so it outbids ordinary work as
/// the Baron's does; two of a guild cannot raise for the same square, since a square being raised for is passed
/// over (<see cref="BotProwl.Raising"/>).
/// </para>
/// </summary>
public sealed class BotReeve : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotReeve));

    public string Name => "Reeve";

    public BotStanding Rung => BotStanding.Free;

    public static bool Running { get; set; } = true;

    public static long Asked { get; private set; }

    public static long Unfit { get; private set; }

    public static long Guildless { get; private set; }

    public static long Landless { get; private set; }

    public static long Safe { get; private set; }

    public static long Held { get; private set; }

    public static long Hurt { get; private set; }

    public static long Resting { get; private set; }

    public static long Offered { get; private set; }

    public static long Left { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (!Running || map == null || map == Map.Internal || body is not { Alive: true })
        {
            return null;
        }

        if (body is not BotMobile leader || leader.Class is BotBaron)
        {
            return null;
        }

        Asked++;

        if (leader.Guild is not Guild guild)
        {
            Guildless++;

            return null;
        }

        if (bot.Bond?.Weapon?.Skill == null || BotLadder.Novice(body))
        {
            Unfit++;

            return null;
        }

        if (BotHarrow.Resting(body))
        {
            Resting++;

            return null;
        }

        if (!BotSquads.Running)
        {
            return null;
        }

        if (bot is not IBotSquadMember { Squad: null })
        {
            Held++;

            return null;
        }

        if (body.HitsMax <= 0 || body.Hits < body.HitsMax * BotHunter.FitAt)
        {
            Hurt++;

            return null;
        }

        var (square, dead) = Dire(map, guild.Name, body.Location);

        if (square == Point3D.Zero)
        {
            return null;
        }

        Offered++;

        logger.Information(
            "{Name}, {Class} of {Guild}, has been offered the harrowing of the guild's own square at ({X}, {Y}), where {Dead} have died",
            body.Name,
            leader.Class?.Name ?? "nobody",
            guild.Name,
            square.X,
            square.Y,
            dead
        );

        return new BotHarrow(map, square, dead);
    }

    private static (Point3D Square, int Dead) Dire(Map map, string guild, Point3D from)
    {
        var best = Point3D.Zero;
        var bestDead = 0;
        var closest = int.MaxValue;
        var owned = 0;

        foreach (var (key, owner, _) in BotClaim.Owned())
        {
            if (key.Map != map.MapID || !string.Equals(owner, guild, System.StringComparison.Ordinal))
            {
                continue;
            }

            owned++;

            var middle = new Point3D(key.X * BotQuad.Side + BotQuad.Side / 2, key.Y * BotQuad.Side + BotQuad.Side / 2, 0);

            if (BotQuad.Safety(map, middle) > BotQuad.Dire || BotProwl.Raising(map, middle))
            {
                continue;
            }

            var quad = BotQuad.At(map, middle);

            if (quad == null)
            {
                continue;
            }

            var at = BotQuad.Stand(quad);

            if (at == Point3D.Zero)
            {
                continue;
            }

            var away = System.Math.Max(System.Math.Abs(at.X - from.X), System.Math.Abs(at.Y - from.Y));

            if (away >= closest)
            {
                continue;
            }

            closest = away;
            best = at;
            bestDead = quad.Deaths;
        }

        if (owned == 0)
        {
            Landless++;
        }
        else if (best == Point3D.Zero)
        {
            Safe++;
        }

        return (best, bestDead);
    }

    public static bool Keeps(Map map, Point3D where)
    {
        if (!Running)
        {
            return false;
        }

        var owner = BotClaim.Owner(map, where);

        if (owner == null)
        {
            return false;
        }

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is not { Deleted: false, Alive: true } || bot.Class is BotBaron || BotLadder.Novice(bot)
                || bot is not IBotWilful { Bond.Weapon: not null })
            {
                continue;
            }

            if (bot.Guild is Guild guild && string.Equals(guild.Name, owner, System.StringComparison.Ordinal))
            {
                Left++;

                return true;
            }
        }

        return false;
    }

    public static string Describe() =>
        !Running
            ? "guilds raise no companies for their own ground"
            : $"{Asked} fighters asked: {Guildless} in no guild, {Unfit} unarmed or novices, {Landless} of a guild with no ground, {Safe} with no square of their own at or below dire ({BotQuad.Dire:F2}), {Held} already in a company, {Hurt} too hurt, {Resting} while the harrow rested, {Offered} offered the harrowing of their guild's own square; the Baron left {Left} dire squares to the guild that holds them";

    public static void Forget()
    {
        Asked = 0;
        Guildless = 0;
        Landless = 0;
        Safe = 0;
        Held = 0;
        Unfit = 0;
        Hurt = 0;
        Resting = 0;
        Offered = 0;
        Left = 0;
    }
}
