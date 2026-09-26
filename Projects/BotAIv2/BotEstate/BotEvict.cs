using System;
using Server.Guilds;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Walking over to somebody working your guild's land and telling them to move along.
///
/// <para>
/// <b>What makes the land real, and it is deliberately almost nothing.</b> The errand is a walk, a sentence
/// and a look at whether they went. No blow is struck here at any standing — striking is what a war is for,
/// and a war is declared by <see cref="BotRegard"/> out of what these errands find, never inside one of
/// them. Patrick's plan says eviction is rare and the measure that says it has gone wrong is the completion
/// band falling while evictions rise; an errand that could kill would make that measure impossible to read.
/// </para>
///
/// <para>
/// <b>The refusal is the point rather than the going.</b> A bot told to move along does not obey — nothing
/// on this shard obeys anybody — so what actually happens is that the guild learns something: they were
/// asked, and they are still there. That is what moves the opinion, and it is why the errand waits a few
/// seconds after speaking before looking again. Somebody who has wandered off in that time is not defying
/// anybody, and counting them as though they had would be a war built out of ordinary walking.
/// </para>
/// </summary>
public sealed class BotEvict : BotDeed
{
    public const string Trade = "evict";

    public static double Prior { get; set; } = 380.0;

    public static double WorkMinutes { get; set; } = 1.0;

    public static int Reach { get; set; } = 3;

    public static int GraceMs { get; set; } = 8000;

    public static int ChaseMs { get; set; } = 90000;

    public static long Moved { get; private set; }

    public static long Stayed { get; private set; }

    public static long Missed { get; private set; }

    private readonly BotMobile _them;

    private readonly string _ours;

    private long _spoke;

    private bool _said;

    private long _began;

    private bool _begun;

    public BotEvict(BotMobile them, string ours)
    {
        _them = them;
        _ours = ours;
    }

    public override string Kind => Trade;

    public override bool Summons => true;

    public override Map Map => _them?.Map;

    public override Point3D Where => _them?.Location ?? Point3D.Zero;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override bool Unpaid => true;

    public override string Stage =>
        _said ? $"waiting to see whether {_them?.Name} moves" : $"to {_them?.Name}, who is working our land";

    public override bool Bend(IBotWilful bot)
    {
        if (_said || _them is not { Deleted: false } || !BotLand.Trespassing(_them, out var whose) || whose != _ours)
        {
            return true;
        }

        BotBailiff.Unreached(bot?.Self, _them);

        return false;
    }

    public override void Drop(IBotWilful bot) => BotBailiff.Release(_them);

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _them is not { Deleted: false } || _them.Map == null || _them.Map == Map.Internal)
        {
            Missed++;

            return BotDoing.Failed("whoever it was is gone");
        }

        BotBailiff.Hold(_them);

        if (!_begun)
        {
            _begun = true;
            _began = Core.TickCount;
        }

        if (!BotLand.Trespassing(_them, out var whose) || whose != _ours)
        {
            if (!_said)
            {
                Missed++;

                return BotDoing.Done($"{_them.Name} had already left our land");
            }

            Moved++;

            return BotDoing.Done($"{_them.Name} moved off the land of {_ours}");
        }

        if (!_said)
        {
            if (Core.TickCount - (_began + ChaseMs) >= 0)
            {
                Missed++;

                return BotDoing.Failed($"never got within speaking distance of {_them.Name}");
            }

            if (!body.InRange(_them.Location, Reach))
            {
                return BotDoing.Walk(_them.Map, _them, BotArrival.Within(Reach), $"over to {_them.Name}");
            }

            body.Say($"This is the land of {_ours}. Move along.");
            _said = true;
            _spoke = Core.TickCount;

            return BotDoing.Work($"told {_them.Name} to move along");
        }

        if (Core.TickCount - (_spoke + GraceMs) < 0)
        {
            return BotDoing.Work("waiting to see whether they go");
        }

        Stayed++;
        BotRegard.Defied(_them.Guild?.Name, _ours);
        BotBailiff.Told(_them);

        return BotDoing.Done($"{_them.Name} was told to leave the land of {_ours} and stayed");
    }

    public static string Describe() =>
        Moved + Stayed + Missed == 0
            ? "nobody has been moved along"
            : $"{Moved} moved along when told, {Stayed} stayed and were minded, {Missed} were gone before anybody arrived";

    public static void Forget()
    {
        Moved = 0;
        Stayed = 0;
        Missed = 0;
    }
}
