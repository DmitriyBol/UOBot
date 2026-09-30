using Server.Items;
using Server.Logging;
using Server.Spells;
using Server.Spells.Fourth;

namespace Server.BotAI.V2;

/// <summary>
/// Recall instead of a long walk: the shortcut a far walk asks for before it walks.
///
/// <para>
/// <b>Not a deed, and that is on purpose.</b> The walks that go far — a journey to a town (<see cref="BotTravel"/>), the walk
/// home (<see cref="BotHomeward"/>), a restock at a counter (<see cref="BotRestock"/>) — are already the right work with the
/// right price and the right ending; what changes is only how the bot gets there. So each holds one of these and asks it
/// first (<see cref="Instead"/>): when the bot is a mage holding a rune near where it is going, and the engine would let it
/// cast Recall, the answer is "stand still and cast" for a beat or two; when the recall has carried it, or cannot, the answer
/// is null and the walk goes on as it always did, from wherever the bot now stands.
/// </para>
///
/// <para>
/// <b>A failed cast walks.</b> A fizzle is tried again after <see cref="BotRunes.RetryMs"/>, up to
/// <see cref="BotRunes.Tries"/>; then the walk takes over and the recall is not asked again for this walk. A bot that could not
/// cast at the start — short of mana, say — is asked again every <see cref="ReconsiderMs"/> while it is still far.
/// </para>
/// </summary>
public sealed class BotRecall
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRecall));

    public static int ReconsiderMs { get; set; } = 30000;

    private readonly Map _map;

    private readonly Point3D _where;

    private readonly int _arrival;

    private readonly string _purpose;

    private RunebookEntry _entry;

    private Spell _spell;

    private int _tries;

    private int _from;

    private long _nextTick;

    private bool _over;

    private bool _asked;

    private bool _held;

    private bool _retrying;

    public bool Moved { get; private set; }

    private BotRecall(Map map, Point3D where, int arrival, string purpose)
    {
        _map = map;
        _where = where;
        _arrival = arrival;
        _purpose = purpose;
        _nextTick = Core.TickCount;
    }

    public static BotDoing? Instead(IBotWilful bot, Map map, Point3D where, int arrival, string purpose, ref BotRecall recall)
    {
        if (!BotRunes.Running || map == null)
        {
            return null;
        }

        recall ??= new BotRecall(map, where, arrival, purpose);

        return recall.Step(bot);
    }

    private BotDoing? Step(IBotWilful bot)
    {
        if (_over || bot?.Self is not BotMobile body || !body.Alive || body.Map != _map)
        {
            return null;
        }

        var now = Core.TickCount;

        if (_spell != null)
        {
            return Casting(body, now);
        }

        if (now - _nextTick < 0)
        {
            return _retrying ? BotDoing.Work($"gathering itself to recall to {_entry?.Description} again") : null;
        }

        if (_entry == null && !Plan(body))
        {
            return null;
        }

        if (!BotRunes.Ready(body, BotRunes.Recall, out var scroll, out _, out var why))
        {
            BotRunes.RecallsUnready++;
            BotRunes.Unready(why);
            _nextTick = now + ReconsiderMs;
            _retrying = false;

            _entry = null;

            return null;
        }

        _retrying = false;

        body.Journey?.Finish();
        _spell = new RecallSpell(body, _entry, null, scroll);

        if (!_spell.Cast())
        {
            _spell = null;

            return Failed(body, now, "the engine would not begin the cast");
        }

        return BotDoing.Work($"recalling to {_entry.Description}");
    }

    private bool Plan(BotMobile body)
    {
        var away = BotRunes.Apart(body.Location, _where);

        if (away < BotRunes.Far)
        {
            _over = true;

            return false;
        }

        if (!BotRunes.Mage(body))
        {
            _over = true;

            return false;
        }

        if (!_asked)
        {
            _asked = true;
            BotRunes.RecallsAsked++;
        }

        if (!BotRunes.Holds(body, _map, _where, System.Math.Max(BotRunes.Near, _arrival), out var entry)
            || away - BotRunes.Apart(entry.Location, _where) < BotRunes.LeastSaved)
        {
            _nextTick = Core.TickCount + ReconsiderMs;

            return false;
        }

        if (!SpellHelper.CheckTravel(body, TravelCheckType.RecallFrom, out _)
            || !SpellHelper.CheckTravel(body, entry.Map, entry.Location, TravelCheckType.RecallTo, out _))
        {
            BotRunes.Unready("the engine forbids the recall here");
            _nextTick = Core.TickCount + ReconsiderMs;

            return false;
        }

        if (!_held)
        {
            _held = true;
            BotRunes.RecallsHeld++;
        }

        _entry = entry;
        _from = away;

        return true;
    }

    private BotDoing? Casting(BotMobile body, long now)
    {
        if (ReferenceEquals(body.Spell, _spell) && _spell.State != SpellState.None)
        {
            return BotDoing.Work($"recalling to {_entry.Description}");
        }

        _spell = null;

        if (body.Map == _entry.Map && body.InRange(_entry.Location, 2))
        {
            Moved = true;
            _over = true;

            var left = BotRunes.Apart(body.Location, _where);

            BotRunes.Recalls++;
            BotRunes.TilesSaved += System.Math.Max(0, _from - left);

            if (body.Guild != null)
            {
                BotRuneShelf.Used(body.Guild.Name, body.Map, _entry.Location);
            }

            body.Journey?.Finish();

            logger.Information(
                "{Name} the {Class} recalled to {Place} instead of walking {Tiles} tiles ({Purpose}); {Left} left to walk",
                body.Name,
                body.Class?.Name,
                _entry.Description,
                _from,
                _purpose,
                left
            );

            return null;
        }

        return Failed(body, now, "the spell fizzled");
    }

    private BotDoing? Failed(BotMobile body, long now, string how)
    {
        _tries++;
        BotRunes.RecallFizzles++;

        if (_tries >= BotRunes.Tries)
        {
            _over = true;
            BotRunes.RecallFallbacks++;

            logger.Information(
                "{Name} the {Class} could not recall to {Place} in {Tries} tries ({How}) and walks {Tiles} tiles instead ({Purpose})",
                body.Name,
                body.Class?.Name,
                _entry?.Description,
                _tries,
                how,
                _from,
                _purpose
            );

            return null;
        }

        _nextTick = now + BotRunes.RetryMs;
        _retrying = true;

        return BotDoing.Work($"{how}; recalling to {_entry?.Description} again");
    }
}
