using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Whether a bot has anything in its hands, asked at the moment it matters.
///
/// <para>
/// <b>Being armed was treated as an event and it is a condition.</b> A weapon is put on at birth, and
/// <c>BotMobile.Rearm</c> puts one back on after a death, after a bot recovers its own corpse, and after a
/// shopping trip — three moments, all of them chosen. Nothing anywhere asks the question at the only moment
/// it decides anything, which is the moment a fight starts. So a staff that wears out mid-session, or a
/// weapon that comes back into the pack by any route nobody thought of, leaves the bot swinging its fists
/// until something kills it: healers and mages were seen doing exactly that on 24.08.2026, holding nothing,
/// against creatures that hit back.
/// </para>
///
/// <para>
/// <b>The brawler is the one exception and it is a real one.</b> Its whole build is wrestling — its skills
/// are in its hands, and putting a blade on it would be worse than useless. Every other class carrying
/// nothing is a class that has lost something.
/// </para>
///
/// <para>
/// This fixes what it can from the pack and counts what it cannot, and the second half is the point: "the
/// staff is in the backpack" and "the staff no longer exists" want completely different remedies, and
/// nothing so far could tell them apart.
/// </para>
/// </summary>
public static class BotArms
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotArms));

    public const string Brawler = "Brawler";

    public static int EveryMs { get; set; } = 5000;

    public static long Caught { get; private set; }

    public static long Rearmed { get; private set; }

    public static long Empty { get; private set; }

    public static long Dressed { get; private set; }

    public static long Declined { get; private set; }

    public static void Dressing(int worn, int refused)
    {
        Dressed += worn;
        Declined += refused;
    }

    private static bool _said;

    public static bool Armed(Mobile body, BotClass klass) =>
        body?.Weapon is not (null or Fists) || klass?.Name == Brawler;

    public static bool Suit(Mobile body, Mobile foe, int keepAway)
    {
        if (body is not BotMobile { Class.Closes: true } closer || foe is not { Deleted: false })
        {
            return false;
        }

        var near = body.InRange(foe.Location, keepAway);

        closer.Draw(melee: near);

        return near;
    }

    public static long Dry { get; private set; }

    public static long Restrung { get; private set; }

    public static bool Stocked(BotMobile bot, BotClass klass)
    {
        var pack = bot?.Backpack;

        if (pack == null || klass?.Kit.Ranged is not { Count: > 0 } options)
        {
            return false;
        }

        for (var i = 0; i < options.Count; i++)
        {
            var ammo = options[i].Ammunition;

            if (ammo != null && pack.GetAmount(ammo) > 0)
            {
                return true;
            }
        }

        return false;
    }

    public static void Quiver(Mobile body, BotClass klass)
    {
        if (body is not BotMobile bot || klass?.Kit.Ranged is not { Count: > 0 } options)
        {
            return;
        }

        var pack = bot.Backpack;

        if (pack == null)
        {
            return;
        }

        var stocked = Stocked(bot, klass);

        var held = bot.Weapon as Item;
        var shooting = held is BaseRanged && held.Parent == bot;

        if (shooting == stocked)
        {
            return;
        }

        if (!bot.Draw(melee: !stocked))
        {
            return;
        }

        if (stocked)
        {
            Restrung++;
        }
        else
        {
            Dry++;
        }
    }

    public static bool Check(Mobile body, BotClass klass)
    {
        Quiver(body, klass);

        if (Armed(body, klass))
        {
            return true;
        }

        Caught++;

        var worn = (body as BotMobile)?.Rearm() ?? 0;

        if (worn > 0 && Armed(body, klass))
        {
            Rearmed++;

            return true;
        }

        Empty++;

        Once(body, klass);

        return false;
    }

    private static void Once(Mobile body, BotClass klass)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Error(
            "{Name} the {Class} is fighting bare-handed and has nothing in its pack to put on; only a {Brawler} may do that",
            body.Name,
            klass?.Name ?? "bot",
            Brawler
        );
    }

    public static string Describe() =>
        Caught == 0
            ? $"nobody has been caught bare-handed; {Dry} found with an empty quiver and {Restrung} took the bow back up; {Dressed} things put on, {Declined} refused by the engine, {BotMobile.Misfits} passed over as beyond this body"
            : $"{Caught} found bare-handed: {Rearmed} had one in the pack, {Empty} had nothing at all; {Dry} found with an empty quiver and {Restrung} took the bow back up; {Dressed} things put on, {Declined} refused by the engine, {BotMobile.Misfits} passed over as beyond this body";

    public static void Forget()
    {
        _said = false;
        Caught = 0;
        Rearmed = 0;
        Empty = 0;
        Dressed = 0;
        Declined = 0;
        Dry = 0;
        Restrung = 0;
    }
}
