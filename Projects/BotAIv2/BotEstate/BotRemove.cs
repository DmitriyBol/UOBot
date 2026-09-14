using System.Collections.Generic;
using Server.Guilds;
using Server.Items;
using Server.Mobiles;
using Server.Multis;

namespace Server.BotAI.V2;

/// <summary>
/// Carrying a beaten guild's hall out of the winner's yard and putting it down again.
///
/// <para>
/// <b>The only thing on this shard that takes a building down.</b> The engine has no way to move a house —
/// a player demolishes and places again — so this does exactly that, in that order, and the order matters:
/// the ground is proved first, and the old hall only comes down once the new one has somewhere to go.
/// A guild left with neither is a guild that lost a war and its home, which is not what was asked for.
/// </para>
///
/// <para>
/// <b>What the move costs is the fittings, and that is the whole of the punishment.</b> The old hall's
/// merchant, its counter and everything standing on it go with the building; the new hall is furnished from
/// scratch by <c>BotFittings</c> and its shelf starts empty. No levy is taken — a guild that has just lost a
/// war is not asked for five thousand gold as well, and a punishment that can be refused for want of money
/// is not a punishment.
/// </para>
///
/// <para>
/// <b>Nothing is paid for and nothing is earned</b>, so this is <c>Unpaid</c>. See <c>BotBaron</c> for the
/// twelve minutes a bot once stood still because unpaid work was marked down for being unpaid.
/// </para>
/// </summary>
public sealed class BotRemove : BotDeed
{
    public const string Trade = "remove";

    public static double Prior { get; set; } = 400.0;

    public static double WorkMinutes { get; set; } = 2.0;

    public static int Reach => BotHall.Reach;

    public static long Done { get; private set; }

    public static long Refused { get; private set; }

    public static long Seated { get; private set; }

    public static long MerchantsCarried { get; private set; }

    private readonly Guild _guild;

    private readonly Map _map;

    private readonly Point3D _plot;

    private readonly bool _seating;

    private int _fitted;

    public BotRemove(Guild guild, Map map, Point3D plot) : this(guild, map, plot, false)
    {
    }

    public BotRemove(Guild guild, Map map, Point3D plot, bool seating)
    {
        _guild = guild;
        _map = map;
        _plot = plot;
        _seating = seating;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _plot;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override bool Unpaid => true;

    public override string Stage =>
        _seating
            ? $"carrying the hall of {_guild?.Name} to its seat, to {_plot.X},{_plot.Y}"
            : $"carrying the hall of {_guild?.Name} out to {_plot.X},{_plot.Y}";

    public override bool Bend(IBotWilful bot) => false;

    public override void Drop(IBotWilful bot) => BotOffice.Release(BotExile.Office, _guild);

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _guild == null)
        {
            return BotDoing.Failed("no body");
        }

        if (_seating ? !BotSeat.Moving(_guild, out _, out _) : BotExile.Owed(_guild) is not { Deleted: false })
        {
            return BotDoing.Failed($"{_guild.Name} owes nobody a move any more");
        }

        var old = BotEstate.Hall(_guild);

        if (old is not { Deleted: false })
        {
            return BotDoing.Failed($"{_guild.Name} has no hall to move");
        }

        BotOffice.Hold(BotExile.Office, _guild, BotExile.ClaimMs);

        if (!body.InRange(_plot, Reach))
        {
            return BotDoing.Walk(_map, _plot, BotArrival.Within(Reach), $"to the new ground at {_plot.X},{_plot.Y}");
        }

        var result = HousePlacement.Check(body, BotPlot.MultiID, _plot, out var toMove, Direction.South);

        if (result != HousePlacementResult.Valid)
        {
            BotPlot.Spend();
            Refused++;

            return BotDoing.Failed($"the new ground would not take the hall: {result}");
        }

        for (var i = 0; i < toMove.Count; i++)
        {
            if (toMove[i] is Item)
            {
                Refused++;

                return BotDoing.Failed("there are things lying on the new ground");
            }
        }

        var price = old.Price;

        var merchant = BotShelf.Of(old);

        if (merchant is { Deleted: false })
        {
            merchant.House = null;
            merchant.Internalize();
        }

        old.Delete();

        BotPlot.Spend();

        var house = new SmallOldHouse(body, BotPlot.MultiID);

        if (house.Deleted)
        {
            Refused++;

            if (merchant is { Deleted: false })
            {
                merchant.MoveToWorld(old.Location, _map);
            }

            return BotDoing.Failed("the hall would not go up on the new ground");
        }

        house.Price = price;
        house.MoveToWorld(_plot, _map);

        for (var i = 0; i < toMove.Count; i++)
        {
            if (toMove[i] is Mobile mobile)
            {
                mobile.Location = house.BanLocation;
            }
        }

