using System;
using System.Collections.Generic;
using Server.BotAI.V2;
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
    /// <summary>How far round itself the bot is said to see.</summary>
    public static int Notice { get; set; } = 14;

    /// <summary>How many past outcomes are recited before the model chooses again.</summary>
    public static int Recall { get; set; } = 6;

    /// <summary>
    /// The standing instruction. Short on purpose: everything situational belongs in the state, where it can
    /// be measured against what happened, and a system prompt that carries facts is a system prompt that
    /// goes stale without anybody noticing.
    /// </summary>
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
         """;

    /// <summary>
    /// How many other bots there are, counted rather than written down.
    ///
    /// <para>
    /// It said "fourteen" for as long as this file has existed and the population has been thirty-three since
    /// the 19th. Nothing broke, which is the point: a fact hard-written into a system prompt goes stale
    /// silently, and the only symptom is a mind reasoning about a world half the size of the one it is in.
    /// </para>
    /// </summary>
    private static int Others() => Math.Max(0, BotPopulation.Bots.Count - 1);

    /// <summary>How many other minds are running. Same reasoning as <see cref="Others"/>.</summary>
    private static int Thinkers() => Math.Max(0, BotMinds.All.Count - 1);

    /// <summary>
    /// The paragraph that says what this particular bot is for, or nothing for the three whose trade is
    /// already the ordinary business of the shard.
    ///
    /// <para>
    /// <b>It exists for one mind, and writing it for all four would have been the mistake.</b> A warrior, a
    /// smith and a caster all live by the same rule the prompt above states — work is worth what it pays and
    /// the number you give is a forecast of that — so anything added for them would be flavour, and flavour
    /// in a system prompt is a fact that goes stale where nobody can see it. The Baron does not live by that
    /// rule at all: he is paid nothing, he hands away everything his company takes, and the thing he is
    /// actually trying to change is the state of the island. A mind told only the general rule would sit
    /// there predicting its own income, which is nought, for ever.
    /// </para>
    /// </summary>
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
            _ => ""
        };

    /// <summary>Everything about right now, as the question.</summary>
    public static string State(BotMind mind, BotMobile body, IReadOnlyList<string> trades)
    {
        var sb = ValueStringBuilder.Create(2048);

        try
        {
            Body(ref sb, body);
            Around(ref sb, body);
            Ground(ref sb, mind, body);
            Past(ref sb, mind);
            Heard(ref sb, mind);
            Lessons(ref sb, mind);
            Offers(ref sb, trades);

            sb.Append("\nChoose one trade from the list, say what you expect it to be worth per minute, how many minutes you expect to spend on it, and why in one sentence.");
            sb.Append(" You may also put one short line in `say` for the other thinking bots to read — something you have found, somewhere worth coming to, something you have given up on. Leave it empty unless it is worth their attention.");

            return sb.ToString();
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

        // Named as room left rather than as a number of stones. See the note at the top of this file: the
        // engine's unit for weight was read as a cargo of rocks and cost a whole first run.
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

        // Towns forbid fighting outright, and a mind that does not know that will pick hunting inside one all
        // day and wonder why nothing ever comes of it.
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

    /// <summary>
    /// The ground that has killed people and has not been dealt with — for the one mind whose whole subject
    /// it is.
    ///
    /// <para>
    /// <b>Written for the Baron and nobody else, because for everybody else it would be a distraction with
    /// coordinates in it.</b> A miner shown a list of deadly squares will reason about them; it has no
    /// errand that can touch one, so every word of that reasoning is spent. The Baron has exactly one errand
    /// and this is the whole of what it is about, and without it his state would say "the Baron trade has
    /// work in it" and nothing about why or where.
    /// </para>
    ///
    /// <para>
    /// Said as squares and the dead in them, never as the peril reading. The reading is a decaying frequency
    /// and it answers a different question — see <c>BotPeril</c> — and a mind given both numbers would be a
    /// mind asked to reconcile two rankings that are allowed to disagree.
    /// </para>
    /// </summary>
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

    /// <summary>How many squares of dangerous ground are recited. A list, not a map.</summary>
    public static int Squares { get; set; } = 6;

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

            // A rate is only quoted where there is one. Told "you expected 250 a minute and got 0 a minute"
            // about six seconds of walking, a mind has been handed a false comparison and will reason from
            // it — so a short piece of work is reported as what it was, in seconds and in gold, and left
            // uncompared.
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

    // Where a section about empty trades used to be, and the note is worth more than the section was.
    //
    // The menu lists what exists, not what is available, and this tried to bridge the gap in words: "nothing
    // came of it 12 seconds ago; choosing it again now will most likely come to nothing again." The model
    // read it back to front — "the peddler trade was only attempted seconds ago, making it the highest
    // probability for quick coin conversion" — and chose the empty trade seven times running, citing the
    // warning each time as its reason. The gap is now closed where it cannot be reinterpreted: an empty
    // trade is left off the list the sampler is constrained to. See BotMind.Menu.
    //
    // And the gap itself is gone as well, which is the better half of the fix. The list is no longer "the
    // trades that exist, less the ones lately found empty" but "the trades that answered with real work when
    // asked a moment ago" — see BotMinds.Working. The barren memory stays, because a trade can still empty
    // out in the seconds between being asked and being taken up, but it is now a fence and not the wall.

    /// <summary>
    /// What the other thinking bots have said lately.
    ///
    /// <para>
    /// Their words, unedited, with who said them and how long ago — and nothing else. No instruction to obey
    /// them, agree with them or answer them: a remark from somebody else is a fact about the world, on the
    /// same footing as a wraith being visible or a pack being full, and what to do about it is the decision
    /// being asked for. A mind told to <em>respond</em> would spend its one answer a minute on conversation.
    /// </para>
    /// </summary>
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
        // Named as what they are: not the trades that exist, but the ones with something in them this
        // minute. Every proposer on this list was asked a moment ago and answered with a real piece of work.
        sb.AppendLine("\nTRADES WITH WORK IN THEM RIGHT NOW");

        for (var i = 0; i < trades.Count; i++)
        {
            sb.Append("- ");
            sb.AppendLine(Explain(trades[i]));
        }
    }

    /// <summary>
    /// A trade's name with a plain sentence about it.
    ///
    /// <para>
    /// The names are the shard's own — a proposer is called <c>Miner</c> and that is the word the schema
    /// constrains the answer to — but a bare list of nouns leaves the model guessing what half of them mean,
    /// and a guess about <c>Gleaner</c> or <c>Muster</c> is a decision made for the wrong reason. Anything
    /// unrecognised is named without a gloss rather than being hidden: a trade this file has not been taught
    /// about is still a trade the bot may take.
    /// </para>
    /// </summary>
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
