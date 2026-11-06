using ModernUO.Serialization;
using Server.Items;

namespace Server.BotAI.V2;

[SerializationGenerator(0, false)]
public partial class BotCasterStaff : QuarterStaff
{

    private const int CasterStrRequirement = 10;

    [Constructible]
    public BotCasterStaff()
    {
        Name = "a caster's staff";
        StrRequirement = CasterStrRequirement;
    }
}
