using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Items;
using Server.Mobiles;
using Server.Text;

namespace Server.BotAI.V2;

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

    public static long UnderKeep { get; private set; }

    public static long UnderKeepGold { get; private set; }

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

        var room = BotYield.Spendable(bot) - BotEstate.Keep;

        if (want > 0 && room < want)
        {
            UnderKeep++;
            UnderKeepGold += want - Math.Max(0, room);
            want = Math.Max(0, room);
        }

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

        BotElection.Gave(bot, paid);

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

        if (BotPurpose.Goal(guild, out var voted, out var price))
        {
            what = voted;

            return price;
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
                ? $"dues of {Share:P0} on the coin members' work brings in, only above the {BotEstate.Keep}gp keep: {Paid}gp paid in {Payments} times, {UnderKeep} held back ({UnderKeepGold}gp) for a member at or under the keep, {Unpaid} owed and already spent, {Full} not taken for a chest at {Ceiling}gp; "
                : "no dues are taken; "
        );

        say.Append($"chests kept {Kept}gp back from everyday draws for what their guilds save for; members stood {BotGuilds.Stood}gp for each other out of the same spare the hall asks for ({BotGuilds.Cannot} times they could not)");

        say.Append("; ");
        say.Append(BotPurpose.Describe());

        say.Append("; ");
        say.Append(BotClaim.DescribeMoney());

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

                var wealth = BotYield.Spendable(member);

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
        UnderKeep = 0;
        UnderKeepGold = 0;
        _paidBy.Clear();
        _keptBy.Clear();
    }
}
