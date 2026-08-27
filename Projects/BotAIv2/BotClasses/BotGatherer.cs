namespace Server.BotAI.V2;

/// <summary>
/// Ore and timber, and the only bot that can find a reagent in the grass.
///
/// <para>
/// Its pickaxe and hatchet weigh nothing, like every granted tool, and for this class that is not a
/// convenience — it is the difference between working and standing still. A gatherer's whole job is to
/// fill a pack, and the engine starts charging stamina for every step once the load crosses a
/// threshold, then refuses the step outright at zero. The first version lost three bots to exactly this
/// for an entire session: they dug past the limit and spent three hours unable to move, while the log
/// insisted six hundred times over that the ground was clear and the step was allowed. It was. Stamina
/// was not in the message. Tools that weigh are ore that cannot be carried.
/// </para>
///
/// <para>
/// <b>The forage is the point of this class existing.</b> Reagents are handed out once, at birth, and
/// nothing replaces them: a caster that dies without recovering its corpse is out of the trade, and its
/// only recourse is to post on the board and pay whoever brings more. Until now nobody could bring any
/// — the only reagents in the world were the ones the world started with. This is the tap. A handful of
/// one kind every quarter of an hour, which is deliberately less than the fifteen a caster orders at a
/// time: the gatherer becomes a supplier rather than a one-off answer, and the shortage stays worth
/// paying to fix.
/// </para>
/// </summary>
public sealed class BotGatherer : BotClass
{
    public override string Name => "Gatherer";

    public override BotRole Role => BotRole.Producer;

    public override SkillName? MainSkill => SkillName.Mining;

    /// <summary>
    /// Buys a horse, and is the first class to. Its day is the walk; see <see cref="BotClass.Rides"/>.
    /// </summary>
    public override bool Rides => true;

    protected override void Defaults()
    {
        Str = 55;
        Dex = 30;
        Int = 15;

        Skills =
        [
            (SkillName.Mining, 80.0),
            (SkillName.Lumberjacking, 75.0),
            (SkillName.Tactics, 40.0),
            (SkillName.Healing, 40.0)
        ];

        ForageIntervalMs = 900000;
        ForageYieldMin = 3;
        ForageYieldMax = 6;

        Kit = new BotKit
        {
            Melee = BotArsenal.Melee(40.0),
            Tools = [typeof(Server.Items.Pickaxe), typeof(Server.Items.Hatchet)]
        };
    }
}
