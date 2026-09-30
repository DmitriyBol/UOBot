using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Items;
using Server.Mobiles;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// A guild's dues: the share of what its members' work brings in that goes into the guild's chest, and what the chest keeps
/// back from everyday spending while the guild saves for its hall or its house in Britain.
///
/// <para>
/// <b>Why no hall had been raised since the wipe of 29.09.2026, measured before anything was changed.</b> A guild's money was
/// never a store. <c>BotEstate.Fund</c> was the sum, at the moment of asking, of what each member held above
/// <c>BotEstate.Keep</c> (300gp), and it did not count the chest at all. On the day of the wipe the middling purse read
/// 19 to 242gp in all 207 money lines, 68gp at the median (19:53: "poorest 0gp, middling 27gp"), so hardly a member ever
/// stood above the keep — and the
/// few who did were emptied again within minutes by <c>BotGuilds.Stand</c>, which draws on exactly the same spare, richest
/// first, down to the same 300, to pay for other members' armour and, since build 259, their compulsory supplies. Fifty-three
/// of the day's 207 money lines name a fattest purse of exactly 300gp; one session stood 29,374gp for members (16:04:31). Two
/// claimants on one pool, and the one that spends every minute always wins against the one that has to wait for five
/// thousand. The chest, the one thing that does keep, is fed only by tithes on held ground, ground is a hall's yard or a
/// claim, and a claim is offered only to a guild with a hall — 1,366 of 1,366 looks at 19:53 "had no hall to want ground
/// near". A ring of three gates, and the Estate line printed "0 of 5000" for every guild all day.
/// </para>
///
/// <para>
/// <b>The repair is a store, not a bigger number.</b> <see cref="Share"/> of the coin a member's settled work brought in (the
/// takings' own <c>Coin</c>, pack, bank and escrow, the same figure the ledger learns from) is paid into the guild's chest
/// the moment the work is settled — before the next purchase or the next stand can reach it — less whatever that work
/// already tithed to the same chest on the guild's own ground. Nothing is minted: the coin moves from a member to its guild,
/// and a hall's price leaves the world when the hall is bought, as it always has. It is booked aside for the member
/// (<c>BotYield.Aside</c>), so no piece of work is taught that dues are its cost. The chest now counts in <c>Fund</c>, which
/// every purchase's gate asks; and while the guild has no hall — or is saving for a house in Britain — the chest keeps that
/// price back from the everyday draws (<c>BotProvision.Fund</c>, a ship's price), which still go to the members as before.
/// </para>
///
/// <para>
/// <b>What it costs the members, in the day's numbers.</b> Settled work brought in some 14,000 to 17,500gp an hour across
/// the population on 29.09.2026 (the positive coin of every finished line: 14,176/h 17:38–18:38, 17,569/h 19:12–19:36), about
/// nine tenths of it to guild members (86 of 96 bots at 19:53). A fifth is then some 2,500 to 3,100gp an hour into seven
/// chests — a guild of fifteen reaches the hall's 5,000 in nine to eleven hours, a guild of six in about a day. That is a
/// judgement, not an accident: the dial is <see cref="Share"/> and the price is <c>BotEstate.Price</c>, and the
/// "Treasuries:" line prints what each chest gained since the start, so the rate can be read before either is moved.
/// </para>
/// </summary>
public static class BotDues
{
    public static bool Running { get; set; } = true;

    public static double Share { get; set; } = 0.2;

    public static int Ceiling { get; set; } = 20000;

    public static bool Saving { get; set; } = true;

    public static long Paid { get; private set; }

    public static long Payments { get; private set; }

    public static long Full { get; private set; }

    public static long Unpaid { get; private set; }

    public static long Kept { get; private set; }

    private static readonly Dictionary<string, long> _paidBy = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, long> _keptBy = new(StringComparer.OrdinalIgnoreCase);

    public static int Pay(Mobile body, int coin, int tithed)
    {
        if (!Running || Share <= 0.0 || coin <= 0 || body is not BotMobile { Deleted: false } bot || bot.Guild is not Guild guild)
        {
            return 0;
        }

        if (BotUnderworld.Band(guild))
        {
            return 0;
        }

        var held = BotChest.Holds(guild.Name);

        if (held >= Ceiling)
        {
            Full++;

            return 0;
        }

        var want = Math.Min((int)(coin * Share) - Math.Max(0, tithed), Ceiling - held);

        if (want <= 0)
        {
            return 0;
        }

        var pack = bot.Backpack;
        var fromPack = pack == null ? 0 : Math.Min(pack.GetAmount(typeof(Gold)), want);

        if (fromPack > 0 && !pack.ConsumeTotal(typeof(Gold), fromPack))
        {
            fromPack = 0;
        }

        var fromBank = Math.Min(want - fromPack, Banker.GetBalance(bot));

        if (fromBank > 0 && !Banker.Withdraw(bot, fromBank))
        {
            fromBank = 0;
        }

        var paid = fromPack + fromBank;

        if (paid <= 0)
        {
            Unpaid++;

            return 0;
        }

        BotChest.Put(guild.Name, paid);

        BotYield.Aside(bot, paid);

        Paid += paid;
        Payments++;
        _paidBy[guild.Name] = _paidBy.GetValueOrDefault(guild.Name) + paid;

        return paid;
    }

