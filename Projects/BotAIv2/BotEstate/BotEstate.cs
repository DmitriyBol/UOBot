using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;
using Server.Items;
using Server.Mobiles;
using Server.Multis;

namespace Server.BotAI.V2;

/// <summary>
/// The halls the guilds own: where they stand, who paid for them, and what they hold.
///
/// <para>
/// <b>Ordered by Patrick on 08.09.2026, and the shape of the order is the interesting part.</b> The plan
/// said "a bot buys a house". The arithmetic said otherwise: the cheapest classic house in this engine costs
/// 35,250gp (<c>HousePlacementEntry.ClassicHouses</c>), the fattest purse on this shard was 843gp when the
/// question was asked, the middle one 355gp, and the whole population of forty-nine held under 25,000gp
/// between them — with <c>BotProgress.Savings</c> off by his own order of the same day, so none of it
/// survives a restart. A house per bot at the engine's price is a threshold nothing on the shard can reach:
/// the exact defect this project keeps meeting, two numbers on one shelf that never meet.
/// </para>
///
/// <para>
/// So the guild buys it. Four halls rather than forty-nine huts, paid for out of a levy on the members, and
/// the guild tag stops being a word over a head: it is the thing that opens a door, holds a chest, and owns
/// the forge inside. That is what makes <see cref="BotGuilds"/> worth having.
/// </para>
///
/// <para>
/// <b>A hall outlives the population that raised it, and that is what this class is really for.</b> Bots are
/// rebuilt from nothing every start and their owner mobile is deleted with them; the house is a world object
/// and stays. An unowned house in this engine decays. So every start this reads the world back — houses are
/// found by the name on their sign, which is the guild's — and hands each one to whoever leads that guild
/// now. The sign is the record. Nothing else is written down, because anything else could disagree with the
/// world and this cannot.
/// </para>
/// </summary>
public static class BotEstate
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotEstate));

    public static bool Running { get; set; } = true;

    public static int Price { get; set; } = 5000;

    public static int Keep { get; set; } = 300;

    public static int MaxHalls { get; set; } = 4;

    public static bool Merchanting { get; set; } = true;

    public static int MostMerchants { get; set; } = 1;

    public static long Merchanted { get; private set; }

    public static long Contracts { get; private set; }

    private static readonly Dictionary<string, BaseHouse> _halls = new();

    public static long Raised { get; private set; }

    public static long Adopted { get; private set; }

    public static long Levied { get; private set; }

    public static long Payers { get; private set; }

    public static long Failed { get; private set; }

    public static long Fitted { get; private set; }

    public static long Razed { get; private set; }

    public static long Barred { get; private set; }

    public static bool MayUse(Mobile bot, Map map, Point3D where)
    {
        if (!Running || bot == null || map == null || map == Map.Internal)
        {
            return true;
        }

        var house = BaseHouse.FindHouseAt(where, map, 16);

        if (house is not { Deleted: false })
        {
            return true;
        }

        return house.IsOwner(bot) || house.IsCoOwner(bot) || house.IsFriend(bot);
    }

    public static void Bar() => Barred++;

    public static BaseHouse Hall(Guild guild)
    {
        if (guild == null || !_halls.TryGetValue(guild.Name, out var house))
        {
            return null;
        }

        if (house is { Deleted: false })
        {
            return house;
        }

        _halls.Remove(guild.Name);

        return null;
    }

    public static BaseHouse Hall(Mobile bot) => Hall(bot?.Guild as Guild);

    public static IEnumerable<BaseHouse> Halls => _halls.Values;

    public static Guild Whose(BaseHouse hall) => BaseGuild.FindByName(WhoseName(hall)) as Guild;

    public static string WhoseName(BaseHouse hall)
    {
        if (hall is not { Deleted: false })
        {
            return null;
        }

        foreach (var (name, house) in _halls)
        {
            if (house == hall)
            {
                return name;
            }
        }

        return null;
    }

    public static Dictionary<string, BaseHouse> Held => _halls;

    public static int Standing => _halls.Count;

    public static int Fund(Guild guild)
    {
        if (guild?.Members == null)
        {
            return 0;
        }

        var total = 0;

        for (var i = 0; i < guild.Members.Count; i++)
        {
            if (guild.Members[i] is not BotMobile { Deleted: false } member)
            {
                continue;
            }

            var spare = BotYield.Wealth(member) - Keep;

            if (spare > 0)
            {
                total += spare;
            }
        }

        return total;
    }

    /// <summary>What one member paid, kept so it can be handed back if the purchase falls over.</summary>
    public readonly struct Contribution
    {
        public Contribution(BotMobile who, int paid)
        {
            Who = who;
            Paid = paid;
        }

        public BotMobile Who { get; }

        public int Paid { get; }
    }

    public static int Levy(Guild guild, int want, List<Contribution> paid)
    {
        var got = Take(guild, want, paid);

        Levied += got;

        return got;
    }

    public static int Tax(Guild guild, int want)
    {
        var got = Take(guild, want, null);

        Taxed += got;

        return got;
    }

    public static long Taxed { get; private set; }

    public static long Taxpayers { get; private set; }

    private static int Take(Guild guild, int want, List<Contribution> paid)
    {
        if (guild?.Members == null || want <= 0)
        {
            return 0;
        }

        var members = new List<BotMobile>();

        for (var i = 0; i < guild.Members.Count; i++)
        {
            if (guild.Members[i] is BotMobile { Deleted: false } member)
            {
                members.Add(member);
            }
        }

        members.Sort((a, b) => BotYield.Wealth(b).CompareTo(BotYield.Wealth(a)));

        var got = 0;

        for (var i = 0; i < members.Count && got < want; i++)
        {
            var member = members[i];
            var pack = member.Backpack;

            if (pack == null)
            {
                continue;
            }

            var spare = Math.Min(BotYield.Wealth(member) - Keep, want - got);

            if (spare <= 0)
            {
                continue;
            }

            var carried = pack.GetAmount(typeof(Gold));
            var fromPack = Math.Min(carried, spare);
            var fromBank = spare - fromPack;

            if (fromPack > 0 && !pack.ConsumeTotal(typeof(Gold), fromPack))
            {
                continue;
            }

            if (fromBank > 0 && !Banker.Withdraw(member, fromBank))
            {
                if (fromPack > 0)
                {
                    pack.DropItem(new Gold(fromPack));
                }

                continue;
            }

            got += spare;
            paid?.Add(new Contribution(member, spare));

            if (paid == null)
            {
                Taxpayers++;
            }
            else
            {
                Payers++;
            }
        }

        return got;
    }

    public static void Refund(List<Contribution> paid)
    {
        if (paid == null)
        {
            return;
        }

        for (var i = 0; i < paid.Count; i++)
        {
            var back = paid[i];

            if (back.Who is { Deleted: false } && back.Paid > 0)
            {
                Banker.Deposit(back.Who, back.Paid);
                Levied -= back.Paid;
                Payers--;
            }
        }
    }

    public static void Register(Guild guild, BaseHouse house, bool bought)
    {
        if (guild == null || house is not { Deleted: false })
        {
            return;
        }

        _halls[guild.Name] = house;

        if (bought)
        {
            Raised++;
        }
        else
        {
            Adopted++;
        }
    }

    public static void Lost() => Failed++;

    public static void Fit(int many) => Fitted += Math.Max(0, many);

    public static int Merchants(BaseHouse hall)
    {
        var many = 0;

        if (hall?.PlayerVendors == null)
        {
            return 0;
        }

        for (var i = 0; i < hall.PlayerVendors.Count; i++)
        {
            if (hall.PlayerVendors[i] is { Deleted: false })
            {
                many++;
            }
        }

        return many;
    }

    public static void Hired(BaseHouse hall, Mobile merchant, int paid)
    {
        Merchanted++;
        Contracts += paid;

        logger.Information(
            "A merchant now stands in {Guild} at {X},{Y}; the contract cost {Paid}gp",
            hall?.Sign?.Name ?? "a hall",
            merchant?.X ?? 0,
            merchant?.Y ?? 0,
            paid
        );
    }

    public static void Adopt()
    {
        if (!Running)
        {
            return;
        }

        var houses = BaseHouse.AllHouses;

        for (var i = 0; i < houses.Count; i++)
        {
            var house = houses[i];

            if (house is not { Deleted: false })
            {
                continue;
            }

            var named = house.Sign?.Name;

            if (string.IsNullOrEmpty(named))
            {
                continue;
            }

            var guild = BotGuilds.Find(named);

            if (guild == null || _halls.ContainsKey(guild.Name))
            {
                continue;
            }

            Take(guild, house);

            var moved = BotFittings.Tidy(house);

            if (moved > 0)
            {
                logger.Information(
                    "{Moved} things standing outside the hall of {Guild} were moved into the room",
                    moved,
                    guild.Name
                );
            }

            Register(guild, house, false);
        }
    }

    public static void Take(Guild guild, BaseHouse house)
    {
        if (guild == null || house is not { Deleted: false })
        {
            return;
        }

        if (guild.Leader is BotMobile { Deleted: false } leader)
        {
            house.Owner = leader;
        }

        house.CoOwners ??= new List<Mobile>();
        house.CoOwners.Clear();

        if (guild.Members != null)
        {
            for (var i = 0; i < guild.Members.Count; i++)
            {
                if (guild.Members[i] is BotMobile { Deleted: false } member && member != house.Owner)
                {
                    house.CoOwners.Add(member);
                }
            }
        }

        house.Public = true;

        if (house.Sign != null)
        {
            house.Sign.Name = guild.Name;
        }

        Unlock(house);
        house.RefreshDecay();

        for (var i = 0; i < house.PlayerVendors.Count; i++)
        {
            if (house.PlayerVendors[i] is { Deleted: false } merchant && guild.Leader is BotMobile keeper)
            {
                merchant.Owner = keeper;
            }
        }
    }

    public static void Unlock(BaseHouse house)
    {
        if (house?.Doors == null)
        {
            return;
        }

        for (var i = 0; i < house.Doors.Count; i++)
        {
            if (house.Doors[i] is BaseDoor door)
            {
                door.Locked = false;
            }
        }
    }

    public static int Raze()
    {
        var gone = 0;

        foreach (var house in new List<BaseHouse>(_halls.Values))
        {
            if (house is { Deleted: false })
            {
                house.Delete();
                gone++;
            }
        }

        _halls.Clear();
        Razed += gone;

        return gone;
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "the population buys no halls";
        }

        var say = Server.Text.ValueStringBuilder.Create(384);
        var wanting = Server.Text.ValueStringBuilder.Create(256);

        try
        {
            var standing = 0;

            foreach (var (name, house) in _halls)
            {
                if (house is not { Deleted: false })
                {
                    continue;
                }

                if (standing++ > 0)
                {
                    say.Append(", ");
                }

                say.Append(name);
                say.Append(" at ");
                say.Append(house.X.ToString());
                say.Append(',');
                say.Append(house.Y.ToString());
            }

            var many = 0;

            foreach (var guild in BotGuilds.Standing)
            {
                if (Hall(guild) != null)
                {
                    continue;
                }

                var fund = Fund(guild);

                if (many++ > 0)
                {
                    wanting.Append(", ");
                }

                wanting.Append(guild.Name);
                wanting.Append(" has ");
                wanting.Append(fund.ToString());
                wanting.Append(" of ");
                wanting.Append(Price.ToString());
                if (fund < Price)
                {
                    wanting.Append(" and is ");
                    wanting.Append((Price - fund).ToString());
                    wanting.Append(" short");
                }
                else
                {
                    wanting.Append(" and can pay");
                }
            }

            var halls = standing == 0 ? "no halls stand" : $"{standing} halls stand: {say.ToString()}";

            var ledger =
                $"{Raised} raised and {Adopted} taken back from the save, {Failed} purchases lost, {Fitted} fittings set up, {Levied}gp levied off {Payers} members and {Taxed}gp taken off {Taxpayers} in tax";

            var owed = wanting.Length == 0 ? "every guild has one" : wanting.ToString();

            return
                $"{halls}; {ledger}; {owed}; {BotSteward.Describe()}; {BotFitter.Describe()}; {BotHirer.Describe()}; {Merchanted} merchants hired for {Contracts}gp; {BotSupplier.Describe()}; {BotOffice.Describe()}; {BotShelf.Describe()}; {BotLand.Describe()}; {BotRegard.Describe()}; {BotBailiff.Describe()}; {BotFeuder.Describe()}; {BotExile.Describe()}; {BotRemover.Describe()}; {BotHolder.Describe()}; {Barred} times a workshop was passed over as somebody else's; {BotPlot.Describe()}";
        }
        finally
        {
            wanting.Dispose();
            say.Dispose();
        }
    }

    public static void Forget()
    {
        _halls.Clear();
        Raised = 0;
        Adopted = 0;
        Levied = 0;
        Payers = 0;
        Failed = 0;
        Fitted = 0;
        Razed = 0;
        Barred = 0;
        Taxed = 0;
        Taxpayers = 0;
        Merchanted = 0;
        Contracts = 0;
    }
}
