using System;
using System.Collections.Generic;
using Server.BotAI.V2;
using Server.Engines.Craft;
using Server.Text;
using Server.Items;
using Server.Mobiles;

namespace Server.BotAI.Mind;

/// <summary>
/// The world as one bot can see it, written out for the model.
///
/// <para>
/// <b>Every line here is a defect surface, and that is not a figure of speech.</b> The first version of this
/// wrote "Carrying 39 of 215 stones" — the engine's own unit for weight — and the model read it as cargo:
/// the first plan that bot ever made in its life was to walk to the market and sell its stones. Nothing was
/// wrong with the code, the data or the schema. The sentence was wrong. So each fact below is written the
/// way a person would say it, units named, and nothing is included that the bot could not actually be said
/// to know.
/// </para>
///
/// <para>
/// <b>A weight is not a cargo, and the very first live answer proved it again.</b> Told only that its pack
/// was "12% full by weight", the model chose to go and sell what it was carrying — of an empty pack, on a
/// bot that had just been born. The percentage was true and it was not the fact being asked about. So the
/// count of things is said beside it: how heavy the pack is answers whether there is room, and how many
/// things are in it answers whether there is anything to sell.
/// </para>
///
/// <para>
/// <b>And it is given what the shard already knows.</b> A mind told only "you are at 1440, 1470" will decide
/// to go and look for a forge while standing next to one — the second lesson of the first run. What the
/// population has already surveyed is a fact about the bot's world, so it goes in the state rather than
/// being left for the model to rediscover by walking.
/// </para>
/// </summary>
public static class BotMindSight
{
    public static int Notice { get; set; } = 14;

    public static int Recall { get; set; } = 6;

    public static string System(BotMind mind) =>
        $"""
         You are the mind of {mind.Name}, a {mind.Trade} living on an Ultima Online shard among {Others()} other
         bots who work, trade and fight for a living. {Thinkers()} of the others think as you do and you can
         hear them; the rest work by instinct. You are not narrating a story and you are not talking to a
         person: you choose this bot's next piece of work and nothing else.

         The shard runs the work itself — walking, swinging, digging, buying. Your one decision is which trade
         to take up next, and how much you expect it to be worth, in gold-equivalent per minute. Skill and
         goods count towards that as well as coin.

         Every trade on the list has real work in it right now — that is why it is on the list — and which
         one is taken up is settled by the shard weighing the work itself. Your number changes nothing about
         that: it cannot win you the work and it cannot lose you the work. It is a forecast and only a
         forecast, checked afterwards against what the work actually paid, and you are shown the comparison.
         There is nothing to be gained by predicting low or high, so predict what you believe.

         Fighting and staying alive are not yours to decide. Those are reflexes and they happen without you.
         {Calling(mind)}
         {Charter()}
         """;

    private static string Charter() =>
        """
        You are also the maker of your guild, which means you founded it and it cannot exist without you. A
        guild is worth something to its members when it has a hall, ground of its own, and money in the
        common purse; a member of a guild that has none of those may walk out, and does. So the guild is
        yours to build, and the questions are: what does it lack, what can it get next, and who among the
        other guilds is worth trading with or worth minding.

        With every choice you may also charge your whole band, in three ways, all optional:
        'gather' names a trade you want the band collecting at, 'make' names one you want them making at, and
        'marchx'/'marchy' name a spot on the map you want them all near. Each is a price, not an order: it
        makes that work worth half again to your members, and one with better work in front of it will
        ignore you. An order stands for a quarter of an hour and then lapses. Say 'none' for gather and make,
        and nought for the coordinates, when the band should carry on as it is — which is most of the time. A
        band charged with something new every few minutes finishes nothing.

        You also choose who is in the band. When 'expel' and 'recruit' appear in your answer, you may name
        one member to put out and one bot outside every band to take on; they are the only two lists you may
        name from, and each name comes with how far along its trade that bot is. A band that is short of
        members cannot raise a hall, because a hall is paid for by a levy on the members; a band carrying
        somebody who is learning nothing is carrying it. Say 'none' for either when you are content. Using
        the power rests it for half an hour, so spend it on the clearest case rather than the first one.
        """;

    private static int Others() => Math.Max(0, BotPopulation.Bots.Count - 1);

    private static int Thinkers() => Math.Max(0, BotMinds.All.Count - 1);