        BotEstate.Take(_guild, house);

        _fitted = BotFittings.Furnish(house, _guild);

        if (merchant is { Deleted: false })
        {
            var spot = BotFittings.Spot(house);

            merchant.House = house;
            merchant.MoveToWorld(spot == Point3D.Zero ? house.BanLocation : spot, _map);

            if (_guild.Leader is BotMobile keeper)
            {
                merchant.Owner = keeper;
            }

            MerchantsCarried++;
            _fitted++;
        }

        BotEstate.Fit(_fitted);
        BotEstate.Register(_guild, house, false);

        if (_seating)
        {
            BotSeat.Moved(_guild.Name, true);
            Seated++;
        }
        else
        {
            BotExile.Paid(_guild);

            BotSeat.Set(_guild.Name, _plot, false);
        }

        Done++;

        return BotDoing.Done(
            _seating
                ? $"the hall of {_guild.Name} now stands at {_plot.X},{_plot.Y}, by its seat, {_fitted} things inside"
                : $"the hall of {_guild.Name} now stands at {_plot.X},{_plot.Y}, out of the winner's yard, {_fitted} things inside"
        );
    }

    public static string Describe() =>
        Done + Refused == 0
            ? "no hall has been moved"
            : $"{Done} halls moved ({Seated} of them to their seat, {MerchantsCarried} merchants carried along), {Refused} refused by the new ground";

    public static void Forget()
    {
        Done = 0;
        Refused = 0;
        Seated = 0;
        MerchantsCarried = 0;
    }
}

/// <summary>
/// Offers a member of a beaten guild the work of moving its hall.
///
/// <para>
/// Offered to whoever of the guild is choosing work, not to its leader, for the reason <c>BotSteward</c>
/// carries at length: a guild whose leader is in a company is a guild that can never build anything. The
/// guild's own claim keeps it to one member at a time.
/// </para>
///
/// <para>
/// <b>The ground is looked for before anybody is offered anything</b>, and it is looked for with the
/// winner's hall shunned — see <c>BotPlot.Find</c>'s four-argument form. An offer made before the ground
/// exists is an errand that walks across the island to discover there is nowhere to go.
/// </para>
/// </summary>
public sealed class BotRemover : IBotProposer
{
    public static long Asked { get; private set; }

    public static long Offered { get; private set; }

    public static long Settled { get; private set; }

    public static long Claimed { get; private set; }

    public static long Groundless { get; private set; }

    public static long Seating { get; private set; }

    public static long NoNearer { get; private set; }

    public string Name => "remover";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        if (!BotEstate.Running || !BotExile.Running)
        {
            return null;
        }

        if (bot?.Self is not BotMobile { Deleted: false, Alive: true } body || body.Map == null ||
            body.Map == Map.Internal)
        {
            return null;
        }

        if (body.Guild is not Guild ours)
        {
            return null;
        }

        Asked++;

        var winner = BotExile.Owed(ours);

        if (winner == null)
        {
            if (!BotSeat.Moving(ours, out var hall, out var seat))
            {
                Settled++;

                return null;
            }

            if (BotOffice.Busy(BotExile.Office, ours))
            {
                Claimed++;

                return null;
            }

            if (!BotPlot.Find(body, seat, out var ground))
            {
                Groundless++;

                return null;
            }

            if (BotSeat.Gap(ground, seat) >= BotSeat.Gap(hall.Location, seat))
            {
                NoNearer++;
                BotSeat.Moved(ours.Name, false);

                return null;
            }

            Seating++;
            Offered++;
            BotOffice.Offering(BotExile.Office, ours);

            return new BotRemove(ours, BotPopulation.Home, ground, true);
        }

        if (BotOffice.Busy(BotExile.Office, ours))
        {
            Claimed++;

            return null;
        }

        if (!BotPlot.Find(body, BotSeat.Of(ours), winner.Location, BotExile.Clear, out var plot))
        {
            Groundless++;

            return null;
        }

        Offered++;
        BotOffice.Offering(BotExile.Office, ours);

        return new BotRemove(ours, BotPopulation.Home, plot);
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been looked at for a removal"
            : $"the remover looked {Asked} times and sent {Offered} ({Seating} of them to carry a hall to its seat): {Settled} owed no move, {Claimed} already had somebody on it, "
            + $"{Groundless} found no ground outside the winner's yard or by the seat, {NoNearer} found none nearer the seat than the hall already is; {BotRemove.Describe()}";

    public static void Forget()
    {
        Asked = 0;
        Offered = 0;
        Settled = 0;
        Claimed = 0;
        Groundless = 0;
        Seating = 0;
        NoNearer = 0;
    }
}
