using System;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a bot with an axe a trip to the woods, and only when somebody wants the wood.
///
/// <para>
/// <b>Gated on demand rather than on an empty pack, and that is the difference between a supply line and a
/// woodpile.</b> Nothing on this island eats logs by itself: they exist to become shafts, and shafts exist
/// to become arrows. So the question this proposer asks is not "am I short of wood" but "is anybody asking
/// for wood, or for something made of it" — which is the board, and the board is where the archer's own
/// want lands when the provisioner's twenty arrows are not enough.
/// </para>
///
/// <para>
/// That makes the whole of Patrick's chain one loop of reading and answering, with nobody told anything: an
/// archer runs low and posts arrows; a fletcher reads arrows and finds it is short of wood; this reads the
/// same board and sends somebody to a tree; the hunters read the feather want the fletcher raises and go
/// looking for birds. Four trades, one board, no messages.
/// </para>
/// </summary>
public sealed class BotWoodsman : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotWoodsman));

    private static bool _said;

    public static long Asked { get; private set; }

    public static long NoAxe { get; private set; }

    public static long NoCall { get; private set; }

    public static long Stocked { get; private set; }

    public static long NoTree { get; private set; }

    public static long Sent { get; private set; }

    public string Name => "Woodsman";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (BotTimber.Tool(body) == null || BotTimber.System == null)
        {
            NoAxe++;

            return null;
        }

        Asked++;

        if (!Fletching(body) && !Wanted(typeof(Log)) && !Wanted(typeof(Arrow)))
        {
            NoCall++;

            return null;
        }

        if (BotTimber.Logs(body) >= BotTimber.Worthwhile)
        {
            Stocked++;

            return null;
        }

        var tree = BotTimber.Find(body);

        if (tree == null)
        {
            NoTree++;

            return null;
        }

        Sent++;
        Once(body);

        return new BotChop(map, new Point3D(tree.X, tree.Y, tree.Z), BotTimber.Worthwhile - BotTimber.Logs(body));
    }

    private static bool Fletching(Mobile body) =>
        BotFletching.Kit(body) != null
        && BotFletching.Feathers(body) > 0
        && BotFletching.Logs(body) + BotFletching.Shafts(body) < BotFletching.LeastArrows;

    private static bool Wanted(Type kind)
    {
        var wants = BotAuction.Wants;

        for (var i = 0; i < wants.Count; i++)
        {
            if (wants[i].IsOpen && wants[i].Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    private static void Once(Mobile body)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information(
            "{Name} is the first bot on this shard ever to cut wood; until now Lumberjacking was a skill nobody had an errand for",
            body.Name
        );
    }

    public static string Describe() =>
        Asked == 0
            ? $"nobody has been offered wood ({NoAxe} answers went to bots with no axe)"
            : $"{Asked} asked to cut wood: {Sent} sent to a tree, {NoCall} found nobody asking for wood or arrows, {Stocked} were carrying enough already, {NoTree} had no tree within {BotTimber.Reach} tiles ({BotTimber.Townbound} passed over for standing inside a town and {BotTimber.Fenced} on ground that has refused the population); {BotChop.Spoken} trees given up because the engine said they were cut out against {BotChop.Silent} given up by the clock alone, {BotChop.Unreached} for the engine calling every swing out of range; "
              + $"{BotTimber.Ordered} logs went straight into somebody's order and {BotTimber.Listed} onto a stall, above the {BotTimber.Keeps} a cutter that can fletch keeps back";

    public static void Forget()
    {
        Asked = 0;
        NoAxe = 0;
        NoCall = 0;
        Stocked = 0;
        NoTree = 0;
        BotTimber.ForgetTrade();
        Sent = 0;
    }
}
