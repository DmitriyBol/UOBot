using ModernUO.Serialization;
using Server.Items;

namespace Server.BotAI.V2;

[SerializationGenerator(0, false)]
public partial class BotGuildDoor : BaseDoor
{

    [SerializableField(0)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private string _guild;

    [Constructible]
    public BotGuildDoor(DoorFacing facing, string guild) : base(
        0x6A5 + 2 * (int)facing,
        0x6A6 + 2 * (int)facing,
        0xEA,
        0xF1,
        GetOffset(facing)
    ) => _guild = guild;

    public override void Use(Mobile from)
    {
        if (from != null && !Open && !BotGuildHouses.MayOpen(this, from))
        {

            from.LocalOverheadMessage(MessageType.Regular, 0x3B2, 502503);
            BotGuildHouses.Refused(from);

            return;
        }

        base.Use(from);
    }
}
