using Server.Guilds;
using Server.Logging;

namespace Server.BotAI.V2;

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

    public static long Unsupplied { get; private set; }

    public static long Few { get; private set; }

    public static long Across { get; private set; }

    public static long Overwhelmed { get; private set; }

    public static int Ring { get; set; } = 1;

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

        if (!BotProvision.Fit(body, out _))
        {
            Unsupplied++;

            return null;
        }

        if (Fighters(guild) < BotHarrow.Least)
        {
            Few++;

            return null;
        }

        if (!BotHarrow.Reaches(map, body.Location, BotFeud.MusterPoint(guild, body), count: false))
        {
            Across++;

            return null;
        }

        var (square, dead) = Dire(map, guild, body.Location, BotThreat.Power(body));

        if (square == Point3D.Zero)
        {
            return null;
        }

        if (BotHarrow.Taken(map, square))
        {
            BotHarrow.Declined();

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

        return new BotHarrow(map, square, dead, guild);
    }

    public static int Fighters(Guild guild)
    {
        var members = guild?.Members;
        var fighters = 0;

        for (var i = 0; i < members?.Count; i++)
        {
            if (members[i] is BotMobile { Deleted: false, Alive: true } bot && bot.Class is { } klass
                && klass.Role != BotRole.Producer && klass is not BotBaron && !BotLadder.Novice(bot)
                && bot is IBotWilful { Bond.Weapon: not null })
            {
                fighters++;
            }
        }

        return fighters;
    }

    public static string Warden(Map map, Point3D where)
    {
        var owner = BotClaim.Owner(map, where);

        if (owner != null || map == null)
        {
            return owner;
        }

        var key = BotQuad.Key(map, where);

        foreach (var (name, hall) in BotEstate.Held)
        {
            if (hall is not { Deleted: false } || hall.Map != map)
            {
                continue;
            }

            var seat = BotQuad.Key(map, hall.Location);

            if (System.Math.Abs(seat.X - key.X) <= Ring && System.Math.Abs(seat.Y - key.Y) <= Ring)
            {
                return name;
            }
        }

        return null;
    }

    private static (Point3D Square, int Dead) Dire(Map map, Guild guild, Point3D from, double head)
    {
        var best = Point3D.Zero;
        var bestDead = 0;
        var closest = int.MaxValue;
        var owned = 0;

        foreach (var (key, owner, _) in BotClaim.Owned())
        {
            if (key.Map != map.MapID || !string.Equals(owner, guild.Name, System.StringComparison.Ordinal))
            {
                continue;
            }

            owned++;

            Consider(map, key.X, key.Y, from, head, guild, ref best, ref bestDead, ref closest);
        }

        if (BotEstate.Hall(guild) is { Deleted: false } hall && hall.Map == map)
        {
            var seat = BotQuad.Key(map, hall.Location);

            for (var dx = -Ring; dx <= Ring; dx++)
            {
                for (var dy = -Ring; dy <= Ring; dy++)
                {
                    var middle = Middle(seat.X + dx, seat.Y + dy);

                    if (BotClaim.Owner(map, middle) != null)
                    {
                        continue;
                    }

                    owned++;

                    Consider(map, seat.X + dx, seat.Y + dy, from, head, guild, ref best, ref bestDead, ref closest);
                }
            }
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

    private static Point3D Middle(int x, int y) => new(x * BotQuad.Side + BotQuad.Side / 2, y * BotQuad.Side + BotQuad.Side / 2, 0);

    private static void Consider(
        Map map, int x, int y, Point3D from, double head, Guild guild, ref Point3D best, ref int bestDead, ref int closest
    )
    {
        var middle = Middle(x, y);

        if (BotQuad.Safety(map, middle) > BotQuad.Dire || BotProwl.Raising(map, middle))
        {
            return;
        }

        var quad = BotQuad.At(map, middle);

        if (quad == null)
        {
            return;
        }

        var at = BotQuad.Stand(quad);

        if (at == Point3D.Zero)
        {
            return;
        }

        if (BotPeril.Overwhelms(map, at, from, BotHarrow.Expected(map, at, head, guild), out _, count: false))
        {
            Overwhelmed++;

            return;
        }

        var away = System.Math.Max(System.Math.Abs(at.X - from.X), System.Math.Abs(at.Y - from.Y));

        if (away >= closest)
        {
            return;
        }

        closest = away;
        best = at;
        bestDead = quad.Deaths;
    }

    public static bool Keeps(Map map, Point3D where)
    {
        if (!Running)
        {
            return false;
        }

        var owner = Warden(map, where);

        if (owner == null || BaseGuild.FindByName(owner) is not Guild guild || Fighters(guild) < BotHarrow.Least)
        {
            return false;
        }

        Left++;

        return true;
    }

    public static string Describe() =>
        !Running
            ? "guilds raise no companies for their own ground"
            : $"{Asked} fighters asked: {Guildless} in no guild, {Unfit} unarmed or novices, {Landless} of a guild with no ground, {Safe} with no square of their own at or below dire ({BotQuad.Dire:F2}), {Held} already in a company, {Hurt} too hurt, {Resting} while the harrow rested, {Unsupplied} short of their supplies, {Few} of a guild with fewer than {BotHarrow.Least} fighters, {Across} round the far side of something by road from their hall, {Overwhelmed} dire squares passed over for a fight {BotPeril.Overwhelm:F1} times the guild's company, {Offered} offered the harrowing of their guild's own square; the Baron left {Left} dire squares to the guild that holds them";

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
        Unsupplied = 0;
        Few = 0;
        Across = 0;
        Overwhelmed = 0;
        Offered = 0;
        Left = 0;
    }
}
