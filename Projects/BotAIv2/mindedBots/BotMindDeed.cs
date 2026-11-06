using System;
using Server.BotAI.V2;
using Server.Items;
using Server.Mobiles;

namespace Server.BotAI.Mind;

public sealed class BotMindDeed : BotDeed
{
    private readonly BotDeed _work;

    private readonly BotMind _mind;

    private readonly double _bids;

    private readonly double _foretells;

    private readonly string _trade;

    private readonly BotMindChoice _choice;

    private long _began;

    private int _opened;

    private string _ending = "dropped";

    private bool _settled;

    private bool _claimed;

    public BotMindDeed(BotMind mind, BotDeed work, BotMindChoice choice, Mobile body)
    {
        _mind = mind;
        _work = work;
        _choice = choice;

        _began = Core.TickCount;
        _opened = Worth(body);

        _bids = work.Expects * Insistence;

        _foretells = Math.Max(0.0, choice?.Expect ?? work.Expects);

        _trade = choice?.Intent ?? work.Kind;
    }

    public static double Insistence { get; set; } = 1.25;

    public BotDeed Work => _work;

    public override string Kind => $"mind-{_work.Kind}";

    public override Map Map => _work.Map;

    public override Point3D Where => _work.Where;

    public override double Expects => _bids;

    public override double Minutes => _work.Minutes;

    public override SkillName? Trains => _work.Trains;

    public override int Outlay => _work.Outlay;

    public override bool AtCounter => _work.AtCounter;

    public override bool Braves => _work.Braves;

    public override double Coin => _work.Coin;

    public override int Made => _work.Made;

    public override bool Alongside => _work.Alongside;

    public override bool Hurries => _work.Hurries;

    public override bool Standing => _work.Standing;

    public override bool Committed => _work.Committed;

    public override string Stage => _work.Stage;

    public override bool Pressing(IBotWilful bot) => _work.Pressing(bot);

    public override bool Bend(IBotWilful bot) => _work.Bend(bot);

    private void Claim(IBotWilful bot)
    {
        if (_claimed)
        {
            return;
        }

        _claimed = true;
        _began = Core.TickCount;
        _opened = Worth(bot?.Self);

        _mind.Began(_choice);
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        Claim(bot);

        var doing = _work.Advance(bot);

        _ending = doing.Kind switch
        {
            BotDoingKind.Done => "finished",
            BotDoingKind.Failed => "failed",
            _ => _ending
        };

        return doing;
    }

    public override void Drop(IBotWilful bot)
    {

        Claim(bot);

        _work.Drop(bot);

        if (_settled)
        {
            return;
        }

        _settled = true;

        var minutes = Math.Max(0.01, (Core.TickCount - _began) / 60000.0);
        var gained = Worth(bot?.Self) - _opened;

        _mind.Settle(_trade, _foretells, gained, minutes, _ending);
    }

    private static int Worth(Mobile body)
    {
        if (body == null)
        {
            return 0;
        }

        var carried = body.Backpack?.TotalGold ?? 0;

        return carried + Banker.GetBalance(body);
    }
}
