using System.Collections.Generic;
using Server.Guilds;

namespace Server.BotAI.V2;

public sealed class BotSailing
{
    public enum Way
    {

        Out,

        Home
    }

    public enum Tide
    {
        Mustering,
        Sailing,
        Landed,
        Over
    }

    public Guild Guild;

    public BotMobile Captain;

    public Way Heading;

    public bool Founding;

    public Map Map;

    public BotDock From;

    public BotDock To;

    public Point3D Goal;

    public BotColony Colony;

    public int Wanted;

    public int Least;

    public readonly List<Mobile> Roster = [];

    public readonly List<Mobile> Aboard = [];

    public Tide State;

    public long Began;

    public long AtDock;

    public string Why;

    public bool AtHand(Mobile m) =>
        m is { Deleted: false, Alive: true } && Captain is { Deleted: false } && m.Map == Captain.Map
        && m.InRange(Captain.Location, System.Math.Max(2, BotHelm.CrewReach - 2));

    public int Present()
    {
        var count = 0;

        for (var i = 0; i < Roster.Count; i++)
        {
            if (Roster[i] != Captain && AtHand(Roster[i]))
            {
                count++;
            }
        }

        return count;
    }

    public bool Signed(Mobile m) => m != null && Roster.Contains(m);

    public override string ToString() =>
        $"{Guild?.Name}'s {(Heading == Way.Out ? Founding ? "expedition" : "relief" : "run home")} under {Captain?.Name} from {From?.Name} to {To?.Name}, {State}";
}
