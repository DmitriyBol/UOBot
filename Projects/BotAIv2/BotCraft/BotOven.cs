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
    public static CraftSystem System => DefCooking.CraftSystem;

    public static SkillName Skill => SkillName.Cooking;

    public static BaseTool Kit(Mobile bot) => bot?.Backpack?.FindItemByType<Skillet>();

    public static IReadOnlyList<Type> Raw { get; } =
    [
        typeof(RawRibs),
        typeof(RawLambLeg),
        typeof(RawBird),
        typeof(RawChickenLeg),
        typeof(RawFishSteak)
    ];

    public static int Reach { get; set; } = 2;

    public static bool AtAHearth(Mobile bot) => CraftItem.NearHeatSource(bot);

    public static int Worthwhile { get; set; } = 2;

    public static int Worth { get; set; } = 6;

    public static int Keeps { get; set; } = 20;

    public static long Spared { get; private set; }

    public static long Sold { get; private set; }

    public static void Forget()
    {
        Spared = 0;
        Sold = 0;
    }

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

            if (Amount(bot, kind) <= Keeps && BotAuction.Demand(bot as IBotWilful, kind) == null)
            {
                Spared++;

                return true;
            }

            Sold++;

            return false;
        }

        return false;
    }

    public static int Amount(Mobile bot, Type stuff) =>
        stuff == null ? 0 : bot?.Backpack?.GetAmount(stuff) ?? 0;

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

    public static CraftItem Choose(Mobile bot, Type raw) =>
        raw == null ? null : BotCraftwork.Choose(bot, System, Skill, raw, Amount(bot, raw));

    public static bool IsMeal(Type kind) =>
        kind == typeof(Ribs)
        || kind == typeof(LambLeg)
        || kind == typeof(CookedBird)
        || kind == typeof(ChickenLeg)
        || kind == typeof(FishSteak);
}
