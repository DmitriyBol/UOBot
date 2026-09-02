namespace Server.BotAI.V2;

/// <summary>
/// Metal, cloth and leather. Fights only because everybody has to.
///
/// <para>
/// <b>Mining is its highest target and Blacksmithing is its trade, and both statements are needed.</b>
/// Smelting ore into ingots is a Mining check, so a smith with a low Mining burns the ore it dug and
/// never reaches the anvil at all — the first version measured a smith at Mining 26 turning two ore out
/// of a hundred into ingots, and its <c>things made</c> counter stayed at zero for a whole session. So
/// the opening skill points, which go to the three highest targets in order, have to land on Mining
/// first. But then a champion of this class came out a grandmaster <em>miner</em> holding a journeyman's
/// hammer, because rank read the largest target as the trade. That is why <see cref="MainSkill"/> is
/// stated outright here instead of inferred.
/// </para>
///
/// <para>
/// <b>The free craft is aimed at a measured bottleneck, not at the skill.</b> A pack holds about twelve
/// ore, which is twenty ingots, which is one or two helmets, and then it is back to the mine — so a
/// smith's output is limited by trips underground rather than by anything at the forge. Once an hour one
/// attempt yields two items and charges materials for one, which is worth roughly one trip saved.
/// </para>
/// </summary>
public sealed class BotCrafter : BotClass
{
    public override string Name => "Crafter";

    public override BotRole Role => BotRole.Producer;

    /// <summary>Stated, not inferred. See the remarks: Mining is higher and Blacksmithing is the trade.</summary>
    public override SkillName? MainSkill => SkillName.Blacksmith;

    protected override void Defaults()
    {
        Str = 50;
        Dex = 30;
        Int = 20;

        Skills =
        [
            (SkillName.Mining, 100.0),
            (SkillName.Blacksmith, 100.0),
            (SkillName.Tailoring, 100.0),
            (SkillName.Tinkering, 100.0),
            (SkillName.Tactics, 100.0),
            (SkillName.Healing, 100.0)
        ];

        FreeCraftIntervalMs = 3600000;

        Kit = new BotKit
        {
            // Trained low: it carries a blade because everything on this island does, not because it
            // intends to use one. Every point above this would come out of the trade.
            Melee = BotArsenal.Melee(100.0),

            // The hammer is the trade. Without one a smith is a bot with an opinion about metal — it
            // cannot forge, cannot take commissions, and quietly spends its life hitting skeletons like
            // everybody else, which is exactly what the first version's smiths all did.
            //
            // Leatherwork needs no third tool: in this era the sewing kit makes leather armour as well
            // as cloth, so "tailoring and leather" is one skill and one implement.
            Tools = [typeof(Server.Items.SmithHammer), typeof(Server.Items.Pickaxe), typeof(Server.Items.SewingKit)]
        };
    }
}
