using ModernUO.Serialization;
using Server.Items;

namespace Server.BotAI.V2;

[SerializationGenerator(0, false)]
public partial class BotGuildSign : Sign
{

    [SerializableField(0)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private string _guild;

    [Constructible]
    public BotGuildSign(SignType type, SignFacing facing, string guild) : base(type, facing) => _guild = guild;
}
