using System;
using Server.BotAI.V2;
using Server.Items;
using Server.Mobiles;

namespace Server.BotAI.Mind;

/// <summary>
/// A real piece of the shard's work, taken up because a mind asked for it, and measured because a mind
/// predicted something about it.
///
/// <para>
/// <b>It forwards everything and invents nothing.</b> The work inside is the same object the shard's own
/// proposer would have handed out — the same digging, the same hunt, the same shopping trip — so a thinking
/// bot does exactly what the others do, by exactly the same code, and there is no second implementation of
/// anything to drift out of step. Two members are this wrapper's own and they are the only two that could
/// be: what it is <em>called</em>, and what it is <em>expected to be worth</em>.
/// </para>
///
/// <para>
/// <b>The name matters more than it looks.</b> Reported as <c>mind-hunt</c> rather than <c>hunt</c>, the
/// ledger files a thinking bot's hunts separately from the population's, so the two learn about the same
/// ground independently and neither one's experience is quietly averaged into the other's. It is still a
/// kind of work and not one instance of it, which is the rule the ledger key has to satisfy.
/// </para>
///
/// <para>
/// <b>And the takings are measured here rather than taken from the ledger.</b> What the mind needs to be
/// shown is the thing it predicted — gold a minute — measured over the same stretch of time by something
/// that is not the mind. Pack and bank together, because banking money is not spending it.
/// </para>
/// </summary>
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
