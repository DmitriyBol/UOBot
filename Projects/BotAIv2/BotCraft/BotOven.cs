using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// Cooking: what a meal is made of, and who can make one.
///
/// <para>
/// <b>The island was already producing the whole of the raw side and throwing it away.</b> Every hunt ends
/// in a carcass and every carcass carves into meat, so raw ribs, bird and lamb were the commonest things on
/// the market — and the commonest thing on it that nobody wanted. The peddler's own figures on 05.09.2026:
/// six trips carrying ribs earned thirty-six gold between them, and twenty of the thirty stalls that session
/// were a bot walking meat across the island to a butcher for two gold a piece. A trade that turns that into
/// something a bot actually wants is the shortest chain on this shard: one ingredient, one swing, no fire.
/// </para>
///
/// <para>
/// <b>A fire, and the rule is not where it was first looked for.</b> <c>DefCooking.CanCraft</c> asks for the
/// tool and nothing else, and that was read as "a cook needs no place at all". It is not: every one of the
/// five recipes below carries <c>SetNeedHeat(index, true)</c>, and the heat is checked per recipe rather
/// than by the system. The cost of getting that wrong was measured on 05.09.2026 — a cook stood in a field
/// swinging a skillet at three lamb legs, eight swings a round, four rounds, nothing cooked and not one line
/// in the log, because <c>CraftItem.Craft</c> answers a refusal by sending the message to a player's screen
/// and a bot has no screen. A gate that is real, silent, and in a different file from the one that documents
/// it is the shape this project keeps paying for.
/// </para>
///
/// <para>
/// <b>Heat is wider than a forge, which is why the cook does not queue behind the smith.</b> The engine
/// counts ovens, fireplaces, campfires, firepits, heating stands, braziers and forges alike. See
/// <c>BotGround.Hearths</c>, which is that whole family, against <c>BotGround.Fires</c>, which is the four
/// forge-and-anvil workshops a smith needs.
/// </para>
///
/// <para>
/// <b>One resource a recipe, so this is a caller of <see cref="BotCraftwork"/> rather than another
/// <c>BotFlask</c>.</b> Alchemy needed its own file because every draught wants a reagent and a bottle;
/// cooked bird wants a raw bird. See <c>BotCraftwork.Simple</c>, which refuses anything more complicated and
/// says why.
/// </para>
/// </summary>
public static class BotOven
{
    /// <summary>The cooking system, or null before content initialisation has built it.</summary>
    public static CraftSystem System => DefCooking.CraftSystem;

    /// <summary>The skill it is worked with.</summary>
    public static SkillName Skill => SkillName.Cooking;

    /// <summary>The tool. A skillet, which is what <c>DefCooking</c> hangs its recipes on.</summary>
    public static BaseTool Kit(Mobile bot) => bot?.Backpack?.FindItemByType<Skillet>();

    /// <summary>
    /// The raw meats this trade turns into meals, in the order a cook would reach for them.
    ///
    /// <para>
    /// Named rather than discovered, and this is the one list in the file that is a list. The recipe table
    /// holds forty-six entries — flour, dough, pies, sushi, cake — and almost all of them need ingredients
    /// this island has no source for at all. What it does have is carcasses, so what the cook is offered is
    /// the five recipes that turn one carcass into one supper. Anything else would be a trade that proposes
    /// work it can never finish, which is the shape this project keeps paying for.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Type> Raw { get; } =
    [
        typeof(RawRibs),
        typeof(RawLambLeg),
        typeof(RawBird),
        typeof(RawChickenLeg),
        typeof(RawFishSteak)
    ];

    /// <summary>
    /// How near the fire a cook has to stand.
    ///
    /// <b>Two, because <c>CraftItem.Find</c> says two.</b> Written here so that the walk aims where the
    /// engine will actually accept a swing, and not at the remembered tile from an arm's length further out.
    /// </summary>
    public static int Reach { get; set; } = 2;

    /// <summary>
    /// Whether a swing would be accepted where this bot is standing.
    ///
    /// Asked of the engine, never of the distance to a remembered hearth: the remembered point may be a
    /// tile the bot cannot quite reach, and only the engine knows. The same ruling <c>BotAnvil.AtASmithy</c>
    /// makes, and for the same reason.
    /// </summary>
    public static bool AtAHearth(Mobile bot) => CraftItem.NearHeatSource(bot);

