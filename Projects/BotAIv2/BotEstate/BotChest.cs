using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// The guild's chest: what the guild's ground earns, and what it pays for before any member's pack is asked.
///
/// <para>
/// <b>A claim was a cost and never an income.</b> A guild levied its members for a square, held it, and got
/// nothing back but a pin on the map and a war when somebody else wanted it; the ground was hunted by everybody
/// and paid the holder nothing. Patrick's fourth Quad idea of 16.09.2026, taken that evening: hunting on ground a
/// guild holds pays the holder a tithe, and the chest pays the guild's next claim or hall before any member's pack
/// is asked (<see cref="BotEstate.Levy"/>). The reeve (<see cref="BotReeve"/>) is the other half of the same
/// idea — the guild's own company for the guild's own ground.
/// </para>
///
/// <para>
/// Only coin that actually arrived in a pack is tithed (<see cref="BotTakings.Coin"/>), only from work that braves
/// the ground (<see cref="BotDeed.Braves"/>: hunts, prowls, bands, harrows, plunder), and only when the pack still
/// has it; otherwise nothing is taken and nothing is owed. The holder's own members pay the same tithe — into their
/// own chest. Kept across restarts by <see cref="BotChestStore"/>; wiped with the claims on a reset.
/// </para>
/// </summary>
public static class BotChest
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotChest));

    public static bool Running { get; set; } = true;

    public static double Rate { get; set; } = 0.1;

    private static readonly Dictionary<string, int> _chests = new(StringComparer.Ordinal);

    public static long Tithes { get; private set; }

    public static long TitheGold { get; private set; }

    public static long Draws { get; private set; }

    public static long DrawnGold { get; private set; }

    public static int Holds(string guild) => guild != null && _chests.TryGetValue(guild, out var gold) ? gold : 0;

    public static void Tithe(Mobile body, Map map, Point3D where, int coin)
    {
        if (!Running || coin <= 0 || map == null || map == Map.Internal || body?.Backpack is not { } pack)
        {
            return;
        }

        var owner = BotLand.Holder(map, where);

        if (owner == null)
        {
            return;
        }

        var own = body.Guild?.Name == owner;
        var rate = own ? Rate : BotToll.Rate(owner, body);

        if (rate <= 0.0)
        {
            if (!own)
            {
                BotToll.Owed(owner, body);
            }

            return;
        }

        var share = (int)(coin * rate);

        if (share <= 0 || !pack.ConsumeTotal(typeof(Gold), share))
        {
            return;
        }

        _chests[owner] = Holds(owner) + share;

        if (own)
        {
            Tithes++;
            TitheGold += share;
        }
        else
        {
            BotToll.Paid(owner, body, share);
        }
    }

    public static int Draw(string guild, int want)
    {
        if (!Running || want <= 0)
        {
            return 0;
        }

        var held = Holds(guild);

        if (held <= 0)
        {
            return 0;
        }

        var drawn = Math.Min(held, want);

        _chests[guild] = held - drawn;
        Draws++;
        DrawnGold += drawn;

        logger.Information("{Guild}'s chest paid {Gold}gp of the {Want}gp asked of the guild; {Left}gp left in it", guild, drawn, want, held - drawn);

        return drawn;
    }

    public static long Thefts { get; private set; }

    public static long StolenGold { get; private set; }

    public static int Steal(string guild, int want)
    {
        var held = Holds(guild);

        if (!Running || want <= 0 || held <= 0)
        {
            return 0;
        }

        var taken = Math.Min(held, want);

        _chests[guild] = held - taken;
        Thefts++;
        StolenGold += taken;

        return taken;
    }

    internal static void Save(IGenericWriter writer)
    {
        writer.WriteEncodedInt(_chests.Count);

        foreach (var (guild, gold) in _chests)
        {
            writer.Write(guild);
            writer.WriteEncodedInt(gold);
        }
    }

    internal static int Load(IGenericReader reader)
    {
        _chests.Clear();

        var count = reader.ReadEncodedInt();

        for (var i = 0; i < count; i++)
        {
            var guild = reader.ReadString();
            var gold = reader.ReadEncodedInt();

            if (!string.IsNullOrEmpty(guild) && gold > 0)
            {
                _chests[guild] = gold;
            }
        }

        return _chests.Count;
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "no tithes are taken";
        }

        using var say = ValueStringBuilder.Create(256);

        say.Append($"{Tithes} tithes of {TitheGold}gp paid to the guilds holding the ground hunted ({Rate:P0} of the coin), {Draws} draws of {DrawnGold}gp on the chests; chests: ");

        if (_chests.Count == 0)
        {
            say.Append("none hold anything");

            return say.ToString();
        }

        var first = true;

        foreach (var (guild, gold) in _chests)
        {
            if (!first)
            {
                say.Append(", ");
            }

            first = false;
            say.Append(guild);
            say.Append(' ');
            say.Append(gold);
            say.Append("gp");
        }

        return say.ToString();
    }

    public static void Wipe() => _chests.Clear();

    public static void Forget()
    {
        Tithes = 0;
        TitheGold = 0;
        Draws = 0;
        DrawnGold = 0;
    }
}
