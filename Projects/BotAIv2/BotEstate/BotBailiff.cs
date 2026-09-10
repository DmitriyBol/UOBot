using System.Collections.Generic;
using Server.Guilds;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Whether anybody is standing on this guild's land who should not be, and who is going to say so.
///
/// <para>
/// <b>Rare by construction rather than by a dial.</b> Three things have to be true at once before this ever
/// offers anything: the bot is on its own guild's land, somebody of another guild is on it too and within
/// sight, and the guild already thinks poorly enough of theirs to bother. Without the third condition an
/// island of five guilds would spend its afternoon telling each other to move along, which is not a
/// population, it is a queue.
/// </para>
///
/// <para>
/// <b>And it is one bot at a time per trespasser, not per guild.</b> The other three officers claim their
/// guild because their errand is the guild's — one hall, one shopkeeper. This errand is about a person, and
/// two members telling the same stranger to move along is the shard shouting. See <c>BotOffice</c> for the
/// pattern and why the claim is short.
/// </para>
/// </summary>
public sealed class BotBailiff : IBotProposer
{
    /// <summary>How long a claim on a trespasser lasts after the bot holding it was last heard from.</summary>
    public static int ClaimMs { get; set; } = 60000;

    /// <summary>
    /// How poorly the guild must already think of theirs before anybody is told to move.
    ///
    /// <para>
    /// Below nought, which means the border drift or an earlier trespass has to have happened first. A guild
    /// with no opinion of its neighbours does not police its yard, so the first minutes after a hall goes up
    /// are quiet and the quarrel builds out of something rather than arriving with the building.
    /// </para>
    /// </summary>
    public static double Minding { get; set; } = -3.0;

    /// <summary>How far a member looks for somebody who should not be there.</summary>
    public static int Watch { get; set; } = 12;

    /// <summary>Bots looked at.</summary>
    public static long Asked { get; private set; }

    /// <summary>Evictions offered.</summary>
    public static long Offered { get; private set; }

    /// <summary>Times the bot was not on its own guild's land, which is most of them.</summary>
    public static long Elsewhere { get; private set; }

    /// <summary>Times nobody was trespassing within sight.</summary>
    public static long Quiet { get; private set; }

    /// <summary>Times somebody was, and the guild did not mind them enough to say anything.</summary>
    public static long Tolerated { get; private set; }

    /// <summary>Times somebody else was already dealing with them.</summary>
    public static long Claimed { get; private set; }

    private static readonly Dictionary<Serial, long> _claims = [];

    public string Name => "bailiff";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        if (!BotEstate.Running || !BotLand.Running || !BotRegard.Running)
        {
            return null;
        }

        if (bot?.Self is not BotMobile { Deleted: false } body || body.Map == null || body.Map == Map.Internal)
        {
            return null;
        }

        if (body.Guild is not Guild ours || BotEstate.Standing == 0)
        {
            return null;
        }

        Asked++;

        // On its own land, and that is asked of where the bot is standing rather than of where its hall is:
        // a member across the island is not keeping anybody's yard.
        if (BotLand.Holder(body.Map, body.Location) != ours.Name)
        {
            Elsewhere++;

            return null;
        }

        BotMobile worst = null;
        var lowest = Minding;
        var anybody = false;

        // <b>One sweep, and the two questions asked of it together.</b> The first cut asked a second time
        // whether anybody was there at all, which doubled the cost of the commonest answer — an area query
        // per bot per beat, for a rule that fires almost never.
        foreach (var near in body.GetMobilesInRange<BotMobile>(Watch))
        {
            if (near == body || near.Deleted || near.Guild is not Guild theirs || theirs == ours)
            {
                continue;
            }

            // On our land specifically, not merely near us: a member of ours standing at the edge of its own
            // yard can see well past it.
            if (BotLand.Holder(near.Map, near.Location) != ours.Name)
            {
                continue;
            }

            anybody = true;

            var held = BotRegard.Of(ours.Name, theirs.Name);

            if (held > lowest)
            {
                continue;
            }

            worst = near;
            lowest = held;
        }

        if (worst == null)
        {
            // Told apart, because "nobody was there" and "somebody was there and we did not mind" are the
            // two halves of whether this rule is doing anything at all, and one nought for both would hide
            // a threshold set so low that nothing ever fires.
            if (anybody)
            {
                Tolerated++;
            }
            else
            {
                Quiet++;
            }

            return null;
        }

        var now = Core.TickCount;

        if (_claims.TryGetValue(worst.Serial, out var until) && now - until < 0)
        {
            Claimed++;

            return null;
        }

        Offered++;
        _claims[worst.Serial] = now + BotOffice.OfferedMs;

        return new BotEvict(worst, ours.Name);
    }

    /// <summary>The errand is alive and still on it.</summary>
    public static void Hold(Mobile them)
    {
        if (them != null)
        {
            _claims[them.Serial] = Core.TickCount + ClaimMs;
        }
    }

    /// <summary>The errand is over, however it went.</summary>
    public static void Release(Mobile them)
    {
        if (them != null)
        {
            _claims.Remove(them.Serial);
        }
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been looked at for keeping a guild's yard"
            : $"the bailiff looked {Asked} times and sent {Offered}: {Elsewhere} were not on their own land, {Quiet} saw nobody on it, "
              + $"{Tolerated} saw somebody and did not mind them enough, {Claimed} found somebody already dealing with it; {BotEvict.Describe()}";

    public static void Forget()
    {
        Asked = 0;
        Offered = 0;
        Elsewhere = 0;
        Quiet = 0;
        Tolerated = 0;
        Claimed = 0;
        _claims.Clear();
        BotEvict.Forget();
    }
}