    public static int Goal(Guild guild, out string what)
    {
        what = null;

        if (!Saving || guild == null)
        {
            return 0;
        }

        if (BotEstate.Running && BotEstate.Hall(guild) == null && !BotUnderworld.Band(guild))
        {
            what = "the hall";

            return BotEstate.Price;
        }

        var britain = BotGuildHouses.Saving(guild);

        if (britain > 0)
        {
            what = "a house in Britain";

            return britain;
        }

        return 0;
    }

    public static int Spare(Guild guild)
    {
        if (guild == null)
        {
            return 0;
        }

        var holds = BotChest.Holds(guild.Name);
        var keep = Math.Min(Goal(guild, out _), (int)(holds * Math.Clamp(SaveShare, 0.0, 1.0)));

        return Math.Max(0, holds - keep);
    }

    public static double SaveShare { get; set; } = 0.75;

    public static void Keep(Guild guild, int asked, int spare)
    {
        var kept = Math.Min(asked, BotChest.Holds(guild?.Name)) - spare;

        if (guild == null || kept <= 0)
        {
            return;
        }

        Kept += kept;
        _keptBy[guild.Name] = _keptBy.GetValueOrDefault(guild.Name) + kept;
    }

    public static string Describe()
    {
        using var say = ValueStringBuilder.Create(1024);

        say.Append(
            Running
                ? $"dues of {Share:P0} on the coin members' work brings in: {Paid}gp paid in {Payments} times, {Unpaid} owed and already spent, {Full} not taken for a chest at {Ceiling}gp; "
                : "no dues are taken; "
        );

        say.Append($"chests kept {Kept}gp back from everyday draws for what their guilds save for; members stood {BotGuilds.Stood}gp for each other out of the same spare the hall asks for ({BotGuilds.Cannot} times they could not)");

        foreach (var guild in BotGuilds.Standing)
        {
            if (guild is not { Disbanded: false } || BotUnderworld.Band(guild))
            {
                continue;
            }

            var above = 0;
            var members = 0;
            var richest = 0;

            for (var i = 0; i < guild.Members.Count; i++)
            {
                if (guild.Members[i] is not BotMobile { Deleted: false } member)
                {
                    continue;
                }

                members++;

                var wealth = BotYield.Wealth(member);

                if (wealth > BotEstate.Keep)
                {
                    above++;
                }

                richest = Math.Max(richest, wealth);
            }

            var chest = BotChest.Holds(guild.Name);
            var spare = BotEstate.Spare(guild);
            var goal = Goal(guild, out var what);
            var hall = BotEstate.Hall(guild);
            var house = BotGuildHouses.Of(guild.Name);
            var seat = BotSeat.Of(guild);

            say.Append("; ");
            say.Append(guild.Name);
            say.Append($": chest {chest}gp + members' spare {spare}gp ({above} of {members} above the {BotEstate.Keep}gp keep, the richest {richest}gp)");

            if (goal > 0)
            {
                say.Append($" = {chest + spare} of {what}'s {goal}gp");
            }

            say.Append($", dues {_paidBy.GetValueOrDefault(guild.Name)}gp in, {_keptBy.GetValueOrDefault(guild.Name)}gp kept back; ");
            say.Append(
                house != null
                    ? $"house in {house.Town}"
                    : seat != Point3D.Zero
                        ? $"seat at {seat.X},{seat.Y}"
                        : "no seat"
            );
            say.Append(hall is { Deleted: false } ? $", hall at {hall.X},{hall.Y}" : ", no hall");
            say.Append($", {BotClaim.Holds(guild.Name)} squares");
        }

        return say.ToString();
    }

    public static void Forget()
    {
        Paid = 0;
        Payments = 0;
        Full = 0;
        Unpaid = 0;
        Kept = 0;
        _paidBy.Clear();
        _keptBy.Clear();
    }
}
