using System;
using Server.Guilds;
using Server.Multis;

namespace Server.BotAI.V2;

/// <summary>
/// The sizes a guild's hall comes in, smallest first: what each is, how many members it holds, and what it costs.
///
/// <para>
/// <b>Patrick's order of 26.09.2026: "different houses for a guild, expansions".</b> Asked what that takes in, he chose all
/// three: the hall grows with the guild (a small house, then bigger), outposts on its far land, and the +10 places tied to
/// the size of the house — "a small hall will not hold more than 15". So a hall has a size, the size has room for so many
/// members (<see cref="Holds(Guild)"/> is a ceiling on <c>BotGuilds.Ceiling</c>), and a guild that has filled its hall moves
/// into the next size up (<see cref="BotEnlarger"/>).
/// </para>
///
/// <para>
/// <b>Only houses of one storey with a door on the ground,</b> because the hall's room — where its chest, benches and
/// counter stand — is found by flooding the ground floor from the middle (<c>BotFittings.Room</c>), and towers, villas and
/// cabins would put the chest on a roof. The engine's classic houses that qualify, by footprint: the small house (7×8),
/// the sandstone patio house (12×9), the large patio house (15×15) and the large marble house (15×15). The prices are the
/// shard's own, not the engine's deed prices (43,800 to 192,000), for the reason the first hall's price was: a guild's
/// purse is thousands, not hundreds of thousands.
/// </para>
/// </summary>
public sealed class BotHallKind
{
    private BotHallKind(int size, string name, Type type, int multiID)
    {
        Size = size;
        Name = name;
        Type = type;
        MultiID = multiID;
    }

    public int Size { get; }

    public string Name { get; }

    public Type Type { get; }

    public int MultiID { get; }

    public int Price =>
        Size switch
        {
            1 => BotEstate.Price,
            2 => PatioPrice,
            3 => LargePatioPrice,
            _ => MarblePrice
        };

    public static int PatioPrice { get; set; } = 15000;

    public static int LargePatioPrice { get; set; } = 35000;

    public static int MarblePrice { get; set; } = 60000;

    public static int PatioHolds { get; set; } = 25;

    public static int LargePatioHolds { get; set; } = 35;

    public static int MarbleHolds { get; set; } = 45;

    private static readonly BotHallKind[] _kinds =
    [
        new(1, "a small house", typeof(SmallOldHouse), 0x0064),
        new(2, "a sandstone patio house", typeof(SandStonePatio), 0x009C),
        new(3, "a large patio house", typeof(LargePatioHouse), 0x008C),
        new(4, "a large marble house", typeof(LargeMarbleHouse), 0x0096)
    ];

    public static BotHallKind First => _kinds[0];

    public static BotHallKind Sized(int size) => _kinds[Math.Clamp(size, 1, _kinds.Length) - 1];

    public BotHallKind Next => Size < _kinds.Length ? _kinds[Size] : null;

    public int Room =>
        Size switch
        {
            1 => BotGuilds.Most,
            2 => PatioHolds,
            3 => LargePatioHolds,
            _ => MarbleHolds
        };

    public static BotHallKind Of(BaseHouse house)
    {
        if (house == null)
        {
            return First;
        }

        var type = house.GetType();

        for (var i = 0; i < _kinds.Length; i++)
        {
            if (_kinds[i].Type == type)
            {
                return _kinds[i];
            }
        }

        return First;
    }

    public static int Holds(Guild guild) => Of(guild == null ? null : BotEstate.Hall(guild)).Room;

    public int Multi => Size == 1 ? BotPlot.MultiID : MultiID;

    public BaseHouse Make(Mobile owner) =>
        Size == 1 ? new SmallOldHouse(owner, Multi) : Activator.CreateInstance(Type, owner) as BaseHouse;

    public override string ToString() => Name;
}
