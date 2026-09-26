using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Server.BotAI.V2;
using Server.Logging;
using Server.Text;

namespace Server.BotAI.Mind;

/// <summary>
/// The marshal of events: the fourth of Argus's watchers, who organises rather than diagnoses.
///
/// <para>
/// <b>Patrick's order of 16.09.2026, evening:</b> the guilds' masters are ordinary bots; the events — camps,
/// tournaments, errands, orders — are the business of one thinking bot in Argus's squad. Every
/// <see cref="EveryMs"/> it is shown the population's numbers, the board of errands, the treasury, the tournament
/// and the revels' ledger, and asked for one event or none: a revel (one trade worth ×3 for twelve minutes with a
/// prize), a camp (a revel with an orc camp raised where it says), a tournament, an errand on the board (kill,
/// gather or scout, paid from the treasury), a standing order, a bounty on a square, a price on a head, or a fair.
/// </para>
///
/// <para>
/// <b>It invents no mechanism.</b> Every one of these is a verb the door already had, so what the marshal does is
/// what a hand at the keyboard could do, on a clock, with a reason written to the watchers' log. While it stands the
/// revels are its business too, and Argus goes back to watching.
/// </para>
/// </summary>
public static class BotMarshal
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMarshal));

    public static string Name { get; set; } = "Hermes";

    public static bool Running { get; set; } = true;

    public static int EveryMs { get; set; } = 600000;

    public static int Hue { get; set; } = 53;

    public static BotWatcher Standing { get; internal set; }

    private static long _thoughtTick;

    public static long Asked { get; private set; }

    public static long Events { get; private set; }

    public static long Nothings { get; private set; }

    public static long Unread { get; private set; }

    public static long Refused { get; private set; }

    private static string _last;

    public static int RefusedRestMs { get; set; } = 1500000;

    private static string _refusedEvent;

    private static long _refusedTick;

    private static string _refusedWhy;

    public static long Repeated { get; private set; }

    public static bool Due(long now) => Running && Standing != null && now - _thoughtTick >= EveryMs;

    public static void Woke(long now) => _thoughtTick = now;

    public static string Schema => Build();

    private static List<string> _open;

    private static long _openTick;

    private static readonly string[] EventWords = ["nothing", "revel", "camp", "tourney", "errand", "order", "bounty", "head", "fair"];

    private static readonly string[] ErrandKinds = ["kill", "gather", "scout"];

    private static readonly string[] Gathered =
    [
        "IronOre", "Log", "Hides", "Leather", "RawRibs", "Feather", "Wool", "Garlic", "Ginseng", "MandrakeRoot", "Nightshade",
        "Bloodmoss", "SpidersSilk", "BlackPearl", "SulfurousAsh"
    ];

    private const int MostX = 7167;

    private const int MostY = 4095;

    private static string Build()
    {
        var open = _open != null && Core.TickCount - _openTick < 5000 ? _open : Open();
        var stalls = Stalls(30);
        var buffer = new MemoryStream();

        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteStartArray("anyOf");

            for (var i = 0; i < open.Count; i++)
            {
                switch (open[i])
                {
                    case "nothing":
                        new Shape(w, "nothing").Close(false);

                        break;

                    case "revel":
                        new Shape(w, "revel").Words("kind", BotRevel.Trades).Number("pay", 0, BotRevel.MostPrize).Close();

                        break;

                    case "camp":
                        new Shape(w, "camp").Number("pay", 0, BotRevel.MostPrize).Number("x", 0, MostX).Number("y", 0, MostY).Close();

                        break;

                    case "tourney":
                        new Shape(w, "tourney").Close();

                        break;

                    case "errand":
                        {
                            var pays = Math.Max(1, Math.Min(BotQuests.MostReward, BotCity.Purse));
                            var creatures = BotLairs.Kinds(BotPopulation.Home);

                            if (creatures.Count > 0)
                            {
                                new Shape(w, "errand").Words("kind", ["kill"]).Words("name", creatures)
                                    .Number("number", 1, BotQuests.MostAmount).Number("pay", 1, pays).Close();
                            }

                            var goods = Goods(stalls);

                            if (goods.Count > 0)
                            {
                                new Shape(w, "errand").Words("kind", ["gather"]).Words("name", goods)
                                    .Number("number", 1, BotQuests.MostAmount).Number("pay", 1, pays)
                                    .Number("x", -1, MostX).Number("y", -1, MostY).Close();
                            }

                            new Shape(w, "errand").Words("kind", ["scout"]).Number("pay", 1, pays)
                                .Number("x", -1, MostX).Number("y", -1, MostY).Close();

                            break;
                        }

                    case "order":
                        if (stalls.Count > 0)
                        {
                            new Shape(w, "order").Words("name", stalls).Number("number", 1, BotQuests.MostAmount)
                                .Number("pay", 1, 1000).Close();
                        }

                        break;

                    case "bounty":
                        new Shape(w, "bounty").Number("pay", 1, Math.Max(1, Math.Min(BotCity.Cap, BotCity.Purse)))
                            .Number("x", 0, MostX).Number("y", 0, MostY).Close();

                        break;

                    case "head":
                        {
                            var reds = Reds();

                            if (reds.Count > 0)
                            {
                                new Shape(w, "head").Words("name", reds)
                                    .Number("pay", 1, Math.Max(1, Math.Min(BotCity.Cap, BotCity.Purse))).Close();
                            }
                        }

                        break;

                    case "fair":
                        new Shape(w, "fair").Number("number", 10, 120).Close();

                        break;
                }
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// One event's shape in the form, its fields written and required in alphabetical order.
    ///
    /// <para>
    /// <b>Alphabetical because that is the order the model is held to.</b> Asked with thinking on, Ollama 0.34 sorts a
    /// schema's properties by name before the answer is constrained (a Go map is written out sorted): deepseek-r1:14b,
    /// asked on 17.09.2026 for "a standing order for 10 Leather", answered <c>{"event": "errand", "gold": …, "kind":
    /// "scout", …}</c> and then a bounty — every shape whose first field sorted before "event" ("amount", in the kill, the
    /// gather and the order) was closed to an answer that began with "event", so build 102's form left Hermes the scout, the
    /// bounty and the events with no amount. Its answers at 11:25, 11:49 and 12:00 were scouts whose reasons were
    /// standing orders. The names are chosen so the sorted order is the answer's order — event, kind, name, number, pay,
    /// say, why, x, y — and a qwen3 asked without thinking, which keeps the written order, is given the same.
    /// </para>
    /// </summary>
    private sealed class Shape
    {
        private readonly Utf8JsonWriter _w;

        private readonly List<(string Field, Action<Utf8JsonWriter> Write)> _fields = [];

        public Shape(Utf8JsonWriter w, string what)
        {
            _w = w;

            _fields.Add(("event", x => Enumerated(x, [what])));
        }

        public Shape Words(string field, IReadOnlyList<string> words)
        {
            _fields.Add((field, x => Enumerated(x, words)));

            return this;
        }

        public Shape Number(string field, int least, int most)
        {
            _fields.Add(
                (field, x =>
                    {
                        x.WriteString("type", "integer");
                        x.WriteNumber("minimum", least);
                        x.WriteNumber("maximum", Math.Max(least, most));
                    })
            );

            return this;
        }

        private static void Enumerated(Utf8JsonWriter x, IReadOnlyList<string> words)
        {
            x.WriteString("type", "string");
            x.WriteStartArray("enum");

            for (var i = 0; i < words.Count; i++)
            {
                x.WriteStringValue(words[i]);
            }

            x.WriteEndArray();
        }

        private static void Text(Utf8JsonWriter x) => x.WriteString("type", "string");

        public void Close(bool says = true)
        {
            if (says)
            {
                _fields.Add(("say", Text));
            }

            _fields.Add(("why", Text));
            _fields.Sort((a, b) => string.CompareOrdinal(a.Field, b.Field));

            _w.WriteStartObject();
            _w.WriteString("type", "object");
            _w.WriteStartObject("properties");

            for (var i = 0; i < _fields.Count; i++)
            {
                _w.WriteStartObject(_fields[i].Field);
                _fields[i].Write(_w);
                _w.WriteEndObject();
            }

            _w.WriteEndObject();
            _w.WriteStartArray("required");

            for (var i = 0; i < _fields.Count; i++)
            {
                _w.WriteStringValue(_fields[i].Field);
            }

            _w.WriteEndArray();
            _w.WriteEndObject();
        }
    }

    private static List<string> Open()
    {
        List<string> open = [];
        var paid = BotCity.Running && BotCity.Purse >= 1;

        for (var i = 0; i < EventWords.Length; i++)
        {
            var what = EventWords[i];

            if (what != "nothing" && what == _refusedEvent && Core.TickCount - _refusedTick < RefusedRestMs)
            {
                continue;
            }

            var can = what switch
            {
                "revel" or "camp" => !BotRevel.Running,
                "tourney" => BotTourney.Ready,
                "errand" => paid && BotQuests.Running,
                "order" => BotCity.Running && Stalls(1).Count > 0,
                "bounty" => paid,
                "head" => paid && Reds().Count > 0,
                "fair" => BotCity.Running && BotCity.Purse >= BotCity.FairFloor && !BotCity.Fairing,
                _ => true
            };

            if (can)
            {
                open.Add(what);
            }
        }

        _open = open;
        _openTick = Core.TickCount;

        return open;
    }

    private static List<string> Reds()
    {
        List<string> reds = [];
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false } bot && BotOutlaw.IsRed(bot) && !reds.Contains(bot.Name))
            {
                reds.Add(bot.Name);
            }
        }

        return reds;
    }

    private static List<string> Goods(List<string> stalls)
    {
        List<string> goods = [..stalls];

        for (var i = 0; i < Gathered.Length; i++)
        {
            var type = AssemblyHandler.FindTypeByName(Gathered[i]);

            if (type != null && typeof(Item).IsAssignableFrom(type) && !goods.Contains(type.Name))
            {
                goods.Add(type.Name);
            }
        }

        return goods;
    }

    private static string Offered(int most)
    {
        var names = Stalls(most);

        return names.Count == 0 ? "nothing on the stalls" : string.Join(", ", names);
    }

    private static List<string> Stalls(int most)
    {
        var listings = BotAuction.Listings;
        Dictionary<string, int> amounts = [];

        for (var i = 0; i < listings.Count; i++)
        {
            var listing = listings[i];

            if (listing?.Kind == null || listing.Amount <= 0)
            {
                continue;
            }

            amounts.TryGetValue(listing.Kind.Name, out var had);
            amounts[listing.Kind.Name] = had + listing.Amount;
        }

        List<(string Name, int Amount)> sorted = [];

        foreach (var (name, amount) in amounts)
        {
            sorted.Add((name, amount));
        }

        sorted.Sort((a, b) => b.Amount.CompareTo(a.Amount));

        List<string> names = [];

        for (var i = 0; i < sorted.Count && i < most; i++)
        {
            names.Add(sorted[i].Name);
        }

        return names;
    }

    public static string System(string name)
    {
        var home = BotPopulation.Where;

        return $"You are {name}, the marshal of events on a shard of autonomous bots, one of {BotVigil.Name}'s watchers. "
               + "Every so often you may set one event going, or none. 'event' is one of the events open to you this time, "
               + "which the end of your sight names; the others are these, for when they open again. "
               + "revel: one kind of work worth three times as much for twelve minutes, with a prize of 'pay' gold to whoever "
               + $"does most of it; 'kind' is one of {string.Join(", ", BotRevel.Trades)}. "
               + $"camp: the same, with an orc camp raised at 'x','y' between {BotRevel.NearestCamp} and "
               + $"{BotRevel.FurthestCamp} tiles from the population's home at ({home.X}, {home.Y}), for the population to "
               + "fight over; use 'kind' hunt. "
               + "tourney: a tournament, one against one, run by the watchers at the drill ground. "
               + "errand: an errand on the board, paid from the treasury when it is done: 'kind' is kill, gather or scout; "
               + "'name' is the creature's or the thing's type name (Ogre, Leather, RawRibs); 'number' how many; 'pay' the "
               + "reward in gold. A kill has no place: it is sought at the nearest place a spawner on the island keeps the "
               + "creature (one kept only in the dungeons is refused). A delivery's 'x','y' is where to bring the goods, or -1 "
               + "for home; a scout's is where to look, or -1 for the nearest ground outside the walls nobody has counted "
               + "lately — a scout is paid for standing at its place, so one inside the walls is refused. Any bot fit "
               + "for it may take it and sees it through; an errand "
               + "three takers give up with nothing done comes down and its gold goes back. A reward is weighed against what "
               + "the bots earn a minute at their other work, walk and work together. "
               + "order: a standing order: the city takes 'number' of 'name' off the stalls whenever it is offered at up "
               + "to 'pay' gold each. "
               + "bounty: 'pay' gold on the square at 'x','y'; the Baron marches there before anywhere else, and the company "
               + "that clears it is paid. "
               + "head: 'pay' gold on the head of the murderer named in 'name', paid to whoever catches it. "
               + "fair: for 'number' minutes the city takes what is on the stalls at 80 percent of the asking price, "
               + $"spending evenly over the minutes and keeping {BotCity.FairReserve}gp back; only when the treasury holds "
               + $"{BotCity.FairFloor}gp or more. "
               + $"Everything but a revel is paid from the city's treasury, which holds {BotCity.Purse}gp of "
               + $"{BotCity.Cap} and fills at {BotCity.MintPerHour}gp an hour; a revel's prize comes from the watchers' own "
               + $"purse of {BotRevel.Purse}gp. Choose what the numbers ask for: idle bots want an errand or a revel, a "
               + "market with goods sitting unsold wants a fair or an order, a square where bots die wants a bounty, a "
               + "murderer at large wants a price on its head, a quiet and healthy evening wants a tournament or nothing. "
               + "Answer nothing when the shard is busy and healthy; that is a good answer and you should give it often. "
               + "Do not repeat an event whose last outcome, in what you are shown, was that nobody came. "
               + "'say' is what you shout to the island, in a herald's voice, one sentence. 'why' is your reasoning, for "
               + "the log. Write the fields your event takes, numbers as whole numbers, and an errand's x and y as -1 when "
               + "it has no place. What came of your last answer is shown to you; if it was refused, the reason is given, "
               + "and the same answer will be refused again.";
    }

    public static string Sight()
    {
        using var say = ValueStringBuilder.Create(2048);

        say.Append("THE MARSHAL'S OWN SIGHT. The board of errands: ");
        say.Append(BotQuests.Tell());
        say.Append(". The city: ");
        say.Append(BotCity.Describe());
        say.Append(". The tournament: ");
        say.Append(BotTourney.Describe());
        say.Append(". Past revels: ");
        say.Append(BotRevel.Ledger());
        say.Append(". Murderers: ");
        say.Append(BotOutlaw.Describe());
        say.Append(". Idle: ");
        say.Append(BotVigil.Loitering());
        say.Append(". The island's squares: ");
        say.Append(BotQuad.Describe());
        say.Append(". An order's thing is named as the city knows it, as these on the stalls now are: ");
        say.Append(Offered(15));
        say.Append(". The events open to you this time: ");
        say.Append(string.Join(", ", Open()));
        say.Append(". Your last answer: ");
        say.Append(_last ?? "none yet");
        say.Append('.');

        return say.ToString();
    }

    public static string Act(BotMarshalPlan plan, Map map)
    {
        Asked++;

        if (plan == null)
        {
            Unread++;

            return null;
        }

        var what = (plan.Event ?? "nothing").Trim().ToLowerInvariant();
        var by = Name;
        var stamp = DateTime.Now.ToString("HH:mm");
        var before = Signature();
        string answer;

        if (what != "nothing" && what == _refusedEvent && Core.TickCount - _refusedTick < RefusedRestMs)
        {
            Refused++;
            Repeated++;
            _last = $"at {stamp}, {what} again, turned away without trying: it was refused {(Core.TickCount - _refusedTick) / 60000} minutes ago ({_refusedWhy}); choose another event, or nothing";

            return $"{what} turned away: refused {(Core.TickCount - _refusedTick) / 60000} minutes ago ({plan.Why})";
        }

        switch (what)
        {
            case "nothing":
                Nothings++;
                _last = $"at {stamp}, nothing";

                return $"nothing this time: {plan.Why}";

            case "revel":
                if (!BotRevel.Known(plan.Kind))
                {
                    Refused++;
                    _last = $"at {stamp}, a revel, refused: \"{plan.Kind}\" is not work anybody takes; kind must be one of {string.Join(", ", BotRevel.Trades)}";

                    return $"no revel: \"{plan.Kind}\" is not work anybody takes ({plan.Why})";
                }

                answer = BotRevel.Declare(plan.Kind, plan.Gold, plan.Say, plan.Why, false, Point3D.Zero);

                break;

            case "camp":
                answer = BotRevel.Declare(BotRevel.Known(plan.Kind) ? plan.Kind : "hunt", plan.Gold, plan.Say, plan.Why, true, plan.Spot);

                break;

            case "tourney":
                answer = BotTourney.Start(by);

                break;

            case "errand":
                answer = BotQuests.PostByName(
                    plan.Kind,
                    plan.What,
                    plan.Amount,
                    plan.Gold,
                    map,
                    plan.Spot != Point3D.Zero ? plan.Spot.X : -1,
                    plan.Spot != Point3D.Zero ? plan.Spot.Y : -1,
                    by
                );

                break;

            case "order":
                answer = BotCity.Want(plan.What ?? "", plan.Amount, plan.Gold, by);

                break;

            case "bounty":
                if (plan.Spot == Point3D.Zero)
                {
                    Refused++;
                    _last = $"at {stamp}, a bounty, refused: it needs x and y";

                    return $"no bounty: it needs x and y ({plan.Why})";
                }

                answer = BotCity.Bounty(map, plan.Spot.X, plan.Spot.Y, plan.Gold, by);

                break;

            case "head":
                {
                    var red = Named(plan.What);

                    if (red == null)
                    {
                        Refused++;
                        _last = $"at {stamp}, a price on a head, refused: no bot of ours is called \"{plan.What}\"";

                        return $"no price on a head: no bot of ours is called {plan.What} ({plan.Why})";
                    }

                    answer = BotCity.Head(red, plan.Gold, by);

                    break;
                }

            case "fair":
                if (BotCity.Purse < BotCity.FairFloor)
                {
                    Refused++;
                    Refuse(what, $"the treasury held {BotCity.Purse}gp of the {BotCity.FairFloor}gp a fair wants");
                    _last = $"at {stamp}, a fair, refused: the treasury holds {BotCity.Purse}gp and a fair is held at {BotCity.FairFloor}gp or more";

                    return $"no fair: the treasury holds {BotCity.Purse}gp of the {BotCity.FairFloor}gp a fair wants ({plan.Why})";
                }

                answer = BotCity.Fair(plan.Amount > 0 ? plan.Amount : Math.Max(1, BotCity.FairMs / 60000), BotCity.FairShare, by);

                break;

            default:
                Refused++;
                _last = $"at {stamp}, \"{plan.Event}\", refused: no such event";

                return $"\"{plan.Event}\" is no event the marshal knows ({plan.Why})";
        }

        if (Signature() == before)
        {
            Refused++;
            _last = $"at {stamp}, {what}, refused: {answer}";

            logger.Information("{Name} asked for {Event} and the city refused it: {Answer} ({Why})", Name, what, answer, plan.Why);

            return $"{what} refused: {answer} ({plan.Why})";
        }

        Events++;
        _last = $"at {stamp}, {what}: {answer}";

        logger.Information("{Name} set an event going: {Event} — {Answer} ({Why})", Name, what, answer, plan.Why);

        return $"{what}: {answer} ({plan.Why})";
    }

    private static void Refuse(string what, string why)
    {
        _refusedEvent = what;
        _refusedTick = Core.TickCount;
        _refusedWhy = why;
    }

    private static (long Revels, long Tourneys, long Errands, long City) Signature() =>
        (BotRevel.Declared, BotTourney.Called, BotQuests.Posted, BotCity.Changes);

    private static BotMobile Named(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is { Deleted: false } && string.Equals(bot.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return bot;
            }
        }

        return null;
    }

    public static string Describe() =>
        Standing == null
            ? "no marshal stands"
            : $"{Name} was asked {Asked} times: {Events} events set going, {Nothings} times nothing, {Refused} refused ({Repeated} of them the same event again too soon), {Unread} answers unreadable";

    public static void Forget()
    {
        Asked = 0;
        Events = 0;
        Nothings = 0;
        Unread = 0;
        Refused = 0;
    }
}

