using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Five bots underground: what they have taken, whose it is, how many of them may still be put back on
/// their feet, and the promise that every one of them comes home.
///
/// <para>
/// <b>Patrick's order of 11.09.2026 is four rules, and they pull against each other in a way worth being
/// explicit about.</b> Gold is divided at the end, half to the leader and the rest evenly between the other
/// four; a bot that dies with no resurrection left goes back to the world and <i>keeps what it had already
/// accumulated, unchanged</i>; and only those still alive go on accumulating. The first rule alone would be
/// one division at the end, which cannot express the second: at the end there is no record of what anybody
/// had accumulated, because nothing accumulated anything.
/// </para>
///
/// <para>
/// <b>So the pot is kept as it fills.</b> Every beat, whatever a member has picked up since the last one is
/// swept into the pot and credited there and then — half to the leader, the rest evenly between the others
/// still down there. A bot sent home at nine minutes keeps the credit of the first nine minutes exactly as
/// it stood, and the four who fought on divide the last eleven between them. At the end each member is
/// handed its own credit in coin. Read across a whole delve with nobody lost, this is precisely the
/// fifty-fifty split of the first rule; read across one with a loss, it is the second and third.
/// </para>
///
/// <para>
/// <b>Sweeping is also what keeps the money safe.</b> A bot that dies drops its pack on its corpse, and a
/// corpse on the floor of Destard is gold out of the world. What the party has already taken is in the pot,
/// which is a number rather than an item, and nothing underground can kill a number.
/// </para>
///
/// <para>
/// <b>Three resurrections to a party, and the fourth death is the way out.</b> Raised where they fell,
/// because a delver carried home is a delver who has left; the ordinary revival — a minute face down and
/// then home to Britain — is exactly what should happen to the fourth, and it happens by this class doing
/// nothing at all. See <c>BotPopulation.Revive</c>, which asks here first.
/// </para>
///
/// <para>
/// <b>And the one promise that matters more than any of it: everybody comes up.</b> A bot left in a dungeon
/// is a bot with no road home — the block has no walkable connection to the island — so it would stand
/// there failing errands until the world was reloaded. Every party is on a list here, the list is walked on
/// the delve's own beat, and anything that has outlived its delve by <see cref="GraceMs"/> is lifted out
/// whatever state the errand got itself into. The count of those is printed: a backstop that starts doing
/// the ordinary work is a backstop that has become the design, and this project has watched that happen
/// twice.
/// </para>
/// </summary>
public sealed class BotDelveParty
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDelveParty));

    public static int RaiseMs { get; set; } = 10000;

    public static int Raisings { get; set; } = 3;

    public static double LeadersShare { get; set; } = 0.5;

    public static int GraceMs { get; set; } = 60000;

    public static long Delves { get; private set; }

    public static long Taken { get; private set; }

    public static long Paid { get; private set; }

    public static long Raised { get; private set; }

    public static long Lost { get; private set; }

    public static long Stranded { get; private set; }

    private static readonly List<BotDelveParty> _parties = [];

    private sealed class Share
    {
        public BotMobile Who;

        public int Floor;

        public int Credit;

        public bool Down = true;
    }

    private readonly List<Share> _shares = [];

    private readonly BotDungeon.Deep _deep;

    private readonly long _bornTick;

    public BotDelveParty(BotDungeon.Deep deep, BotMobile leader)
    {
        _deep = deep;
        Leader = leader;
        Left = Raisings;
        _bornTick = Core.TickCount;
        _parties.Add(this);
        Delves++;
    }

    public BotMobile Leader { get; }

    public int Left { get; private set; }

    public int Pot { get; private set; }

    public bool Ended { get; private set; }

    public int Down
    {
        get
        {
            var down = 0;

            for (var i = 0; i < _shares.Count; i++)
            {
                if (_shares[i].Down && _shares[i].Who is { Deleted: false })
                {
                    down++;
                }
            }

            return down;
        }
    }

    public void Add(BotMobile bot)
    {
        if (bot is not { Deleted: false } || Holds(bot))
        {
            return;
        }

        _shares.Add(new Share { Who = bot, Floor = Purse(bot) });
    }

    public bool Holds(Mobile bot)
    {
        for (var i = 0; i < _shares.Count; i++)
        {
            if (ReferenceEquals(_shares[i].Who, bot))
            {
                return true;
            }
        }

        return false;
    }

    public void Sweep()
    {
        var took = 0;

        for (var i = 0; i < _shares.Count; i++)
        {
            var share = _shares[i];

            if (share.Who is not { Deleted: false } body)
            {
                continue;
            }

            var purse = Purse(body);

            if (purse < share.Floor)
            {
                share.Floor = purse;

                continue;
            }

            var gain = purse - share.Floor;

            if (gain <= 0 || body.Backpack?.ConsumeTotal(typeof(Gold), gain) != true)
            {
                continue;
            }

            share.Floor = Purse(body);
            took += gain;
        }

        if (took <= 0)
        {
            return;
        }

        Pot += took;
        Taken += took;

        Credit(took);
    }

    private void Credit(int coins)
    {
        var leaders = (int)Math.Round(coins * LeadersShare, MidpointRounding.AwayFromZero);
        var rest = coins - leaders;

        var sharers = 0;

        for (var i = 0; i < _shares.Count; i++)
        {
            if (_shares[i].Down && !ReferenceEquals(_shares[i].Who, Leader) && _shares[i].Who is { Deleted: false })
            {
                sharers++;
            }
        }

        var each = sharers == 0 ? 0 : rest / sharers;

        var leftover = rest - each * sharers;

        for (var i = 0; i < _shares.Count; i++)
        {
            var share = _shares[i];

            if (ReferenceEquals(share.Who, Leader))
            {
                share.Credit += leaders + leftover;

                continue;
            }

            if (share.Down && share.Who is { Deleted: false })
            {
                share.Credit += each;
            }
        }
    }

    public bool Fell(BotMobile bot)
    {
        var share = Of(bot);

        if (share == null || !share.Down)
        {
            return false;
        }

        if (Left <= 0)
        {
            share.Down = false;
            Lost++;

            logger.Information(
                "{Bot} has fallen for the last time in {Deep}: the party has no raisings left, so it goes back to the world with {Credit}gp of the pot",
                bot.Name,
                _deep?.Name ?? "the deep",
                share.Credit
            );

            return false;
        }

        if (Core.TickCount - (bot.FellTick + RaiseMs) < 0)
        {
            return true;
        }

        bot.Resurrect();

        if (!bot.Alive)
        {
            share.Down = false;

            return false;
        }

        Left--;
        Raised++;

        logger.Information(
            "{Bot} is back on its feet in {Deep} where it fell; the party has {Left} raisings left",
            bot.Name,
            _deep?.Name ?? "the deep",
            Left
        );

        return true;
    }

    public void Settle(string why)
    {
        if (Ended)
        {
            return;
        }

        Ended = true;

        Sweep();

        var handed = 0;

        for (var i = 0; i < _shares.Count; i++)
        {
            var share = _shares[i];

            if (share.Who is not { Deleted: false } body)
            {
                continue;
            }

            if (share.Credit > 0 && body.Backpack != null)
            {
                body.Backpack.DropItem(new Gold(share.Credit));
                handed += share.Credit;
                Paid += share.Credit;
            }

            if (share.Down || (_deep != null && _deep.Holds(body.Location)))
            {
                Surface(body);
            }

            share.Down = false;
        }

        logger.Information(
            "The delve into {Deep} is over — {Why}: {Pot}gp taken, {Handed}gp handed out between {Count}, {Left} raisings unspent",
            _deep?.Name ?? "the deep",
            why,
            Pot,
            handed,
            _shares.Count,
            Left
        );

        _parties.Remove(this);
    }

    private static void Surface(BotMobile bot)
    {
        if (bot is not { Deleted: false })
        {
            return;
        }

        BotPopulation.Carry(bot);
        bot.Journey?.Finish();
    }

    public int Owed(Mobile bot) => Of(bot)?.Credit ?? 0;

    private Share Of(Mobile bot)
    {
        for (var i = 0; i < _shares.Count; i++)
        {
            if (ReferenceEquals(_shares[i].Who, bot))
            {
                return _shares[i];
            }
        }

        return null;
    }

    private static int Purse(Mobile bot) => bot?.Backpack?.GetAmount(typeof(Gold)) ?? 0;

    public static bool Raise(BotMobile bot)
    {
        for (var i = 0; i < _parties.Count; i++)
        {
            if (_parties[i].Holds(bot))
            {
                return _parties[i].Fell(bot);
            }
        }

        return false;
    }

    public static bool Delving(Mobile bot)
    {
        for (var i = 0; i < _parties.Count; i++)
        {
            if (_parties[i].Holds(bot) && !_parties[i].Ended)
            {
                return true;
            }
        }

        return false;
    }

    public static int Inside(BotDungeon.Deep deep)
    {
        var count = 0;

        for (var i = 0; i < _parties.Count; i++)
        {
            if (!_parties[i].Ended && ReferenceEquals(_parties[i]._deep, deep))
            {
                count++;
            }
        }

        return count;
    }

    public static void Watch()
    {
        var now = Core.TickCount;

        for (var i = _parties.Count - 1; i >= 0; i--)
        {
            var party = _parties[i];

            var gone = party.Leader is not { Deleted: false };
            var overrun = now - (party._bornTick + BotDelve.CapMs + GraceMs) >= 0;

            if (!gone && !overrun)
            {
                continue;
            }

            var down = party.Down;

            if (down > 0)
            {
                Stranded += down;

                logger.Warning(
                    "A party of {Down} was still in {Deep} {Minutes} minutes after going down, and {Why}; they have been lifted out",
                    down,
                    party._deep?.Name ?? "a dungeon",
                    (now - party._bornTick) / 60000,
                    gone ? "their leader is gone" : "nothing had ended the delve"
                );
            }

            party.Settle(gone ? "its leader is gone" : "it outlived its own delve");

            _parties.Remove(party);
        }
    }

    public static string Describe() =>
        Delves == 0
            ? "nobody has been down a dungeon"
            : $"{Delves} parties went down, {_parties.Count} are down now: {Taken}gp swept into their pots and {Paid}gp handed out, "
            + $"{Raised} raised where they fell out of {Raisings} to a party, {Lost} went home for want of one, "
            + $"{Stranded} had to be lifted out";

    public static void Forget()
    {
        _parties.Clear();
        Delves = 0;
        Taken = 0;
        Paid = 0;
        Raised = 0;
        Lost = 0;
        Stranded = 0;
    }
}
