using System;
using Server.Guilds;
using Server.Mobiles;

namespace Server.BotAI.V2;

public sealed class BotQuarrel : BotDeed
{

    public const string Trade = "quarrel";

    public static double Prior { get; set; } = 400.0;

    public static double DefendPrior { get; set; } = 700.0;

    public static long Defences { get; private set; }

    public static double WorkMinutes { get; set; } = 2.0;

    public static int Reach { get; set; } = 1;

    public static int FightMs { get; set; } = 120000;

    public static long Felled { get; private set; }

    public static long Fallen { get; private set; }

    public static long Broken { get; private set; }

    private readonly BotMobile _them;

    private readonly string _ours;

    public string Ours => _ours;

    private readonly bool _defending;

    private long _began;

    private bool _begun;

    private bool _struck;

    public BotQuarrel(BotMobile them, string ours) : this(them, ours, false)
    {
    }

    public BotQuarrel(BotMobile them, string ours, bool defending)
    {
        _them = them;
        _ours = ours;
        _defending = defending;

        if (defending)
        {
            Defences++;
        }
    }

    public override string Kind => Trade;

    public override bool Summons => true;

    public override Map Map => _them?.Map;

    public override Point3D Where => _them?.Location ?? Point3D.Zero;

    public override double Expects => _defending ? DefendPrior : Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override bool Unpaid => true;

    public override string Stage =>
        (_defending ? "defending our ground: " : "")
        + (_struck ? $"fighting {_them?.Name} of the enemy" : $"closing with {_them?.Name} of the enemy");

    public override bool Bend(IBotWilful bot) => false;

    public override bool Pressing(IBotWilful bot) => _defending;

    public override void Drop(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body != null && body.Combatant == _them)
        {
            body.Combatant = null;
        }
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _them is not { Deleted: false } || _them.Map == null || _them.Map == Map.Internal)
        {
            Broken++;

            return BotDoing.Failed("whoever it was is gone");
        }

        if (!_begun)
        {
            _begun = true;
            _began = Core.TickCount;
        }

        if (!_them.Alive)
        {
            Felled++;
            body.Combatant = null;

            BotFeud.Drop(body.Guild as Guild);

            return BotDoing.Done($"{_them.Name} of the enemy is down");
        }

        if (!body.Alive)
        {
            Fallen++;

            return BotDoing.Failed($"went down fighting {_them.Name}");
        }

        if (!BotFeud.Quarrel(body, _them))
        {
            Broken++;
            body.Combatant = null;

            return BotDoing.Failed($"no quarrel with {_them.Name} any more");
        }

        if (Core.TickCount - (_began + FightMs) >= 0)
        {
            Broken++;
            body.Combatant = null;

            return BotDoing.Failed($"{_them.Name} was not brought down inside {FightMs / 1000}s");
        }

        BotFeud.Call(body.Guild as Guild, _them);

        if (!body.InRange(_them.Location, Reach))
        {

            return BotDoing.Walk(_them.Map, _them, BotArrival.Within(Reach), $"at {_them.Name} of the enemy");
        }

        body.Combatant = _them;
        _struck = true;

        return BotDoing.Work($"fighting {_them.Name}");
    }

    public static string Describe() =>
        Felled + Fallen + Broken == 0
            ? "no blows have been struck between guilds"
            : $"{Felled} enemies brought down, {Fallen} members lost doing it, {Broken} quarrels broken off, {Defences} taken up in defence of the guild's own ground at {DefendPrior:F0}/min";

    public static void Forget()
    {
        Felled = 0;
        Fallen = 0;
        Broken = 0;
        Defences = 0;
    }
}
