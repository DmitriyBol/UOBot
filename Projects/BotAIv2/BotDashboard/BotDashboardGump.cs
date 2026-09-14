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
            or ClaimTab or GuildTab
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

        builder.AddLabel(14, 12, Head, "BotAI v2 — dashboard");

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

        builder.AddButton(Width - 90, 12, 4014, 4016, 5);
        builder.AddLabel(Width - 60, 12, Ink, "refresh");

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

        return string.IsNullOrEmpty(rank) ? name : $"{name}, {rank}";
    }

    private void BotsPage(ref DynamicGumpBuilder builder)
    {
        builder.AddLabel(14, 44, Head, "name");
        builder.AddLabel(270, 44, Head, "ai");
        builder.AddLabel(310, 44, Head, "class");
        builder.AddLabel(386, 44, Head, "rung");
        builder.AddLabel(452, 44, Head, "doing");
        builder.AddLabel(672, 44, Head, "power");
        builder.AddLabel(734, 44, Head, "mood");
        builder.AddLabel(786, 44, Head, "vector");
        builder.AddLabel(848, 44, Head, "purse");
        builder.AddLabel(914, 44, Head, "bank");
        builder.AddLabel(988, 44, Head, "box");
        builder.AddLabel(1034, 44, Head, "stalls");
        builder.AddLabel(1090, 44, Head, "onsale");

        for (var i = 0; i < _bots.Count; i++)
        {
            var bot = _bots[i];
            var y = 68 + i * RowHeight;
            var resolve = bot.Resolve;

            builder.AddLabelCropped(14, y, 250, 20, bot.Alive ? Ink : Bad, Named(bot));
            builder.AddLabel(270, y, Ink, bot.Minded ? "(AI)" : "");
            builder.AddLabelCropped(310, y, 70, 20, Ink, bot.Class?.Name ?? "?");
            builder.AddLabelCropped(386, y, 60, 20, Rung(resolve), $"{resolve.Standing}");
            builder.AddLabelCropped(452, y, 214, 20, Ink, Doing(bot));
            builder.AddLabel(672, y, Ink, $"{BotThreat.Power(bot):N0}");
            builder.AddLabel(734, y, Shade(bot.Mood, 0.5), $"{bot.Mood:P0}");
            builder.AddLabel(786, y, Shade(bot.Progress, 0.35), $"{bot.Progress:P0}");
            builder.AddLabel(848, y, Ink, $"{Purse(bot)}");
            builder.AddLabel(914, y, Ink, $"{Banker.GetBalance(bot)}");
            builder.AddLabel(988, y, Ink, $"{bot.BankBox?.TotalItems ?? 0}");
            builder.AddLabel(1034, y, Ink, $"{BotAuction.StallsOf(bot)}");
            builder.AddLabel(1090, y, Ink, $"{BotAuction.WorthOf(bot)}");

            builder.AddButton(1146, y, 4005, 4007, 100 + i);
        }

        if (_bots.Count == 0)
        {
            builder.AddLabel(14, 68, Bad, "No bots. Check bots.population.enabled and bot-population.json");
        }

        var (units, worth) = BotAuction.Offered();

        builder.AddLabel(14, Height - 52, Ink, $"{BotPopulation.Count} bots, {BotPopulation.Living} alive");
        builder.AddLabelCropped(150, Height - 52, Width - 170, 20, Ink, BotWill.Describe());

        builder.AddLabel(14, Height - 34, Ink, $"market: {BotAuction.Stalls} stalls, {units} things worth {worth}gp, {BotAuction.Asks} wants");
    }

    private void MarketPage(ref DynamicGumpBuilder builder)
    {
        builder.AddLabel(14, 44, Head, "item");
        builder.AddLabel(250, 44, Head, "amount");
        builder.AddLabel(320, 44, Head, "price");
        builder.AddLabel(390, 44, Head, "worth");
        builder.AddLabel(460, 44, Head, "sold");
        builder.AddLabel(520, 44, Head, "earned");
        builder.AddLabel(596, 44, Head, "moves");
        builder.AddLabel(670, 44, Head, "seller");

        for (var i = 0; i < _stalls.Count; i++)
        {
            var stall = _stalls[i];
            var y = 66 + i * (RowHeight + 8);

            builder.AddItem(20, y - 4, stall.ItemId, stall.Hue);

            builder.AddLabelCropped(64, y, 180, 20, Ink, stall.Label);
            builder.AddLabel(250, y, Ink, $"{stall.Amount}");
            builder.AddLabel(320, y, Ink, $"{stall.Price}");
            builder.AddLabel(390, y, Ink, $"{stall.Worth}");
            builder.AddLabel(460, y, Ink, $"{stall.Sold}");
            builder.AddLabel(520, y, Ink, $"{stall.Earned}");
            builder.AddLabel(596, y, stall.Raises >= stall.Cuts ? Good : Bad, $"+{stall.Raises}/-{stall.Cuts}");
            builder.AddLabelCropped(670, y, 150, 20, Ink, stall.Seller?.Self?.Name ?? "gone");

            builder.AddButton(838, y, 4005, 4007, 200 + i);
            builder.AddLabel(858, y, Ink, "buy");
        }

        if (_stalls.Count == 0)
        {
            builder.AddLabel(14, 68, Bad, "Nothing on offer yet. Bots list what they produce when they bank it");
        }

        builder.AddLabel(14, Height - 34, Ink, BotAuction.Describe());
    }

    private void NeedsPage(ref DynamicGumpBuilder builder)
    {
        builder.AddLabel(14, 44, Head, "wanted");
        builder.AddLabel(250, 44, Head, "count");
        builder.AddLabel(310, 44, Head, "offer");
        builder.AddLabel(370, 44, Head, "down");
        builder.AddLabel(436, 44, Head, "filled");
        builder.AddLabel(496, 44, Head, "paid");
        builder.AddLabel(560, 44, Head, "moves");
        builder.AddLabel(632, 44, Head, "held");
        builder.AddLabel(686, 44, Head, "buyer");

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];
            var y = 66 + i * (RowHeight + 8);

            builder.AddItem(20, y - 4, want.ItemId, want.Hue);

            builder.AddLabelCropped(64, y, 180, 20, Ink, want.Label);
            builder.AddLabel(250, y, want.IsOpen ? Ink : Bad, $"{want.Amount}");
            builder.AddLabel(310, y, Ink, $"{want.Offer}");
            builder.AddLabel(370, y, want.Escrow >= want.Offer ? Ink : Bad, $"{want.Escrow}");
            builder.AddLabel(436, y, Ink, $"{want.Filled}");
            builder.AddLabel(496, y, Ink, $"{want.Paid}");

            builder.AddLabel(560, y, want.Raises > want.Cuts ? Bad : Good, $"+{want.Raises}/-{want.Cuts}");
            builder.AddLabel(632, y, want.Waiting > 0 ? Good : Ink, $"{want.Waiting}");
            builder.AddLabelCropped(686, y, 150, 20, Ink, want.Buyer?.Self?.Name ?? "gone");
        }

        if (_wants.Count == 0)
        {
            builder.AddLabel(14, 68, Bad, "Nobody is short of anything they cannot buy off a shelf");
        }

        builder.AddLabel(14, Height - 34, Ink, BotAuction.Describe());
    }

    private void QuadPage(ref DynamicGumpBuilder builder)
    {
        builder.AddLabel(14, 100, Head, "quadrant");
        builder.AddLabel(160, 100, Head, "safety");
        builder.AddLabel(240, 100, Head, "standing");
        builder.AddLabel(390, 100, Head, "crossings");
        builder.AddLabel(480, 100, Head, "blows");
        builder.AddLabel(550, 100, Head, "dead");
        builder.AddLabel(620, 100, Head, "the Baron");

        var quads = BotQuad.Worst(Rows - 2);

        for (var i = 0; i < quads.Count; i++)
        {
            var quad = quads[i];
            var y = 122 + i * (RowHeight + 8);
            var middle = quad.Middle;

            builder.AddLabel(14, y, quad.Trodden ? Ink : Bad, $"({middle.X}, {middle.Y})");

            var tint = quad.Safety <= BotQuad.Wanted ? Bad : quad.Safety > BotQuad.TooQuiet ? Good : Ink;

            builder.AddLabel(160, y, tint, $"{quad.Safety:F2}");
            builder.AddLabelCropped(240, y, 140, 20, tint, Standing(quad));
            builder.AddLabel(390, y, Ink, $"{quad.Passes}");
            builder.AddLabel(480, y, quad.Blows > 0 ? Bad : Ink, $"{quad.Blows}");
            builder.AddLabel(550, y, quad.Deaths > 0 ? Bad : Ink, $"{quad.Deaths}");

            var (word, colour) = Baron(quad);

            builder.AddLabelCropped(620, y, 200, 20, colour, word);
        }

        if (quads.Count == 0)
        {
            builder.AddLabel(14, 124, Bad, "The population has not walked anywhere yet");
        }

        builder.AddLabel(14, Height - 34, Ink, BotQuad.Describe());
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
        builder.AddLabel(14, 44, Head, "trade");
        builder.AddLabel(280, 44, Head, "pays/min");
        builder.AddLabel(370, 44, Head, "seen");
        builder.AddLabel(430, 44, Head, "mind");

        builder.AddLabel(520, 44, Head, "trade");
        builder.AddLabel(620, 44, Head, "claims");
        builder.AddLabel(690, 44, Head, "pays");
        builder.AddLabel(760, 44, Head, "seen");

        var known = BotCommons.Best(Rows);

        for (var i = 0; i < known.Count; i++)
        {
            var (kind, _, _, perMinute, settled, minded) = known[i];
            var y = 66 + i * (RowHeight + 8);

            builder.AddLabelCropped(14, y, 120, 20, Ink, kind);
            builder.AddLabel(280, y, perMinute > 0 ? Good : Bad, $"{perMinute:F0}");

            builder.AddLabel(370, y, settled >= 4 ? Ink : Bad, $"{settled}");
            builder.AddLabel(430, y, minded > 0 ? Good : Ink, $"{minded}");
        }

        if (known.Count == 0)
        {
            builder.AddLabel(14, 68, Bad, "The population has not found out anything about anywhere yet");
        }

        var gaps = BotCommons.Gaps(Rows);

        for (var i = 0; i < gaps.Count; i++)
        {
            var (kind, claimed, measured, settled, minded) = gaps[i];
            var y = 66 + i * (RowHeight + 8);

            builder.AddLabelCropped(520, y, 90, 20, minded > 0 ? Good : Ink, kind);
            builder.AddLabel(620, y, Ink, $"{claimed:F0}");

            builder.AddLabel(690, y, measured * 1.5 < claimed ? Bad : Good, $"{measured:F0}");
            builder.AddLabel(760, y, settled >= 25 ? Ink : Bad, $"{settled}");
        }

        if (gaps.Count == 0)
        {
            builder.AddLabel(520, 68, Bad, "No trade has been measured against its own claim yet");
        }

        builder.AddLabel(14, Height - 34, Ink, BotCommons.Describe());
    }

    private void Footer(ref DynamicGumpBuilder builder)
    {
        builder.AddImageTiled(14, Height - 62, Width - 28, 1, 9274);

        if (_page > 0)
        {
            builder.AddButton(Width - 150, Height - 34, 4014, 4016, 3);
        }

        builder.AddLabel(Width - 120, Height - 34, Ink, $"page {_page + 1} of {_pages}");

        if (_page + 1 < _pages)
        {
            builder.AddButton(Width - 34, Height - 34, 4005, 4007, 4);
        }
    }

    private const int CrownLots = 10;

    private void CrownPage(ref DynamicGumpBuilder builder)
    {
        var (units, worth) = BotAuction.Offered();

        builder.AddLabel(14, 44, Head, "the city sends for goods");

        builder.AddLabel(
            14,
            76,
            Ink,
            $"The market holds {BotAuction.Stalls} stalls: {units} things the population is asking {worth}gp for."
        );

        builder.AddLabel(
            14,
            100,
            Ink,
            $"Buying takes {CrownLots} stalls at random and pays the sellers what they asked."
        );

        builder.AddLabel(14, 124, Bad, "This makes new gold. Nothing else on the shard does.");

        builder.AddButton(14, 160, 4005, 4007, 8);
        builder.AddLabel(52, 160, Head, $"buy {CrownLots} lots from afar");

        builder.AddLabel(14, 200, Ink, $"so far: {BotAuction.Sales} sales on this market, {BotAuction.Turnover}gp turned over");
    }

    private static void Sent(Mobile from)
    {
        var (lots, units, paid) = BotAuction.Crown(CrownLots);

        if (lots <= 0)
        {
            from.SendMessage("The city sent for goods and found nothing on offer.");

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
        builder.AddLabel(14, 44, Head, "being claimed now");
        builder.AddLabel(14, 68, Head, "square");
        builder.AddLabel(160, 68, Head, "by");
        builder.AddLabel(300, 68, Head, "for");
        builder.AddLabel(430, 68, Head, "gathered");
        builder.AddLabel(540, 68, Head, "blood");
        builder.AddLabel(650, 68, Head, "left");
        builder.AddLabel(760, 68, Head, "taken from");

        var running = 0;

        foreach (var bid in BotClaim.Bids)
        {
            if (running >= 5)
            {
                break;
            }

            var y = 92 + running++ * RowHeight;
            var left = BotClaim.Left(bid) / 1000;

            builder.AddLabel(14, y, Ink, $"{bid.Middle.X}, {bid.Middle.Y}");
            builder.AddLabelCropped(160, y, 130, 20, Head, BotClaim.Short(bid.Guild) ?? "nobody");
            builder.AddLabel(
                300,
                y,
                Ink,
                bid.Want switch
                {
                    BotClaim.Kind.Oust  => "to take it",
                    BotClaim.Kind.Strip => "to strike a name off",
                    _                   => "to settle it"
                }
            );

            builder.AddLabel(
                430,
                y,
                bid.Peak >= BotClaim.Gather ? Good : Bad,
                $"{bid.Peak} of {BotClaim.Gather}"
            );

            builder.AddLabel(
                540,
                y,
                bid.Fallen > bid.Felled ? Bad : Ink,
                bid.Felled + bid.Fallen == 0 ? "none" : $"{bid.Felled} for, {bid.Fallen} against"
            );

            builder.AddLabel(650, y, Ink, $"{left}s");
            builder.AddLabelCropped(760, y, 140, 20, Ink, BotClaim.Short(bid.From) ?? "nobody");

            if (running - 1 < _spots.Count)
            {
                builder.AddButton(940, y, 4005, 4007, 400 + running - 1);
                builder.AddLabel(970, y, Ink, "go");
            }
        }

        if (running == 0)
        {
            builder.AddLabel(14, 92, Ink, "Nobody is claiming anything at the moment.");
        }

        var top = 92 + Math.Max(1, running) * RowHeight + 18;

        builder.AddImageTiled(14, top - 10, Width - 28, 1, 9274);
        builder.AddLabel(14, top, Head, "held");
        builder.AddLabel(160, top, Head, "square");
        builder.AddLabel(300, top, Head, "how it was taken");
        builder.AddLabel(520, top, Head, "safety");

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
            builder.AddLabel(160, y, Ink, $"{middle.X}, {middle.Y}");
            builder.AddLabel(300, y, Ink, bought ? $"bought for {BotClaim.Price}gp or taken" : "a free claim");

            var quad = BotQuad.Known(BotPopulation.Home, middle);

            builder.AddLabel(
                520,
                y,
                Ink,
                quad == null ? "not walked" : $"{BotQuad.Reading(quad):F1}"
            );

            var spot = 5 + held - 1;

            if (spot < _spots.Count)
            {
                builder.AddButton(640, y, 4005, 4007, 400 + spot);
                builder.AddLabel(670, y, Ink, "go");
            }
        }

        if (held == 0)
        {
            builder.AddLabel(14, top + 26, Ink, "No square of the island belongs to anybody yet.");
        }
    }

    private void GuildsPage(ref DynamicGumpBuilder builder)
    {
        builder.AddLabel(14, 60, Head, "guild");
        builder.AddLabel(150, 60, Head, "of them");
        builder.AddLabel(230, 60, Head, "purse");
        builder.AddLabel(320, 60, Head, "ground");
        builder.AddLabel(400, 60, Head, "working towards");
        builder.AddLabel(640, 60, Head, "all doing now");
        builder.AddLabel(900, 60, Head, "at war with");
        builder.AddLabel(1040, 60, Head, "allied to");

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
            builder.AddLabel(150, y, Ink, $"{guild.Members?.Count ?? 0}");
            builder.AddLabel(230, y, Ink, $"{BotEstate.Fund(guild)}gp");
            builder.AddLabel(320, y, Ink, $"{BotClaim.Holds(guild.Name)}");
            builder.AddLabelCropped(400, y, 230, 20, Ink, BotGuilds.Aim(guild));
            builder.AddLabelCropped(640, y, 250, 20, Ink, BotGuilds.Task(guild));
            builder.AddLabelCropped(900, y, 130, 20, wars == null ? Ink : Bad, wars ?? "nobody");
            builder.AddLabelCropped(1040, y, 126, 20, allies == null ? Ink : Good, allies ?? "nobody");
        }

        if (row == 0)
        {
            builder.AddLabel(14, 86, Bad, "No guild has been mustered.");
        }

        var foot = 86 + Math.Max(1, row) * (RowHeight + 4) + 18;

        builder.AddImageTiled(14, foot - 10, Width - 28, 1, 9274);

        builder.AddLabel(14, foot, Ink, "Alliances are the engine's own list; nothing on this shard yet asks a guild to make one.");
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
        builder.AddLabel(14, 60, Head, "guild");
        builder.AddLabel(160, 60, Head, "where");
        builder.AddLabel(280, 60, Head, "owner");
        builder.AddLabel(440, 60, Head, "of the guild");
        builder.AddLabel(560, 60, Head, "inside");
        builder.AddLabel(700, 60, Head, "condition");
        builder.AddLabel(860, 60, Head, "paid");
        builder.AddLabel(960, 60, Head, "go");

        for (var i = 0; i < _estate.Count && i < Rows; i++)
        {
            var hall = _estate[i];
            var y = 86 + i * (RowHeight + 4);

            builder.AddLabelCropped(14, y, 140, 20, Head, hall.Sign?.Name ?? "unnamed");
            builder.AddLabel(160, y, Ink, $"{hall.X}, {hall.Y}");
            builder.AddLabelCropped(280, y, 150, 20, Ink, hall.Owner?.Name ?? "nobody");
            builder.AddLabel(440, y, Ink, $"{(hall.CoOwners?.Count ?? 0) + 1}");
            builder.AddLabel(560, y, Ink, $"{Furnishings(hall)} things");
            builder.AddLabelCropped(700, y, 150, 20, Ink, Condition(hall));
            builder.AddLabel(860, y, Ink, $"{hall.Price}gp");

            builder.AddButton(960, y, 4005, 4007, 300 + i);
        }

        if (_estate.Count == 0)
        {
            builder.AddLabel(14, 86, Bad, "Nothing has been built on this island yet.");
        }

        var y2 = 86 + Math.Max(1, _estate.Count) * (RowHeight + 4) + 20;

        builder.AddImageTiled(14, y2 - 12, Width - 28, 1, 9274);
        builder.AddLabel(14, y2, Head, "still saving");

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
                builder.AddLabel(160, row, Good, $"has {fund}gp of {BotEstate.Price} — it is the ground it is waiting for");
            }
            else
            {
                builder.AddLabel(160, row, Ink, $"has {fund}gp of {BotEstate.Price}, {BotEstate.Price - fund}gp short");
            }
        }

        if (waiting == 0)
        {
            builder.AddLabel(160, y2, Good, "every guild has one");
        }

        builder.AddLabelCropped(14, Height - 34, Width - 28, 20, Ink, BotPlot.Describe());
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
            builder.AddLabel(14, 60, Bad, "No watcher is running on this shard, so nothing is being declared.");
            builder.AddLabel(14, 84, Ink, "Revels come from the minds assembly. Without it this tab has nothing to show.");

            return;
        }

        builder.AddLabel(14, 60, Head, "what is on");

        if (notice.Running)
        {
            builder.AddLabel(
                14,
                86,
                Good,
                $"{notice.Kind} is worth x{notice.Bonus:F1} for another {Clock(notice.EndsIn)}, prize {notice.Prize}gp"
            );

            builder.AddLabel(14, 112, Head, "it said");
            builder.AddHtml(120, 112, Width - 150, 40, notice.Said ?? "nothing at all");

            builder.AddLabel(14, 158, Head, "because");
            builder.AddHtml(120, 158, Width - 150, 60, notice.Why ?? "no reason was given");

            if (notice.Entered == 0)
            {
                builder.AddLabel(14, 226, Bad, "nobody has taken it up yet");
            }
            else
            {
                builder.AddLabel(
                    14,
                    226,
                    Ink,
                    $"{notice.Entered} have taken it up; {notice.Leading} leads with {notice.LeadingDid}"
                );
            }

            if (notice.Wave > 0)
            {
                builder.AddLabel(14, 250, Good, $"wave {notice.Wave} is standing, {notice.Standing} of it alive");
            }
        }
        else
        {
            builder.AddLabel(14, 86, Ink, $"Nothing is on. The watcher may declare again in {Clock(notice.NextIn)}.");

            if (notice.Past is { Length: > 0 })
            {
                builder.AddLabel(14, 112, Ink, $"the last one — {notice.Past[0]}");
            }
            else
            {
                builder.AddLabel(14, 112, Ink, "nothing has been declared yet");
            }
        }

        var where = notice.Map != null && notice.Where != Point3D.Zero;

        builder.AddImageTiled(14, 262, Width - 28, 1, 9274);

        if (where)
        {
            builder.AddButton(14, 276, 4005, 4007, 12);

            if (notice.Camp)
            {
                builder.AddLabel(52, 276, Head, $"go to the camp at ({notice.Where.X}, {notice.Where.Y})");
            }
            else
            {
                builder.AddLabel(52, 276, Head, $"go to where it was called, ({notice.Where.X}, {notice.Where.Y})");
            }
        }
        else
        {
            builder.AddLabel(14, 276, Ink, "nowhere in particular: this one is a price, not a place");
        }

        if (notice.WatcherMap != null && notice.Watcher != Point3D.Zero)
        {
            builder.AddButton(14, 306, 4005, 4007, 13);
            builder.AddLabel(
                52,
                306,
                Head,
                $"go to {notice.WatcherName ?? "the watcher"}, standing at ({notice.Watcher.X}, {notice.Watcher.Y})"
            );
        }

        builder.AddImageTiled(14, 340, Width - 28, 1, 9274);

        builder.AddLabel(14, 354, Head, "the crown");
        builder.AddLabel(
            120,
            354,
            Ink,
            $"{notice.Declared} declared, {notice.Won} won, {notice.Ignored} ignored, {notice.Bands} taken by a whole guild; {notice.Paid}gp paid out, {notice.Collected}gp taken in tax, {notice.Purse}gp in the purse"
        );

        builder.AddLabelCropped(14, 384, Width - 28, 20, Ink, notice.Waves ?? "no waves have been called");

        builder.AddLabelCropped(14, 404, Width - 28, 20, Ink, notice.Ledger ?? "none have been held yet");

        builder.AddLabel(14, 434, Head, "before this");

        var past = notice.Past ?? [];

        for (var i = 0; i < past.Length && i < 5; i++)
        {
            builder.AddLabelCropped(120, 434 + i * 24, 540, 20, Ink, past[i]);
        }

        var squad = BotCrier.Squad();

        builder.AddLabel(680, 434, Head, "the watchers");

        if (squad.Count == 0)
        {
            builder.AddLabel(680, 458, Ink, "nobody is watching");
        }

        for (var i = 0; i < squad.Count && i < 4; i++)
        {
            var (name, _, at) = squad[i];

            builder.AddButton(680, 458 + i * 24, 4005, 4007, 60 + i);
            builder.AddLabelCropped(718, 458 + i * 24, Width - 740, 20, Ink, $"go to {name}, standing at ({at.X}, {at.Y})");
        }

        if (past.Length == 0)
        {
            builder.AddLabel(120, 434, Ink, "nothing has ended yet");
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