    private static string Calling(BotMind mind) =>
        mind.Trade switch
        {
            "baron" =>
                """

                One more thing, and it is the whole of what makes you different from the others.

                You are a Baron. You are not making a living and you do not need money: you have a stipend,
                you take no share of anything your company kills, and every coin and every item off every
                corpse is divided among the five bots who came with you. That division is the only reason
                anybody follows you, so when you are asked what a harrowing is worth, the number you give is
                what you expect THEM to carry away, not what you will.

                What you are for is ground that has killed people. Squares where two or more have died stand
                on the board until somebody empties them, and nothing else on this island will: the others
                hunt where hunting pays, which is never the places that have proved they kill. You are the
                only bot who will walk into one on purpose, and you go with five behind you or you do not go.
                A harrowing ends when twenty things in it are dead or after forty minutes, and then that
                square comes off the board for good.

                When no ground is standing, walk your town. It pays nothing and it is meant to: it is where
                you are between harrowings, not a way of filling time you should feel bad about. Your only
                sorrow is the dead and the squares nobody has dealt with — never an empty purse, and never an
                idle afternoon.
                """,
            "crafter" =>
                """

                One more thing, and it is the whole of what makes you different from the others.

                YOU ARE ONE OF THE FOUR CRAFTERS, AND THERE ARE NO OTHERS. Nobody else on this island makes
                anything. Every weapon, every piece of armour, every arrow, every potion, every scroll and
                every meal is made by the four of you or it does not exist here. The others fight, gather and
                carry; you are the only ones who turn what they bring back into something worth having.

                READ YOUR STATE IN THIS ORDER, AND STOP AT THE FIRST THING YOU CAN DO.

                First, the board. A want there is a bot that has already paid for something it cannot make:
                the gold is lodged and waiting, so you are not gambling on being paid, you are collecting.
                Each row tells you which craft makes the thing, what skill it takes and what you have. If a
                row says you can make it now, that is your work and nothing on this list beats it. A want
                whose price has been raised again and again with nothing delivered is the loudest sentence
                this island can say: it has been trying to buy that for a long time and nobody could.

                Second, what the island is short of. Bare armour slots, shooters with nothing to shoot, bots
                that have not eaten — those numbers are counted at the moment you read them, and every one of
                them is a thing only you can fix. A bot with an empty slot is a bot that dies sooner; an
                archer with no arrows is a bow being carried for nothing. Nobody has ordered these. Make them
                anyway, put them on a stall, and the island pays you for them.

                Third, your skill, if it is what is stopping you. Skill counts as earnings on this shard, so
                an hour spent getting better is paid work, not a detour. Make the dearest thing you are
                allowed to attempt — your bench lists them — until the number moves. Do not practise a skill
                that is already at its target; it earns you nothing more.

                Fourth, material. You carry a pickaxe, an axe and the skill to use both, and the stalls list
                what everything costs. Buy it if it is on a stall and you can afford it; dig or cut it if it
                is not, or if buying costs more than the finished thing is worth. Ore, wood, leather and
                cloth all reach you either way.

                Fifth, selling. A stack of ingots in your pack is worth nothing to anybody. The same ingots
                on a stall, or turned into what somebody asked for, are the island getting richer, and that
                is what you are judged on. Put spare material where the others can buy it.

                THREE RULES THAT OVERRIDE ALL OF THAT.

                Do not make what has already failed to sell. Your own stalls tell you what has been cut in
                price and still sold nothing. That is the island answering you in money, and it is worth more
                than any forecast you can make.

                Do not stand at a bench another crafter is already at. You are shown what the other three are
                holding right now. Four of you on one trade is three of you wasted, and the wants nobody can
                fill will still be sitting there afterwards.

                Do not take work for a thing nothing on this island can produce. The board hides those from
                you already; if you find yourself reasoning about one anyway, you have invented it.

                Hunt, dig or cut when it is the shortest road to what you are making, or when the board is
                empty and the island is short of nothing. Not as a way of filling an afternoon.
                """,
            _ => ""
        };

