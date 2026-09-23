using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Gumps;
using Server.Items;
using Server.Logging;
using Server.Mobiles;
using Server.Multis;
using Server.Network;

namespace Server.BotAI.V2;

/// <summary>
/// One window onto the whole population, onto the market it trades in, and onto what it cannot get hold of.
///
/// <para>
/// <b>This exists because the first version could not answer "why is that bot doing that".</b> Its decisions
/// were unobservable, so every question about behaviour was answered by watching a shard for an evening — and
/// the answers, when they finally came, were things like "it is trading, one tick at a time, and walking a
/// graveyard while it does". Everything on the first tab is a number the decision layer already keeps; the
/// only new thing here is that they are in one place, side by side, per bot.
/// </para>
///
/// <para>
/// The column that matters most is the last kind: <b>the vector</b>. A bot's class declares what it is
/// working towards, and that share — how far along its own trade it is — is the only measure of whether this
/// population is going anywhere at all. Money says what a bot has; the vector says whether it is becoming
/// something.
/// </para>
///
/// <para>
/// The third tab is the newest and the one an admin will reach for when the population looks idle: it is the
/// demand side of the market — who is short of what, at what price, and with how much of their own money
/// already down. A shard where nothing is being asked for and a shard where everything is being asked for at
/// four times its opening offer look identical on the other two tabs.
/// </para>
///
/// <para>
/// Built as a <see cref="DynamicGump"/> rather than a cached one because every row is different, and behind
/// a static <see cref="DisplayTo"/> because that is this shard's rule: prerequisites are checked before the
/// gump exists, so it can never be sent empty.
/// </para>
/// </summary>
public sealed class BotDashboardGump : DynamicGump
{
    private const int Width = 1180;

    private const int Height = 560;

    private const int Rows = 12;

    private const int RowHeight = 26;

    private const int Ink = 0x480;

    private const int Head = 0x481;

    private const int Good = 0x3F;

    private const int Bad = 0x21;

    private const int Gold = 0x35;

