using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a bot with goods the island is asking for a shift keeping shop by Britain's bank — the market — or, for a guild's
/// member, by its guild's own town's bank.
///
/// <para>
/// <b>Asked of anybody, decided by what is in the pack and the bank box.</b> No class is a shopkeeper by birth: a hunter with
/// forty reagents off corpses in the bank, a weaver with bandages cut from its own cloth, a smith with the pieces nobody ordered
/// — each of them is a shop for as long as it holds what somebody is short of. The goods are counted the way the
/// porter counts them (<see cref="BotShopkeep.Look"/>), priced the way a shop will price them, and the shift is offered only
/// when what the island is actually asking for comes to <see cref="BotShopkeep.OpenWorth"/> or more: a shop of bones is not
/// offered at all, and a shop that nobody wants anything from would be a bot standing at a bank for eight minutes.
/// </para>
///
/// <para>
/// <b>Priced at what such a shift sells, and corrected by what it sold.</b> The claim is the demanded stock times the share of
/// it a shift is measured to sell (<see cref="BotShopkeep.Share"/>), over the shift's minutes; the ledger then corrects it by
/// what this bot's shifts at this bank really took, so a keeper whose customers never come stops being offered the shift, and
/// one whose bank is busy is offered it over the dig.
/// </para>
///
/// <para>
/// <b>An offer reserves nothing.</b> The pitch is chosen here so the walk to it is stable, but it is taken only when the keeper
/// is standing on it and opens (<see cref="BotShopkeep.OpenShop"/>): an offer that loses the auction has spoken for nothing.
/// </para>
/// </summary>
public sealed class BotShopkeeper : IBotProposer
{
    public string Name => "Shopkeeper";

    public BotStanding Rung => BotStanding.Free;

    public static long Looks { get; private set; }

    public static long Offered { get; private set; }

    public static long Rested { get; private set; }

    public static long Full { get; private set; }

    public static long Thin { get; private set; }

    public static long Unwanted { get; private set; }

    public static long Outlawed { get; private set; }

    public static long NoBank { get; private set; }

    public static long Crowded { get; private set; }

    public static long NoPitch { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        if (!BotShopkeep.Running || bot?.Self is not BotMobile body || !body.Alive)
        {
            return null;
        }

        var map = body.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        Looks++;

        if (!FightersKeepShop && !Merchant(body))
        {
            Fighters++;

            return null;
        }

        if (BotShopkeep.Resting(body))
        {
            Rested++;

            return null;
        }

        if (BotShopkeep.Spoken >= BotShopkeep.MostOpen)
        {
            Full++;

            return null;
        }

        var stock = BotShopkeep.Look(body);

        if (stock.Worth < BotShopkeep.OpenWorth)
        {
            Thin++;

            return null;
        }

        if (stock.Demanded < BotShopkeep.OpenWorth)
        {
            Unwanted++;

            return null;
        }

        if (body.Kills >= 5 || body.Criminal || BotUnderworld.Member(body))
        {
            Outlawed++;

            return null;
        }

        var bank = Home(map, body);

        if (bank != Point3D.Zero)
        {
            ToHome++;
        }
        else
        {
            bank = Britain(map, body);

            if (bank != Point3D.Zero)
            {
                ToBritain++;
            }
        }

        if (bank == Point3D.Zero)
        {
            NoBank++;

            return null;
        }

        if (!Utility.InRange(body.Location, bank, ShiftReach))
        {
            Afar++;

            return null;
        }

        if (BotShopkeep.Crowded(map, bank, body))
        {
            Crowded++;

            return null;
        }

        var stand = BotShopkeep.Pitch(map, bank, bot);

        if (stand == Point3D.Zero)
        {
            NoPitch++;

            return null;
        }

        Offered++;

        return new BotKeepShop(map, stand, bank, BotShopkeep.Claim(stock.Demanded), stock.Demanded);
    }

    public static bool FightersKeepShop { get; set; } = false;

    public static long Fighters { get; private set; }

    public static bool Merchant(Mobile body) => body is BotMobile { Class: { } klass } && klass.Role == BotRole.Producer;

    public static int BritainReach { get; set; } = 200;

    public static int ShiftReach { get; set; } = 200;

    public static long Afar { get; private set; }

    public static long ToBritain { get; private set; }

    public static long ToHome { get; private set; }

    public static int HomeMs { get; set; } = 60000;

    private static readonly Dictionary<string, (Point3D Bank, long Tick)> _homes = [];

    private static Point3D Home(Map map, Mobile body)
    {
        if (body?.Guild is not Guilds.Guild guild || BotUnderworld.Member(body))
        {
            return Point3D.Zero;
        }

        var now = Core.TickCount;
        var name = guild.Name ?? "";

        if (!_homes.TryGetValue(name, out var known) || now - known.Tick >= HomeMs)
        {
            var town = BotGuildHouses.TownOf(guild);

            if (town == null)
            {
                BotParley.Seat(guild, out town, out _);
            }

            var bank = Point3D.Zero;

            if (town != null && !string.Equals(town.Name, BotCapital.Name, StringComparison.OrdinalIgnoreCase))
            {
                var hearth = BotSettle.Of(town)?.Hearth ?? town.Square;

                bank = hearth == Point3D.Zero ? Point3D.Zero : BotShopkeep.BankNear(map, hearth);
            }

            if (_homes.Count > 256)
            {
                _homes.Clear();
            }

            _homes[name] = known = (bank, now);
        }

        var home = known.Bank;

        if (home == Point3D.Zero || !Utility.InRange(body.Location, home, ShiftReach) || !BotGates.Joined(map, body.Location, home)
            || BotShopkeep.Crowded(map, home, body))
        {
            return Point3D.Zero;
        }

        return home;
    }

    private static Point3D Britain(Map map, Mobile body)
    {
        var town = BotTowns.Find(BotCapital.Name);

        if (town == null)
        {
            return Point3D.Zero;
        }

        var hearth = BotSettle.Of(town)?.Hearth ?? town.Square;
        var bank = hearth == Point3D.Zero ? Point3D.Zero : BotShopkeep.BankNear(map, hearth);

        if (bank == Point3D.Zero || !Utility.InRange(body.Location, bank, BritainReach) || !BotGates.Joined(map, body.Location, bank)
            || BotShopkeep.Crowded(map, bank, body))
        {
            return Point3D.Zero;
        }

        return bank;
    }

    public static string Describe() =>
        Looks == 0
            ? "nobody has been looked at for a shop"
            : $"{Looks} looks for a shop: {Offered} shifts offered, {Fighters} were fighters, who keep no shop, {Thin} held under {BotShopkeep.OpenWorth}gp of goods, "
              + $"{Unwanted} held nothing the island is short of or has money down for, {Rested} were resting from a shift, "
              + $"{Outlawed} were outlaws, {ToBritain} sent to Britain's bank (within {BritainReach} tiles), {ToHome} guild members sent to their guild's own town's bank, {Afar} further than {ShiftReach} tiles from the bank, {NoBank} were near neither Britain nor their guild's town, {Crowded} found the bank already had {BotShopkeep.PerBank} shops, "
              + $"{NoPitch} found no pitch free there, {Full} found the island had {BotShopkeep.MostOpen} open";

    public static void Forget()
    {
        Looks = 0;
        Fighters = 0;
        Offered = 0;
        Rested = 0;
        Full = 0;
        Thin = 0;
        Unwanted = 0;
        Outlawed = 0;
        NoBank = 0;
        ToBritain = 0;
        ToHome = 0;
        _homes.Clear();
        Afar = 0;
        Crowded = 0;
        NoPitch = 0;
    }
}