    private static void Band(ref ValueStringBuilder sb, BotMobile body)
    {
        if (body?.Guild is not Server.Guilds.Guild guild)
        {
            sb.Append("\n\nYour band: you are in none.");

            return;
        }

        var hall = BotEstate.Hall(guild);
        var fund = BotEstate.Fund(guild);
        var ground = BotClaim.Holds(guild.Name);

        sb.Append("\n\nYour band, ");
        sb.Append(guild.Name);
        sb.Append(": ");
        sb.Append(guild.Members?.Count ?? 0);
        sb.Append(" of you, ");
        sb.Append(fund);
        sb.Append("gp in the common purse of the ");
        sb.Append(BotEstate.Price);
        sb.Append(" a hall costs, ");

        if (hall is { Deleted: false })
        {
            sb.Append("a hall at ");
            sb.Append(hall.X);
            sb.Append(',');
            sb.Append(hall.Y);
        }
        else
        {
            sb.Append("no hall");
        }

        sb.Append(", ");
        sb.Append(ground);
        sb.Append(" squares of ground held (");
        sb.Append(BotClaim.Free);
        sb.Append(" are free to take, then ");
        sb.Append(BotClaim.Price);
        sb.Append("gp each). Standing orders: ");
        sb.Append(BotCharter.Says(guild));
        sb.Append('.');

        var worst = 0.0;
        string sourest = null;

        foreach (var other in BotGuilds.Standing)
        {
            if (other == null || other == guild)
            {
                continue;
            }

            var held = BotRegard.Of(other.Name, guild.Name);

            if (held < worst)
            {
                worst = held;
                sourest = other.Name;
            }
        }

        if (sourest != null)
        {
            sb.Append(" Of the other bands, ");
            sb.Append(sourest);
            sb.Append(" thinks worst of you at ");
            sb.Append(worst.ToString("F0", global::System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(" (war is declared below ");
            sb.Append(BotRegard.Enmity.ToString("F0", global::System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(").");
        }

        if (guild.Enemies is { Count: > 0 })
        {
            sb.Append(" You are at war with ");
            sb.Append(guild.Enemies.Count);
            sb.Append(" of them.");
        }

        Roll(ref sb, guild);
    }

    private static void Roll(ref ValueStringBuilder sb, Server.Guilds.Guild guild)
    {
        var waits = BotRoster.Waits(guild);

        sb.Append(" Your roll: ");
        sb.Append(guild.Members?.Count ?? 0);
        sb.Append(" of the ");
        sb.Append(BotGuilds.Most);
        sb.Append(" you may hold");

        if (waits > 0 || !BotRoster.Free(guild))
        {
            sb.Append("; you may change it again in ");
            sb.Append(waits);
            sb.Append(" min.");

            return;
        }

        sb.Append(". You may put one member out and take one bot on, and doing either rests the power for ");
        sb.Append(BotRoster.EveryMs / 60000);
        sb.Append(" min. Judge them by how far along their trade they are, 0 to 1.");

        Names(ref sb, " Weakest of yours: ", BotRoster.Weakest(guild, BotRoster.Shortlist));
        Names(ref sb, " In no band: ", BotRoster.Candidates(guild, BotRoster.Shortlist));
    }

    private static void Names(ref ValueStringBuilder sb, string lead, List<BotMobile> who)
    {
        if (who.Count == 0)
        {
            return;
        }

        sb.Append(lead);

        for (var i = 0; i < who.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(who[i].Name);
            sb.Append(" the ");
            sb.Append(who[i].Class?.Name ?? "nothing");
            sb.Append(" at ");
            sb.Append(who[i].Progress.ToString("F2", global::System.Globalization.CultureInfo.InvariantCulture));
        }

        sb.Append('.');
    }

    public static int LastChars { get; private set; }

    public static bool Dump { get; set; }

    public static string State(BotMind mind, BotMobile body, IReadOnlyList<string> trades)
    {
        var sb = ValueStringBuilder.Create(2048);

        try
        {
            Body(ref sb, body);
            Around(ref sb, body);
            Ground(ref sb, mind, body);
            Bench(ref sb, mind, body);
            Board(ref sb, mind, body);
            Fellows(ref sb, mind, body);
            Materials(ref sb, mind);
            Island(ref sb, mind, body);
            Shelf(ref sb, mind, body);
            Past(ref sb, mind);
            Heard(ref sb, mind);
            Lessons(ref sb, mind);
            Band(ref sb, body);
            Offers(ref sb, trades);

            sb.Append("\nChoose one trade from the list, say what you expect it to be worth per minute, how many minutes you expect to spend on it, and why in one sentence.");
            sb.Append(" You may also put one short line in `say` for the other thinking bots to read — something you have found, somewhere worth coming to, something you have given up on. Leave it empty unless it is worth their attention.");
            sb.Append(" And if your band should be doing something different, set `gather`, `make` or `marchx`/`marchy`; otherwise leave them at none and nought.");

            var state = sb.ToString();

            LastChars = state.Length;

            if (Dump)
            {
                Dump = false;

                BotMindLog.Write(mind.Name, $"the whole of what it reads, {state.Length} characters", state);
            }

            return state;
        }
        finally
        {
            sb.Dispose();
        }
    }

    private static void Body(ref ValueStringBuilder sb, BotMobile body)
    {
        var hits = body.HitsMax > 0 ? body.Hits * 100 / body.HitsMax : 0;
        var mana = body.ManaMax > 0 ? body.Mana * 100 / body.ManaMax : 0;
        var stamina = body.StamMax > 0 ? body.Stam * 100 / body.StamMax : 0;

        sb.AppendLine("YOURSELF");
        sb.Append("Health ");
        sb.Append(hits);
        sb.Append("%, stamina ");
        sb.Append(stamina);
        sb.Append("%, mana ");
        sb.Append(mana);
        sb.AppendLine("%.");

        var gold = body.Backpack?.TotalGold ?? 0;
        var banked = Banker.GetBalance(body);

        sb.Append("Money: ");
        sb.Append(gold);
        sb.Append(" gold in your pack and ");
        sb.Append(banked);
        sb.AppendLine(" gold in the bank.");

        var load = BotLadder.Load(body);
        var ceiling = Math.Max(1, BotLadder.Ceiling(body));
        var room = Math.Max(0, 100 - load * 100 / ceiling);

        sb.Append("Your pack is ");
        sb.Append(100 - room);
        sb.Append("% full by weight and holds ");
        sb.Append(body.Backpack?.Items.Count ?? 0);
        sb.AppendLine(" things.");

        var weapon = body.Weapon as Item;

        sb.Append("In hand: ");
        sb.AppendLine(weapon?.GetType().Name ?? "nothing but your fists");
    }

    private static void Around(ref ValueStringBuilder sb, BotMobile body)
    {
        sb.AppendLine("\nWHERE YOU ARE");

        var region = body.Region?.Name;

        sb.Append("Standing at ");
        sb.Append(body.Location.X);
        sb.Append(", ");
        sb.Append(body.Location.Y);

        if (!string.IsNullOrEmpty(region))
        {
            sb.Append(" in ");
            sb.Append(region);
        }

        sb.AppendLine(".");

        var home = BotPopulation.Where;
        var away = (int)body.GetDistanceToSqrt(home);

        sb.Append("Your camp is ");
        sb.Append(away);
        sb.AppendLine(" tiles away.");

        if (body.Region is Server.Regions.GuardedRegion)
        {
            sb.AppendLine("This is guarded ground: nothing can be fought here, but shops and a bank are at hand.");
        }

        var worst = BotThreat.Strongest(body, Notice);

        if (worst != null)
        {
            sb.Append("The most dangerous thing in sight is ");
            sb.Append(worst.Name);
            sb.Append(", about ");
            sb.Append((int)body.GetDistanceToSqrt(worst.Location));
            sb.AppendLine(" tiles off.");
        }
        else
        {
            sb.AppendLine("Nothing hostile is in sight.");
        }

        var friends = 0;
        var map = body.Map;

        if (map != null && map != Map.Internal)
        {
            foreach (var other in map.GetMobilesInRange<BotMobile>(body.Location, Notice))
            {
                if (other != body && other.Alive)
                {
                    friends++;
                }
            }
        }

        sb.Append(friends);
        sb.AppendLine(friends == 1 ? " of your own people is nearby." : " of your own people are nearby.");
    }

    private static void Ground(ref ValueStringBuilder sb, BotMind mind, BotMobile body)
    {
        if (mind.Trade != "baron")
        {
            return;
        }

        var map = body.Map;

        if (map == null || map == Map.Internal)
        {
            return;
        }

        sb.AppendLine("\nGROUND THAT HAS KILLED PEOPLE");

        var listed = 0;

        foreach (var (where, _, blows, deaths) in BotPeril.Worst(Squares))
        {
            if (deaths < BotPeril.Deadly)
            {
                continue;
            }

            listed++;

            sb.Append("- (");
            sb.Append(where.X);
            sb.Append(", ");
            sb.Append(where.Y);
            sb.Append("): ");
            sb.Append(deaths);
            sb.Append(" have died there and ");
            sb.Append(blows);
            sb.Append(" blows have landed, about ");
            sb.Append((int)body.GetDistanceToSqrt(where));
            sb.AppendLine(" tiles off.");
        }

        if (listed == 0)
        {
            sb.AppendLine("Nowhere. Nobody has died anywhere that is still standing on the board.");
        }
    }

    public static int Squares { get; set; } = 6;

    public static string Brief(BotMind mind, BotMobile body)
    {
        if (body == null)
        {
            return "no body";
        }

        using var line = ValueStringBuilder.Create(320);

        var pack = body.Backpack?.TotalGold ?? 0;
        var load = BotLadder.Load(body);
        var ceiling = Math.Max(1, BotLadder.Ceiling(body));

        line.Append($"saw: {pack}gp in pack, {Banker.GetBalance(body)}gp banked, {load * 100 / ceiling}% loaded");

        if (mind.Trade == "crafter")
        {
            var canMake = 0;
            var cannot = 0;
            string best = null;
            var bestOffer = 0;

            var wants = BotAuction.Wants;

            for (var i = 0; i < wants.Count; i++)
            {
                var want = wants[i];

                if (want == null || want.Amount <= 0 || !BotShopper.Makeable(want.Kind))
                {
                    continue;
                }

                var recipe = BotShopper.MadeBy(want.Kind);
                var have = recipe == null ? 0.0 : body.Skills[recipe.Value.Skill]?.Base ?? 0.0;

                if (recipe != null && have >= recipe.Value.MinSkill)
                {
                    canMake++;

                    if (want.Offer > bestOffer)
                    {
                        bestOffer = want.Offer;
                        best = want.Label;
                    }
                }
                else
                {
                    cannot++;
                }
            }

            line.Append($"; board: {canMake} wants it could make now, {cannot} beyond its skill");

            if (best != null)
            {
                line.Append($", dearest reachable {best} at {bestOffer}gp");
            }

            var stalls = BotAuction.Listings;
            var kinds = 0;
            var cheapest = int.MaxValue;

            for (var i = 0; i < stalls.Count; i++)
            {
                if (stalls[i] == null)
                {
                    continue;
                }

                kinds++;
                cheapest = Math.Min(cheapest, stalls[i].Price);
            }

            line.Append($"; stalls: {kinds} listings from {(kinds == 0 ? 0 : cheapest)}gp");

            var minds = BotMinds.All;
            var others = 0;

            for (var i = 0; i < minds.Count; i++)
            {
                if (minds[i] != mind && minds[i]?.Body is { Deleted: false })
                {
                    if (others == 0)
                    {
                        line.Append("; others: ");
                    }
                    else
                    {
                        line.Append(", ");
                    }

                    others++;
                    line.Append($"{minds[i].Name} {minds[i].Body.Resolve?.Deed?.Kind ?? "-"}");
                }
            }
        }

        return line.ToString();
    }

    public static int Rows { get; set; } = 8;

    private static void Bench(ref ValueStringBuilder sb, BotMind mind, BotMobile body)
    {
        if (mind.Trade != "crafter")
        {
            return;
        }

        var klass = body.Class;

        if (klass?.Skills == null || klass.Skills.Count == 0)
        {
            return;
        }

        sb.AppendLine("\nWHAT YOU CAN MAKE, AND HOW GOOD YOU ARE AT IT");

        for (var i = 0; i < klass.Skills.Count; i++)
        {
            var (skill, target) = klass.Skills[i];
            var have = body.Skills[skill]?.Base ?? 0.0;

            sb.Append("- ");
            sb.Append(skill.ToString());

            var bench = Bench(skill);

            if (bench != null)
            {
                sb.Append(" (the trade called ");
                sb.Append(bench);
                sb.Append(')');
            }

            sb.Append(": ");
            sb.Append((int)have);
            sb.Append(" of ");
            sb.Append((int)target);

            if (have >= target)
            {
                sb.AppendLine(" - as good as this trade asks. Practice pays you nothing more here.");
            }
            else if (have < 30.0)
            {
                sb.AppendLine(" - a beginner. Only the cheapest recipes come out, and half the material is lost on a failure.");
            }
            else
            {
                sb.AppendLine(" - getting better still pays.");
            }

            Recipes(ref sb, skill, have);
        }
    }

    private static int Cheapest(string material)
    {
        if (string.IsNullOrEmpty(material))
        {
            return 0;
        }

        var listings = BotAuction.Listings;
        var best = 0;

        for (var i = 0; i < listings.Count; i++)
        {
            var listing = listings[i];

            if (listing?.Label == null || listing.Kind?.Name != material)
            {
                continue;
            }

            if (best == 0 || listing.Price < best)
            {
                best = listing.Price;
            }
        }

        return best;
    }

    private static string Bench(SkillName skill)
    {
        CraftSystem[] systems = [BotAnvil.System, BotThread.System, BotFlask.System, BotFletching.System, BotQuill.System];

        for (var i = 0; i < systems.Length; i++)
        {
            var recipes = systems[i]?.CraftItems;

            if (recipes == null)
            {
                continue;
            }

            for (var r = 0; r < recipes.Count; r++)
            {
                var recipe = recipes[r];
                var needs = recipe?.Skills?.Count > 0 ? recipe.Skills[0] : null;

                if (needs?.SkillToMake != skill)
                {
                    continue;
                }

                return BotShopper.MadeBy(recipe.ItemType)?.Trade;
            }
        }

        return null;
    }

    private static void Recipes(ref ValueStringBuilder sb, SkillName skill, double have)
    {
        CraftSystem[] systems = [BotAnvil.System, BotThread.System, BotFlask.System, BotFletching.System, BotQuill.System];

        var can = 0;
        var shown = 0;

        for (var i = 0; i < systems.Length; i++)
        {
            var recipes = systems[i]?.CraftItems;

            if (recipes == null)
            {
                continue;
            }

            for (var r = 0; r < recipes.Count; r++)
            {
                var recipe = recipes[r];
                var needs = recipe?.Skills?.Count > 0 ? recipe.Skills[0] : null;

                if (needs == null || needs.SkillToMake != skill || needs.MinSkill > have)
                {
                    continue;
                }

                can++;

                if (shown >= 3)
                {
                    continue;
                }

                shown++;

                sb.Append(shown == 1 ? "    you can make: " : ", ");
                sb.Append(recipe.NameString ?? recipe.ItemType?.Name ?? "something");

                if (shown == 1)
                {
                    var eats = BotShopper.MadeBy(recipe.ItemType);

                    if (eats?.Material != null && eats.Value.Needs > 0)
                    {
                        sb.Append(" (eats ");
                        sb.Append(eats.Value.Needs);
                        sb.Append(' ');
                        sb.Append(eats.Value.Material);

                        var price = Cheapest(eats.Value.Material);

                        if (price > 0)
                        {
                            sb.Append(", on the stalls from ");
                            sb.Append(price);
                            sb.Append("gp");
                        }
                        else
                        {
                            sb.Append(", none on any stall");
                        }

                        sb.Append(')');
                    }
                }
            }
        }

        if (shown == 0)
        {
            sb.AppendLine(
                Bench(skill) == null
                    ? "    nothing is made with this skill - it fetches material."
                    : "    you cannot make anything with this skill yet."
            );

            return;
        }

        if (can > shown)
        {
            sb.Append(" and ");
            sb.Append(can - shown);
            sb.Append(" others");
        }

        sb.AppendLine(".");
    }

    private static void Board(ref ValueStringBuilder sb, BotMind mind, BotMobile body)
    {
        if (mind.Trade != "crafter")
        {
            return;
        }

        sb.AppendLine("\nTHE BOARD: WHAT THE ISLAND HAS PAID FOR AND CANNOT GET");

        var wants = BotAuction.Wants;
        var shown = 0;
        var unmakeable = 0;

        var seen = new Dictionary<(string Label, int Offer), (int Wanted, int Orders, int Escrow, int Filled, int Raises, Type Kind)>();

        for (var i = 0; i < wants.Count; i++)
        {
            var want = wants[i];

            if (want == null || want.Amount <= 0)
            {
                continue;
            }

            if (!BotShopper.Makeable(want.Kind))
            {
                unmakeable++;

                continue;
            }

            var key = (want.Label, want.Offer);

            if (seen.TryGetValue(key, out var had))
            {
                seen[key] = (had.Wanted + want.Amount, had.Orders + 1, had.Escrow + want.Escrow, had.Filled + want.Filled, Math.Max(had.Raises, want.Raises), had.Kind);
            }
            else
            {
                seen[key] = (want.Amount, 1, want.Escrow, want.Filled, want.Raises, want.Kind);
            }
        }

        foreach (var ((label, offer), row) in seen)
        {
            if (shown >= Rows)
            {
                break;
            }

            shown++;

            sb.Append("- ");
            sb.Append(row.Wanted);
            sb.Append(" x ");
            sb.Append(label);
            sb.Append(" at ");
            sb.Append(offer);
            sb.Append("gp each, ");
            sb.Append(row.Escrow);
            sb.Append("gp already lodged");

            if (row.Orders > 1)
            {
                sb.Append(", across ");
                sb.Append(row.Orders);
                sb.Append(" separate orders");
            }

            if (row.Filled > 0)
            {
                sb.Append(", ");
                sb.Append(row.Filled);
                sb.Append(" delivered so far");
            }

            if (row.Raises > 0)
            {
                sb.Append(". The price has been raised ");
                sb.Append(row.Raises);
                sb.Append(row.Raises == 1 ? " time" : " times");
                sb.Append(row.Filled > 0 ? " and it is still short" : " and nothing has ever been delivered");
            }

            var recipe = BotShopper.MadeBy(row.Kind);

            if (recipe != null)
            {
                var fact = recipe.Value;
                var have = body.Skills[fact.Skill]?.Base ?? 0.0;

                sb.Append(". Made by ");
                sb.Append(fact.Craft);
                sb.Append(" - take the trade called ");
                sb.Append(fact.Trade);
                sb.Append(" - needs ");
                sb.Append(fact.Skill.ToString());
                sb.Append(' ');
                sb.Append((int)fact.MinSkill);
                sb.Append("; you have ");
                sb.Append((int)have);
                sb.Append(have >= fact.MinSkill ? " and can make it now" : " and cannot make it yet");

                if (fact.Material != null && fact.Needs > 0)
                {
                    sb.Append(". Each one eats ");
                    sb.Append(fact.Needs);
                    sb.Append(' ');
                    sb.Append(fact.Material);
                }
            }

            sb.AppendLine(".");
        }

        if (shown == 0)
        {
            sb.AppendLine("Nothing. Nobody has paid for anything you could make - so make what sells, or go and get material.");
        }

        if (unmakeable > 0)
        {
            sb.Append("(");
            sb.Append(unmakeable);
            sb.AppendLine(" more wants are for things no craft on this island can produce. They are not yours; leave them.)");
        }
    }

    private static void Fellows(ref ValueStringBuilder sb, BotMind mind, BotMobile body)
    {
        if (mind.Trade != "crafter")
        {
            return;
        }

        var minds = BotMinds.All;
        var said = 0;

        sb.AppendLine("\nTHE OTHER CRAFTERS, AND WHAT THEY ARE DOING NOW");

        for (var i = 0; i < minds.Count; i++)
        {
            var other = minds[i];

            if (other == null || other == mind || other.Trade != "crafter")
            {
                continue;
            }

            var fellow = other.Body;

            if (fellow is not { Deleted: false })
            {
                continue;
            }

            said++;

            sb.Append("- ");
            sb.Append(other.Name);
            sb.Append(": ");
            sb.AppendLine(fellow.Resolve?.Deed?.Kind ?? "nothing yet");
        }

        if (said == 0)
        {
            sb.AppendLine("Nobody. You are the only crafter working on this island right now.");
        }
        else
        {
            sb.AppendLine("Four of you share one island. Two at the same bench is one bench wasted.");
        }
    }

    private static void Materials(ref ValueStringBuilder sb, BotMind mind)
    {
        if (mind.Trade != "crafter")
        {
            return;
        }

        sb.AppendLine("\nWHAT IS FOR SALE ON THE STALLS, AND WHAT IT COSTS");
        sb.AppendLine("You pay from your pack, and whatever it is short of is taken from your bank account automatically. Both purses spend.");

        var listings = BotAuction.Listings;
        var seen = new Dictionary<string, (int Count, int Cheapest)>();

        for (var i = 0; i < listings.Count; i++)
        {
            var listing = listings[i];

            if (listing?.Label == null)
            {
                continue;
            }

            if (seen.TryGetValue(listing.Label, out var had))
            {
                seen[listing.Label] = (had.Count + 1, Math.Min(had.Cheapest, listing.Price));
            }
            else
            {
                seen[listing.Label] = (1, listing.Price);
            }
        }

        if (seen.Count == 0)
        {
            sb.AppendLine("Nothing at all is on sale. Whatever you need, you will have to go and get it yourself.");

            return;
        }

        var listed = 0;

        foreach (var (label, what) in seen)
        {
            if (listed >= Rows)
            {
                break;
            }

            listed++;

            sb.Append("- ");
            sb.Append(label);
            sb.Append(": ");
            sb.Append(what.Count);
            sb.Append(what.Count == 1 ? " stall, from " : " stalls, from ");
            sb.Append(what.Cheapest);
            sb.AppendLine("gp.");
        }

        if (seen.Count > listed)
        {
            sb.Append("(and ");
            sb.Append(seen.Count - listed);
            sb.AppendLine(" other kinds of thing on other stalls.)");
        }
    }

    private static void Island(ref ValueStringBuilder sb, BotMind mind, BotMobile body)
    {
        if (mind.Trade != "crafter")
        {
            return;
        }

        var bots = BotPopulation.Bots;
        var layers = BotHarness.Layers;

        var alive = 0;
        var bare = 0;
        var slots = 0;
        var shooters = 0;
        var dry = 0;
        var hungry = 0;

        for (var i = 0; i < bots.Count; i++)
        {
            var other = bots[i];

            if (other is not { Deleted: false, Alive: true })
            {
                continue;
            }

            alive++;

            var open = 0;

            for (var l = 0; l < layers.Count; l++)
            {
                if (other.FindItemOnLayer(layers[l]) == null)
                {
                    open++;
                }
            }

            if (open > 0)
            {
                bare++;
                slots += open;
            }

            if (other.Class?.Kit.Ranged is { Count: > 0 })
            {
                shooters++;

                if (!BotArms.Stocked(other, other.Class))
                {
                    dry++;
                }
            }

            if (!BotMeal.IsFed(other))
            {
                hungry++;
            }
        }

        sb.AppendLine("\nWHAT THE ISLAND IS SHORT OF, COUNTED THIS MOMENT");

        sb.Append("- Armour: ");
        sb.Append(bare);
        sb.Append(" of ");
        sb.Append(alive);
        sb.Append(" bots have a bare slot, ");
        sb.Append(slots);
        sb.AppendLine(" empty slots between them.");
        sb.AppendLine("  Metal armour is forged (the trade called Smith) out of ingots; leather and cloth armour is sewn (the trade called Tailor). Nobody else can make either.");

        sb.Append("- Arrows: ");

        if (shooters == 0)
        {
            sb.AppendLine("nobody on this island shoots.");
        }
        else if (dry == 0)
        {
            sb.Append("every one of the ");
            sb.Append(shooters);
            sb.AppendLine(" shooters has something to shoot. Nothing wanted here.");
        }
        else
        {
            sb.Append(dry);
            sb.Append(" of ");
            sb.Append(shooters);
            sb.AppendLine(" shooters are out of ammunition. Arrows are made by fletching (the trade called Fletcher) out of boards and feathers, and nobody but a crafter can make one.");
        }

        sb.Append("- Food: ");
        sb.Append(hungry);
        sb.Append(" of ");
        sb.Append(alive);
        sb.AppendLine(" have not eaten in the last while. Nobody starves here - a meal only quickens recovery and lifts the mood - and food is cooked (the trade called Cook) out of raw meat and flour.");
    }

    private static void Shelf(ref ValueStringBuilder sb, BotMind mind, BotMobile body)
    {
        if (mind.Trade != "crafter")
        {
            return;
        }

        var listings = BotAuction.Listings;

        var mine = 0;
        var sold = 0;
        var earned = 0;
        var stale = 0;
        string worst = null;
        var worstCuts = 0;

        for (var i = 0; i < listings.Count; i++)
        {
            var listing = listings[i];

            if (listing?.Seller != body)
            {
                continue;
            }

            mine++;
            sold += listing.Sold;
            earned += listing.Earned;

            if (listing.Sold == 0 && listing.Cuts > 0)
            {
                stale++;

                if (listing.Cuts > worstCuts)
                {
                    worstCuts = listing.Cuts;
                    worst = listing.Label;
                }
            }
        }

        sb.AppendLine("\nYOUR OWN STALLS");

        if (mine == 0)
        {
            sb.AppendLine("You have nothing on the market. Nothing you have made is earning while you do something else.");

            return;
        }

        sb.Append("You are holding ");
        sb.Append(mine);
        sb.Append(mine == 1 ? " stall. " : " stalls. ");
        sb.Append(sold);
        sb.Append(" things sold off them for ");
        sb.Append(earned);
        sb.AppendLine("gp all told.");

        if (stale > 0)
        {
            sb.Append(stale);
            sb.Append(stale == 1 ? " stall has" : " stalls have");
            sb.Append(" had the price cut and still sold nothing");

            if (worst != null)
            {
                sb.Append(" - worst of them ");
                sb.Append(worst);
                sb.Append(", cut ");
                sb.Append(worstCuts);
                sb.Append(worstCuts == 1 ? " time" : " times");
            }

            sb.AppendLine(". Nobody wants more of that; make something else.");
        }
    }

    private static void Past(ref ValueStringBuilder sb, BotMind mind)
    {
        var past = mind.Past;

        if (past.Count == 0)
        {
            sb.AppendLine("\nWHAT YOUR CHOICES HAVE COME TO\nNothing yet: this is your first decision.");

            return;
        }

        sb.AppendLine("\nWHAT YOUR CHOICES HAVE COME TO");

        var from = Math.Max(0, past.Count - Recall);

        for (var i = from; i < past.Count; i++)
        {
            var done = past[i];

            sb.Append("- ");
            sb.Append(done.Trade);

            if (done.Long)
            {
                sb.Append(": you expected ");
                sb.Append(done.Expected, "F0");
                sb.Append(" a minute, it came to ");
                sb.Append(done.Measured, "F0");
                sb.Append(" a minute over ");
                sb.Append(done.Minutes, "F1");
                sb.Append(" minutes (");
                sb.Append(done.Ending);
                sb.AppendLine(").");
            }
            else
            {
                sb.Append(": ");
                sb.Append(done.Ending);
                sb.Append(" after only ");
                sb.Append(done.Minutes * 60, "F0");
                sb.Append(" seconds with ");
                sb.Append(done.Gained);
                sb.AppendLine(" gold — too short to be worth anything a minute either way.");
            }
        }
    }

    private static void Heard(ref ValueStringBuilder sb, BotMind mind)
    {
        var said = false;

        foreach (var (who, what, ago) in BotMindTalk.Heard(mind.Name))
        {
            if (!said)
            {
                said = true;

                sb.AppendLine("\nWHAT THE OTHERS HAVE SAID");
            }

            sb.Append("- ");
            sb.Append(who);
            sb.Append(", ");
            sb.Append(ago);
            sb.Append("s ago: ");
            sb.AppendLine(what);
        }
    }

    private static void Lessons(ref ValueStringBuilder sb, BotMind mind)
    {
        var lessons = mind.Lessons;

        if (lessons.Count == 0)
        {
            return;
        }

        sb.AppendLine("\nWHAT YOU HAVE LEARNED");

        for (var i = 0; i < lessons.Count; i++)
        {
            sb.Append("- ");
            sb.AppendLine(lessons[i]);
        }
    }

    private static void Offers(ref ValueStringBuilder sb, IReadOnlyList<string> trades)
    {
        sb.AppendLine("\nTRADES WITH WORK IN THEM RIGHT NOW");

        for (var i = 0; i < trades.Count; i++)
        {
            sb.Append("- ");
            sb.AppendLine(Explain(trades[i]));
        }
    }

    private static string Explain(string trade) =>
        trade switch
        {
            "Hunter" => "Hunter — go and kill something on your own, and take what it carries.",
            "Muster" => "Muster — call a company of nearby bots against something too big for one of you.",
            "Gleaner" => "Gleaner — pick up spent arrows and bolts from the ground.",
            "Miner" => "Miner — dig ore, smelt it into ingots, and put them on the market or in the bank.",
            "Shopper" => "Shopper — buy back the supplies you are short of from a shopkeeper.",
            "Peddler" => "Peddler — sell what you are carrying to a shopkeeper for coin.",
            "Seeker" => "Seeker — fill a standing order another bot has posted on the market.",
            "Tailor" => "Tailor — buy cloth and sew goods to sell.",
            "Scribe" => "Scribe — buy blank scrolls and write spells to sell.",
            "Surgeon" => "Surgeon — go and heal one of your own people who is hurt.",
            "Smith" => "Smith — take metal to an anvil and forge goods, filling the board's orders first.",
            "Bullion" => "Bullion — buy metal from a shopkeeper instead of spending eight minutes digging it.",
            "Upkeep" => "Upkeep — post an order on the market for a replacement of something you are wearing out.",
            "Armoury" => "Armoury — buy a few attack scrolls to open a fight with or to break away from one.",
            "Rescuer" => "Rescuer — go to the aid of one of your own people who is being set upon.",
            "Undertaker" => "Undertaker — go back for your own corpse and recover what you were carrying.",
            "Porter" => "Porter — carry goods to where they were asked for.",
            "Baron" => "Baron — take five bots to the ground that has killed the most people and empty it. Twenty dead or forty minutes, and the square comes off the board. Everything it drops goes to them.",
            "Stroll" => "Stroll — walk your town. It pays nothing; it is where you are when no ground is standing.",
            _ => trade
        };
}
