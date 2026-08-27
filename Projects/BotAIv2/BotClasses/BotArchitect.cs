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

    /// <summary>The forge. Mining is the higher number and the smaller half of the trade.</summary>
    public override SkillName? MainSkill => SkillName.Blacksmith;

    /// <summary>Born holding both trades. See the class note for why this one is allowed to.</summary>
    public override bool Seasoned => true;

    /// <summary>A hundredth of every sale, out of the seller's share. The one class that takes one.</summary>
    public override bool Levies => true;

    /// <summary>
    /// Buys a horse, for the same reason the gatherer does: half its trade is at the far end of a walk.
    ///
    /// <para>
    /// <b>The riding flag went on the class that digs and was not put on the other one that digs.</b> This
    /// bot mines its own ore — see the note above on why both halves of the chain are one class — so the
    /// two hundred and forty tiles out to the cave are its walk as much as the gatherer's, and then it has
    /// to carry the ore back to a forge that is in town. It arguably needs the horse more: the gatherer
    /// banks at the counter it passes, while this one's day is a round trip by construction.
    /// </para>
    /// </summary>
    public override bool Rides => true;

    protected override void Defaults()
    {
        // A smith's build: it swings a hammer all day and carries ore up out of a hole, and it is not
        // expected to win a fight. The intelligence is there because tinkering wants it.
        Str = 65;
        Dex = 20;
        Int = 15;

        Skills =
        [
            (SkillName.Mining, 82.0),
            (SkillName.Blacksmith, 80.0),
            (SkillName.Tailoring, 72.0),
            (SkillName.Tinkering, 70.0),
            (SkillName.Tactics, 45.0),
            (SkillName.Healing, 45.0)
        ];

        Kit = new BotKit
        {
            // Carried because everything on this island does, and trained where a crafter's is: it goes to
            // the forge, not to the graveyard.
            Melee = BotArsenal.Melee(45.0),

            Tools = [typeof(SmithHammer), typeof(Pickaxe), typeof(SewingKit), typeof(TinkerTools)],

            Bandages = 30
        };
    }
}
