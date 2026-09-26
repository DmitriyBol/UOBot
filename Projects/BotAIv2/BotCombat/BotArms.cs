using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Text;

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

    public static long Casting { get; private set; }

    public static long Dressed { get; private set; }

    public static long Declined { get; private set; }

    public static void Dressing(int worn, int refused)
    {
        Dressed += worn;
        Declined += refused;
    }

    private static readonly HashSet<Serial> _saidFor = [];

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

        if (pack == null || klass?.Kit.Ranged is not { Count: > 0 })
        {
            return false;
        }

        if (bot.Weapon is BaseRanged { Deleted: false } held && held.Parent == bot && Loaded(pack, held))
        {
            return true;
        }

        foreach (var item in pack.Items)
        {
            if (item is BaseRanged { Deleted: false } carried && bot.Suits(carried) && Loaded(pack, carried))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Loaded(Container pack, BaseRanged bow) =>
        bow.AmmoType != null && pack.GetAmount(bow.AmmoType) > 0;

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

        if (body.Spell != null)
        {
            Casting++;

            return false;
        }

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
        if (body == null || !_saidFor.Add(body.Serial))
        {
            return;
        }

        logger.Error(
            "{Name} the {Class} is fighting bare-handed and has nothing in its pack to put on; only a {Brawler} may do that; its bound things: {Where}",
            body.Name,
            klass?.Name ?? "bot",
            Brawler,
            Whereabouts(body)
        );
    }

    private static string Whereabouts(Mobile body)
    {
        if (body is not BotMobile { Bond: { } bond })
        {
            return "no bond to read";
        }

        var say = ValueStringBuilder.Create(512);

        try
        {
            var found = 0;

            foreach (var serial in bond.Items)
            {
                if (found++ > 0)
                {
                    say.Append("; ");
                }

                var item = World.FindItem(serial);

                if (item == null || item.Deleted)
                {
                    say.Append(serial.ToString());
                    say.Append(" gone from the world");

                    continue;
                }

                say.Append(item.GetType().Name);

                switch (item.RootParent)
                {
                    case Mobile holder when ReferenceEquals(holder, body):
                        say.Append(item.Parent is Mobile ? " in hand" : " in its own pack");

                        break;

                    case Mobile holder:
                        say.Append(" carried by ");
                        say.Append(holder.Name ?? "somebody");

                        break;

                    case Item box:
                        say.Append(" inside ");
                        say.Append(box.GetType().Name);
                        say.Append(" at (");
                        say.Append(box.X);
                        say.Append(", ");
                        say.Append(box.Y);
                        say.Append(")");

                        break;

                    default:
                        say.Append(" on the ground at (");
                        say.Append(item.X);
                        say.Append(", ");
                        say.Append(item.Y);
                        say.Append(")");

                        break;
                }
            }

            return found == 0 ? "nothing was ever bound to it" : say.ToString();
        }
        finally
        {
            say.Dispose();
        }
    }

    public static string Describe() =>
        Caught == 0
            ? $"nobody has been caught bare-handed; {Dry} found with an empty quiver and {Restrung} took the bow back up; {Dressed} things put on, {Declined} refused by the engine, {BotMobile.Misfits} passed over as beyond this body"
            : $"{Caught} found bare-handed: {Rearmed} had one in the pack, {Empty} had nothing at all, {Casting} had a spell going up; {Dry} found with an empty quiver and {Restrung} took the bow back up; {Dressed} things put on, {Declined} refused by the engine, {BotMobile.Misfits} passed over as beyond this body, {BotMobile.Rewielded} weapons put away for a better one of the bot's own kind, {BotMobile.Reverted} not put in a hand again so soon, {BotBinding.Refused} bound things turned away from a stall or a want";

    public static void Forget()
    {
        _saidFor.Clear();
        Caught = 0;
        Rearmed = 0;
        Empty = 0;
        Casting = 0;
        Dressed = 0;
        Declined = 0;
        Dry = 0;
        Restrung = 0;
    }
}
