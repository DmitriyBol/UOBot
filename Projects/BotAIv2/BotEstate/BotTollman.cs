using System.Collections.Generic;
using Server.Guilds;

namespace Server.BotAI.V2;

public sealed class BotTollman : IBotProposer
{

    public static int Reach { get; set; } = 160;

    public static int ClaimMs { get; set; } = 30000;

    public static int ShunMs { get; set; } = 300000;

    public static long Asked { get; private set; }

    public static long Offered { get; private set; }

    public static long Nobody { get; private set; }

    public static long Far { get; private set; }

    public static long Claimed { get; private set; }

    public static long Passed { get; private set; }

    private static readonly Dictionary<Serial, long> _claims = [];

    private static readonly Dictionary<(Serial Bot, Serial Them), long> _unreached = [];

    public string Name => "tollman";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        if (!BotToll.Running || bot?.Self is not BotMobile { Deleted: false, Alive: true } body || body.Map == null
            || body.Map == Map.Internal)
        {
            return null;
        }

        if (body.Guild is not Guild ours || BotUnderworld.Band(ours) || body.Class is not { } klass || klass.Role == BotRole.Producer)
        {
            return null;
        }

        if (BotToll.Way(ours.Name) != BotTollWay.Patrolled)
        {
            return null;
        }

        Asked++;

        var them = BotToll.Nearest(ours.Name, body);

        if (them == null)
        {
            Nobody++;

            return null;
        }

        if (!body.InRange(them.Location, Reach))
        {
            Far++;

            return null;
        }

        var now = Core.TickCount;

        if (_claims.TryGetValue(them.Serial, out var until) && now - until < 0)
        {
            Claimed++;

            return null;
        }

        if (_unreached.TryGetValue((body.Serial, them.Serial), out var shunned) && now - shunned < 0)
        {
            Passed++;

            return null;
        }

        Offered++;
        _claims[them.Serial] = now + BotOffice.OfferedMs;

        return new BotWard(them, ours.Name);
    }

    public static void Hold(Mobile them)
    {
        if (them != null)
        {
            _claims[them.Serial] = Core.TickCount + ClaimMs;
        }
    }

    public static void Release(Mobile them)
    {
        if (them != null)
        {
            _claims.Remove(them.Serial);
        }
    }

    public static void Unreached(Mobile bot, Mobile them)
    {
        if (bot != null && them != null)
        {
            _unreached[(bot.Serial, them.Serial)] = Core.TickCount + ShunMs;
        }
    }

    public static string Describe() =>
        Asked == 0
            ? "no warden has been asked"
            : $"{Asked} fighters of patrolling guilds asked, {Offered} sent to a stranger, {Nobody} found nobody on the land, "
              + $"{Far} too far, {Claimed} already spoken for, {Passed} passed over after a walk that failed; {BotWard.Describe()}";

    public static void Forget()
    {
        Asked = 0;
        Offered = 0;
        Nobody = 0;
        Far = 0;
        Claimed = 0;
        Passed = 0;
        _claims.Clear();
        _unreached.Clear();
        BotWard.Forget();
    }
}
