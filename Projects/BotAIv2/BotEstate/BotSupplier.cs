using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Items;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Whether the guild's counter is short of something its members keep running out of, and who is going to
/// fetch it.
///
/// <para>
/// The fourth of the estate's officers and the same shape as the other three — any member may go, one at a
/// time per guild, and what the guild can afford is asked here rather than inside the errand. The order that
/// falls out of the costs is a hall, then the benches its trade needs, then a shopkeeper, then something for
/// the shopkeeper to sell; nothing enforces it, and nothing needs to.
/// </para>
///
/// <para>
/// <b>It stocks what has actually been asked for.</b> <see cref="BotShopper"/> writes down every shortage it
/// meets, so this reads a measurement rather than a list: whatever the population has most often reached for
/// and not found is what the guild's shelf gets. That is the difference between a store cupboard and a
/// warehouse of things nobody wanted, which is what the first sketch of this would have built — the ignored
/// stalls, by construction the goods the whole island has already refused.
/// </para>
///
/// <para>
/// <b>And it must never become a second faucet.</b> Nothing here sells to a shopkeeper; it only buys, at the
/// shelf price, exactly as a member would have. So the errand is gold-neutral against the alternative and
/// the only thing it changes is how far somebody walks. See <c>BotPeddle</c>, which is the shard's one
/// faucet and stays that way.
/// </para>
/// </summary>
public sealed class BotSupplier : IBotProposer
{
    public static int ClaimMs { get; set; } = 240000;

    public static int Shelf { get; set; } = 60;

    public static int Batch { get; set; } = 20;

    public static int Spend { get; set; } = 240;

    public static int Tools { get; set; } = 10;

    public static int Enough(Type kind) =>
        kind != null && typeof(IUsesRemaining).IsAssignableFrom(kind) ? Tools : Shelf;

    private static readonly Dictionary<Type, double> _stones = [];

    public static int Fits(Mobile body, Type kind)
    {
        if (body == null || kind == null)
        {
            return 0;
        }

        var room = (BotLadder.Ceiling(body) - BotLadder.Load(body)) * 0.75;

        if (room <= 0.0)
        {
            return 0;
        }

        if (!_stones.TryGetValue(kind, out var each))
        {
            var sample = kind.CreateInstance<Item>();

            each = sample?.Weight ?? 1.0;
            sample?.Delete();

            each = Math.Max(0.1, each);
            _stones[kind] = each;
        }

        return (int)(room / each);
    }

    public static int Considered { get; set; } = 8;

    public static long Asked { get; private set; }

    public static long Offered { get; private set; }

    public static long Shopless { get; private set; }

    public static long Claimed { get; private set; }

    public static long Stocked { get; private set; }

    public static long Unsold { get; private set; }

    public static long Wanting { get; private set; }

    public static int Nearest { get; private set; }

    public static int Short { get; private set; }

    public static long Unmeasured { get; private set; }

    public static long Laden { get; private set; }

    public static long Below { get; private set; }

    public static long Trimmed { get; private set; }

    public static long Spared { get; private set; }

    public static long Cramped { get; private set; }

    public static long Closed { get; private set; }

    public static void Shut() => Closed++;

    public const string Office = "supplier";

    public string Name => "supplier";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        if (!BotEstate.Running || !BotEstate.Merchanting || bot?.Self is not BotMobile { Deleted: false } body)
        {
            return null;
        }

        if (body.Map == null || body.Map == Map.Internal || body.Guild is not Guild guild)
        {
            return null;
        }

        var hall = BotEstate.Hall(guild);

        if (hall is not { Deleted: false } || hall.Map != body.Map)
        {
            return null;
        }

        Asked++;

        if (BotDungeon.Under(body.Location))
        {
            Below++;

            return null;
        }

        if (BotLadder.Load(body) >= BotLadder.Ceiling(body))
        {
            Laden++;

            return null;
        }

