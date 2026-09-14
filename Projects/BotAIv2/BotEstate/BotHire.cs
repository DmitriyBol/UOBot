using Server.Items;
using Server.Mobiles;
using Server.Multis;

namespace Server.BotAI.V2;

/// <summary>
/// Hiring a merchant for the guild's hall: buy the contract in town, walk it home, and set the shopkeeper up.
///
/// <para>
/// <b>Patrick's order of 09.09.2026, and he had the engine right.</b> A contract of employment is sold over a
/// counter by bankers, barkeepers and innkeepers — <c>SBBanker</c>, <c>SBBarkeeper</c>, <c>SBInnKeeper</c> —
/// at 1,252gp. So the price is the engine's own and not ours, which is the rule everywhere on this shard
/// except the workbenches, where no engine price exists at all.
/// </para>
///
/// <para>
/// <b>The engine's own conditions are met rather than worked around.</b> <c>ContractOfEmployment</c> asks for
/// four things — the contract in the pack, a house at the bot's feet, the bot a friend of it, and the house
/// public — and a guild hall satisfies every one: our halls are public and make every member a co-owner,
/// which <c>BaseHouse.IsFriend</c> accepts. The one thing this does differently from a player double-clicking
/// the contract is that it walks the bot into the room first, because "a house at the bot's feet" means the
/// bot has to be standing in it.
/// </para>
///
/// <para>
/// Who may use it is the hall's question and already answered: a merchant stands inside a guild hall, and
/// <see cref="BotEstate.MayUse"/> refuses anything in there to anybody outside the guild — the same rule,
/// word for word, as the one that keeps a forge private.
/// </para>
/// </summary>
public sealed class BotHire : BotDeed
{
    public const string Trade = "hire";

    public static double Prior { get; set; } = 300.0;

    public static double WorkMinutes { get; set; } = 1.0;

    public static int Reach => BotShops.CounterReach;

    private readonly Guilds.Guild _guild;

    private readonly BaseHouse _hall;

    private readonly BaseVendor _shop;

    private readonly int _price;

    private bool _bought;

    private bool _hired;

    public BotHire(Guilds.Guild guild, BaseHouse hall, BaseVendor shop, int price)
    {
        _guild = guild;
        _hall = hall;
        _shop = shop;
        _price = price;
    }

    public override string Kind => Trade;

    public override Map Map => _hall?.Map;

    public override Point3D Where => _bought ? _hall?.Location ?? Point3D.Zero : _shop?.Location ?? Point3D.Zero;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override bool AtCounter => _shop != null && !_bought;

    public override double Coin => 0.0;

    public override bool Unpaid => true;

    public override int Made => _mine;

    private int _mine;

    public static int TrekLimit { get; set; } = 200;

    private int _nearest = int.MaxValue;

    private int _stalled;

    private bool Closing(Mobile body, Point3D at)
    {
        var gap = System.Math.Max(System.Math.Abs(body.X - at.X), System.Math.Abs(body.Y - at.Y));

        if (gap < _nearest)
        {
            _nearest = gap;
            _stalled = 0;

            return true;
        }

        return ++_stalled < TrekLimit;
    }

    private void Fresh()
    {
        _nearest = int.MaxValue;
        _stalled = 0;
    }

    public override string Stage =>
        _hired ? $"hired a merchant for {_guild?.Name}"
            : _bought ? $"carrying a contract to the hall of {_guild?.Name}"
            : $"to {_shop?.Name} for a contract of employment";

    public override bool Bend(IBotWilful bot) => false;

    public override void Drop(IBotWilful bot) => BotHirer.Release(_guild);

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _guild == null || _hall is not { Deleted: false })
        {
            return BotDoing.Failed("the hall is gone");
        }

        BotHirer.Hold(_guild);

        if (BotEstate.Merchants(_hall) >= BotEstate.MostMerchants)
        {
            return BotDoing.Failed($"{_guild.Name} has all the merchants it may have");
        }

        var pack = body.Backpack;

        if (pack == null)
        {
            return BotDoing.Failed("no pack to carry a contract in");
        }

        var contract = pack.FindItemByType<ContractOfEmployment>();

        if (contract == null)
        {
            if (_shop is not { Deleted: false } || _shop.Map == null)
            {
                return BotDoing.Failed("the shopkeeper is gone");
            }

            if (!body.InRange(_shop.Location, Reach))
            {
                if (!Closing(body, _shop.Location))
                {
                    bot?.Resolve?.Ledger?.Beware(BotShops.ShopKind, _shop.Map, _shop.Location);

                    return BotDoing.Failed($"the walk to {_shop.Name} stopped closing {_nearest} tiles short");
                }

                return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(Reach), $"to {_shop.Name} for a contract");
            }

            var wealth = BotYield.Wealth(body);
            var mine = wealth;

            if (wealth < _price && !BotGuilds.Stand(body as BotMobile, _price - wealth))
            {
                return BotDoing.Failed($"{_guild.Name} could not raise {_price}gp for a contract");
            }

            if (BotShops.Buy(bot, _shop, typeof(ContractOfEmployment), 1, out var refused) <= 0)
            {
                return BotDoing.Failed(refused ?? "the shopkeeper would not sell a contract");
            }

            _mine += System.Math.Max(0, mine - BotYield.Wealth(body));

            _bought = true;

            Fresh();

            return BotDoing.Work("a contract of employment bought");
        }

        _bought = true;

        if (!BotFittings.InRoom(_hall, body.Location))
        {
            var spot = BotFittings.Spot(_hall);

            if (spot == Point3D.Zero)
            {
                return BotDoing.Failed($"there is nowhere in the hall of {_guild.Name} to stand a merchant");
            }

            if (!Closing(body, spot))
            {
                return BotDoing.Failed($"the walk into the hall of {_guild.Name} stopped closing {_nearest} tiles short");
            }

            return BotDoing.Walk(_hall.Map, spot, BotArrival.Exactly, $"into the hall of {_guild.Name}");
        }

        if (!_hall.Public || !_hall.CanPlaceNewVendor())
        {
            return BotDoing.Failed("the hall will not take a merchant");
        }

        BaseHouse.IsThereVendor(body.Location, body.Map, out var standing, out var rental);

        if (standing || rental)
        {
            return BotDoing.Failed("something is already standing on that spot");
        }

        var merchant = new PlayerVendor(body, _hall)
        {
            Direction = body.Direction & Direction.Mask
        };

        merchant.MoveToWorld(body.Location, body.Map);
        merchant.SayTo(body, 503246);

        contract.Delete();

        BotEstate.Hired(_hall, merchant, _price);
        _hired = true;

        return BotDoing.Done($"a merchant stands in the hall of {_guild.Name}, {_price}gp for the contract");
    }
}
