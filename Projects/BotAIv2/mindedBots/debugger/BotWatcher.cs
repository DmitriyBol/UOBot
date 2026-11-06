using System;
using System.Collections.Generic;
using Server.BotAI.V2;

namespace Server.BotAI.Mind;

public sealed class BotWatcher
{

    public static readonly (string Charge, string[] Stems)[] Charges =
    [
        (
            "the wars, the guilds, the war companies and the estate: who fights whom, whether companies form and hold, whether halls, seats, claims and evictions behave",
            ["Regard", "War", "Feud", "Quarrel", "Rally", "Exile", "Claim", "Hold", "Estate", "Squad", "Muster", "Enlist", "Delve", "Guild", "Charter", "Roster", "Bailiff", "Evict", "Land", "Seat", "Remov", "Plot", "Steward", "Hall", "Baron", "Harrow", "Drill", "School", "Lesson", "Attend", "Spoils", "Formation", "Patrol", "Sweep", "Band", "Scatter", "Levy", "Office", "Fittings", "Bench"]
        ),
        (
            "the work, the trades, the market and the money: what is taken on and finished, what each craft makes, what sells, who is poor and why",
            ["Will", "Appraisal", "Commons", "Market", "Courier", "Deliver", "Handover", "Want", "City", "Ledger", "Purse", "Shop", "Peddl", "Quarry", "Smith", "Tailor", "Fletch", "Alchem", "Cook", "Store", "Upkeep", "Bullion", "Craft", "Anvil", "Oven", "Herb", "Forag", "Pick", "Outfit", "Suppl", "Shelf", "Hire", "Fitter", "Unload", "Restock", "Order", "Acquire", "Armour", "Seeker", "Inscri", "Scribe", "Quill", "Grimoire", "Mind", "Progress", "Stable", "Woodsman", "Ground", "Dig", "Chop", "Mine", "Harvest", "Yield", "Urges", "Meal", "Flask", "Brew", "Sew", "Glean", "Plunder", "Freedom", "Stroll", "Stipend", "Rounds", "Recipe", "Kit", "Bind"]
        ),
        (
            "getting about, standing still, the ground, the fighting and the clock: the pathfinder's bill, stuck and looping bots, deaths and rescues, whether blows land, and what the loop costs",
            ["Path", "Walk", "Journey", "Reach", "Refused", "Barred", "Step", "Beat", "Stall", "Homer", "Homeward", "Quad", "Peril", "Scout", "Warden", "Marker", "Halls", "Population", "Rescue", "Cry", "Threat", "Slay", "Hunter", "Prowl", "Bolt", "Arms", "Signs", "Alarm", "Tail", "Mobile", "Avoid", "Errand", "Arrival", "Strike", "Spell", "Movement", "Medic", "Mend", "Gasp"]
        )
    ];

    public static int Owner(string typeName) => Owner(typeName, out _);

    public static int Owner(string typeName, out int stem)
    {
        stem = -1;

        if (string.IsNullOrEmpty(typeName))
        {
            return 0;
        }

        var bare = typeName.StartsWith("Bot", StringComparison.Ordinal) ? typeName[3..] : typeName;

        for (var c = 0; c < Charges.Length; c++)
        {
            var stems = Charges[c].Stems;

            for (var i = 0; i < stems.Length; i++)
            {
                if (bare.StartsWith(stems[i], StringComparison.OrdinalIgnoreCase))
                {
                    stem = i;

                    return c;
                }
            }
        }

        return 0;
    }

    public BotWatcher(string name, int hue, int rank)
    {
        Name = name;
        Hue = hue;
        Rank = rank;
        OwnCharge = Charges[rank % Charges.Length].Charge;
    }

    public string Name { get; }

    public int Hue { get; }

    public int Rank { get; }

    public string OwnCharge { get; }

    public string Charge
    {
        get
        {
            var keeper = BotVigil.Keeper;

            if (keeper == null)
            {
                return OwnCharge;
            }

            if (ReferenceEquals(keeper, this))
            {
                return $"the danger zones alone, standing in one after another until their accuracy holds (its own charge, {OwnCharge}, is carried by the others meanwhile)";
            }

            return !Organiser && BotVigil.Heirs().Contains(this)
                ? $"{OwnCharge}; and, while {keeper.Name} keeps the zones, {keeper.OwnCharge}"
                : OwnCharge;
        }
    }

    public bool Organiser { get; init; }

    public bool Watches(string typeName) => Owns(typeName) || Borrows(typeName);

    public bool Owns(string typeName) => !BotVigil.IsKeeper(this) && Owner(typeName) == Rank % Charges.Length;

    public bool Borrows(string typeName)
    {
        var keeper = BotVigil.Keeper;

        if (keeper == null || ReferenceEquals(keeper, this))
        {
            return false;
        }

        var owner = Owner(typeName, out var stem);
        var theirs = keeper.Rank % Charges.Length;

        if (owner != theirs || theirs == Rank % Charges.Length)
        {
            return false;
        }

        var heirs = BotVigil.Heirs();
        var at = heirs.IndexOf(this);

        return at >= 0 && Math.Max(0, stem) % heirs.Count == at;
    }

    public BotDebugger Body { get; set; }

    public BotDebugNote Last { get; set; }

    public long Asked { get; set; }

    public long Findings { get; set; }

    public long Labels { get; set; }

    public long Echoes { get; set; }

    public long Quiet { get; set; }

    public long Reflections { get; set; }

    public string Wanted { get; set; }

    public string Because { get; set; } = "it was the first bot I saw";

    public List<string> Found { get; } = [];

    public long ReportedTick { get; set; }

    public string Beside { get; set; }

    public string Asking { get; private set; }

    public DateTime AskingSince { get; private set; }

    public DateTime? ThoughtAt { get; private set; }

    public string ThoughtKind { get; private set; }

    public string Question { get; private set; }

    public string Thought { get; private set; }

    public string Answer { get; private set; }

    public string About { get; private set; }

    public double Sure { get; private set; } = -1;

    public List<BotDebuggerLine> Feed { get; } = [];

    public static int FeedKeep { get; set; } = 12;

    public void Ask(string what)
    {
        Asking = what;
        AskingSince = DateTime.Now;
    }

    public void Think(string kind, string question, string thought, string answer, string about, double sure = -1)
    {
        Asking = null;
        ThoughtAt = DateTime.Now;
        ThoughtKind = kind;
        Question = question;
        Thought = thought;
        Answer = answer;
        About = string.IsNullOrWhiteSpace(about) || about == "-" ? null : about;
        Sure = sure;
    }

    public void Heard(string line)
    {
        Feed.Add(new BotDebuggerLine(DateTime.Now, line));

        while (Feed.Count > Math.Max(1, FeedKeep))
        {
            Feed.RemoveAt(0);
        }
    }

    public string Describe() =>
        Body is not { Deleted: false }
            ? $"{Name} has no body"
            : $"{Name} at {Body.Location.X},{Body.Location.Y} after {Body.Hops} hops, {Asked} asked, {Findings} findings, {Labels} labels and {Echoes} echoes turned away, {Quiet} quiet looks, {Reflections} reflections";
}
