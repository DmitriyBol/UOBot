using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// What one guild thinks of another, and the two thresholds that turn an opinion into a war.
///
/// <para>
/// <b>Stage four of Patrick's order of 08.09.2026, and the one he deliberately left unbuilt</b> — his own
/// words: the only part that can make the population smaller. So it is built with the fighting behind a
/// switch that is off, and the opinions running and visible from the first minute. An evening of watching
/// guilds come to dislike each other, with nobody dying of it, is what says whether the numbers are right;
/// the switch is then one dial.
/// </para>
///
/// <para>
/// <b>A number per ordered pair, and it is only ever moved by something that happened.</b> Nothing here
/// decays towards a designed outcome and nothing drifts on a timer except the border, which is itself a
/// fact about the ground. Every mover is listed in <see cref="Describe"/> with its own count, so a war can
/// always be explained by what caused it rather than guessed at.
/// </para>
///
/// <para>
/// <b>Two thresholds and not one.</b> War is declared below <see cref="Enmity"/> and ended above
/// <see cref="Amity"/>, and the gap between them is the whole reason a war does not flicker on and off every
/// minute as one trade lands. That is the same shape the shard's other hysteresis has — see
/// <c>BotUnload</c>'s pair — and it is here for the same reason.
/// </para>
///
/// <para>
/// <b>The engine does the enforcing.</b> <c>Guild.AddEnemy</c> makes the other side
/// <c>Notoriety.Enemy</c>, which makes striking them lawful with no criminal flag and no guard reaction,
/// and makes their corpses lawful to loot. Nothing here touches karma, the criminal flag or the watch, and
/// no looting code is written: <c>BotPickings</c> already loots what a bot killed itself.
/// </para>
/// </summary>
public static class BotRegard
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRegard));

    /// <summary>Whether guilds form opinions of each other at all.</summary>
    public static bool Running { get; set; } = true;

    /// <summary>
    /// Whether a bad enough opinion is allowed to become a war.
    ///
    /// <para>
    /// <b>On, by Patrick's order of 09.09.2026: "they should feel the consequences."</b> It was built off,
    /// because he had named this the one part of the plan that can shrink the population; it ran for an
    /// evening with the opinions moving and nothing dying of them, and the numbers behaved — a quarrel driven
    /// by trespass and by bots refusing to move along, mended by trade, reaching -94 of the -100 it takes.
    /// </para>
    ///
    /// <para>
    /// What to read now that it bites, and none of it can be read before: deaths an hour, skill lost per
    /// death, and how much of what was looted was ever used by whoever took it. Those are Patrick's own three
    /// from <c>PLAN-guild-lands.md</c>. Off again is one dial — <c>dial BotRegard.Warring false</c> through
    /// Argus, or <c>"War": false</c> in <c>bot-estate.json</c> — and a war already declared ends by itself
    /// when the two guilds trade their way back above <see cref="Amity"/>.
    /// </para>
    /// </summary>
    public static bool Warring { get; set; } = true;

    /// <summary>Below this, one guild declares war on another.</summary>
    public static double Enmity { get; set; } = -100.0;

    /// <summary>And above this, the war ends. The gap is what stops it flickering.</summary>
    public static double Amity { get; set; } = -20.0;

    /// <summary>As low and as high as an opinion may go, so one bad afternoon cannot be past mending.</summary>
    public static double Floor { get; set; } = -200.0;

    /// <summary>And the ceiling. Friendship does not accumulate for ever either.</summary>
    public static double Ceiling { get; set; } = 100.0;

    // ---- What moves an opinion. Every one of these has a bucket, because a war nobody can explain is a war
    // nobody can tune. ------------------------------------------------------------------------------------

    /// <summary>What finishing a piece of work on another guild's land costs, each time.</summary>
    public static double Trespass { get; set; } = -0.5;

    /// <summary>What being told to move along and not going costs.</summary>
    public static double Defiance { get; set; } = -6.0;

    /// <summary>What killing one of theirs costs.</summary>
    public static double Blood { get; set; } = -25.0;

    /// <summary>
    /// What two halls standing on top of each other costs, per drift, before distance softens it.
    ///
    /// <para>
    /// <b>Four, and the first cut of one was measured wrong within five minutes.</b> At one a drift, against
    /// trade at two a sale both ways, the first reading was 22 trades against 12 drifts — so every opinion on
    /// the island was rising and the whole of stage four was inert: a system that can only ever end in peace
    /// is not a system, it is a decoration. The two numbers had never been put beside each other.
    /// </para>
    ///
    /// <para>
    /// At four, softened by distance, the closest pair of halls on the island loses about thirty-seven an
    /// hour and reaches war in something under three hours of shard time. That is slow enough to be a
    /// quarrel rather than a switch and fast enough that an evening shows one.
    /// </para>
    /// </summary>
    public static double Border { get; set; } = -4.0;

    /// <summary>
    /// How near two halls must be for their guilds to count as neighbours at all.
    ///
    /// <para>
    /// Twice the reach of a land claim, and taken from it rather than chosen: two guilds are neighbours
    /// exactly when their yards touch, which is a fact about <see cref="BotLand.Reach"/> and not a second
    /// number to keep in step with it. Moving the claim moves this.
    /// </para>
    /// </summary>
    public static int Neighbouring => BotLand.Reach * 2;

    /// <summary>How often the border drift is applied, in milliseconds.</summary>
    public static int DriftMs { get; set; } = 300000;

    /// <summary>
    /// What trading with them is worth. The only thing that mends an opinion.
    ///
    /// Half, and small on purpose: a sale is one bot buying one thing, it happens a few times a minute
    /// across the island, and at two it drowned every other mover put together. Mending a quarrel should
    /// take a trading relationship rather than a transaction.
    /// </summary>
    public static double Trade { get; set; } = 0.5;

    /// <summary>Opinions worsened by somebody working on land that was not theirs.</summary>
    public static long Trespasses { get; private set; }

    /// <summary>Opinions worsened by somebody refusing to move.</summary>
    public static long Defiances { get; private set; }

    /// <summary>Opinions worsened by a killing.</summary>
    public static long Bloodshed { get; private set; }

    /// <summary>Opinions worsened for sharing a border.</summary>
    public static long Borders { get; private set; }

    /// <summary>Opinions mended by trade.</summary>
    public static long Trades { get; private set; }

    /// <summary>Wars declared.</summary>
    public static long Declared { get; private set; }

    /// <summary>Wars ended.</summary>
    public static long Ended { get; private set; }

    /// <summary>Times a war would have been declared and the switch was off.</summary>
    public static long Withheld { get; private set; }

    private static readonly Dictionary<(string Of, string For), double> _regard = [];

    private static long _drifted;

    /// <summary>Whether the drift has ever run. A tick count of nought is a legitimate reading; see rule 20.</summary>
    private static bool _everDrifted;

    /// <summary>What <paramref name="of"/> thinks of <paramref name="about"/>. Nought between strangers.</summary>
    public static double Of(string of, string about)
    {
        if (of == null || about == null || of == about)
        {
            return 0.0;
        }

        return _regard.TryGetValue((of, about), out var held) ? held : 0.0;
    }

    /// <summary>The same, of two guilds.</summary>
    public static double Of(Guild of, Guild about) => Of(of?.Name, about?.Name);

    /// <summary>
    /// Moves an opinion, and asks afterwards whether it has crossed either threshold.
    ///
    /// <para>
    /// Both ways round, because an opinion is between two guilds and not one: a bot of the Blade digging on
    /// the Crown's land is a thing the Crown minds, and it is the Crown's opinion of the Blade that moves.
    /// Only that one. The Blade has no opinion about having been somewhere.
    /// </para>
    /// </summary>
    public static void Move(string of, string about, double by, string why)
    {
        if (!Running || of == null || about == null || of == about || by == 0.0)
        {
            return;
        }

        var was = Of(of, about);
        var now = Math.Clamp(was + by, Floor, Ceiling);

        if (Math.Abs(now - was) < 0.0001)
        {
            return;
        }

        _regard[(of, about)] = now;

        Reckon(of, about, was, now, why);
    }

    /// <summary>Somebody of <paramref name="who"/> finished a piece of work on <paramref name="whose"/> land.</summary>
    public static void Trespassed(string who, string whose)
    {
        if (who == null || whose == null || who == whose)
        {
            return;
        }

        Trespasses++;
        Move(whose, who, Trespass, "working our land");
    }

    /// <summary>Somebody of <paramref name="who"/> was told to move along and did not.</summary>
    public static void Defied(string who, string whose)
    {
        if (who == null || whose == null || who == whose)
        {
            return;
        }

        Defiances++;
        Move(whose, who, Defiance, "refusing to move along");
    }

    /// <summary>Somebody of <paramref name="killer"/> killed a member of <paramref name="fallen"/>.</summary>
    public static void Killed(string killer, string fallen)
    {
        if (killer == null || fallen == null || killer == fallen)
        {
            return;
        }

        Bloodshed++;
        Move(fallen, killer, Blood, "blood");
    }

    /// <summary>Somebody of <paramref name="buyer"/> bought from or filled an order of <paramref name="seller"/>.</summary>
    public static void Traded(string buyer, string seller)
    {
        if (buyer == null || seller == null || buyer == seller)
        {
            return;
        }

        Trades++;
        Move(seller, buyer, Trade, "trade");
        Move(buyer, seller, Trade, "trade");
    }

    /// <summary>
    /// The slow souring between neighbours, applied on a timer rather than by an event.
    ///
    /// <para>
    /// This is the one mover with no incident behind it, and it is here because without it nothing ever
    /// happens: two guilds whose members never meet have no reason to quarrel, and quarrels between guilds
    /// at opposite ends of the island would be arbitrary. Sharing a border is the reason, and it is a fact
    /// about the ground rather than a number anybody chose.
    /// </para>
    /// </summary>
    public static void Drift()
    {
        if (!Running || Border == 0.0)
        {
            return;
        }

        var now = Core.TickCount;

        if (_everDrifted && now - (_drifted + DriftMs) < 0)
        {
            return;
        }

        _drifted = now;
        _everDrifted = true;

        foreach (var (mine, ours) in BotEstate.Held)
        {
            if (ours is not { Deleted: false })
            {
                continue;
            }

            foreach (var (theirs, yours) in BotEstate.Held)
            {
                if (mine == theirs || yours is not { Deleted: false } || yours.Map != ours.Map)
                {
                    continue;
                }

                var gap = Math.Max(Math.Abs(ours.X - yours.X), Math.Abs(ours.Y - yours.Y));

                if (gap >= Neighbouring)
                {
                    continue;
                }

                // <b>How much their yards actually overlap, not merely that they do.</b> A flat penalty for
                // every pair within reach makes an island of four halls one quarrel — every guild at war
                // with every other within the same three hours, which is not neighbours falling out, it is
                // a scheduled event. Scaled, the two halls eighteen tiles apart lose four times as much a
                // drift as the two seventy-six apart, and the shard produces a rivalry rather than a war.
                Borders++;
                Move(mine, theirs, Border * (1.0 - gap / (double)Neighbouring), "a shared border");
            }
        }
    }

    /// <summary>
    /// Whether these two guilds are at war, asked of the engine rather than of the number above.
    ///
    /// The opinion decides when to declare; the engine holds whether it is declared. Keeping a second copy
    /// of that here is how a bot ends up striking somebody the engine still calls an ally.
    /// </summary>
    public static bool AtWar(Guild a, Guild b) => a != null && b != null && a != b && a.IsWar(b);

    /// <summary>Whether these two bots' guilds are at war.</summary>
    public static bool AtWar(Mobile a, Mobile b) => AtWar(a?.Guild as Guild, b?.Guild as Guild);

    private static void Reckon(string of, string about, double was, double now, string why)
    {
        if (BaseGuild.FindByName(of) is not Guild mine || BaseGuild.FindByName(about) is not Guild theirs)
        {
            return;
        }

        var warring = mine.IsWar(theirs);

        if (!warring && now <= Enmity && was > Enmity)
        {
            if (!Warring)
            {
                Withheld++;

                return;
            }

            mine.AddEnemy(theirs);
            Declared++;

            logger.Warning(
                "{Mine} has declared war on {Theirs}: regard fell to {Now:F1} over {Why}",
                of,
                about,
                now,
                why
            );

            return;
        }

        if (warring && now >= Amity)
        {
            mine.RemoveEnemy(theirs);
            Ended++;

            logger.Information("{Mine} is at peace with {Theirs} again: regard back up to {Now:F1}", of, about, now);
        }
    }

    /// <summary>The worst opinion anybody holds, for the summary.</summary>
    private static string Worst()
    {
        string worst = null;
        var lowest = 0.0;

        foreach (var ((of, about), held) in _regard)
        {
            if (held < lowest)
            {
                lowest = held;
                worst = $"{of} of {about} at {held:F0}";
            }
        }

        return worst ?? "nobody thinks ill of anybody";
    }

    public static string Describe() =>
        !Running
            ? "guilds form no opinions of each other"
            : $"{_regard.Count} opinions between guilds, worst {Worst()}; moved by {Trespasses} pieces of work on somebody else's land, "
              + $"{Defiances} refusals to move along, {Bloodshed} killings, {Borders} border drifts and {Trades} trades; "
              + $"{Declared} wars declared, {Ended} ended"
              + (Warring ? "" : $", and {Withheld} withheld because war is switched off");

    public static void Forget()
    {
        _regard.Clear();
        _drifted = 0;
        _everDrifted = false;
        Trespasses = 0;
        Defiances = 0;
        Bloodshed = 0;
        Borders = 0;
        Trades = 0;
        Declared = 0;
        Ended = 0;
        Withheld = 0;
    }
}
