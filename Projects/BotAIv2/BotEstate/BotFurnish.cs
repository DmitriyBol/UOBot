using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Multis;

namespace Server.BotAI.V2;

public static class BotFurnish
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotFurnish));

    public static bool Running { get; set; } = true;

    public static int BedPrice { get; set; } = 800;

    public static double EveryHours { get; set; } = 6.0;

    public static long Bought { get; private set; }

    public static long Spent { get; private set; }

    public static long NoRoom { get; private set; }

    public static long NotHome { get; private set; }

    public static long Short { get; private set; }

    private static readonly Dictionary<string, long> _tried = new(StringComparer.Ordinal);

    public static void Tick(long now)
    {
        if (!Running)
        {
            return;
        }

        foreach (var house in BotAbode.Houses)
        {
            if (house is not { Deleted: false } || house.Map == null || house.Map == Map.Internal)
            {
                continue;
            }

            if (house.Owner is not BotMobile owner || owner.Deleted || !owner.Alive || string.IsNullOrEmpty(owner.Name))
            {
                continue;
            }

            if (HasBed(house))
            {
                continue;
            }

            if (_tried.TryGetValue(owner.Name, out var tried)
                && now - tried < Math.Max(1.0, EveryHours) * 3600000)
            {
                continue;
            }

            if (!house.IsInside(owner) && (house.BanLocation == Point3D.Zero || !owner.InRange(house.BanLocation, 2)))
            {
                NotHome++;

                continue;
            }

            if (BotYield.Spendable(owner) < BedPrice + BotAbode.Keep)
            {
                Short++;

                continue;
            }

            var bed = BotFittings.InstallOwned(house, static () => new ElvenBedSouthAddon())
                      ?? BotFittings.InstallOwned(house, static () => new ElvenBedEastAddon());

            _tried[owner.Name] = now;

            if (bed == null)
            {
                NoRoom++;

                continue;
            }

            if (!BotMarket.Charge(owner, BedPrice, out _))
            {

                house.Addons?.Remove(bed);
                bed.Delete();
                Short++;

                continue;
            }

            BotYield.Aside(owner, BedPrice);
            Bought++;
            Spent += BedPrice;

            logger.Information("{Owner} has furnished {House} with a bed for {Price}gp", owner.Name, house.Sign?.Name ?? "its house", BedPrice);

            BotEvents.Post("abode", owner, $"furnished its house with a bed for {BedPrice}gp");
            BotCampTalk.Say(owner, "abode:furnished", null, null, null);

            return;
        }
    }

    public static bool HasBed(BaseHouse house)
    {
        if (house.Addons == null)
        {
            return false;
        }

        for (var i = 0; i < house.Addons.Count; i++)
        {
            if (house.Addons[i] is { Deleted: false } addon && addon.GetType().Name.Contains("Bed", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "furnishing is off";
        }

        var bedded = 0;
        var standing = 0;

        foreach (var house in BotAbode.Houses)
        {
            if (house is not { Deleted: false })
            {
                continue;
            }

            standing++;

            if (HasBed(house))
            {
                bedded++;
            }
        }

        return $"furnishing: {Bought} beds bought for {Spent}gp, {bedded} of {standing} houses have a bed, {NoRoom} houses with nowhere a bed fits, {NotHome} looks with the owner out, {Short} short of the price";
    }

    public static void Forget()
    {
        _tried.Clear();
        Bought = 0;
        Spent = 0;
        NoRoom = 0;
        NotHome = 0;
        Short = 0;
    }
}