    private const int Red = 0x26;

    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDashboardGump));

    private const int BotsTab = 0;

    private const int MarketTab = 1;

    private const int NeedsTab = 2;

    private const int CrownTab = 3;

    private const int KnownTab = 4;

    private const int QuadTab = 5;

    private const int RevelTab = 6;

    private const int HallTab = 7;

    private const int ClaimTab = 8;

    private const int GuildTab = 9;

    private const int BandTab = 10;

    private readonly int _tab;

    private readonly int _page;

    private readonly int _pages;

    private readonly List<BotMobile> _bots = [];

    private readonly List<BotListing> _stalls = [];

    private readonly List<BotWant> _wants = [];

    private readonly List<BaseHouse> _estate = [];

    private readonly List<(Map Map, Point3D Where, string What)> _spots = [];

    public override bool Singleton => true;

    private BotDashboardGump(int tab, int page) : base(30, 30)
    {
        _tab = tab is MarketTab or NeedsTab or CrownTab or KnownTab or QuadTab or RevelTab or HallTab
            or ClaimTab or GuildTab or BandTab
            ? tab
            : BotsTab;

        if (_tab == MarketTab)
        {
            var market = Market();

            _pages = Math.Max(1, (market.Count + Rows - 1) / Rows);
            _page = Math.Clamp(page, 0, _pages - 1);

            Fill(market, _stalls);

            return;
        }

        if (_tab == NeedsTab)
        {
            var needs = Needs();

            _pages = Math.Max(1, (needs.Count + Rows - 1) / Rows);
            _page = Math.Clamp(page, 0, _pages - 1);

            Fill(needs, _wants);

            return;
        }

        if (_tab is QuadTab or RevelTab or ClaimTab or GuildTab)
        {
            _pages = 1;
            _page = 0;

            if (_tab == ClaimTab)
            {
                foreach (var bid in BotClaim.Bids)
                {
                    if (_spots.Count >= 5)
                    {
                        break;
                    }

                    _spots.Add((bid.Map, bid.Middle, $"{BotClaim.Short(bid.Guild)} is claiming this square"));
                }

                while (_spots.Count < 5)
                {
                    _spots.Add((null, Point3D.Zero, null));
                }

                foreach (var (key, guild, _) in BotClaim.Owned())
                {
                    if (_spots.Count >= 5 + Rows - 2)
                    {
                        break;
                    }

                    var middle = BotClaim.Middle(
                        BotPopulation.Home,
                        new Point3D(
                            key.X * BotQuad.Side + BotQuad.Side / 2,
                            key.Y * BotQuad.Side + BotQuad.Side / 2,
                            0
                        )
                    );

                    _spots.Add((BotPopulation.Home, middle, $"{BotClaim.Short(guild)} holds this square"));
                }
            }

            return;
        }

        if (_tab == HallTab)
        {
            _pages = 1;
            _page = 0;

            foreach (var hall in BotEstate.Halls)
            {
                if (hall is { Deleted: false })
                {
                    _estate.Add(hall);
                }
            }

            return;
        }

        var people = Population();

        _pages = Math.Max(1, (people.Count + Rows - 1) / Rows);
        _page = Math.Clamp(page, 0, _pages - 1);

        Fill(people, _bots);
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder)
    {
        builder.AddPage();
        builder.AddBackground(0, 0, Width, Height, 5054);
        builder.AddAlphaRegion(8, 8, Width - 16, Height - 16);

        builder.AddLabelCropped(14, 12, 1070, 20, Head, "BotAI v2 — dashboard");

        Tab(ref builder, 250, "Bots", BotsTab);
        Tab(ref builder, 335, "Market", MarketTab);
        Tab(ref builder, 430, "Needs", NeedsTab);
        Tab(ref builder, 515, "City", CrownTab);
        Tab(ref builder, 590, "Known", KnownTab);
        Tab(ref builder, 675, "Quad", QuadTab);
        Tab(ref builder, 750, "Revel", RevelTab);
        Tab(ref builder, 830, "Halls", HallTab);
        Tab(ref builder, 905, "Claims", ClaimTab);
        Tab(ref builder, 995, "Guilds", GuildTab);
        Tab(ref builder, 1075, "Band", BandTab);

        builder.AddButton(Width - 90, 12, 4014, 4016, 5);
        builder.AddLabelCropped(Width - 60, 12, 46, 20, Ink, "refresh");

        builder.AddImageTiled(14, 36, Width - 28, 1, 9274);

        if (_tab == MarketTab)
        {
            MarketPage(ref builder);
        }
        else if (_tab == CrownTab)
        {
            CrownPage(ref builder);
        }
        else if (_tab == NeedsTab)
        {
            NeedsPage(ref builder);
        }
        else if (_tab == KnownTab)
        {
            KnownPage(ref builder);
        }
        else if (_tab == QuadTab)
        {
            QuadPage(ref builder);
        }
        else if (_tab == RevelTab)
        {
            RevelPage(ref builder);
        }
        else if (_tab == HallTab)
        {
            HallsPage(ref builder);
        }
        else if (_tab == ClaimTab)
        {
            ClaimsPage(ref builder);
        }
        else if (_tab == GuildTab)
        {
            GuildsPage(ref builder);
        }
        else if (_tab == BandTab)
        {
            BandPage(ref builder);
        }
        else
        {
            BotsPage(ref builder);
        }

        Footer(ref builder);
    }

    private static string Named(BotMobile bot)
    {
        var name = bot.Name ?? "?";
        var rank = bot.BotRank;
        var named = string.IsNullOrEmpty(rank) ? name : $"{name}, {rank}";

        if (bot.NameHue == BotMobile.ChampionHue)
        {
            named += " — champion";
        }

        if (BotOutlaw.Jailed(bot))
        {
            named += $" — in a cell, {BotOutlaw.LeftInCell(bot)} min";
        }
        else if (BotOutlaw.IsRed(bot))
        {
            named += " — RED";
        }

        return named;
    }

    private static int NameHue(BotMobile bot) =>
        !bot.Alive ? Bad
        : BotOutlaw.IsRed(bot) || BotOutlaw.Jailed(bot) ? Red
        : bot.NameHue == BotMobile.ChampionHue ? Gold
        : Ink;

    private void BotsPage(ref DynamicGumpBuilder builder)
    {
        builder.AddLabelCropped(14, 44, 250, 20, Head, "name");
        builder.AddLabelCropped(270, 44, 34, 20, Head, "ai");
        builder.AddLabelCropped(310, 44, 70, 20, Head, "class");
        builder.AddLabelCropped(386, 44, 60, 20, Head, "rung");
        builder.AddLabelCropped(452, 44, 214, 20, Head, "doing");
        builder.AddLabelCropped(672, 44, 56, 20, Head, "power");
        builder.AddLabelCropped(734, 44, 46, 20, Head, "mood");
        builder.AddLabelCropped(786, 44, 56, 20, Head, "vector");
        builder.AddLabelCropped(848, 44, 60, 20, Head, "purse");
        builder.AddLabelCropped(914, 44, 68, 20, Head, "bank");
        builder.AddLabelCropped(988, 44, 40, 20, Head, "box");
        builder.AddLabelCropped(1034, 44, 50, 20, Head, "stalls");
        builder.AddLabelCropped(1090, 44, 76, 20, Head, "onsale");

        for (var i = 0; i < _bots.Count; i++)
        {
            var bot = _bots[i];
            var y = 68 + i * RowHeight;
            var resolve = bot.Resolve;

            builder.AddLabelCropped(14, y, 250, 20, NameHue(bot), Named(bot));
            builder.AddLabelCropped(270, y, 34, 20, Ink, bot.Minded ? "(AI)" : "");
            builder.AddLabelCropped(310, y, 70, 20, Ink, bot.Class?.Name ?? "?");
            builder.AddLabelCropped(386, y, 60, 20, Rung(resolve), $"{resolve.Standing}");
            builder.AddLabelCropped(452, y, 214, 20, Ink, Doing(bot));
            builder.AddLabelCropped(672, y, 56, 20, Ink, $"{BotThreat.Power(bot):N0}");
            builder.AddLabelCropped(734, y, 46, 20, Shade(bot.Mood, 0.5), $"{bot.Mood:P0}");
            builder.AddLabelCropped(786, y, 56, 20, Shade(bot.Progress, 0.35), $"{bot.Progress:P0}");
            builder.AddLabelCropped(848, y, 60, 20, Ink, $"{Purse(bot)}");
            builder.AddLabelCropped(914, y, 68, 20, Ink, $"{Banker.GetBalance(bot)}");
            builder.AddLabelCropped(988, y, 40, 20, Ink, $"{bot.BankBox?.TotalItems ?? 0}");
            builder.AddLabelCropped(1034, y, 50, 20, Ink, $"{BotAuction.StallsOf(bot)}");
            builder.AddLabelCropped(1090, y, 50, 20, Ink, $"{BotAuction.WorthOf(bot)}");

            builder.AddButton(1146, y, 4005, 4007, 100 + i);
        }

        if (_bots.Count == 0)
        {
            builder.AddLabelCropped(
                14,
                68,
                1152,
                20,
                Bad,
                "No bots. Check bots.population.enabled and bot-population.json"
            );
        }

        var (units, worth) = BotAuction.Offered();

        builder.AddLabelCropped(14, Height - 52, 130, 20, Ink, $"{BotPopulation.Count} bots, {BotPopulation.Living} alive");
        builder.AddLabelCropped(150, Height - 52, 1016, 20, Ink, BotWill.Describe());

        builder.AddLabelCropped(
            14,
            Height - 34,
            1010,
            20,
            Ink,
            $"market: {BotAuction.Stalls} stalls, {units} things worth {worth}gp, {BotAuction.Asks} wants"
        );
    }

    private void MarketPage(ref DynamicGumpBuilder builder)
    {
        builder.AddLabelCropped(14, 44, 230, 20, Head, "item");
        builder.AddLabelCropped(250, 44, 64, 20, Head, "amount");
        builder.AddLabelCropped(320, 44, 64, 20, Head, "price");
        builder.AddLabelCropped(390, 44, 64, 20, Head, "worth");
        builder.AddLabelCropped(460, 44, 54, 20, Head, "sold");
        builder.AddLabelCropped(520, 44, 70, 20, Head, "earned");
        builder.AddLabelCropped(596, 44, 68, 20, Head, "moves");
        builder.AddLabelCropped(670, 44, 496, 20, Head, "seller");

        for (var i = 0; i < _stalls.Count; i++)
        {
            var stall = _stalls[i];
            var y = 66 + i * (RowHeight + 8);

            builder.AddItem(20, y - 4, stall.ItemId, stall.Hue);

            builder.AddLabelCropped(64, y, 180, 20, Ink, stall.Label);
            builder.AddLabelCropped(250, y, 64, 20, Ink, $"{stall.Amount}");
            builder.AddLabelCropped(320, y, 64, 20, Ink, $"{stall.Price}");
            builder.AddLabelCropped(390, y, 64, 20, Ink, $"{stall.Worth}");
            builder.AddLabelCropped(460, y, 54, 20, Ink, $"{stall.Sold}");
            builder.AddLabelCropped(520, y, 70, 20, Ink, $"{stall.Earned}");
            builder.AddLabelCropped(
                596,
                y,
                68,
                20,
                stall.Raises >= stall.Cuts ? Good : Bad,
                $"+{stall.Raises}/-{stall.Cuts}"
            );
            builder.AddLabelCropped(670, y, 150, 20, Ink, stall.Seller?.Self?.Name ?? "gone");

            builder.AddButton(838, y, 4005, 4007, 200 + i);
            builder.AddLabelCropped(858, y, 308, 20, Ink, "buy");
        }

        if (_stalls.Count == 0)
        {
            builder.AddLabelCropped(
                14,
                68,
                1152,
                20,
                Bad,
                "Nothing on offer yet. Bots list what they produce when they bank it"
            );
        }

        builder.AddLabelCropped(14, Height - 34, 1010, 20, Ink, BotAuction.Describe());
    }

    private void NeedsPage(ref DynamicGumpBuilder builder)
    {
        builder.AddLabelCropped(14, 44, 230, 20, Head, "wanted");
        builder.AddLabelCropped(250, 44, 54, 20, Head, "count");
        builder.AddLabelCropped(310, 44, 54, 20, Head, "offer");
        builder.AddLabelCropped(370, 44, 60, 20, Head, "down");
        builder.AddLabelCropped(436, 44, 54, 20, Head, "filled");
        builder.AddLabelCropped(496, 44, 58, 20, Head, "paid");
        builder.AddLabelCropped(560, 44, 66, 20, Head, "moves");
        builder.AddLabelCropped(632, 44, 48, 20, Head, "held");
        builder.AddLabelCropped(686, 44, 480, 20, Head, "buyer");

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];
            var y = 66 + i * (RowHeight + 8);

            builder.AddItem(20, y - 4, want.ItemId, want.Hue);

            builder.AddLabelCropped(64, y, 180, 20, Ink, want.Label);
            builder.AddLabelCropped(250, y, 54, 20, want.IsOpen ? Ink : Bad, $"{want.Amount}");
            builder.AddLabelCropped(310, y, 54, 20, Ink, $"{want.Offer}");
            builder.AddLabelCropped(370, y, 60, 20, want.Escrow >= want.Offer ? Ink : Bad, $"{want.Escrow}");
            builder.AddLabelCropped(436, y, 54, 20, Ink, $"{want.Filled}");
            builder.AddLabelCropped(496, y, 58, 20, Ink, $"{want.Paid}");

            builder.AddLabelCropped(560, y, 66, 20, want.Raises > want.Cuts ? Bad : Good, $"+{want.Raises}/-{want.Cuts}");
            builder.AddLabelCropped(632, y, 48, 20, want.Waiting > 0 ? Good : Ink, $"{want.Waiting}");
            builder.AddLabelCropped(686, y, 150, 20, Ink, want.Buyer?.Self?.Name ?? "gone");
        }

        if (_wants.Count == 0)
        {
            builder.AddLabelCropped(14, 68, 1152, 20, Bad, "Nobody is short of anything they cannot buy off a shelf");
        }

        builder.AddLabelCropped(14, Height - 34, 1010, 20, Ink, BotAuction.Describe());
    }

    private void QuadPage(ref DynamicGumpBuilder builder)
    {
        builder.AddLabelCropped(14, 100, 140, 20, Head, "quadrant");
        builder.AddLabelCropped(160, 100, 74, 20, Head, "safety");
        builder.AddLabelCropped(240, 100, 144, 20, Head, "standing");
        builder.AddLabelCropped(390, 100, 84, 20, Head, "crossings");
        builder.AddLabelCropped(480, 100, 64, 20, Head, "blows");
        builder.AddLabelCropped(550, 100, 64, 20, Head, "dead");
        builder.AddLabelCropped(620, 100, 546, 20, Head, "the Baron");

        var quads = BotQuad.Worst(Rows - 2);

        for (var i = 0; i < quads.Count; i++)
        {
            var quad = quads[i];
            var y = 122 + i * (RowHeight + 8);
            var middle = quad.Middle;

            builder.AddLabelCropped(14, y, 140, 20, quad.Trodden ? Ink : Bad, $"({middle.X}, {middle.Y})");

            var tint = quad.Safety <= BotQuad.Wanted ? Bad : quad.Safety > BotQuad.TooQuiet ? Good : Ink;

            builder.AddLabelCropped(160, y, 74, 20, tint, $"{quad.Safety:F2}");
            builder.AddLabelCropped(240, y, 140, 20, tint, Standing(quad));
            builder.AddLabelCropped(390, y, 84, 20, Ink, $"{quad.Passes}");
            builder.AddLabelCropped(480, y, 64, 20, quad.Blows > 0 ? Bad : Ink, $"{quad.Blows}");
            builder.AddLabelCropped(550, y, 64, 20, quad.Deaths > 0 ? Bad : Ink, $"{quad.Deaths}");

            var (word, colour) = Baron(quad);

            builder.AddLabelCropped(620, y, 200, 20, colour, word);
        }

        if (quads.Count == 0)
        {
            builder.AddLabelCropped(14, 124, 1152, 20, Bad, "The population has not walked anywhere yet");
        }

        builder.AddLabelCropped(14, Height - 34, 1010, 20, Ink, BotQuad.Describe());
    }

    private static string Standing(BotQuad.Quad quad)
    {
        if (!quad.Trodden)
        {
            return "never stood in";
        }

        if (quad.Safety <= BotQuad.Dire)
        {
            return "dire";
        }

        if (quad.Safety <= BotQuad.Wanted)
        {
            return "worth hunting";
        }

        if (quad.Safety > BotQuad.TooQuiet)
        {
            return "too quiet to hunt";
        }

        return quad.Swept ? "swept by rangers" : "ordinary";
    }

    private static (string Word, int Colour) Baron(BotQuad.Quad quad)
    {
        if (BotHarrow.Square != Point3D.Zero && BotQuad.Key(quad.Map, BotHarrow.Square) == (quad.Map?.MapID ?? -1, quad.X, quad.Y))
        {
            return ("marching on it now", Good);
        }

        if (quad.HarrowedTick != 0)
        {
            return ("harrowed already", Good);
        }

        return quad.Safety <= BotQuad.Dire ? ("dire, and nobody going", Bad) : ("—", Ink);
    }

    private void KnownPage(ref DynamicGumpBuilder builder)
    {
        builder.AddLabelCropped(14, 44, 260, 20, Head, "trade");
        builder.AddLabelCropped(280, 44, 84, 20, Head, "pays/min");
        builder.AddLabelCropped(370, 44, 54, 20, Head, "seen");
        builder.AddLabelCropped(430, 44, 84, 20, Head, "mind");

        builder.AddLabelCropped(520, 44, 94, 20, Head, "trade");
        builder.AddLabelCropped(620, 44, 64, 20, Head, "claims");
        builder.AddLabelCropped(690, 44, 64, 20, Head, "pays");
        builder.AddLabelCropped(760, 44, 406, 20, Head, "seen");

        var known = BotCommons.Best(Rows);

        for (var i = 0; i < known.Count; i++)
        {
            var (kind, _, _, perMinute, settled, minded) = known[i];
            var y = 66 + i * (RowHeight + 8);

            builder.AddLabelCropped(14, y, 120, 20, Ink, kind);
            builder.AddLabelCropped(280, y, 84, 20, perMinute > 0 ? Good : Bad, $"{perMinute:F0}");

            builder.AddLabelCropped(370, y, 54, 20, settled >= 4 ? Ink : Bad, $"{settled}");
            builder.AddLabelCropped(430, y, 736, 20, minded > 0 ? Good : Ink, $"{minded}");
        }

        if (known.Count == 0)
        {
            builder.AddLabelCropped(14, 68, 1152, 20, Bad, "The population has not found out anything about anywhere yet");
        }

        var gaps = BotCommons.Gaps(Rows);

        for (var i = 0; i < gaps.Count; i++)
        {
            var (kind, claimed, measured, settled, minded) = gaps[i];
            var y = 66 + i * (RowHeight + 8);

            builder.AddLabelCropped(520, y, 90, 20, minded > 0 ? Good : Ink, kind);
            builder.AddLabelCropped(620, y, 64, 20, Ink, $"{claimed:F0}");

            builder.AddLabelCropped(690, y, 64, 20, measured * 1.5 < claimed ? Bad : Good, $"{measured:F0}");
            builder.AddLabelCropped(760, y, 406, 20, settled >= 25 ? Ink : Bad, $"{settled}");
        }

        if (gaps.Count == 0)
        {
            builder.AddLabelCropped(520, 68, 646, 20, Bad, "No trade has been measured against its own claim yet");
        }

        builder.AddLabelCropped(14, Height - 34, 1010, 20, Ink, BotCommons.Describe());
    }

    private void Footer(ref DynamicGumpBuilder builder)
    {
        builder.AddImageTiled(14, Height - 62, Width - 28, 1, 9274);

        if (_page > 0)
        {
            builder.AddButton(Width - 150, Height - 34, 4014, 4016, 3);
        }

        builder.AddLabelCropped(Width - 120, Height - 34, 80, 20, Ink, $"page {_page + 1} of {_pages}");

        if (_page + 1 < _pages)
        {
            builder.AddButton(Width - 34, Height - 34, 4005, 4007, 4);
        }
    }

    private const int CrownLots = 10;

    private void CrownPage(ref DynamicGumpBuilder builder)
    {
        var (units, worth) = BotAuction.Offered();

        builder.AddLabelCropped(14, 44, 1152, 20, Head, "the city sends for goods");

        builder.AddLabelCropped(
            14,
            76,
            1152,
            20,
            Ink,
            $"The market holds {BotAuction.Stalls} stalls: {units} things the population is asking {worth}gp for."
        );

        builder.AddLabelCropped(
            14,
            100,
            1152,
            20,
            Ink,
            $"Buying takes {CrownLots} stalls, the longest-standing first, and pays the sellers what they asked."
        );

        builder.AddLabelCropped(
            14,
            124,
            1152,
            20,
            Bad,
            $"Paid from the treasury: {BotCity.Purse}gp of {BotCity.Cap}, minted at {BotCity.MintPerHour}gp an hour. Argus has the same lever."
        );

        builder.AddButton(14, 160, 4005, 4007, 8);
        builder.AddLabelCropped(52, 160, 1114, 20, Head, $"buy {CrownLots} lots from afar");

        builder.AddLabelCropped(14, 200, Width - 28, 20, Ink, $"standing orders: {BotCity.Wants()}");
        builder.AddLabelCropped(
            14,
            224,
            1152,
            20,
            Ink,
            $"so far: {BotAuction.Sales} sales on this market, {BotAuction.Turnover}gp turned over; the city spent {BotCity.Spent}gp"
        );
    }

    private static void Sent(Mobile from)
    {
        var (lots, units, paid) = BotCity.Buy(CrownLots, from.Name);

        if (lots <= 0)
        {
            from.SendMessage(BotCity.Purse <= 0 ? "The treasury is empty." : "The city sent for goods and found nothing on offer it could pay for.");

            return;
        }

        from.SendMessage($"The city bought {units} things from {lots} stalls for {paid}gp.");

        logger.Information(
            "{Who} had the city buy {Units} things from {Lots} stalls for {Paid}gp",
            from.Name,
            units,
            lots,
            paid
        );
    }

    private void BandPage(ref DynamicGumpBuilder builder)
    {
        builder.AddLabelCropped(14, 100, 140, 20, Head, "bandit");
        builder.AddLabelCropped(160, 100, 94, 20, Head, "trade");
        builder.AddLabelCropped(260, 100, 154, 20, Head, "standing");
        builder.AddLabelCropped(420, 100, 84, 20, Head, "murders");
        builder.AddLabelCropped(510, 100, 74, 20, Head, "unseen");
        builder.AddLabelCropped(590, 100, 94, 20, Head, "robberies");
        builder.AddLabelCropped(690, 100, 94, 20, Head, "demands");
        builder.AddLabelCropped(790, 100, 94, 20, Head, "price");
        builder.AddLabelCropped(890, 100, 139, 20, Head, "where");
        builder.AddLabelCropped(1035, 100, 131, 20, Head, "go");

        var roll = BotUnderworld.AtLarge();

        for (var i = 0; i < roll.Count && i < Rows - 2; i++)
        {
            var one = roll[i];
            var y = 122 + i * (RowHeight + 8);

            builder.AddLabelCropped(14, y, 140, 20, one.Red ? Red : one.Fence ? Gold : Ink, one.Name);
            builder.AddLabelCropped(160, y, 95, 20, Ink, one.Trade);
            builder.AddLabelCropped(260, y, 155, 20, one.Known ? Bad : Ink, Standing(one));
            builder.AddLabelCropped(420, y, 84, 20, one.Murders > 0 ? Bad : Ink, $"{one.Murders}");
            builder.AddLabelCropped(510, y, 74, 20, Ink, $"{one.Unseen}");
            builder.AddLabelCropped(590, y, 94, 20, Ink, $"{one.Robberies}");
            builder.AddLabelCropped(690, y, 94, 20, Ink, $"{one.Extortions}");
            builder.AddLabelCropped(790, y, 94, 20, one.Price > 0 ? Gold : Ink, $"{one.Price}gp");
            builder.AddLabelCropped(890, y, 139, 20, Ink, $"({one.Where.X}, {one.Where.Y})");
            builder.AddButton(1035, y, 4005, 4007, 500 + i);
        }

        if (roll.Count == 0)
        {
            builder.AddLabelCropped(14, 124, 1152, 20, Good, "No bandit is at large on the island");
        }

        builder.AddButton(14, Height - 66, 4005, 4007, 18);
        builder.AddLabelCropped(
            46,
            Height - 66,
            1120,
            20,
            BotUnderworld.Exists && BotUnderworld.Hideout != Point3D.Zero ? Gold : Ink,
            "stand in the band's hideout"
        );

        builder.AddLabelCropped(14, Height - 34, 1010, 20, Ink, BotUnderworld.Describe());
    }

    private static string Standing(BotUnderworld.Wanted one)
    {
        if (one.Fence)
        {
            return one.Hunted ? "the band's fence, wanted" : "the band's fence";
        }

        if (one.Member)
        {
            return one.Red ? "of The Shadow, red" : one.Hunted ? "of The Shadow, wanted" : "of The Shadow";
        }

        return one.Red ? "red" : one.Hunted ? "wanted" : "at large";
    }

    private void Tab(ref DynamicGumpBuilder builder, int x, string name, int tab)
    {
        if (_tab == tab)
        {
            builder.AddLabel(x + 20, 12, Head, name);

            return;
        }

        builder.AddButton(
            x,
            12,
            4005,
            4007,
            tab switch
            {
                MarketTab => 2,
                NeedsTab  => 6,
                CrownTab  => 7,
                KnownTab  => 9,
                QuadTab   => 10,
                RevelTab  => 11,
                HallTab   => 14,
                ClaimTab  => 15,
                GuildTab  => 16,
                BandTab   => 17,
                _         => 1
            }
        );
        builder.AddLabel(x + 20, 12, Ink, name);
    }

    public override void OnResponse(NetState sender, in RelayInfo info)
    {
        var from = sender?.Mobile;

        if (from == null || from.AccessLevel < AccessLevel.Administrator)
        {
            return;
        }

        var button = info.ButtonID;

        switch (button)
        {
            case 0:
                return;

            case 1:
                DisplayTo(from, BotsTab);

                return;

            case 2:
                DisplayTo(from, MarketTab);

                return;

            case 3:
                DisplayTo(from, _tab, _page - 1);

                return;

            case 4:
                DisplayTo(from, _tab, _page + 1);

                return;

            case 5:
                DisplayTo(from, _tab, _page);

                return;

            case 6:
                DisplayTo(from, NeedsTab);

                return;

            case 7:
                DisplayTo(from, CrownTab);

                return;

            case 8:
                Sent(from);

                DisplayTo(from, CrownTab);

                return;

            case 9:
                DisplayTo(from, KnownTab);

                return;

            case 10:
                DisplayTo(from, QuadTab);

                return;

            case 11:
                DisplayTo(from, RevelTab);

                return;

            case 12:
                Stand(from, false);

                return;

            case 13:
                Stand(from, true);

                return;

            case >= 60 and < 66:
                Beside(from, button - 60);

                return;

            case 14:
                DisplayTo(from, HallTab);

                return;

            case 15:
                DisplayTo(from, ClaimTab);

                return;

            case 16:
                DisplayTo(from, GuildTab);

                return;

            case 17:
                DisplayTo(from, BandTab);

                return;

            case 18:
                Lair(from);

                return;
        }

        if (button >= 500)
        {
            Tail(from, button - 500);

            return;
        }

        if (button >= 400)
        {
            Ground(from, button - 400);

            return;
        }

        if (button >= 300)
        {
            Doorstep(from, button - 300);

            return;
        }

        if (button >= 200)
        {
            Bought(from, button - 200);

            return;
        }

        if (button >= 100)
        {
            Visit(from, button - 100);
        }
    }

    private void ClaimsPage(ref DynamicGumpBuilder builder)
    {
        builder.AddLabelCropped(14, 44, 1152, 20, Head, "being claimed now");
        builder.AddLabelCropped(14, 68, 140, 20, Head, "square");
        builder.AddLabelCropped(160, 68, 134, 20, Head, "by");
        builder.AddLabelCropped(300, 68, 124, 20, Head, "for");
        builder.AddLabelCropped(430, 68, 104, 20, Head, "gathered");
        builder.AddLabelCropped(540, 68, 104, 20, Head, "blood");
        builder.AddLabelCropped(650, 68, 104, 20, Head, "left");
        builder.AddLabelCropped(760, 68, 406, 20, Head, "taken from");

        var running = 0;

        foreach (var bid in BotClaim.Bids)
        {
            if (running >= 5)
            {
                break;
            }

            var y = 92 + running++ * RowHeight;
            var left = BotClaim.Left(bid) / 1000;

            builder.AddLabelCropped(14, y, 140, 20, Ink, $"{bid.Middle.X}, {bid.Middle.Y}");
            builder.AddLabelCropped(160, y, 130, 20, Head, BotClaim.Short(bid.Guild) ?? "nobody");
            builder.AddLabelCropped(
                300,
                y,
                124,
                20,
                Ink,
                bid.Want switch
                {
                    BotClaim.Kind.Oust  => "to take it",
                    BotClaim.Kind.Strip => "to strike a name off",
                    _                   => "to settle it"
                }
            );

            builder.AddLabelCropped(
                430,
                y,
                104,
                20,
                bid.Peak >= BotClaim.Gather ? Good : Bad,
                $"{bid.Peak} of {BotClaim.Gather}"
            );

            builder.AddLabelCropped(
                540,
                y,
                104,
                20,
                bid.Fallen > bid.Felled ? Bad : Ink,
                bid.Felled + bid.Fallen == 0 ? "none" : $"{bid.Felled} for, {bid.Fallen} against"
            );

            builder.AddLabelCropped(650, y, 104, 20, Ink, $"{left}s");
            builder.AddLabelCropped(760, y, 140, 20, Ink, BotClaim.Short(bid.From) ?? "nobody");

            if (running - 1 < _spots.Count)
            {
                builder.AddButton(940, y, 4005, 4007, 400 + running - 1);
                builder.AddLabelCropped(970, y, 196, 20, Ink, "go");
            }
        }

        if (running == 0)
        {
            builder.AddLabelCropped(14, 92, 1152, 20, Ink, "Nobody is claiming anything at the moment.");
        }

        var top = 92 + Math.Max(1, running) * RowHeight + 18;

        builder.AddImageTiled(14, top - 10, Width - 28, 1, 9274);
        builder.AddLabelCropped(14, top, 140, 20, Head, "held");
        builder.AddLabelCropped(160, top, 134, 20, Head, "square");
        builder.AddLabelCropped(300, top, 214, 20, Head, "how it was taken");
        builder.AddLabelCropped(520, top, 646, 20, Head, "safety");

        var held = 0;

        foreach (var (key, guild, bought) in BotClaim.Owned())
        {
            if (held >= Rows - 2)
            {
                break;
            }

            var y = top + 26 + held++ * RowHeight;
            var middle = new Point3D(
                key.X * BotQuad.Side + BotQuad.Side / 2,
                key.Y * BotQuad.Side + BotQuad.Side / 2,
                0
            );

            builder.AddLabelCropped(14, y, 130, 20, Head, BotClaim.Short(guild) ?? "nobody");
            builder.AddLabelCropped(160, y, 134, 20, Ink, $"{middle.X}, {middle.Y}");
            builder.AddLabelCropped(
                300,
                y,
                214,
                20,
                Ink,
                bought ? $"bought for {BotClaim.Price}gp or taken" : "a free claim"
            );

            var quad = BotQuad.Known(BotPopulation.Home, middle);

            builder.AddLabelCropped(520, y, 114, 20, Ink, quad == null ? "not walked" : $"{BotQuad.Reading(quad):F1}");

            var spot = 5 + held - 1;

            if (spot < _spots.Count)
            {
                builder.AddButton(640, y, 4005, 4007, 400 + spot);
                builder.AddLabelCropped(670, y, 496, 20, Ink, "go");
            }
        }

        if (held == 0)
        {
            builder.AddLabelCropped(14, top + 26, 1152, 20, Ink, "No square of the island belongs to anybody yet.");
        }
    }

    private void GuildsPage(ref DynamicGumpBuilder builder)
    {
        builder.AddLabelCropped(14, 60, 130, 20, Head, "guild");
        builder.AddLabelCropped(150, 60, 74, 20, Head, "of them");
        builder.AddLabelCropped(230, 60, 84, 20, Head, "purse");
        builder.AddLabelCropped(320, 60, 74, 20, Head, "ground");
        builder.AddLabelCropped(400, 60, 234, 20, Head, "working towards");
        builder.AddLabelCropped(640, 60, 254, 20, Head, "all doing now");
        builder.AddLabelCropped(900, 60, 134, 20, Head, "at war with");
        builder.AddLabelCropped(1040, 60, 126, 20, Head, "allied to");

        var row = 0;

        foreach (var guild in BotGuilds.Standing)
        {
            if (guild == null || row >= Rows)
            {
                continue;
            }

            var y = 86 + row++ * (RowHeight + 4);
            var wars = Names(guild.Enemies);
            var allies = Names(guild.Allies);

            builder.AddLabelCropped(14, y, 130, 20, Head, BotClaim.Short(guild.Name) ?? guild.Name);
            builder.AddLabelCropped(150, y, 74, 20, Ink, $"{guild.Members?.Count ?? 0}");
            builder.AddLabelCropped(230, y, 84, 20, Ink, $"{BotEstate.Fund(guild)}gp");
            builder.AddLabelCropped(320, y, 74, 20, Ink, $"{BotClaim.Holds(guild.Name)}");
            builder.AddLabelCropped(400, y, 230, 20, Ink, BotGuilds.Aim(guild));
            builder.AddLabelCropped(640, y, 250, 20, Ink, BotGuilds.Task(guild));
            builder.AddLabelCropped(900, y, 130, 20, wars == null ? Ink : Bad, wars ?? "nobody");
            builder.AddLabelCropped(1040, y, 126, 20, allies == null ? Ink : Good, allies ?? "nobody");
        }

        if (row == 0)
        {
            builder.AddLabelCropped(14, 86, 1152, 20, Bad, "No guild has been mustered.");
        }

        var foot = 86 + Math.Max(1, row) * (RowHeight + 4) + 18;

        builder.AddImageTiled(14, foot - 10, Width - 28, 1, 9274);

        builder.AddLabelCropped(
            14,
            foot,
            1152,
            20,
            Ink,
            "Alliances are the engine's own list; nothing on this shard yet asks a guild to make one."
        );
    }

    private static string Names(List<Guild> guilds)
    {
        if (guilds == null || guilds.Count == 0)
        {
            return null;
        }

        var say = "";

        for (var i = 0; i < guilds.Count; i++)
        {
            if (guilds[i] == null)
            {
                continue;
            }

            say += say.Length == 0 ? BotClaim.Short(guilds[i].Name) : ", " + BotClaim.Short(guilds[i].Name);
        }

        return say.Length == 0 ? null : say;
    }

    private void HallsPage(ref DynamicGumpBuilder builder)
    {
        builder.AddLabelCropped(14, 60, 140, 20, Head, "guild");
        builder.AddLabelCropped(160, 60, 114, 20, Head, "where");
        builder.AddLabelCropped(280, 60, 154, 20, Head, "owner");
        builder.AddLabelCropped(440, 60, 114, 20, Head, "of the guild");
        builder.AddLabelCropped(560, 60, 134, 20, Head, "inside");
        builder.AddLabelCropped(700, 60, 154, 20, Head, "condition");
        builder.AddLabelCropped(860, 60, 94, 20, Head, "paid");
        builder.AddLabelCropped(960, 60, 206, 20, Head, "go");

        for (var i = 0; i < _estate.Count && i < Rows; i++)
        {
            var hall = _estate[i];
            var y = 86 + i * (RowHeight + 4);

            builder.AddLabelCropped(14, y, 140, 20, Head, hall.Sign?.Name ?? "unnamed");
            builder.AddLabelCropped(160, y, 114, 20, Ink, $"{hall.X}, {hall.Y}");
            builder.AddLabelCropped(280, y, 150, 20, Ink, hall.Owner?.Name ?? "nobody");
            builder.AddLabelCropped(440, y, 114, 20, Ink, $"{(hall.CoOwners?.Count ?? 0) + 1}");
            builder.AddLabelCropped(560, y, 134, 20, Ink, $"{Furnishings(hall)} things");
            builder.AddLabelCropped(700, y, 150, 20, Ink, Condition(hall));
            builder.AddLabelCropped(860, y, 94, 20, Ink, $"{hall.Price}gp");

            builder.AddButton(960, y, 4005, 4007, 300 + i);
        }

        if (_estate.Count == 0)
        {
            builder.AddLabelCropped(14, 86, 1152, 20, Bad, "Nothing has been built on this island yet.");
        }

        var y2 = 86 + Math.Max(1, _estate.Count) * (RowHeight + 4) + 20;

        builder.AddImageTiled(14, y2 - 12, Width - 28, 1, 9274);
        builder.AddLabelCropped(14, y2, 140, 20, Head, "still saving");

        var waiting = 0;

        foreach (var guild in BotGuilds.Standing)
        {
            if (BotEstate.Hall(guild) != null)
            {
                continue;
            }

            var fund = BotEstate.Fund(guild);
            var row = y2 + 26 + waiting++ * 24;

            builder.AddLabelCropped(14, row, 140, 20, Ink, guild.Name);

            if (fund >= BotEstate.Price)
            {
                builder.AddLabelCropped(
                    160,
                    row,
                    1006,
                    20,
                    Good,
                    $"has {fund}gp of {BotEstate.Price} — it is the ground it is waiting for"
                );
            }
            else
            {
                builder.AddLabelCropped(
                    160,
                    row,
                    1006,
                    20,
                    Ink,
                    $"has {fund}gp of {BotEstate.Price}, {BotEstate.Price - fund}gp short"
                );
            }
        }

        if (waiting == 0)
        {
            builder.AddLabelCropped(160, y2, 1006, 20, Good, "every guild has one");
        }

        builder.AddLabelCropped(14, Height - 34, 1010, 20, Ink, BotPlot.Describe());
    }

    private static int Furnishings(BaseHouse hall) => (hall.Addons?.Count ?? 0) + (hall.LockDowns?.Count ?? 0);

    private static string Condition(BaseHouse hall) => hall.DecayLevel.ToString();

    private void Ground(Mobile from, int row)
    {
        if (row < 0 || row >= _spots.Count)
        {
            return;
        }

        var (map, where, what) = _spots[row];

        if (map == null || map == Map.Internal || where == Point3D.Zero)
        {
            from.SendMessage("There is nothing on that row any more.");

            DisplayTo(from, ClaimTab);

            return;
        }

        from.MoveToWorld(where, map);
        from.SendMessage(what ?? "The square.");

        DisplayTo(from, ClaimTab);
    }

    private void Doorstep(Mobile from, int row)
    {
        if (row < 0 || row >= _estate.Count)
        {
            return;
        }

        var hall = _estate[row];

        if (hall.Deleted || hall.Map == null || hall.Map == Map.Internal)
        {
            from.SendMessage("That hall is not standing any more.");

            DisplayTo(from, HallTab);

            return;
        }

        from.MoveToWorld(hall.BanLocation, hall.Map);
        from.SendMessage($"The hall of {hall.Sign?.Name ?? "somebody"}, {Furnishings(hall)} things inside it.");

        DisplayTo(from, HallTab);
    }

    private void RevelPage(ref DynamicGumpBuilder builder)
    {
        var notice = BotCrier.Read();

        if (notice == null)
        {
            builder.AddLabelCropped(
                14,
                60,
                1152,
                20,
                Bad,
                "No watcher is running on this shard, so nothing is being declared."
            );
            builder.AddLabelCropped(
                14,
                84,
                1152,
                20,
                Ink,
                "Revels come from the minds assembly. Without it this tab has nothing to show."
            );

            return;
        }

        builder.AddLabelCropped(14, 60, 1152, 20, Head, "what is on");

        if (notice.Running)
        {
            builder.AddLabelCropped(
                14,
                86,
                1152,
                20,
                Good,
                $"{notice.Kind} is worth x{notice.Bonus:F1} for another {Clock(notice.EndsIn)}, prize {notice.Prize}gp"
            );

            builder.AddLabelCropped(14, 112, 100, 20, Head, "it said");
            builder.AddHtml(120, 112, Width - 150, 40, notice.Said ?? "nothing at all");

            builder.AddLabelCropped(14, 158, 100, 20, Head, "because");
            builder.AddHtml(120, 158, Width - 150, 60, notice.Why ?? "no reason was given");

            if (notice.Entered == 0)
            {
                builder.AddLabelCropped(14, 226, 1152, 20, Bad, "nobody has taken it up yet");
            }
            else
            {
                builder.AddLabelCropped(
                    14,
                    226,
                    1152,
                    20,
                    Ink,
                    $"{notice.Entered} have taken it up; {notice.Leading} leads with {notice.LeadingDid}"
                );
            }

            if (notice.Wave > 0)
            {
                builder.AddLabelCropped(
                    14,
                    250,
                    1152,
                    20,
                    Good,
                    $"wave {notice.Wave} is standing, {notice.Standing} of it alive"
                );
            }
        }
        else
        {
            builder.AddLabelCropped(
                14,
                86,
                1152,
                20,
                Ink,
                $"Nothing is on. The watcher may declare again in {Clock(notice.NextIn)}."
            );

            if (notice.Past is { Length: > 0 })
            {
                builder.AddLabelCropped(14, 112, 1152, 20, Ink, $"the last one — {notice.Past[0]}");
            }
            else
            {
                builder.AddLabelCropped(14, 112, 1152, 20, Ink, "nothing has been declared yet");
            }
        }

        var where = notice.Map != null && notice.Where != Point3D.Zero;

        builder.AddImageTiled(14, 262, Width - 28, 1, 9274);

        if (where)
        {
            builder.AddButton(14, 276, 4005, 4007, 12);

            if (notice.Camp)
            {
                builder.AddLabelCropped(52, 276, 1114, 20, Head, $"go to the camp at ({notice.Where.X}, {notice.Where.Y})");
            }
            else
            {
                builder.AddLabelCropped(
                    52,
                    276,
                    1114,
                    20,
                    Head,
                    $"go to where it was called, ({notice.Where.X}, {notice.Where.Y})"
                );
            }
        }
        else
        {
            builder.AddLabelCropped(14, 276, 1152, 20, Ink, "nowhere in particular: this one is a price, not a place");
        }

        if (notice.WatcherMap != null && notice.Watcher != Point3D.Zero)
        {
            builder.AddButton(14, 306, 4005, 4007, 13);
            builder.AddLabelCropped(
                52,
                306,
                1114,
                20,
                Head,
                $"go to {notice.WatcherName ?? "the watcher"}, standing at ({notice.Watcher.X}, {notice.Watcher.Y})"
            );
        }

        builder.AddImageTiled(14, 340, Width - 28, 1, 9274);

        builder.AddLabelCropped(14, 354, 100, 20, Head, "the crown");
        builder.AddLabelCropped(
            120,
            354,
            1046,
            20,
            Ink,
            $"{notice.Declared} declared, {notice.Won} won, {notice.Ignored} ignored, {notice.Bands} taken by a whole guild; {notice.Paid}gp paid out, {notice.Collected}gp taken in tax, {notice.Purse}gp in the purse"
        );

        builder.AddLabelCropped(14, 384, Width - 28, 20, Ink, notice.Waves ?? "no waves have been called");

        builder.AddLabelCropped(14, 404, Width - 28, 20, Ink, notice.Ledger ?? "none have been held yet");

        builder.AddLabelCropped(14, 434, 100, 20, Head, "before this");

        var past = notice.Past ?? [];

        for (var i = 0; i < past.Length && i < 5; i++)
        {
            builder.AddLabelCropped(120, 434 + i * 24, 540, 20, Ink, past[i]);
        }

        var squad = BotCrier.Squad();

        builder.AddLabelCropped(680, 434, 486, 20, Head, "the watchers");

        if (squad.Count == 0)
        {
            builder.AddLabelCropped(680, 458, 486, 20, Ink, "nobody is watching");
        }

        for (var i = 0; i < squad.Count && i < 4; i++)
        {
            var (name, _, at) = squad[i];

            builder.AddButton(680, 458 + i * 24, 4005, 4007, 60 + i);
            builder.AddLabelCropped(718, 458 + i * 24, Width - 740, 20, Ink, $"go to {name}, standing at ({at.X}, {at.Y})");
        }

        if (past.Length == 0)
        {
            builder.AddLabelCropped(120, 434, 554, 20, Ink, "nothing has ended yet");
        }
    }

    private static string Clock(long ms)
    {
        var seconds = Math.Max(0, ms / 1000);

        return $"{seconds / 60}m {seconds % 60:D2}s";
    }

    private void Beside(Mobile from, int which)
    {
        var squad = BotCrier.Squad();

        if (which < 0 || which >= squad.Count)
        {
            from.SendMessage("That watcher is nowhere to be found.");

            DisplayTo(from, RevelTab);

            return;
        }

        var (name, map, at) = squad[which];

        if (map == null || map == Map.Internal || at == Point3D.Zero)
        {
            from.SendMessage($"{name} has no body at the moment.");

            DisplayTo(from, RevelTab);

            return;
        }

        from.MoveToWorld(at, map);
        from.SendMessage($"{name} is standing here.");

        DisplayTo(from, RevelTab);
    }

    private void Stand(Mobile from, bool watcher)
    {
        var notice = BotCrier.Read();

        var map = watcher ? notice?.WatcherMap : notice?.Map;
        var at = watcher ? notice?.Watcher ?? Point3D.Zero : notice?.Where ?? Point3D.Zero;

        if (map == null || map == Map.Internal || at == Point3D.Zero)
        {
            from.SendMessage(watcher ? "The watcher is nowhere to be found." : "That revel has no place any more.");

            DisplayTo(from, RevelTab);

            return;
        }

        from.MoveToWorld(at, map);

        if (watcher)
        {
            from.SendMessage($"{notice.WatcherName ?? "The watcher"} is standing here.");
        }
        else
        {
            from.SendMessage($"This is where {notice.Kind ?? "the last revel"} was called.");
        }

        DisplayTo(from, RevelTab);
    }

    private void Lair(Mobile from)
    {
        var at = BotUnderworld.Hideout;
        var map = BotLair.Fire?.Map ?? BotLair.Chest?.Map ?? from.Map;

        if (!BotUnderworld.Exists || at == Point3D.Zero || map == null || map == Map.Internal)
        {
            from.SendMessage("The Shadow has no hideout at the moment.");

            DisplayTo(from, BandTab);

            return;
        }

        from.MoveToWorld(at, map);
        from.SendMessage($"The Shadow's hideout: {BotLair.Describe()}");

        DisplayTo(from, BandTab);
    }

    private void Tail(Mobile from, int row)
    {
        var roll = BotUnderworld.AtLarge();

        if (row < 0 || row >= roll.Count)
        {
            DisplayTo(from, BandTab);

            return;
        }

        var one = roll[row];

        if (one.Map == null || one.Map == Map.Internal || one.Where == Point3D.Zero)
        {
            from.SendMessage($"{one.Name} is nowhere to be found.");

            DisplayTo(from, BandTab);

            return;
        }

        from.MoveToWorld(one.Where, one.Map);
        from.SendMessage($"{one.Name}: {Standing(one)}, {one.Murders} murders, {one.Price}gp on its head.");

        DisplayTo(from, BandTab);
    }

    private void Visit(Mobile from, int row)
    {
        if (row < 0 || row >= _bots.Count)
        {
            return;
        }

        var bot = _bots[row];

        if (bot.Deleted || bot.Map == null || bot.Map == Map.Internal)
        {
            from.SendMessage("That bot is not in the world any more.");

            return;
        }

        from.MoveToWorld(bot.Location, bot.Map);
        from.SendMessage($"{bot.Name} the {bot.Class?.Name}: {Doing(bot)}");

        DisplayTo(from, _tab, _page);
    }

    private void Bought(Mobile from, int row)
    {
        if (row < 0 || row >= _stalls.Count)
        {
            return;
        }

        var stall = _stalls[row];
        var bought = BotAuction.Buy(from, stall, 1);

        if (bought > 0)
        {
            from.SendMessage($"Bought {bought} {stall.Label} for {stall.Price}gp.");
        }
        else
        {
            from.SendMessage("That purchase did not go through — check your gold.");
        }

        DisplayTo(from, _tab, _page);
    }

    public static void DisplayTo(Mobile from, int tab = BotsTab, int page = 0)
    {
        if (from?.NetState == null || from.AccessLevel < AccessLevel.Administrator)
        {
            return;
        }

        if (!BotCore.Enabled)
        {
            from.SendMessage("The bot assembly is switched off — bots.enabled in modernuo.json.");

            return;
        }

        from.CloseGump<BotDashboardGump>();
        from.SendGump(new BotDashboardGump(tab, page));
    }

    private void Fill<T>(List<T> from, List<T> into)
    {
        var start = _page * Rows;

        for (var i = start; i < from.Count && into.Count < Rows; i++)
        {
            into.Add(from[i]);
        }
    }

    private static List<BotMobile> Population()
    {
        var all = BotPopulation.Bots;
        List<BotMobile> alive = [];

        for (var i = 0; i < all.Count; i++)
        {
            if (all[i] is { Deleted: false })
            {
                alive.Add(all[i]);
            }
        }

        return alive;
    }

    private static List<BotWant> Needs()
    {
        var all = BotAuction.Wants;
        List<BotWant> wants = [];

        for (var i = 0; i < all.Count; i++)
        {
            wants.Add(all[i]);
        }

        return wants;
    }

    private static List<BotListing> Market()
    {
        var all = BotAuction.Listings;
        List<BotListing> stalls = [];

        for (var i = 0; i < all.Count; i++)
        {
            if (!all[i].IsEmpty)
            {
                stalls.Add(all[i]);
            }
        }

        return stalls;
    }

    private static int Purse(Mobile bot) => bot.Backpack?.GetAmount(typeof(Gold)) ?? 0;

    private static string Doing(BotMobile bot)
    {
        var deed = bot.Resolve.Deed;

        if (deed != null)
        {
            return deed.ToString();
        }

        return bot.Alive ? "nothing" : "dead";
    }

    private static int Rung(BotResolve resolve) =>
        resolve.Standing switch
        {
            BotStanding.Free => Ink,
            BotStanding.Busy => Good,
            _ => Bad
        };

    private static int Shade(double value, double mark) => value < mark ? Bad : Ink;
}
