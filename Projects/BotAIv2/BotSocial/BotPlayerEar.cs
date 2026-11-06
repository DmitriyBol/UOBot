using System;
using System.Collections.Generic;
using Server.Engines.PartySystem;
using Server.Logging;

namespace Server.BotAI.V2;

public static class BotPlayerEar
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPlayerEar));

    public static bool Running { get; set; } = true;

    public static int EarRange { get; set; } = 12;

    public static int AnswerEveryMs { get; set; } = 15000;

    public static double MetWorth { get; set; } = 2.0;

    public static int InviteEveryMs { get; set; } = 60000;

    public static int InviteTimeoutMs { get; set; } = 45000;

    public static double PartyWorth { get; set; } = 6.0;

    public static long PlayersHeard { get; private set; }

    public static long BotsAnswered { get; private set; }

    public static long AddressedElsewhere { get; private set; }

    public static long Throttled { get; private set; }

    public static long PlayerTiesFormed { get; private set; }

    public static long PartyInvited { get; private set; }

    public static long PartyJoined { get; private set; }

    public static long PartyExpired { get; private set; }

    public static long PartyBusy { get; private set; }

    private static bool _listening;

    private static readonly Dictionary<(Serial, string), long> _answered = [];

    private static readonly Dictionary<(Serial, string), long> _invited = [];

    private static readonly Dictionary<string, (BotMobile Bot, Mobile Player, long At)> _pending = new(StringComparer.Ordinal);

    private static readonly List<string> _settled = [];

    private static InviteTracker _tracker;

    private static readonly string[] Greetings =
    [
        "Well met, {0}.",
        "Hello there, {0}.",
        "Good day to you, {0}.",
        "Hail, {0}."
    ];

    private static readonly string[] JoinLines =
    [
        "Fall in with me, then.",
        "A blade beside mine is welcome. /accept.",
        "Then we walk together. /accept when you are ready."
    ];

    private static readonly string[] GreetWords =
    [
        "hello", "hi", "hail", "greetings", "well met", "good day", "hey",
        "привет", "здравствуй", "здравствуйте", "хай", "ку"
    ];

    private static readonly string[] WorkQuestions =
    [
        "what are you doing", "what do you do", "what are you up to", "what is your work",
        "что делаешь", "что ты делаешь", "чем занимаешься", "какая работа"
    ];

    private static readonly string[] JoinWords =
    [
        "join me", "join my party", "join my group", "party with me", "group with me",
        "в группу", "пойдём со мной", "пойдем со мной", "за мной", "группа"
    ];

    public static void Listen()
    {
        if (_listening)
        {
            return;
        }

        _listening = true;
        EventSink.Speech += Said;

        _tracker?.Stop();
        _tracker = new InviteTracker();
        _tracker.Start();

        logger.Information(
            "The bots' ear for a person is open: within {Range} tiles a greeting or a question about the work is answered by the nearest bot, a named bot by the bot named, each once in {Every}s to the same speaker; a meeting is worth {Worth} regard; a join me brings an invitation into the engine's party",
            EarRange,
            AnswerEveryMs / 1000,
            MetWorth
        );
    }

    public static void Forget()
    {
        if (!_listening)
        {
            return;
        }

        _listening = false;
        EventSink.Speech -= Said;

        _tracker?.Stop();
        _tracker = null;
        _pending.Clear();
    }

    public static string Describe() =>
        !Running
            ? "the bots' ear for a person is shut"
            : $"{PlayersHeard} lines heard from players within {EarRange} tiles, {BotsAnswered} answered, {AddressedElsewhere} meant for a bot beyond earshot, {Throttled} held for coolness, {PlayerTiesFormed} players remembered; "
              + $"{PartyInvited} asks into a party made ({PartyJoined} joined, {PartyExpired} not answered, {PartyBusy} refused, one side already spoken for)";

    public static void ResetCounters()
    {
        PlayersHeard = 0;
        BotsAnswered = 0;
        AddressedElsewhere = 0;
        Throttled = 0;
        PlayerTiesFormed = 0;
        PartyInvited = 0;
        PartyJoined = 0;
        PartyExpired = 0;
        PartyBusy = 0;
        _answered.Clear();
        _invited.Clear();
        _pending.Clear();
    }

    private static void Said(SpeechEventArgs e)
    {
        var from = e?.Mobile;
        var said = e?.Speech;

        if (!Running || from == null || from.Deleted || from is BotMobile || from.NetState == null
            || string.IsNullOrWhiteSpace(said))
        {
            return;
        }

        var text = said.Trim();
        var bots = BotPopulation.Bots;
        BotMobile named = null;
        BotMobile nearest = null;
        var nearestTiles = int.MaxValue;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is not { Deleted: false, Alive: true } bot || bot.Map != from.Map)
            {
                continue;
            }

            if (named == null && Named(text, bot.Name))
            {
                named = bot;
            }

            var tiles = (int)bot.GetDistanceToSqrt(from);

            if (tiles <= EarRange && tiles < nearestTiles)
            {
                nearestTiles = tiles;
                nearest = bot;
            }
        }

        if (named == null)
        {
            for (var i = 0; i < bots.Count; i++)
            {
                if (bots[i] is { Deleted: false } bot && Named(text, bot.Name))
                {
                    AddressedElsewhere++;

                    return;
                }
            }
        }

        if (nearest == null)
        {
            return;
        }

        PlayersHeard++;

        var body = named ?? nearest;

        if (named == null && !Greets(text) && !AsksWork(text) && !AsksJoin(text))
        {
            return;
        }

        var now = Core.TickCount;

        if (_answered.TryGetValue((body.Serial, from.Name), out var last) && now - last < Math.Max(1000, AnswerEveryMs))
        {
            Throttled++;

            return;
        }

        _answered[(body.Serial, from.Name)] = now;

        if (_answered.Count > 1024)
        {
            _answered.Clear();
        }

        if (AsksJoin(text))
        {
            TryInvite(body, from, now);

            return;
        }

        string line;

        if (AsksWork(text))
        {
            line = WorkLine(body);
        }
        else if (Greets(text))
        {
            line = string.Format(Greetings[(int)(Utility.RandomDouble() * Greetings.Length)], from.Name);
        }
        else
        {
            line = "I did not follow that.";
        }

        BotVoice.Aloud(body, line);

        BotsAnswered++;

        var stranger = BotLives.Find(body.Name)?.Tie(from.Name) == null;

        BotTies.MoveToward(body, from.Name, MetWorth, "met and spoken with");

        if (stranger && BotLives.Find(body.Name)?.Tie(from.Name) != null)
        {
            PlayerTiesFormed++;
        }

        logger.Information("{Bot} answers {Player}, {Tiles} tiles off: {Line}", body.Name, from.Name, nearestTiles, line);
    }

    private static string WorkLine(BotMobile body)
    {
        var stage = body.Resolve.Deed?.Stage;

        return stage == null ? "Nothing in particular at the moment." : $"I am {stage}.";
    }

    private static void TryInvite(BotMobile bot, Mobile player, long now)
    {
        if (_invited.TryGetValue((bot.Serial, player.Name), out var last) && now - last < Math.Max(5000, InviteEveryMs))
        {
            Throttled++;

            return;
        }

        if (Party.Get(bot) != null || player.Party != null)
        {
            PartyBusy++;

            return;
        }

        _invited[(bot.Serial, player.Name)] = now;

        if (_invited.Count > 1024)
        {
            _invited.Clear();
        }

        BotVoice.Aloud(bot, JoinLines[(int)(Utility.RandomDouble() * JoinLines.Length)]);
        Party.Invite(bot, player);
        PartyInvited++;

        _pending[player.Name] = (bot, player, now);

        logger.Information("{Bot} asks {Player} into its party", bot.Name, player.Name);
    }

    private static void Track(long now)
    {
        foreach (var (name, pending) in _pending)
        {
            var p = Party.Get(pending.Player);

            if (p?.Contains(pending.Bot) == true)
            {
                PartyJoined++;

                BotTies.MoveToward(pending.Bot, name, PartyWorth, "joined my party");

                logger.Information("{Bot} has {Player} in its party", pending.Bot.Name, name);

                _settled.Add(name);
            }
            else if (pending.Player.Party == null || now - pending.At > Math.Max(10000, InviteTimeoutMs))
            {

                PartyExpired++;

                _settled.Add(name);
            }
        }

        for (var i = 0; i < _settled.Count; i++)
        {
            _pending.Remove(_settled[i]);
        }

        _settled.Clear();
    }

    private sealed class InviteTracker : Timer
    {
        public InviteTracker() : base(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2))
        {
        }

        protected override void OnTick() => Track(Core.TickCount);
    }

    private static bool Named(string said, string name)
    {
        if (string.IsNullOrEmpty(name) || said.Length < name.Length
            || !said.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return said.Length == name.Length || !char.IsLetterOrDigit(said[name.Length]);
    }

    private static bool Greets(string said)
    {
        for (var i = 0; i < GreetWords.Length; i++)
        {
            if (said.StartsWith(GreetWords[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AsksWork(string said)
    {
        for (var i = 0; i < WorkQuestions.Length; i++)
        {
            if (said.StartsWith(WorkQuestions[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AsksJoin(string said)
    {
        for (var i = 0; i < JoinWords.Length; i++)
        {
            if (said.StartsWith(JoinWords[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
