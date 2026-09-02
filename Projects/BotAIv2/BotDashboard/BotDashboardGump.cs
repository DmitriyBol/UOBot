using System;
using System.Collections.Generic;
using Server.Gumps;
using Server.Items;
using Server.Logging;
using Server.Mobiles;
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

    /// <summary>How many rows a page holds. Twelve fits the height without a scrollbar.</summary>
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

    /// <summary>
    /// The city: the one tab that does something to the world rather than reporting on it.
    ///
    /// <para>
    /// Everything else here is a window. This is a lever, and it is on a dashboard whose command is
    /// registered at <c>AccessLevel.Administrator</c> and whose responses check the same again — because a
    /// gump reply is a packet and a packet can be sent by anybody who has ever seen the window.
    /// </para>
    /// </summary>
    private const int CrownTab = 3;

    /// <summary>
    /// What the population knows, as opposed to what any one bot knows.
    ///
    /// <para>
    /// <b>The one tab that is about the shard's memory rather than its state.</b> Everything on the other
    /// four is a photograph — who is alive, what is on a stall, what somebody is short of, what the city
    /// wants — and all of it is true only for the second it was drawn. This is the opposite: what the island
    /// has taught thirty-three bots between them, decaying on its own clock, and it is the page to read when
    /// the question is "why is everybody suddenly doing that".
    /// </para>
    /// </summary>
    private const int KnownTab = 4;

    /// <summary>
    /// The island itself: every quadrant the population has an opinion about, worst ground first.
    ///
    /// <para>
    /// <b>The one tab that is about the world rather than about the bots.</b> Everything else here reports on
    /// what the population is doing; this reports on what it has found out about where it lives, which is the
    /// page to read when deciding where anybody should be sent. Sorted worst first because a list of safe
    /// ground is a list nobody acts on, and beside each dire square is whether the Baron has actually gone
    /// there — the one column that says whether the map is being used or merely kept.
    /// </para>
    /// </summary>
    private const int QuadTab = 5;

    private readonly int _tab;

    private readonly int _page;

    private readonly int _pages;

    /// <summary>
    /// The exact rows this window is showing.
    ///
    /// Snapshotted in the constructor rather than read again while drawing or answering, and that is not
    /// tidiness: bots take turns and the market moves between the moment a window is sent and the moment a
    /// button on it comes back. A row that means one thing on screen and another in the response handler is
    /// how an admin tool teleports somebody to the wrong bot.
    /// </summary>
    private readonly List<BotMobile> _bots = [];

    private readonly List<BotListing> _stalls = [];

    private readonly List<BotWant> _wants = [];

    public override bool Singleton => true;

    private BotDashboardGump(int tab, int page) : base(30, 30)
    {
        _tab = tab is MarketTab or NeedsTab or CrownTab or KnownTab or QuadTab ? tab : BotsTab;

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

        // <b>The quadrant tab's visit list is the rangers, not the population, and that is not a shortcut.</b>
        // Visit reads _bots by row, and Fill only ever puts the *current page* into it — so a button numbered
        // from the whole population would point at whoever happens to be twelfth on this page, or at nothing
        // at all. One list, filled with exactly the bots this page offers to walk to.
        if (_tab == QuadTab)
        {
            _pages = 1;
            _page = 0;

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

        // Tabs. The one being shown is drawn as a label rather than a button, so the window always says which
        // of the two it is.
        Tab(ref builder, 300, "Bots", BotsTab);
        Tab(ref builder, 390, "Market", MarketTab);
        Tab(ref builder, 490, "Needs", NeedsTab);
        Tab(ref builder, 580, "City", CrownTab);
        Tab(ref builder, 660, "Known", KnownTab);
        Tab(ref builder, 750, "Quad", QuadTab);

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
        else
        {
            BotsPage(ref builder);
        }

        Footer(ref builder);
    }

    /// <summary>
    /// The bot's name with its rank behind a comma — "Perri, Apprentice Swordsman" — spelled the way the
    /// engine spells a player's.
    ///
    /// <para>
    /// <b>Here rather than in <c>Mobile.Title</c>, which is where it used to be and where it did harm.</b>
    /// Title means <em>custom</em> title to this engine: <c>AddNameProperties</c> joins it to the name with a
    /// bare space, so a bot in the world read "Perri Apprentice Swordsman" while a player reads "Arold,
    /// Grandmaster Alchemist" — and a non-empty Title makes <c>Titles.ComputeTitle</c> skip the skill-title
    /// branch, which is the only branch that writes the comma. Storing it there both mangled the punctuation
    /// and disabled the code that would have got it right.
    /// </para>
    ///
    /// <para>
    /// So the rank lives on the bot and is spelled out here, in the one frame that wants it, and the world
    /// label is left to the engine.
    /// </para>
    /// </summary>
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

            // Straight to whoever is on this row. The one thing an admin always wants next after reading a
            // line like this is to look at the bot it describes.
            builder.AddButton(1146, y, 4005, 4007, 100 + i);
        }

        if (_bots.Count == 0)
        {
            builder.AddLabel(14, 68, Bad, "No bots. Check bots.population.enabled and bot-population.json");
        }

        var (units, worth) = BotAuction.Offered();

        // Two labels rather than one interpolated line: a string-returning call inside a hole is the one
        // shape that defeats the zero-allocation handler, and the census line is long.
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

            // The thing itself, not its name. A market you can only read is a spreadsheet.
            builder.AddItem(20, y - 4, stall.ItemId, stall.Hue);

            builder.AddLabelCropped(64, y, 180, 20, Ink, stall.Label);
            builder.AddLabel(250, y, Ink, $"{stall.Amount}");
            builder.AddLabel(320, y, Ink, $"{stall.Price}");
            builder.AddLabel(390, y, Ink, $"{stall.Worth}");
            builder.AddLabel(460, y, Ink, $"{stall.Sold}");
            builder.AddLabel(520, y, Ink, $"{stall.Earned}");
            builder.AddLabel(596, y, stall.Raises >= stall.Cuts ? Good : Bad, $"+{stall.Raises}/-{stall.Cuts}");
            builder.AddLabelCropped(670, y, 150, 20, Ink, stall.Seller?.Self?.Name ?? "gone");

            // Buys one, at the asking price, out of your own purse. The only way to see a bot move its own
            // price before a trade exists that buys from another. Unguarded now: nothing empty reaches this
            // list at all — see Market.
            builder.AddButton(838, y, 4005, 4007, 200 + i);
            builder.AddLabel(858, y, Ink, "buy");
        }

        if (_stalls.Count == 0)
        {
            builder.AddLabel(14, 68, Bad, "Nothing on offer yet. Bots list what they produce when they bank it");
        }

        builder.AddLabel(14, Height - 34, Ink, BotAuction.Describe());
    }

    /// <summary>
    /// What the population is short of, at what price, with whose money behind it.
    ///
    /// <para>
    /// <b>This is the tab that answers "why is nobody mining".</b> The other two say what bots are doing and
    /// what they have made; neither can say what the shard has been unable to get hold of. A want with its
    /// offer four times what it opened at and nothing filled is the clearest sentence this population can
    /// speak: somebody has been trying to buy that for half an hour and nobody here can make it.
    /// </para>
    /// </summary>
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

            // Raises mean the opposite of what they mean on a stall: a want that keeps going up is one the
            // shard cannot supply, so the colours are the other way round on purpose.
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

    /// <summary>
    /// What the population has found out: where work pays, where blood is spilt, and how much of it was
    /// learned by the three bots that think.
    ///
    /// <para>
    /// <b>Both maps on one page, because they are read together or not at all.</b> "This patch pays forty a
    /// minute" and "this square has killed two people" are the two halves of every decision a bot makes about
    /// where to go, and they were previously visible only as one sentence each at the bottom of two other
    /// tabs. Side by side they answer the question an admin actually has, which is whether the population is
    /// avoiding somewhere for a good reason.
    /// </para>
    ///
    /// <para>
    /// The <c>mind</c> column is the point of the whole page. Three bots on this shard think with a model and
    /// thirty do not, and the design has always been that the three <em>supplement</em> the rest rather than
    /// replace them — so the useful question is how much of what everybody now knows came from them. A column
    /// of noughts would mean three expensive bots are learning only for themselves.
    /// </para>
    /// </summary>
    /// <summary>
    /// The island, worst ground first, with what the Baron is doing about it.
    ///
    /// <para>
    /// Sorted by safety ascending and never by anything else: a list of safe ground is a list nobody acts on.
    /// The rightmost column is the one that matters — whether a square bad enough to want a great hunt has
    /// actually had one sent to it. "Dire and nobody going" is the single reading on this page that means
    /// something is wrong, and it is the reason the column exists rather than being inferred from two others.
    /// </para>
    /// </summary>
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

            // Red once it is worth going to, green once it is too quiet to bother hunting in, plain between.
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

    /// <summary>What sort of ground this is, in the words the rules are written in.</summary>
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

    /// <summary>
    /// Whether the Baron has gone to this square, is on his way, or has not been sent.
    ///
    /// The last of those is only worth saying about ground bad enough to deserve him: "nobody is going to
    /// that meadow" is true of almost every square on the island and tells nobody anything.
    /// </summary>
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

            // Pale until the board has enough behind it to be believed — see BotCommons.Confidence, which is
            // the same number the arithmetic uses to decide how much of the answer this patch is.
            builder.AddLabel(370, y, settled >= 4 ? Ink : Bad, $"{settled}");
            builder.AddLabel(430, y, minded > 0 ? Good : Ink, $"{minded}");
        }

        if (known.Count == 0)
        {
            builder.AddLabel(14, 68, Bad, "The population has not found out anything about anywhere yet");
        }

        // <b>What every trade says it is worth against what it turned out to be worth.</b> This is the column
        // that changes what somebody does about the shard rather than what they think of it: a row where the
        // claim is far above the payment is a constant in the source that stopped being true, and the whole
        // population has been chasing it. Sorted by that gap, worst overstatement first, because an
        // overstatement sends everybody at work that pays nothing and an understatement only surprises them.
        var gaps = BotCommons.Gaps(Rows);

        for (var i = 0; i < gaps.Count; i++)
        {
            var (kind, claimed, measured, settled, minded) = gaps[i];
            var y = 66 + i * (RowHeight + 8);

            builder.AddLabelCropped(520, y, 90, 20, minded > 0 ? Good : Ink, kind);
            builder.AddLabel(620, y, Ink, $"{claimed:F0}");

            // Red when the claim is more than half again what the work pays: that is a number worth going and
            // looking at, and anything less is the ordinary noise of a shard that changes.
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

    /// <summary>How many stalls one press of the city's button clears.</summary>
    private const int CrownLots = 10;

    /// <summary>
    /// The city, which is to say the only demand on this shard that does not come out of a monster's purse.
    ///
    /// <para>
    /// One button, and it does exactly what the label says: buys ten stalls outright at whatever the sellers
    /// are asking, and pays with money that did not exist a moment earlier. That is the point of it. Every
    /// coin here otherwise enters through a corpse, so the population's market is sixteen bots passing the
    /// same purse round while their stalls fill with goods none of them wants; an outside buyer is what turns
    /// production into income. It also teaches prices, because the purchase is booked exactly as a bot's is.
    /// </para>
    ///
    /// <para>
    /// It stays a button rather than becoming a timer on purpose: printed money is a decision somebody should
    /// have to make, and be able to stop making, one press at a time.
    /// </para>
    /// </summary>
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

    /// <summary>The button's work, said out loud to whoever pressed it and written down for everyone else.</summary>
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

        // <b>Every tab needs a number here and a case below, and the fallback hides a missing one.</b> The
        // arm reads "anything else is the Bots tab", so a tab added without its number does not fail — it
        // quietly becomes a second button for Bots, which is exactly what happened to Known and is
        // indistinguishable from a tab that will not open.
        builder.AddButton(
            x,
            12,
            4005,
            4007,
            tab switch { MarketTab => 2, NeedsTab => 6, CrownTab => 7, KnownTab => 9, QuadTab => 10, _ => 1 }
        );
        builder.AddLabel(x + 20, 12, Ink, name);
    }

    public override void OnResponse(NetState sender, in RelayInfo info)
    {
        var from = sender?.Mobile;

        // Checked again here, and not only in the command: a gump response is a packet, and a packet can be
        // sent by anybody who has ever seen this window.
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

    /// <summary>
    /// The only way in. Everything it checks is checked before the window exists, so the window is never
    /// sent empty and never sent to somebody who cannot use it.
    /// </summary>
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

    /// <summary>The bots that exist, holes left by deleted ones removed.</summary>
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

    /// <summary>
    /// The stalls that actually have something on them.
    ///
    /// <para>
    /// <b>A stall that has sold out is not a lot, and showing it as one made the market unreadable.</b> An
    /// empty stall is deliberately kept for an hour — it is the seller's remembered price and its sales
    /// history, and a miner coming back with a second load tops the same pitch up and inherits both, which
    /// is real and is used. But that is a fact about the <em>seller</em>, not a thing anybody can buy: the
    /// trade itself has always skipped them, so every red nought on this page was a row that could never be
    /// clicked, crowding out the rows that could. Kept in the market and left off the board.
    /// </para>
    /// </summary>
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

    /// <summary>Red below the mark, plain above it. Colour is the only thing a table of numbers cannot say.</summary>
    private static int Shade(double value, double mark) => value < mark ? Bad : Ink;
}
