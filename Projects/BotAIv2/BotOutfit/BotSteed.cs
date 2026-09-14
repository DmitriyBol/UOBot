using ModernUO.Serialization;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// A horse a bot carries in its pack and calls up when it has somewhere to be.
///
/// <para>
/// <b>Nothing here implements a mount, and that is the point.</b> The engine already has exactly the
/// arrangement that was asked for and has had it since the veteran rewards were written: an
/// <c>EtherealMount</c> is an ordinary item that lives in a backpack, calling it up is a two-second spell on
/// this era — <c>Core.AOS ? 3.0 : 2.0</c>, and this shard is Renaissance — and that spell is disturbed by
/// being hurt like any other. So a bot attacked mid-summon simply does not get its horse and fights on foot,
/// with no new rule written anywhere. A second mount system beside the engine's would have been the first
/// version's whole method: a mechanism nobody can compare against the one that already works.
/// </para>
///
/// <para>
/// <b>What this type adds is one fact — whose it is.</b> A steed is bought rather than awarded, so it must
/// not read as a veteran token to anything that inspects it; and it is a bot's own gear, so death must not
/// take it. A miner that loses a five-hundred-gold horse to one bad fight is a miner that never buys
/// another. Both are facts about <em>this</em> horse rather than about mounts, which is why they live in a
/// type of their own.
/// </para>
///
/// <para>
/// A separate class on purpose, by order, so that any class the shard later decides should ride can be given
/// one without anything about it being about mining. See <see cref="BotClass.Rides"/>.
/// </para>
/// </summary>
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
