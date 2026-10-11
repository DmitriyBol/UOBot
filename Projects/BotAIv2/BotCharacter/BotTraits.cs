using System;
using System.Collections.Generic;
using System.Text;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>The ten natures a bot can have, in five opposed pairs. A bot has two or three, never both of one pair.</summary>
[Flags]
public enum BotTrait
{
    None = 0,
    Greedy = 1,
    Generous = 2,
    Cautious = 4,
    Reckless = 8,
    Homebody = 16,
    Wanderer = 32,
    Vindictive = 64,
    Forgiving = 128,
    Sociable = 256,
    Solitary = 512
}

/// <summary>
/// A bot's nature: two or three traits drawn at birth from five opposed pairs, each one a single multiplier in the auction on
/// the work it touches, and the veteran's title.
///
/// <para>
/// <b>Patrick's idea of 07.10.2026, approved for the night: "each bot gets 2–3 traits at birth (greedy/generous,
/// cautious/reckless, homebody/wanderer, vindictive) that really shift its auction of work".</b> A trait is a price, never an
/// order, in the shape the guild's charter and the watcher's revels already are: <see cref="Nature"/> multiplies the score
/// after the root (as the calling and the toll do), so ×1.15 is ×1.15 and not its fifth root, and the take line says it:
/// "; × 1.15 for its nature (greedy)". Greedy prices trading, peddling, keeping a shop, hauling and plunder up and a rescue,
/// an escort, a house call or a lesson down; generous the reverse. Cautious prices delves, sweeps, raids and harrows down, and
/// reckless up. A homebody prices a long walk (past <see cref="FarTiles"/>, or travel, a voyage, a prowl, a venture, a scout, a
/// prospect) down and a wanderer up. Vindictive prices a grudge (<see cref="BotGrudge"/>) or a manhunt up and forgiving down.
/// Sociable prices a fire, a chat, a meeting and a company up and solitary down.
/// </para>
///
/// <para>
/// <b>The population's totals must not swing.</b> Each pair is up by <see cref="Lean"/> on one side and down by the same on the
/// other, so with the sides drawn evenly the mean multiplier on any trade across the island is one; the boot line prints the
/// distribution and the mean the drawn population actually gives each group of work. The whole product is held inside
/// [<see cref="Least"/>, <see cref="Most"/>].
/// </para>
///
/// <para>
/// <b>Cautious and reckless also change two numbers in the fight itself, and never past Patrick's hard limits.</b> A cautious
/// bot alone takes on quarry only to ×<see cref="CautiousDaring"/> of the standing daring (<c>BotQuarry.Daring</c>) and gives a
/// losing fight up at ×<see cref="CautiousFlee"/> of the standing flight share (<c>BotSlay.FleeAt</c>); a reckless one at
/// ×<see cref="RecklessDaring"/> and ×<see cref="RecklessFlee"/>. The daring never goes past the flight rule's tolerance
/// (<c>BotThreat.Tolerance</c>: a bot does not walk to something it would run from) and the flight share never below
/// <see cref="FleeFloor"/>; casters are fought to the end as before, because that rule is the slayer's and is untouched.
/// </para>
///
/// <para>
/// Drawn from the bot's name (a stable hash, not .NET's per-process one), so the first boot after this build gives every
/// living bot the same nature it will have on every boot after; and kept by <see cref="BotCharacterStore"/>, so a dial change
/// to the drawing never rewrites who anybody already is.
/// </para>
/// </summary>
public static class BotTraits
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTraits));

    public static bool On { get; set; } = true;

    public static double Lean { get; set; } = 0.15;

    public static double Least { get; set; } = 0.8;

    public static double Most { get; set; } = 1.25;

    public static int FarTiles { get; set; } = 120;

    public static double CautiousDaring { get; set; } = 0.85;

    public static double RecklessDaring { get; set; } = 1.1;

    public static double CautiousFlee { get; set; } = 1.1;

    public static double RecklessFlee { get; set; } = 0.9;

    public static double FleeFloor { get; set; } = 0.3;

    public static int VeteranKills { get; set; } = 100;

    public static string VeteranTitle { get; set; } = "the Veteran";

    public static long Raised { get; private set; }

    public static long Lowered { get; private set; }

    public static long Dared { get; private set; }

    public static long Fled { get; private set; }

    public static long Veterans { get; private set; }

    public static long Births { get; private set; }

    private static readonly BotTrait[][] Pairs =
    [
        [BotTrait.Greedy, BotTrait.Generous],
        [BotTrait.Cautious, BotTrait.Reckless],
        [BotTrait.Homebody, BotTrait.Wanderer],
        [BotTrait.Vindictive, BotTrait.Forgiving],
        [BotTrait.Sociable, BotTrait.Solitary]
    ];

    public static BotTrait Draw(string name)
    {
        var hash = 2166136261u;

        for (var i = 0; i < name.Length; i++)
        {
            hash ^= name[i];
            hash *= 16777619u;
        }

        var rng = new System.Random((int)(hash & 0x7FFFFFFF));
        Span<int> order = stackalloc int[Pairs.Length];

        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        for (var i = order.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var count = 2 + rng.Next(2);
        var traits = BotTrait.None;

        for (var i = 0; i < count; i++)
        {
            traits |= Pairs[order[i]][rng.Next(2)];
        }

        return traits;
    }

    public static BotTrait Of(Mobile m) => BotLives.Of(m)?.Traits ?? BotTrait.None;

    public static bool Has(Mobile m, BotTrait trait) => (Of(m) & trait) != 0;

    public static bool Veteran(Mobile m) => On && BotLives.Of(m) is { Veteran: true };

    private static int Greed(string kind) =>
        kind switch
        {
            "trade" or "peddle" or "shopkeep" or "haul" or "plunder" or "pickings" => 1,
            "rescue" or "escort" or "housecall" or "tutor" => -1,
            _ => 0
        };

    private static bool Risky(string kind) => kind is "delve" or "sweep" or "raid" or "harrow" or "outpost";

    private static bool Roams(string kind) => kind is "travel" or "voyage" or "prowl" or "venture" or "scout" or "prospect";

    private static bool Grudging(string kind) => kind is BotGrudge.Trade or "manhunt";

    private static bool Company(string kind) => kind is "camp" or "fireside" or "chat" or "meet" or "band" or "enlist";

    public static double Nature(Mobile body, BotDeed deed, out BotTrait bit)
    {
        bit = BotTrait.None;

        if (!On || deed == null || body is not BotMobile)
        {
            return 1.0;
        }

        var traits = Of(body);

        if (traits == BotTrait.None)
        {
            return 1.0;
        }

        var up = 1.0 + Lean;
        var down = 1.0 - Lean;
        var factor = 1.0;
        var kind = deed.Kind;

        var greed = Greed(kind);

        if (greed != 0)
        {
            if ((traits & BotTrait.Greedy) != 0)
            {
                factor *= greed > 0 ? up : down;
                bit |= BotTrait.Greedy;
            }
            else if ((traits & BotTrait.Generous) != 0)
            {
                factor *= greed > 0 ? down : up;
                bit |= BotTrait.Generous;
            }
        }

        if (Risky(kind))
        {
            if ((traits & BotTrait.Cautious) != 0)
            {
                factor *= down;
                bit |= BotTrait.Cautious;
            }
            else if ((traits & BotTrait.Reckless) != 0)
            {
                factor *= up;
                bit |= BotTrait.Reckless;
            }
        }

        if ((traits & (BotTrait.Homebody | BotTrait.Wanderer)) != 0
            && (Roams(kind) || deed.Map == body.Map && deed.Where != Point3D.Zero && Far(body.Location, deed.Where)))
        {
            var home = (traits & BotTrait.Homebody) != 0;
            factor *= home ? down : up;
            bit |= home ? BotTrait.Homebody : BotTrait.Wanderer;
        }

        if (Grudging(kind))
        {
            if ((traits & BotTrait.Vindictive) != 0)
            {
                factor *= up;
                bit |= BotTrait.Vindictive;
            }
            else if ((traits & BotTrait.Forgiving) != 0)
            {
                factor *= down;
                bit |= BotTrait.Forgiving;
            }
        }

        if (Company(kind))
        {
            if ((traits & BotTrait.Sociable) != 0)
            {
                factor *= up;
                bit |= BotTrait.Sociable;
            }
            else if ((traits & BotTrait.Solitary) != 0)
            {
                factor *= down;
                bit |= BotTrait.Solitary;
            }
        }

        if (bit == BotTrait.None)
        {
            return 1.0;
        }

        factor = Math.Clamp(factor, Math.Min(Least, 1.0), Math.Max(Most, 1.0));

        if (factor > 1.0)
        {
            Raised++;
        }
        else if (factor < 1.0)
        {
            Lowered++;
        }

        return factor;
    }

    private static bool Far(Point3D from, Point3D to) => Math.Max(Math.Abs(from.X - to.X), Math.Abs(from.Y - to.Y)) >= FarTiles;

    public static double Daring(Mobile bot)
    {
        var standing = BotQuarry.Daring;

        if (!On)
        {
            return standing;
        }

        var traits = Of(bot);
        var lean = (traits & BotTrait.Cautious) != 0 ? CautiousDaring
            : (traits & BotTrait.Reckless) != 0 ? RecklessDaring
            : 1.0;

        if (lean == 1.0)
        {
            return standing;
        }

        Dared++;

        return Math.Min(standing * lean, Math.Max(standing, BotThreat.Tolerance));
    }

    public static double FleeAt(Mobile bot) => FleeAt(bot, true);

    public static double FleeAt(Mobile bot, bool count)
    {
        var standing = BotSlay.FleeAt;

        if (!On)
        {
            return standing;
        }

        var traits = Of(bot);
        var lean = (traits & BotTrait.Cautious) != 0 ? CautiousFlee
            : (traits & BotTrait.Reckless) != 0 ? RecklessFlee
            : 1.0;

        if (lean == 1.0)
        {
            return standing;
        }

        if (count)
        {
            Fled++;
        }

        return Math.Clamp(standing * lean, Math.Min(FleeFloor, standing), 0.9);
    }

    public static string Suffix(Mobile m, string suffix)
    {
        if (!Veteran(m) || string.IsNullOrEmpty(VeteranTitle))
        {
            return suffix;
        }

        var rest = suffix?.Trim() ?? "";

        return rest.Length == 0 ? $" {VeteranTitle}" : $" {VeteranTitle} {rest}";
    }

    public static void Credit(BotMobile bot, double share)
    {
        if (bot == null || share <= 0.0 || BotLives.Of(bot) is not { } life)
        {
            return;
        }

        life.Kills += share;

        if (life.Veteran || !On || VeteranKills <= 0 || life.Kills < VeteranKills)
        {
            return;
        }

        life.Veteran = true;
        life.VeteranAt = DateTime.UtcNow;
        Veterans++;

        bot.InvalidateProperties();

        var kills = (int)life.Kills;

        logger.Information(
            "{Name} the {Class} is a veteran: {Kills} kills to its credit ({War} of them bots in war), {Deaths} deaths; it may now lead a clearing past the novice bar, call a company past the island's ceiling, and weighs double in its guild's elections",
            bot.Name,
            bot.Class?.Name,
            kills,
            life.WarKills,
            life.Deaths
        );

        BotBiography.Keep(life, "veteran", null, $"A hundred kills and more behind me — {kills} now. They call me {VeteranTitle} these days.", 60.0, kills);

        var fill = new Dictionary<string, string> { ["kills"] = kills.ToString(), ["title"] = VeteranTitle };
        var line = BotVoice.Phrase("character:veteran", null, bot, fill);

        if (line != null)
        {
            BotVoice.Say(bot, bot.Guild != null ? "guild" : "local", line, force: true);
        }
    }

    public static string Words(BotTrait traits)
    {
        if (traits == BotTrait.None)
        {
            return "none";
        }

        var sb = new StringBuilder();

        foreach (var pair in Pairs)
        {
            for (var k = 0; k < 2; k++)
            {
                if ((traits & pair[k]) != 0)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(pair[k].ToString().ToLowerInvariant());
                }
            }
        }

        return sb.ToString();
    }

    public static IEnumerable<string> List(BotTrait traits)
    {
        foreach (var pair in Pairs)
        {
            for (var k = 0; k < 2; k++)
            {
                if ((traits & pair[k]) != 0)
                {
                    yield return pair[k].ToString().ToLowerInvariant();
                }
            }
        }
    }

    public static void Born(BotMobile bot, bool afterBoot)
    {
        if (BotLives.Of(bot) is not { Fresh: true } life)
        {
            return;
        }

        life.Fresh = false;

        if (!afterBoot)
        {
            return;
        }

        Births++;

        logger.Information("{Name} the {Class} is born {Traits}", bot.Name, bot.Class?.Name, Words(life.Traits));
    }

    public static string Distribution()
    {
        var counts = new int[10];
        var n = 0;
        var veterans = 0;

        foreach (var life in BotLives.All)
        {
            if (BotLives.Body(life.Name) == null)
            {
                continue;
            }

            n++;

            if (life.Veteran)
            {
                veterans++;
            }

            for (var b = 0; b < 10; b++)
            {
                if (((int)life.Traits & (1 << b)) != 0)
                {
                    counts[b]++;
                }
            }
        }

        if (n == 0)
        {
            return "nobody in the world";
        }

        var sb = new StringBuilder();

        sb.Append($"{n} bots: ");

        for (var b = 0; b < 10; b++)
        {
            if (b > 0)
            {
                sb.Append(b % 2 == 0 ? "; " : "/");
            }

            sb.Append($"{((BotTrait)(1 << b)).ToString().ToLowerInvariant()} {counts[b]}");
        }

        string[] groups = ["trade work (greedy/generous)", "danger (cautious/reckless)", "far walks (homebody/wanderer)", "grudges (vindictive/forgiving)", "company (sociable/solitary)"];

        sb.Append("; the mean multiplier across them on ");

        for (var p = 0; p < 5; p++)
        {
            var mean = (counts[p * 2] * (1.0 + Lean) + counts[p * 2 + 1] * (1.0 - Lean) + (n - counts[p * 2] - counts[p * 2 + 1])) / n;

            sb.Append(p == 0 ? "" : ", ").Append($"{groups[p]} ×{mean:F3}");
        }

        sb.Append($" (and the reverse on giving work); {veterans} veterans");

        return sb.ToString();
    }

    public static string Describe() =>
        !On
            ? "natures are off"
            : $"{Distribution()}; offers a nature raised {Raised} and lowered {Lowered} (×{1.0 - Lean:F2}–×{1.0 + Lean:F2}, held in ×{Least:F2}–×{Most:F2}), lone hunts weighed at a nature's daring {Dared}, losing fights judged at a nature's flight share {Fled}; {Veterans} veterans made at {VeteranKills} kills ({BotClearer.VeteransAsked} clearings asked of a veteran past the class or novice bar, {BotMuster.VeteransPastCeiling} companies called by a veteran past the island's ceiling), {Births} newcomers born";

    public static void Forget()
    {
        Raised = 0;
        Lowered = 0;
        Dared = 0;
        Fled = 0;
        Veterans = 0;
        Births = 0;
    }
}
