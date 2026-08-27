using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>
/// Everything one bot has resolved, feels and has learned. Lives on the bot, like its journey and its bond.
///
/// <para>
/// <b>All of it in one object, and that is the point.</b> In the first version a bot's state was thirty-two
/// dictionaries keyed by serial in thirty-two files: each needed its own reset, each leaked when the
/// population was torn down, and the question "what is this bot up to" was answered by reading a file per
/// possible answer. A deleted bot takes this with it, and there is nowhere else to look.
/// </para>
///
/// <para>
/// Only <see cref="BotWill"/> writes to it. Everything here is readable by a gump, a command or a log line
/// without asking permission, which is the other half of the same lesson: the first version's decisions
/// were unobservable, so the question "why did the brain not take that plan" had no answer at all — and it
/// turned out to be the question that mattered, because it had not taken 85 of 135 of them.
/// </para>
/// </summary>
public sealed class BotResolve
{
    /// <summary>Boredom and need. See <see cref="BotUrges"/>.</summary>
    public BotUrges Urges { get; } = new();

    /// <summary>What this bot has learned about what pays where. See <see cref="BotLedger"/>.</summary>
    public BotLedger Ledger { get; } = new();

    /// <summary>What it is doing, or null when it has nothing on.</summary>
    public BotDeed Deed { get; internal set; }

    /// <summary>Where it stood when it took that on, so the takings can be worked out when it ends.</summary>
    public BotStake Stake { get; internal set; }

    /// <summary>
    /// The last walk passed on to the journey. Kept so that an undertaking repeating "walk to the forge"
    /// every beat does not buy a fresh path search every beat.
    /// </summary>
    public BotDoing Sent { get; internal set; }

    /// <summary>When the current undertaking was taken on.</summary>
    public long SinceTick { get; internal set; }

    /// <summary>When the auction last ran. What keeps a decision from being remade every tick.</summary>
    public long ReviewedTick { get; internal set; }

    /// <summary>
    /// When the undertaking in hand last answered anything other than "working".
    ///
    /// <para>
    /// <b>The one answer nothing judges, and therefore the one an undertaking can hide behind for ever.</b>
    /// Walking is watched by the journey, finishing and failing end the work, but <c>Work</c> means "I am
    /// standing here on purpose" and is deliberately never questioned — a bot at a forge or a vein has no
    /// business being hurried. On 25.08.2026 that let three tailors stand at their benches for two hours on
    /// a craft lock the engine never released, and Merrick hold a rescue for fifty-seven minutes without
    /// dying, failing or moving. Every summary counted them as working.
    /// </para>
    /// </summary>
    public long StirredTick { get; internal set; }

    /// <summary>
    /// Whether the auction is due whatever the clock says. True for a bot that has never decided anything
    /// and for one that has just finished a job — there is nothing to change its mind about, so it should
    /// not stand about waiting for a review.
    ///
    /// <para>
    /// A flag rather than a zeroed stamp, and that is this shard's rule: on some hosts the tick count is the
    /// physical machine's uptime counter passed straight through, so it starts enormous, can wrap negative,
    /// and zero is a legitimate reading rather than a way of saying "never". See
    /// <c>dev-docs/tick-counts.md</c> in the fork.
    /// </para>
    /// </summary>
    public bool Due { get; internal set; } = true;

    /// <summary>Whether the undertaking in hand is currently set aside by something above it on the ladder.</summary>
    public bool Aside { get; internal set; }

    /// <summary>When it was set aside. Only meaningful while <see cref="Aside"/> is true.</summary>
    public long AsideTick { get; internal set; }

    /// <summary>Whether anything has ever hit this bot. The companion flag to <see cref="HurtTick"/>.</summary>
    public bool Struck { get; internal set; }

    /// <summary>
    /// When something last hit this bot. Set from <c>OnDamage</c> through <see cref="BotWill.Hurt"/>.
    ///
    /// <b>Told, not observed.</b> A caster strikes from eight tiles and never closes, so every test of the
    /// form "is something next to me" is a test that never fires — which is how six bots in the first
    /// version stood in a ring while a lich killed them one at a time.
    /// </summary>
    public long HurtTick { get; internal set; }

