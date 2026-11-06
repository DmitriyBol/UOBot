using System.Collections.Generic;
using ModernUO.Serialization;

namespace Server.BotAI.V2;

[SerializationGenerator(0)]
public partial class BotRelicItem : Item
{

    [SerializableField(0)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private string _key;

    public static readonly List<BotRelicItem> Loaded = [];

    [Constructible]
    public BotRelicItem(string key, int itemId) : base(itemId)
    {
        _key = key;
        Movable = false;
        LootType = LootType.Blessed;
        Weight = 1.0;
    }

    public override bool Decays => false;

    [AfterDeserialization(false)]
    private void AfterDeserialization()
    {
        Loaded.Add(this);
    }
}
