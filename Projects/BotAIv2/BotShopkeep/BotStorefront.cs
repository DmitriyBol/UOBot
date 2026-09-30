using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>
/// One bot's shop while it is open: who keeps it, where it stands, what it holds and at what price, and what it has taken.
///
/// <para>
/// <b>A snapshot, not the goods.</b> Unlike a stall on <c>BotAuction</c>, nothing here is lifted out of the world: the
/// goods stay in the keeper's own pack and bank box, where the engine keeps them and a save keeps them, and they move to a
/// customer only at the till (<see cref="BotShopkeep.Serve"/>). What this holds is the keeper's last count of them, taken
/// every <see cref="BotShopkeep.StockMs"/> and cut down at every sale, so that a customer deciding whether the walk is
/// worth it reads a dictionary rather than two containers.
/// </para>
/// </summary>
public sealed class BotStorefront
{
    private readonly Dictionary<Type, int> _units = [];

    private readonly Dictionary<Type, int> _asks = [];

    private readonly Dictionary<Type, int> _sold = [];

    private readonly HashSet<Serial> _customers = [];

    public BotStorefront(BotMobile keeper, BotDeed deed, Map map, Point3D stand, Point3D bank, string town)
    {
        Keeper = keeper;
        Deed = deed;
        Map = map;
        Location = stand;
        Bank = bank;
        Town = town ?? "the country";
        OpenedTick = Core.TickCount;
    }

    public BotMobile Keeper { get; }

    public BotDeed Deed { get; }

    public Map Map { get; }

    public Point3D Location { get; }

    public Point3D Bank { get; }

    public string Town { get; }

    public long OpenedTick { get; }

    public bool Shut { get; internal set; }

    public long StockedTick { get; internal set; }

    public int Units { get; private set; }

    public int Worth { get; private set; }

    public int Demanded { get; private set; }

    public int Shown { get; internal set; }

    public int Kinds => _units.Count;

    public int Sold { get; private set; }

    public int Sales { get; private set; }

    public int Takings { get; private set; }

    public int Customers => _customers.Count;

    public int Held(Type kind) => kind != null && _units.TryGetValue(kind, out var units) ? units : 0;

    public int Ask(Type kind) => kind != null && _asks.TryGetValue(kind, out var price) ? price : 0;

    public bool SoldAny(Type kind) => kind != null && _sold.ContainsKey(kind);

    public IEnumerable<Type> Goods => _units.Keys;

    internal void Restock(Dictionary<Type, int> units, Dictionary<Type, int> asks, int demanded)
    {
        _units.Clear();
        _asks.Clear();

        var total = 0;
        var worth = 0;

        foreach (var (kind, count) in units)
        {
            if (count <= 0 || !asks.TryGetValue(kind, out var price) || price <= 0)
            {
                continue;
            }

            _units[kind] = count;
            _asks[kind] = price;
            total += count;
            worth += count * price;
        }

        Units = total;
        Worth = worth;
        Demanded = demanded;
        StockedTick = Core.TickCount;
    }

    internal void Took(Mobile buyer, Type kind, int units, int gold)
    {
        if (units <= 0 || kind == null)
        {
            return;
        }

        if (_units.TryGetValue(kind, out var held))
        {
            var left = held - units;

            if (left > 0)
            {
                _units[kind] = left;
            }
            else
            {
                _units.Remove(kind);
            }

            var price = _asks.TryGetValue(kind, out var ask) ? ask : 0;

            Units = Math.Max(0, Units - units);
            Worth = Math.Max(0, Worth - units * price);
        }

        _sold[kind] = (_sold.TryGetValue(kind, out var before) ? before : 0) + units;

        Sold += units;
        Sales++;
        Takings += gold;

        if (buyer != null)
        {
            _customers.Add(buyer.Serial);
        }
    }

    internal void Priced(Type kind, int price)
    {
        if (kind != null && price > 0 && _asks.ContainsKey(kind))
        {
            _asks[kind] = price;
        }
    }

    public List<(Type Kind, int Units, int Ask)> Ranked()
    {
        List<(Type Kind, int Units, int Ask)> ranked = [];

        foreach (var (kind, units) in _units)
        {
            ranked.Add((kind, units, _asks.TryGetValue(kind, out var ask) ? ask : 0));
        }

        ranked.Sort(static (a, b) => (b.Units * b.Ask).CompareTo(a.Units * a.Ask));

        return ranked;
    }

    public override string ToString() => $"{Keeper?.Name}'s shop in {Town} at ({Location.X}, {Location.Y}): {Units} things worth {Worth}gp";
}
