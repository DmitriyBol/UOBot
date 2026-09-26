using System.Collections.Generic;
using Server.Guilds;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Who decides the guild can afford a hall, and offers to go and raise one.
///
/// <para>
/// <b>Any member may go and raise it, and the guild's own claim is what stops two of them doing it.</b>
/// This was written leader-only at first, on the reasoning that one bot cannot race itself — and it was a
/// veto wearing a preference's clothes. The Crown could pay for a hall from its first minute and never
/// bought one: its leader is the Captain, the Captain joins a company, and a bot on the <c>Bound</c> rung
/// has its auction switched off entirely. Six minutes of a running shard with 32 bots busy, 17 bound and
/// none free went by with the money in hand and nobody able to be offered the work. A guild of two whose
/// leader is in a company is a guild that can never build anything.
/// </para>
///
/// <para>
/// So the offer goes to whoever of the guild is choosing work, and the claim below keeps it to one at a
/// time: a claim expires by the clock rather than by an event, because the bot holding it may die, drop
/// the errand or be taken into a company, and none of those three sends anybody a message.
/// </para>
///
/// <para>
/// <b>What it can afford is asked here rather than in the work.</b> An undertaking that says it needs five
/// thousand gold makes its bot feel destitute — <c>BotDeed.Outlay</c> is what poverty is measured against —
/// and the money is not the bot's anyway. So the affordability test lives on this side of the offer, where
/// it costs the bot nothing to fail.
/// </para>
///
/// <para>
/// The plot search is throttled because it is the expensive half: every candidate is a full
/// <c>HousePlacement.Check</c>, and this is asked on the beat of every free leader on the shard.
/// </para>
/// </summary>
public sealed class BotSteward : IBotProposer
{
    public static int LookMs { get; set; } = 2000;

    public static double LookAt { get; set; } = 0.5;

    public static int ClaimMs { get; set; } = 180000;

    public static long Offered { get; private set; }

    public static long Groundless { get; private set; }

    public static long Waiting { get; private set; }

    private static long _next;

    private static bool _looked;

    public string Name => "steward";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        if (!BotEstate.Running || bot?.Self is not BotMobile { Deleted: false } body)
        {
            return null;
        }

        if (body.Map == null || body.Map == Map.Internal || body.Guild is not Guild guild)
        {
            return null;
        }

        if (BotUnderworld.Band(guild))
        {
            return null;
        }

        if (BotEstate.Hall(guild) != null)
        {
            return null;
        }

        if (BotEstate.Standing >= BotEstate.MaxHalls)
        {
            return null;
        }

        var fund = BotEstate.Fund(guild);

        if (fund < BotEstate.Price * LookAt)
        {
            return null;
        }

        var now = Core.TickCount;

        if (Claimed(guild, now))
        {
            return null;
        }

        if (_looked && now - _next < 0)
        {
            return null;
        }

        _looked = true;
        _next = now + LookMs;

        if (!BotPlot.Find(body, BotSeat.Of(guild), out var plot))
        {
            Groundless++;

            return null;
        }

        if (fund < BotEstate.Price)
        {
            Waiting++;

            return null;
        }

        Offered++;
        BotOffice.Offering(Office, guild);

        return new BotHall(guild, body.Map, plot);
    }

    public const string Office = "steward";

    private static bool Claimed(Guild guild, long now) => BotOffice.Busy(Office, guild);

    public static void Hold(Guild guild) => BotOffice.Hold(Office, guild, ClaimMs);

    public static void Release(Guild guild) => BotOffice.Release(Office, guild);

    public static string Describe() =>
        $"the steward offered a hall {Offered} times, held off {Waiting} times with the ground in hand and the money not, and found nothing to build on {Groundless} times";

    public static void Forget()
    {
        Offered = 0;
        Groundless = 0;
        Waiting = 0;
        _next = 0;
        _looked = false;
    }
}
