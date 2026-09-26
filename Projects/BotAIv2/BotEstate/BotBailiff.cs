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
    public static int ClaimMs { get; set; } = 60000;

    public static double Minding { get; set; } = -3.0;

    public static int Watch { get; set; } = 12;

    public static long Asked { get; private set; }

    public static long Offered { get; private set; }

    public static long Elsewhere { get; private set; }

    public static long Quiet { get; private set; }

    public static long Tolerated { get; private set; }

    public static long Claimed { get; private set; }

    public static int ShunMs { get; set; } = 300000;

    public static long Passed { get; private set; }

    private static readonly Dictionary<Serial, long> _claims = [];

    public static int ToldMs { get; set; } = 300000;

    public static long Warned { get; private set; }

    private static readonly Dictionary<Serial, long> _told = [];

    public static void Told(Mobile them)
    {
        if (them != null)
        {
            _told[them.Serial] = Core.TickCount + ToldMs;
        }
    }

    private static readonly Dictionary<(Serial Bot, Serial Them), long> _unreached = [];

    private static readonly List<(Serial Bot, Serial Them)> _lapsed = [];

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

        if (BotUnderworld.Band(ours))
        {
            return null;
        }

        Asked++;

        if (BotLand.Holder(body.Map, body.Location) != ours.Name)
        {
            Elsewhere++;

            return null;
        }

        BotMobile worst = null;
        var lowest = Minding;
        var anybody = false;
        var passed = false;
        var looked = Core.TickCount;

        foreach (var near in body.GetMobilesInRange<BotMobile>(Watch))
        {
            if (near == body || near.Deleted || near.Guild is not Guild theirs || theirs == ours)
            {
                continue;
            }

            if (BotLand.Holder(near.Map, near.Location) != ours.Name)
            {
                continue;
            }

            if (_unreached.TryGetValue((body.Serial, near.Serial), out var shunned) && looked - shunned < 0)
            {
                Passed++;
                passed = true;

                continue;
            }

            if (_told.TryGetValue(near.Serial, out var told) && looked - told < 0)
            {
                Warned++;
                passed = true;

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
            if (anybody)
            {
                Tolerated++;
            }
            else if (!passed)
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
        if (bot == null || them == null)
        {
            return;
        }

        var now = Core.TickCount;

        if (_unreached.Count >= 256)
        {
            foreach (var (key, until) in _unreached)
            {
                if (now - until >= 0)
                {
                    _lapsed.Add(key);
                }
            }

            for (var i = 0; i < _lapsed.Count; i++)
            {
                _unreached.Remove(_lapsed[i]);
            }

            _lapsed.Clear();
        }

        _unreached[(bot.Serial, them.Serial)] = now + ShunMs;
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been looked at for keeping a guild's yard"
            : $"the bailiff looked {Asked} times and sent {Offered}: {Elsewhere} were not on their own land, {Quiet} saw nobody on it, "
              + $"{Tolerated} saw somebody and did not mind them enough, {Claimed} found somebody already dealing with it, {Passed} passed over somebody they had lately failed to get near, {Warned} passed over somebody already told and minded; {BotEvict.Describe()}";

    public static void Forget()
    {
        Asked = 0;
        Offered = 0;
        Elsewhere = 0;
        Quiet = 0;
        Tolerated = 0;
        Claimed = 0;
        Passed = 0;
        Warned = 0;
        _claims.Clear();
        _unreached.Clear();
        _told.Clear();
        BotEvict.Forget();
    }
}
