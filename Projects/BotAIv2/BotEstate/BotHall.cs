using System.Collections.Generic;
using Server.Guilds;
using Server.Mobiles;
using Server.Multis;

namespace Server.BotAI.V2;

/// <summary>
/// Raising the guild's hall: walk to the plot, call the levy, put the house up.
///
/// <para>
/// <b>The engine's own order of business, kept exactly.</b> <c>HousePlacementTool</c> checks the ground,
/// builds the house, takes the money and only then moves it into the world — and deletes it again if the
/// payment fails. Every step of that ordering is there to make sure a house and the coin for it cannot both
/// exist, and this follows it rather than inventing a shorter version.
/// </para>
///
/// <para>
/// <b>The money is the guild's, not the bot's.</b> That is why <see cref="Outlay"/> is nothing: it is the
/// number the decision layer measures a bot's own poverty against, and a buyer made to feel five thousand
/// gold short would refuse every errand on the shard until it stopped. What can be afforded is the
/// proposer's question, asked of <see cref="BotEstate.Fund"/>, and it is asked before the offer is made.
/// </para>
/// </summary>
public sealed class BotHall : BotDeed
{
    public const string Trade = "hall";

    public static double Prior { get; set; } = 400.0;

    public static double WorkMinutes { get; set; } = 1.0;

    public static int Reach { get; set; } = 5;

    private readonly Map _map;

    private readonly Point3D _plot;

    private readonly Guild _guild;

    private int _paid;

    private int _fitted;

    public BotHall(Guild guild, Map map, Point3D plot)
    {
        _guild = guild;
        _map = map;
        _plot = plot;
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

    public override int Made => _mine;

    private int _mine;

    public override string Stage =>
        _paid > 0 ? $"raised {_guild?.Name} for {_paid}gp" : $"to the plot at {_plot.X},{_plot.Y}";

    public override bool Bend(IBotWilful bot) => false;

    public override void Drop(IBotWilful bot) => BotSteward.Release(_guild);

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _guild == null)
        {
            return BotDoing.Failed("no body");
        }

        if (BotEstate.Hall(_guild) != null)
        {
            return BotDoing.Failed($"{_guild.Name} already has a hall");
        }

        BotSteward.Hold(_guild);

        if (!body.InRange(_plot, Reach))
        {
            return BotDoing.Walk(_map, _plot, BotArrival.Within(Reach), $"to the plot at {_plot.X},{_plot.Y}");
        }

        var result = HousePlacement.Check(body, BotPlot.MultiID, _plot, out var toMove, Direction.South);

        if (result != HousePlacementResult.Valid)
        {
            BotPlot.Spend();
            BotEstate.Lost();

            return BotDoing.Failed($"the ground would not take a hall: {result}");
        }

        for (var i = 0; i < toMove.Count; i++)
        {
            if (toMove[i] is Item)
            {
                BotEstate.Lost();

                return BotDoing.Failed("there are things lying on the plot");
            }
        }

        var paid = new List<BotEstate.Contribution>();
        var mine = BotYield.Wealth(body);
        var got = BotEstate.Levy(_guild, BotEstate.Price, paid, body);

        _mine += System.Math.Max(0, mine - BotYield.Wealth(body));

        if (got < BotEstate.Price)
        {
            BotEstate.Refund(paid, body);
            BotEstate.Lost();

            return BotDoing.Failed($"{_guild.Name} could only raise {got} of {BotEstate.Price}gp");
        }

        BotPlot.Spend();

        var house = new SmallOldHouse(body, BotPlot.MultiID);

        if (house.Deleted)
        {
            BotEstate.Refund(paid, body);
            BotEstate.Lost();

            return BotDoing.Failed("the house would not be built");
        }

        house.Price = got;
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
        BotEstate.Fit(_fitted);
        BotEstate.Register(_guild, house, true);

        _paid = got;

        return BotDoing.Done(
            $"raised the hall of {_guild.Name} at {_plot.X},{_plot.Y} for {got}gp off {paid.Count} members, {_fitted} things inside"
        );
    }
}
