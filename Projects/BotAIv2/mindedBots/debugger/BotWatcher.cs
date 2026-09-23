using System;
using System.Collections.Generic;

namespace Server.BotAI.Mind;

/// <summary>
/// One watcher of the squad: a name, a robe, a body, a charge, and what it has said.
///
/// <para>
/// <b>Patrick's order of 13.09.2026: Argus gets two helpers with the same powers, the same permissions and
/// the same thinking model; the squad divides the watching between them, looks, checks and shares what it
/// finds.</b> Until this the debugger was one static body with one opinion, and everything it did not think
/// of was not thought of. Three watchers with three charges is three chances to notice, and a finding one
/// of them makes is put in front of the other two at their next look.
/// </para>
///
/// <para>
/// <b>The charge is a list of subsystem stems, and it decides two things and no more:</b> which of the
/// shard's own summary lines a watcher is quoted, and which bots it goes to stand beside first. The
/// measurements are the same for all three — one pass over the population, taken once — and the model is
/// the same; what differs is what each is told to look at, which is what a division of labour is.
/// </para>
/// </summary>
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
            ["Will", "Appraisal", "Commons", "Auction", "Haggle", "Listing", "Ledger", "Purse", "Shop", "Peddl", "Quarry", "Smith", "Tailor", "Fletch", "Alchem", "Cook", "Store", "Upkeep", "Bullion", "Craft", "Anvil", "Oven", "Herb", "Forag", "Pick", "Outfit", "Suppl", "Shelf", "Hire", "Fitter", "Unload", "Restock", "Order", "Acquire", "Armour", "Seeker", "Inscri", "Scribe", "Quill", "Grimoire", "Mind", "Progress", "Stable", "Woodsman", "Ground", "Dig", "Chop", "Mine", "Harvest", "Yield", "Urges", "Meal", "Flask", "Brew", "Sew", "Glean", "Plunder", "Freedom", "Stroll", "Stipend", "Rounds", "Recipe", "Kit", "Bind"]
        ),
        (
            "getting about, standing still, the ground, the fighting and the clock: the pathfinder's bill, stuck and looping bots, deaths and rescues, whether blows land, and what the loop costs",
            ["Path", "Walk", "Journey", "Reach", "Refused", "Barred", "Step", "Beat", "Stall", "Homer", "Homeward", "Quad", "Peril", "Scout", "Warden", "Marker", "Halls", "Population", "Rescue", "Cry", "Threat", "Slay", "Hunter", "Prowl", "Bolt", "Arms", "Signs", "Alarm", "Tail", "Mobile", "Avoid", "Errand", "Arrival", "Strike", "Spell", "Movement", "Medic", "Mend", "Gasp"]
        )
    ];

    public static int Owner(string typeName)
    {
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
        Charge = Charges[rank % Charges.Length].Charge;
    }

    public string Name { get; }

    public int Hue { get; }

    public int Rank { get; }

    public string Charge { get; }

    public bool Organiser { get; init; }

    public bool Watches(string typeName) => Owner(typeName) == Rank % Charges.Length;

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

    public string Describe() =>
        Body is not { Deleted: false }
            ? $"{Name} has no body"
            : $"{Name} at {Body.Location.X},{Body.Location.Y} after {Body.Hops} hops, {Asked} asked, {Findings} findings, {Labels} labels and {Echoes} echoes turned away, {Quiet} quiet looks, {Reflections} reflections";
}
