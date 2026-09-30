using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// <b>A hurt bot a healer is walking to stops and waits for it (29.09.2026, build 321; Patrick's order of the morning: "full
/// mutual aid").</b>
///
/// <para>
/// A mending of another was the one errand whose goal walked away from it. Of the fifteen mendings on build 318, five ended
/// "could not get nearer to Nerys at (1439, 1365)", "to Keyleth", "to Norna": the patient was about its own work, walking
/// at the healer's own pace, and the walk that follows a moving body gives up after so many plans without getting nearer.
/// The healer was right to go and the patient was right to walk; nobody told the patient. Now the healer asks, once per
/// mending: a patient that is free — out of any fight, holding nothing it may not put down — is pressed to wait where it
/// stands, its work put down to be taken up again, and ends the wait when it is whole, when the healer has gone elsewhere,
/// when something comes at it, or after <see cref="MostMs"/>.
/// </para>
/// </summary>
public sealed class BotAwaitMend : BotDeed
{
    public const string Trade = "await";

    public static int MostMs { get; set; } = 60000;

    public static bool Running { get; set; } = true;

    public static int Nearer { get; set; } = 4;

    public static long Asked { get; private set; }

    public static long Pressed { get; private set; }

    public static long Whole { get; private set; }

    public static long Forsaken { get; private set; }

    public static long Fought { get; private set; }

    public static long Outwaited { get; private set; }

    private readonly Mobile _healer;

    private readonly Map _map;

    private readonly Point3D _where;

    private long _began;

    private bool _stopped;

    private BotAwaitMend(Mobile healer, Mobile patient)
    {
        _healer = healer;
        _map = patient.Map;
        _where = patient.Location;
        _began = Core.TickCount;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => 1.0;

    public override double Minutes => MostMs / 60000.0;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override int Made => 0;

    public override bool Unpaid => true;

    public override bool Committed => true;

    public override bool Still => true;

    public override string Stage => $"waiting for {_healer?.Name} to mend it";

    public static bool Ask(Mobile healer, Mobile patient)
    {
        if (!Running || healer == null || patient is not BotMobile bot || patient == healer)
        {
            return false;
        }

        Asked++;

        if (bot is not { Deleted: false, Alive: true, Hidden: false } || bot.Map != healer.Map || bot.InRange(healer.Location, Nearer))
        {
            return false;
        }

        if (bot.Combatant != null || bot.Squad != null || BotMend.Embattled(bot))
        {
            return false;
        }

        var resolve = bot.Resolve;

        if (resolve == null || resolve.Paused != null)
        {
            return false;
        }

        if (resolve.Deed is { } deed
            && (deed is BotAwaitMend || deed is BotSalve || !deed.Resumes || deed.Committed || deed.Foe != null || deed.Braves))
        {
            return false;
        }

        if (!BotWill.Press(bot, new BotAwaitMend(healer, bot), $"{healer.Name} is coming to mend it"))
        {
            return false;
        }

        Pressed++;

        return true;
    }

    public override void Taken(IBotWilful bot) => _began = Core.TickCount;

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null)
        {
            return BotDoing.Failed("no body");
        }

        if (BotMend.Whole(body))
        {
            Whole++;

            return BotDoing.Done($"mended by {_healer?.Name}");
        }

        if (body.Combatant != null || BotMend.Embattled(body))
        {
            Fought++;

            return BotDoing.Done("something came at it while it waited");
        }

        if (_healer is not { Deleted: false, Alive: true } || _healer.Map != body.Map
            || (_healer as IBotWilful)?.Resolve?.Deed is not BotSalve)
        {
            Forsaken++;

            return BotDoing.Done($"{_healer?.Name} went elsewhere");
        }

        if (Core.TickCount - _began >= MostMs)
        {
            Outwaited++;

            return BotDoing.Done($"waited {MostMs / 1000}s for {_healer.Name}");
        }

        if (!_stopped)
        {
            _stopped = true;
            (body as BotMobile)?.Journey?.Finish();
        }

        return BotDoing.Work($"waiting for {_healer.Name}");
    }

    public static string Describe() =>
        $"{Pressed} patients of {Asked} asked stood still for their healer ({Whole} whole, {Forsaken} left when the healer went elsewhere, {Fought} broke off for a fight, {Outwaited} gave up after {MostMs / 1000}s)";
}