        var merchant = BotShelf.Of(hall);

        if (merchant == null)
        {
            Shopless++;

            return null;
        }

        var now = Core.TickCount;

        if (BotOffice.Busy(Office, guild))
        {
            Claimed++;

            return null;
        }

        var wants = BotShopper.Shortages();
        var dry = BotShops.Dry();

        for (var i = 0; i < dry.Count; i++)
        {
            wants.Add(dry[i]);
        }

        wants.Sort((a, b) => b.Times.CompareTo(a.Times));

        if (wants.Count == 0)
        {
            Unmeasured++;

            return null;
        }

        BotShops.Survey(body.Map, body.Location);

        var looked = Math.Min(Considered, wants.Count);
        var full = 0;
        var unsold = 0;
        var poor = 0;
        var cramped = 0;

        for (var i = 0; i < looked; i++)
        {
            var kind = wants[i].Kind;

            var held = kind == null ? 0 : BotShelf.Held(merchant, kind);

            if (kind == null || held >= Enough(kind))
            {
                full++;

                continue;
            }

            if (!BotShelf.Room(merchant, kind))
            {
                cramped++;

                continue;
            }

            var shop = BotShops.Nearest(bot, kind);

            if (shop == null)
            {
                unsold++;

                continue;
            }

            var price = BotShops.Price(shop, kind);

            if (price <= 0)
            {
                unsold++;

                continue;
            }

            var wanted = Math.Max(1, Enough(kind) - held);
            var carriable = Math.Max(1, Fits(body, kind));
            var asked = Math.Min(Math.Min(Spend / price, Batch), Math.Min(wanted, carriable));
            var batch = Math.Max(1, asked);

            if (batch < Batch)
            {
                Trimmed++;
                Spared += Batch - batch;
            }

            var fund = BotEstate.Fund(guild);

            if (fund < batch * price)
            {
                poor++;

                if (fund > Nearest)
                {
                    Nearest = fund;
                    Short = batch * price - fund;
                }

                continue;
            }

            Offered++;
            BotOffice.Offering(Office, guild);

            return new BotSupply(guild, hall, shop, kind, batch, price);
        }

        if (cramped > 0 && cramped >= full && cramped >= unsold && cramped >= poor)
        {
            Cramped++;
        }
        else if (full >= unsold && full >= poor)
        {
            Stocked++;
        }
        else if (unsold >= poor)
        {
            Unsold++;
        }
        else
        {
            Wanting++;
        }

        return null;
    }

    public static void Hold(Guild guild) => BotOffice.Hold(Office, guild, ClaimMs);

    public static void Release(Guild guild) => BotOffice.Release(Office, guild);

    public static string Describe() =>
        Asked == 0
            ? "nobody has been looked at for a supply run"
            : $"the supplier looked {Asked} times and sent {Offered}: {Shopless} guilds had no counter, {Claimed} already had somebody on it, "
              + $"{Stocked} shelves held enough, {Unsold} wanted what nobody within reach sells, {Wanting} could not raise a batch"
              + (Wanting > 0 ? $" (the best-off had {Nearest}gp free and was {Short} short)" : "")
              + $", {Unmeasured} asked before the population had run short of anything, {Laden} could not have carried it, {Below} were underground, "
              + $"{Cramped} found the shelf with no room left for it ({Closed} more found it full on arrival at the shop)"
              + $"; {Trimmed} lots came back smaller than a packet, {Spared} units the shelf or the courier had no room for";

    public static void Forget()
    {
        Trimmed = 0;
        Spared = 0;
        Cramped = 0;
        Closed = 0;
        Asked = 0;
        Offered = 0;
        Shopless = 0;
        Claimed = 0;
        Stocked = 0;
        Unsold = 0;
        Wanting = 0;
        Unmeasured = 0;
        Nearest = 0;
        Short = 0;
        Laden = 0;
        Below = 0;
    }
}