    /// <summary>How much raw meat is worth setting up for. Below it the swing costs more than the supper.</summary>
    public static int Worthwhile { get; set; } = 2;

    /// <summary>What a cooked meal opens at on the market. Twice the raw, which is the whole of the trade.</summary>
    public static int Worth { get; set; } = 6;

    /// <summary>
    /// How much raw meat of one kind a cook holds back from the market rather than selling.
    ///
    /// <para>
    /// <b>The hunter was selling the supper's only ingredient on its way past the butcher.</b> This trade was
    /// built on the reasoning that the island already produces the whole raw side of it and throws it away,
    /// and it went on throwing it away one step earlier than anybody was looking: <c>BotSlay.Rifle</c> lists
    /// everything off a corpse the moment it is lifted, so raw ribs reached a stall before they ever reached
    /// a pan. On 05.09.2026 that read as 1278 of 1297 asks to cook answering "no meat worth cooking" while
    /// raw meat was among the commonest things on the market.
    /// </para>
    ///
    /// <para>
    /// Held back only by a bot carrying a skillet, and only this much of it. A hunter that cannot cook still
    /// sells its meat — that is the butcher's trade and it is what prices the stuff — and a cook that kept
    /// every carcass it walked past would be a larder rather than a hunter.
    /// </para>
    ///
    /// <para>
    /// <b>Twenty, and it has to be twenty because that is what a cook orders.</b> It was ten for half an
    /// hour, against <c>BotStores.Batch</c> of twenty — so a cook whose order for twenty ribs was filled
    /// immediately listed ten of them straight back, at rather less than it had just paid for them. Nobody
    /// would have noticed: both numbers were defensible on their own and the loop is quiet, a few gold at a
    /// time. It is this project's commonest defect, and this time it was introduced by the person fixing the
    /// trade rather than found in it. The two numbers are one number.
    /// </para>
    /// </summary>
    public static int Keeps { get; set; } = 20;

    /// <summary>Stacks of raw meat kept off the market for a cook's own pan. For the summary.</summary>
    public static long Spared { get; private set; }

    /// <summary>Stacks of raw meat sold instead, because nobody there could cook it or had room to.</summary>
    public static long Sold { get; private set; }

    /// <summary>Forgotten with the world, like every count in this assembly.</summary>
    public static void Forget()
    {
        Spared = 0;
        Sold = 0;
    }

    /// <summary>Raw meat this bot should keep for its own pan instead of listing. See <see cref="Keeps"/>.</summary>
    public static bool Spares(Mobile bot, Item item)
    {
        if (item == null || bot == null || Kit(bot) == null)
        {
            return false;
        }

        var kind = item.GetType();

        for (var i = 0; i < Raw.Count; i++)
        {
            if (Raw[i] != kind)
            {
                continue;
            }

            // Counted after it is in the pack, which is where the caller lifts it to before asking.
            if (Amount(bot, kind) <= Keeps)
            {
                Spared++;

                return true;
            }

            Sold++;

            return false;
        }

        return false;
    }

    /// <summary>How much of a kind is in the pack.</summary>
    public static int Amount(Mobile bot, Type stuff) =>
        stuff == null ? 0 : bot?.Backpack?.GetAmount(stuff) ?? 0;

    /// <summary>
    /// The raw meat this bot has most of, or null when it has none worth cooking.
    ///
    /// Most of, rather than the first found: a cook standing on twenty ribs and one bird should be making
    /// ribs, and the order of the list above should decide nothing but ties.
    /// </summary>
    public static Type Larder(Mobile bot, out int held)
    {
        held = 0;

        Type best = null;

        for (var i = 0; i < Raw.Count; i++)
        {
            var carried = Amount(bot, Raw[i]);

            if (carried > held)
            {
                held = carried;
                best = carried >= Worthwhile ? Raw[i] : null;
            }
        }

        return best;
    }

    /// <summary>The recipe this bot should cook from that meat, or null when its skill will not carry one.</summary>
    public static CraftItem Choose(Mobile bot, Type raw) =>
        raw == null ? null : BotCraftwork.Choose(bot, System, Skill, raw, Amount(bot, raw));

    /// <summary>Whether this is one of the meals this trade makes. Read by the eater. See BotMeal.</summary>
    public static bool IsMeal(Type kind) =>
        kind == typeof(Ribs)
        || kind == typeof(LambLeg)
        || kind == typeof(CookedBird)
        || kind == typeof(ChickenLeg)
        || kind == typeof(FishSteak);
}
