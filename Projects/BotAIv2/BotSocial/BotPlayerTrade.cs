using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

public static class BotPlayerTrade
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPlayerTrade));

    public static bool Running { get; set; } = true;

    public static int WindowMs { get; set; } = 30000;

    public static int MostUnits { get; set; } = 100;

    public static int MeetTiles { get; set; } = 3;

    public static long Asked { get; private set; }

    public static long Unfollowed { get; private set; }

    public static long Quoted { get; private set; }

    public static long Countered { get; private set; }

    public static long Accepted { get; private set; }

    public static long SoldUnits { get; private set; }

    public static long GoldMoved { get; private set; }

    public static long RefusedPoor { get; private set; }

    public static long RefusedStock { get; private set; }

    public static long PackFull { get; private set; }

    public static long Cancelled { get; private set; }

    private static readonly string[] BuyWords = ["buy", "куплю", "купить"];

    private static readonly string[] AcceptWords = ["accept", "deal", "согласен", "беру"];

    private sealed class Quote
    {
        public BotMobile Bot;

        public Mobile Buyer;

        public Type Kind;

        public string Label;

        public int Units;

        public int Price;

        public long At;
    }

    private static readonly Dictionary<string, Quote> _standing = new(StringComparer.Ordinal);

    public static bool AsksBuy(string said)
    {
        for (var i = 0; i < BuyWords.Length; i++)
        {
            if (said.StartsWith(BuyWords[i] + " ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool SaysAccept(string said)
    {
        for (var i = 0; i < AcceptWords.Length; i++)
        {
            if (string.Equals(said, AcceptWords[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static void Buy(BotMobile bot, Mobile player, string said)
    {
        if (!Running || bot is not { Deleted: false, Alive: true } || player == null)
        {
            return;
        }

        Asked++;

        var parts = said.Split(' ');

        if (parts.Length < 4
            || !int.TryParse(parts[1], out var units) || units <= 0
            || !int.TryParse(parts[^1], out var offer) || offer <= 0)
        {
            Unfollowed++;

            BotVoice.Aloud(bot, "Name a number, a thing and a price: buy 20 arrow 3.");

            return;
        }

        var label = parts[2].Trim('s');
        var kind = CarriedType(bot, label);

        if (kind == null)
        {
            RefusedStock++;

            BotVoice.Aloud(bot, $"I carry no {parts[2]} to sell you.");

            return;
        }

        var carried = Carried(bot, kind);
        var selling = Math.Min(Math.Min(units, carried), MostUnits);

        if (selling < units)
        {
            RefusedStock++;

            BotVoice.Aloud(bot, carried <= 0
                ? $"My {parts[2]} are all spent, I am afraid."
                : $"I have {carried} {parts[2]} left, not {units}. Say accept and they are yours.");

            if (carried <= 0)
            {
                return;
            }
        }

        var island = IslandPrice(kind);
        var price = Math.Max(offer, island);

        _standing[player.Name] = new Quote { Bot = bot, Buyer = player, Kind = kind, Label = parts[2], Units = selling, Price = price, At = Core.TickCount };

        if (price > offer)
        {
            Countered++;

            BotVoice.Aloud(bot, $"The island pays {price} for a {parts[2]}. {price} apiece, or nothing. Say accept.");
        }
        else
        {
            Quoted++;

            BotVoice.Aloud(bot, $"{selling} {parts[2]} at {price} apiece, {selling * price} gold in all. Say accept.");
        }

        logger.Information("{Bot} quoted {Player}: {Units} {Thing} at {Price} (asked {Offer}, island {Island})", bot.Name, player.Name, selling, parts[2], price, offer, island);
    }

    public static bool Accept(Mobile player)
    {
        if (!Running || player == null || !_standing.TryGetValue(player.Name, out var quote))
        {
            return false;
        }

        _standing.Remove(player.Name);

        if (quote.Bot is not { Deleted: false, Alive: true } || quote.Buyer is not { Deleted: false, Alive: true })
        {
            Cancelled++;

            return true;
        }

        if (Core.TickCount - quote.At > Math.Max(5000, WindowMs))
        {
            Cancelled++;

            BotVoice.Aloud(quote.Bot, "The moment has passed. Ask me again.");

            return true;
        }

        var bot = quote.Bot;

        if (bot.Map != player.Map || !bot.InRange(player.Location, MeetTiles))
        {
            Cancelled++;

            BotVoice.Aloud(bot, $"Come closer, {player.Name}. I sell hand to hand, not across the field.");

            return true;
        }

        var bill = quote.Units * quote.Price;

        if (GoldIn(player.Backpack) < bill)
        {
            RefusedPoor++;

            BotVoice.Aloud(bot, $"You are short of the {bill} gold we said, {player.Name}.");

            return true;
        }

        var given = Hand(bot, player, quote.Kind, quote.Units);

        if (given <= 0)
        {
            RefusedStock++;

            BotVoice.Aloud(bot, $"My {quote.Label} are gone from my pack since we spoke. Forgive me.");

            return true;
        }

        if (!TakeGold(player.Backpack, given * quote.Price))
        {

            logger.Error("{Bot} could not take {Bill} gold from {Player} after handing {Units} {Thing}", bot.Name, given * quote.Price, player.Name, given, quote.Label);
        }
        else
        {
            BotMarket.Pay(bot, given * quote.Price);
        }

        Accepted++;
        SoldUnits += given;
        GoldMoved += given * quote.Price;

        BotTies.MoveToward(bot, player.Name, 3.0, "sold to them hand to hand");

        if (BotLives.Of(bot) is { } life)
        {
            BotBiography.Keep(
                life,
                "sale",
                player.Name,
                $"A person bought {given} {quote.Label} off me for {given * quote.Price}gp, hand to hand.",
                15.0 + Math.Min(25.0, given * quote.Price / 40.0),
                given * quote.Price
            );
        }

        BotVoice.Aloud(bot, $"There: {given} {quote.Label}, and glad of the custom, {player.Name}.");

        logger.Information("{Bot} sold {Units} {Thing} to {Player} for {Gold} gold", bot.Name, given, quote.Label, player.Name, given * quote.Price);

        return true;
    }

    public static string Describe() =>
        !Running
            ? "the bots do not sell to a person by voice"
            : $"{Asked} asks ({Unfollowed} not followed, {RefusedStock} nothing carried), {Quoted} quoted, {Countered} countered, "
              + $"{Accepted} taken ({SoldUnits} things, {GoldMoved} gold), {RefusedPoor} short of gold, {PackFull} packs too full, {Cancelled} lapsed or afar";

    public static void Forget()
    {
        Asked = 0;
        Unfollowed = 0;
        Quoted = 0;
        Countered = 0;
        Accepted = 0;
        SoldUnits = 0;
        GoldMoved = 0;
        RefusedPoor = 0;
        RefusedStock = 0;
        PackFull = 0;
        Cancelled = 0;
        _standing.Clear();
    }

    private static Type CarriedType(BotMobile bot, string word)
    {
        var pack = bot.Backpack;

        if (pack == null || word.Length == 0)
        {
            return null;
        }

        foreach (var item in pack.EnumerateItems())
        {
            if (item.Deleted)
            {
                continue;
            }

            var name = item.GetType().Name;

            if (name.Equals(word, StringComparison.OrdinalIgnoreCase) || name.Equals(word + "s", StringComparison.OrdinalIgnoreCase))
            {
                return item.GetType();
            }
        }

        return null;
    }

    private static int Carried(BotMobile bot, Type kind)
    {
        var pack = bot.Backpack;

        if (pack == null)
        {
            return 0;
        }

        var total = 0;

        foreach (var item in pack.EnumerateItems())
        {
            if (!item.Deleted && item.GetType() == kind)
            {
                total += Math.Max(1, item.Amount);
            }
        }

        return total;
    }

    private static int IslandPrice(Type kind)
    {
        var wants = BotMarket.Wants;
        var best = 0;

        for (var i = 0; i < wants.Count; i++)
        {
            if (wants[i] is { IsOpen: true } want && want.Kind == kind && want.Offer > best)
            {
                best = want.Offer;
            }
        }

        return best;
    }

    private static int Hand(BotMobile bot, Mobile player, Type kind, int units)
    {
        var from = bot.Backpack;
        var into = player.Backpack;

        if (from == null || into == null)
        {
            return 0;
        }

        var given = 0;

        foreach (var item in from.EnumerateItems())
        {
            if (given >= units)
            {
                break;
            }

            if (item.Deleted || item.GetType() != kind)
            {
                continue;
            }

            var take = Math.Min(units - given, Math.Max(1, item.Amount));

            if (take < Math.Max(1, item.Amount) && (!item.Stackable || Mobile.LiftItemDupe(item, take) == null))
            {
                continue;
            }

            if (!into.TryDropItem(player, item, false))
            {
                PackFull++;

                break;
            }

            given += Math.Max(1, item.Amount);
        }

        return given;
    }

    private static int GoldIn(Container pack)
    {
        if (pack == null)
        {
            return 0;
        }

        var total = 0;

        foreach (var gold in pack.FindItemsByType<Gold>())
        {
            total += gold.Amount;
        }

        return total;
    }

    private static bool TakeGold(Container pack, int bill)
    {
        foreach (var gold in pack.FindItemsByType<Gold>())
        {
            if (bill <= 0)
            {
                break;
            }

            var take = Math.Min(bill, gold.Amount);

            gold.Amount -= take;
            bill -= take;

            if (gold.Amount <= 0)
            {
                gold.Delete();
            }
        }

        return bill <= 0;
    }
}
