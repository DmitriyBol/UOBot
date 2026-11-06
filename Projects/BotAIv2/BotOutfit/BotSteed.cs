using ModernUO.Serialization;
using Server.Mobiles;

namespace Server.BotAI.V2;

[SerializationGenerator(0, false)]
public partial class BotSteed : EtherealMount
{

    private const int Statuette = 0x20DD;

    private const int Mounted = 0x3EAA;

    public static int Price { get; set; } = 500;

    [Constructible]
    public BotSteed() : base(Statuette, Mounted) => Name = "a saddled horse";

    public override int EtherealHue => 0;
}
