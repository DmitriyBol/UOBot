using System.Linq;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// The bot that is paid by the health of the market rather than by any errand in it.
///
/// <para>
/// <b>Its office is a hundredth of every sale, and that number is the whole of its motivation.</b> Every
/// other bot on this shard is paid for a piece of work: a hunt, a seam, a commission. The architect is paid
/// when <em>anybody</em> trades — see <see cref="BotClass.Levies"/> — so the only way it can earn more is for
/// the population as a whole to make more, sell more and be better equipped than it was yesterday. That is
/// not a rule it is told to follow; it is the shape of its income, and it is why this class exists rather
/// than a flag on a crafter.
/// </para>
///
/// <para>
/// <b>Born finished, like the captain, and for the same kind of reason.</b> See
/// <see cref="BotClass.Seasoned"/>: a class that sets it "had better have a reason that is not it would be
/// stronger". This one's is that it is the shard's answer to "nobody here is good enough to make that" — the
/// armourer's own refusal, counted by the hundred on the nights when the population wanted plate and had
/// nothing but leather. An architect still learning its trade is one more bot who cannot make the thing.
/// </para>
///
/// <para>
/// <b>Both trades, and both at Expert, because the two halves of its ambition are one chain.</b> Ore becomes
/// ingots becomes armour, and a bot that could dig but not forge would be handing the bottleneck to somebody
/// else and calling it a job. Tailoring and tinkering come with it a little lower: they are how the rest of
/// the population gets dressed while the forge is busy with mail.
/// </para>
/// </summary>
public sealed class BotArchitect : BotClass
{
    public override string Name => "Architect";

    public override BotRole Role => BotRole.Producer;

    public override SkillName? MainSkill => SkillName.Blacksmith;

    public override bool Seasoned => true;

    public override double Seasoning => 0.78;

    public override bool Levies => true;

    public override bool Rides => true;

    protected override void Defaults()
    {
        Str = 65;
        Dex = 20;
        Int = 15;

        Skills =
        [
            (SkillName.Mining, 100.0),
            (SkillName.Blacksmith, 100.0),
            (SkillName.Tailoring, 100.0),
            (SkillName.Tinkering, 100.0),
            (SkillName.Tactics, 100.0),
            (SkillName.Healing, 100.0)
        ];

        Kit = new BotKit
        {
            Melee = BotArsenal.Melee(100.0),

            Tools = [typeof(SmithHammer), typeof(Pickaxe), typeof(SewingKit), typeof(TinkerTools)],

            Bandages = 30
        };
    }
}