/// <summary>What the marshal answered. A record of its own, for the same reason <see cref="BotRevelPlan"/> is one.</summary>
public sealed class BotMarshalPlan
{
    public string Event { get; init; }

    public string Kind { get; init; }

    public string What { get; init; }

    public int Amount { get; init; }

    public int Gold { get; init; }

    public Point3D Spot { get; init; }

    public string Say { get; init; }

    public string Why { get; init; }

    public static BotMarshalPlan Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new BotMarshalPlan
            {
                Event = Word(root, "event"),
                Kind = Word(root, "kind"),
                What = Word(root, "name") ?? Word(root, "what"),
                Amount = root.TryGetProperty("number", out _) ? Whole(root, "number") : Whole(root, "amount"),
                Gold = root.TryGetProperty("pay", out _) ? Whole(root, "pay") : Whole(root, "gold"),
                Say = Word(root, "say"),
                Why = Word(root, "why"),
                Spot = Tile(root, "x", out var ax) && Tile(root, "y", out var ay) && ax >= 0 && ay >= 0
                    ? new Point3D(ax, ay, 0)
                    : Point3D.Zero
            };
        }
        catch
        {
            return null;
        }
    }

    private static string Word(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Tile(JsonElement root, string name, out int value)
    {
        value = 0;

        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (element.TryGetInt32(out value))
        {
            return true;
        }

        if (!element.TryGetDouble(out var real) || real is <= int.MinValue or >= int.MaxValue)
        {
            return false;
        }

        value = (int)Math.Round(real);

        return true;
    }

    private static int Whole(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return 0;
        }

        if (value.TryGetInt32(out var got))
        {
            return got;
        }

        return value.TryGetDouble(out var real) && real is > int.MinValue and < int.MaxValue ? (int)Math.Round(real) : 0;
    }
}
