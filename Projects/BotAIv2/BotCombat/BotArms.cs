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

    /// <summary>The class that fights with its hands on purpose.</summary>
    public const string Brawler = "Brawler";

    /// <summary>How often one bot is worth checking. A fight asks this every beat; the answer changes rarely.</summary>
    public static int EveryMs { get; set; } = 5000;

    /// <summary>Times a bot was found bare-handed, and how those went. No bucket called "other".</summary>
    public static long Caught { get; private set; }

    public static long Rearmed { get; private set; }

    public static long Empty { get; private set; }

    /// <summary>
    /// Pieces actually put on, and pieces the engine refused, across the whole population.
    ///
    /// <para>
    /// <b>These exist because the only record of dressing was a log line throttled to one a minute per
    /// bot.</b> That throttle is right for a log — a caster picking its staff back up after every cast is a
    /// hundred lines an hour of nothing — and it is exactly wrong as a measurement: with fifteen bots locked
    /// out of armour by a bad guard on 27.08.2026, the log showed two bots dressing in eleven minutes and
    /// two bots dressing is what a working shard looks like too. A count cannot be throttled into agreeing
    /// with a broken one.
    /// </para>
    /// </summary>
    public static long Dressed { get; private set; }

    public static long Declined { get; private set; }

    /// <summary>Told what one bot's re-arm came to. Called by <c>BotMobile.Rearm</c> and nothing else.</summary>
    public static void Dressing(int worn, int refused)
    {
        Dressed += worn;
        Declined += refused;
    }

    private static bool _said;

    /// <summary>Whether this bot is holding a real weapon, or is the one class entitled not to.</summary>
    public static bool Armed(Mobile body, BotClass klass) =>
        body?.Weapon is not (null or Fists) || klass?.Name == Brawler;

    /// <summary>
    /// Puts something in the bot's hands if anything in its pack will go there.
    ///
    /// Returns whether the bot is armed afterwards. Throttled per bot by the caller's own clock — see
    /// <see cref="EveryMs"/> — because the check walks the pack and a fight asks several times a second.
    /// </summary>
    /// <summary>
    /// Puts the right weapon in a closing fighter's hand for the distance the fight is actually at.
    ///
    /// <para>
    /// <b>One rule, asked from both of the places a bot can be in a fight.</b> A bot fights either as itself,
    /// through <c>BotSlay</c>, or as part of a company, through <c>BotSquad.Press</c> — and those two do not
    /// share a line of code, which is exactly the shape of defect this project keeps paying for. Written into
    /// the hunt alone, a captain would draw its sword when hunting on its own and stand there holding a bow
    /// at arm's length the moment it was leading the company it exists to lead. The distance each caller
    /// considers "close" is its own — a standoff is not a press ring — so that is the parameter and the
    /// judgement is not.
    /// </para>
    /// </summary>
    /// <param name="keepAway">Inside this many tiles, the blade; outside it, the bow.</param>
    /// <returns>Whether the bot is now fighting at arm's length rather than at range.</returns>
    public static bool Suit(Mobile body, Mobile foe, int keepAway)
    {
        if (body is not BotMobile { Class.Closes: true } closer || foe is not { Deleted: false })
        {
            return false;
        }

        var near = body.InRange(foe.Location, keepAway);

        // Drawn either way: the swap back to the bow when something dies or runs matters as much as the swap
        // to the blade, and leaving it out is how a class that shoots first shoots first only once.
        closer.Draw(melee: near);

        return near;
    }

    /// <summary>Times a shooter was found with an empty quiver and put a blade in its hand instead.</summary>
    public static long Dry { get; private set; }

    /// <summary>Times one of those took its bow back up because it had something to fire again.</summary>
    public static long Restrung { get; private set; }

    /// <summary>
    /// Keeps a shooter's hand matched to its quiver: the blade when there is nothing to fire, the bow when
    /// there is.
    ///
    /// <para>
    /// <b>An empty quiver is an empty hand, and nothing on this shard knew it.</b> A bow with no arrows is a
    /// weapon by every test in this file and by <c>Mobile.Weapon</c>, and the engine dutifully swings it:
    /// <c>BaseRanged.OnSwing</c> calls <c>OnFired</c>, finds no ammunition, and returns having done nothing
    /// at all — no damage, no message, no clue. Watched from outside it is a bot standing in front of a
    /// mongbat for forty-five seconds with a hundred per cent of the mongbat left. Five archers ran
    /// sixty-seven of those in ten minutes on 04.09.2026, and the rate had been climbing all night as the
    /// population's arrows were spent: nobody fletches, the shopkeepers carry few, and gleaning brings back
    /// one or two at a time off the ground.
    /// </para>
    ///
    /// <para>
    /// <b>Both directions, and one of them alone would have been a worse bug than the one it fixed.</b> A
    /// shooter that drew its dagger and never went back to the bow would be permanently downgraded the
    /// moment it restocked. So the hand follows the quiver, in both directions, and the swap is idempotent —
    /// <c>Draw</c> does nothing when what is held already matches.
    /// </para>
    ///
    /// <para>
    /// This is <c>BotSlay</c>'s own rule about spells, applied to the thing it was written about: "a warrior
    /// down to its last scroll closes and swings like a warrior rather than keeping a mage's distance on the
    /// strength of one arrow".
    /// </para>
    /// </summary>
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

        // Anything this class's bows could fire, not merely what the one in its hands takes: a crossbow in
        // the pack and bolts to go with it is a loaded shooter however empty the bow it happens to hold.
        var stocked = false;

        for (var i = 0; i < options.Count && !stocked; i++)
        {
            var ammo = options[i].Ammunition;

            stocked = ammo != null && pack.GetAmount(ammo) > 0;
        }

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
        // Before "is it holding a weapon", because a bow with nothing to fire passes that test and fails the
        // fight. See Quiver.
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

        // Said once, by name and by class, because a bot fighting with its fists looks in every summary
        // exactly like a bot fighting.
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