    /// <summary>Which rung it was on when it last decided anything.</summary>
    public BotStanding Standing { get; internal set; }

    /// <summary>
    /// Which trades actually had work in them for this bot at the last free review, by proposer name.
    ///
    /// <para>
    /// <b>The auction already knows this and used to throw it away.</b> Every review asks every proposer on
    /// the rung whether it has anything, and all but one of the answers are dropped on the floor — so
    /// "which trades were open to this bot a moment ago" was a fact the shard computed sixteen times a
    /// minute and could not answer. Anything that needs it had to ask the proposers a second time, and asking
    /// twice is not free: every proposer counts its own refusals by reason — <c>Sound</c>, <c>Broke</c>,
    /// <c>NoMetal</c>, <c>NoForge</c> — and that tally is how this whole shard is read. A speculative round
    /// of questions would inflate all of it silently, so "the smith refused four hundred times tonight"
    /// would stop meaning what it says. Recording what was offered costs a name per offer and makes the
    /// second round unnecessary.
    /// </para>
    ///
    /// <para>
    /// Free rung only, so it goes stale while a bot is in trouble — a fact about the last moment the bot was
    /// free to choose, which is exactly what it is wanted for.
    /// </para>
    /// </summary>
    public List<string> Offered { get; } = [];

    /// <summary>When <see cref="Offered"/> was last written, so its age can be judged.</summary>
    public long OfferedTick { get; internal set; }

    /// <summary>
    /// How rough a life this bot has been having lately, as blows landed on it per minute.
    ///
    /// <para>
    /// <b>Measured, because the class does not know.</b> A "warrior" that has spent the afternoon at a
    /// market stall is not in danger and a gatherer that keeps digging next to a graveyard is, and no list of
    /// roles will ever say so. This is the one fact that decides whether armour is worth buying at all: a
    /// piece is not bought by the point, it is bought against being hurt, and a bot nothing has touched in an
    /// hour is being asked to spend its earnings on a hypothetical.
    /// </para>
    ///
    /// <para>
    /// A rate, halving on its own clock, for the same reason the peril map is: a tally that only rises says
    /// what a bot's morning was like for the rest of the day.
    /// </para>
    /// </summary>
    public static int BruiseHalfLifeMs { get; set; } = 1800000;

    private double _blows;

    private long _blowTick;

    private bool _bruised;

    /// <summary>Something hit this bot. Called from the same place the peril map is told.</summary>
    public void Bruise(long now)
    {
        _blows = Faded(now) + 1.0;
        _blowTick = now;
        _bruised = true;
    }

    /// <summary>Blows a minute landing on this bot lately, or nought for one nothing has touched.</summary>
    public double Beaten(long now) =>
        !_bruised ? 0.0 : Faded(now) / (BruiseHalfLifeMs / 60000.0);

    private double Faded(long now)
    {
        var since = now - _blowTick;

        return since <= 0 || _blows <= 0.0 ? _blows : _blows * Math.Pow(0.5, since / (double)BruiseHalfLifeMs);
    }

    /// <summary>
    /// Which rung handed out the undertaking in hand.
    ///
    /// <para>
    /// <b>Not the same as <see cref="Standing"/>, and the difference is what tells work apart from work set
    /// aside.</b> A bandage offered by <c>Failing</c> and a dig offered by <c>Free</c> are both "the deed",
    /// and while the bot is failing only one of them is anything but a distraction. Without this the ladder
    /// could offer a bot its own rescue and then decline to carry it out, on the grounds that a bot in
    /// trouble is not Busy.
    /// </para>
    /// </summary>
    public BotStanding Took { get; internal set; } = BotStanding.Free;

    /// <summary>Why it is doing what it is doing, in words and in numbers. For the log and for a gump.</summary>
    public string Because { get; internal set; }

    public override string ToString() =>
        Deed == null
            ? $"nothing on, {Urges}"
            : $"{Deed}, {Urges}, because {Because}";
}
