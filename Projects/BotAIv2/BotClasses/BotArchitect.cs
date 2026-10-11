using System.Linq;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// The maker of both halves of the armour chain: the seam and the forge.
///
/// <para>
/// <b>Its office was a hundredth of every sale on the auction</b>, paid when <em>anybody</em> traded, so the only
/// way it could earn more was for the population as a whole to make more and sell more. The levy went with the
/// auction on 01.10.2026 (Patrick's order of 30.09: every sale hand to hand, nothing settled from nowhere); what is
/// left is a maker of both halves of the armour chain, paid for its forge and its seam like anybody else.
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

    public override bool Office => true;

    public override BotRole Role => BotRole.Producer;

    public override SkillName? MainSkill => SkillName.Blacksmith;

    public override bool Seasoned => true;

    public override double Seasoning => 0.78;

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
