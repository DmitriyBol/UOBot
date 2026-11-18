using System;
using System.Collections.Generic;
using Server.Engines.PartySystem;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The bots' ear for a person's voice, and the memory of having met one (the night of 06.11.2026, Patrick's night
/// order: houses and the furnishing of them, and the bots' life among players).
///
/// <para>
/// <b>Only a real voice is heard.</b> The sink takes every line said in the world and refuses almost all of them
/// cheaply, as <c>BotHail</c> does: a bot's line, a line from anything without a connected client, an empty line —
/// all pass by. What is left is a person standing somewhere, and only then is the earshot asked.
/// </para>
///
/// <para>
/// <b>What is answered, and by whom.</b> A line that begins with a bot's name goes to that bot; a bot named from
/// beyond <see cref="EarRange"/> tiles is not answered by whoever is nearer — the line was not meant for it
/// (<c>AddressedElsewhere</c>). A bare greeting, or a question about the bot's work, is answered by the nearest bot
/// in earshot, and by one only: forty bots answering one hello is a crowd, not a greeting. Nothing else is answered
/// — the bots do not invent replies, and what they do say is only what they factually know: their own name and the
/// deed they are about (<c>Resolve.Deed.Stage</c>), or that they are about nothing.
/// </para>
///
/// <para>
/// <b>The meeting is remembered.</b> Each answered exchange moves the answering bot's regard for the player a little
/// (<see cref="MetWorth"/>, <c>BotTies.MoveToward</c>) — a tie like any other, decaying and crowding by the same
/// rule. The player's regard for the bot is not touched: that is the player's to give.
/// </para>
/// </summary>
public static class BotPlayerEar
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPlayerEar));

    /// <summary>Whether bots hear a person at all.</summary>
    public static bool Running { get; set; } = true;

    /// <summary>How near a person must stand to be heard, in tiles. The engine's own earshot is twelve.</summary>
    public static int EarRange { get; set; } = 12;

    /// <summary>How long one bot goes between answers to the same person.</summary>
    public static int AnswerEveryMs { get; set; } = 15000;

    /// <summary>What one answered exchange is worth to the answering bot's memory of the player.</summary>
    public static double MetWorth { get; set; } = 2.0;

    /// <summary>
    /// The party, the night of 06.11.2026: a <c>join me</c> asks the bot it was said to, or the nearest bot, into the
    /// engine's own party — the human's to accept or decline, as the engine's invitation already works. One ask a bot
    /// and a player, and only when neither side is already spoken for; what became of the ask is tracked, not assumed.
    /// </summary>
    public static int InviteEveryMs { get; set; } = 60000;

    /// <summary>How long an ask is waited for before it is given up as not answered. The engine declines its own at 30s.</summary>
    public static int InviteTimeoutMs { get; set; } = 45000;

    /// <summary>What a player who really joined is worth to the bot that asked.</summary>
    public static double PartyWorth { get; set; } = 6.0;

    public static long PlayersHeard { get; private set; }

    public static long BotsAnswered { get; private set; }

    /// <summary>Lines that named a bot from beyond earshot: meant for somebody, and not for the hearer.</summary>
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

    /// <summary>Once a process: a reload calls this again, and a hook subscribed twice would answer every line twice.</summary>
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
              + $"{PartyInvited} asks into a party made ({PartyJoined} joined, {PartyExpired} not answered, {PartyBusy} refused, one side already spoken for); "
              + BotPlayerTrade.Describe();

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

    /// <summary>Somebody spoke. Almost always no bot is near, so this must be cheap to refuse.</summary>
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

        // A name said from beyond earshot belongs to the bot it was said to: the nearest keeps quiet, whatever it heard.
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

        // A bare line that is neither a greeting, nor a question about the work, nor a call into a party, nor trade with
        // the speaker, is nobody's to answer; a named bot answers even then, and honestly — it did not follow.
        if (named == null && !Greets(text) && !AsksWork(text) && !AsksJoin(text)
            && !BotPlayerTrade.AsksBuy(text) && !BotPlayerTrade.SaysAccept(text))
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

        // A buy is trade's to answer, and an accept closes what a buy opened (the night queue's P6, 18.11.2026); an
        // accept with nothing standing falls through to the talk below.
        if (BotPlayerTrade.AsksBuy(text))
        {
            BotPlayerTrade.Buy(body, from, text);

            return;
        }

        if (BotPlayerTrade.SaysAccept(text) && BotPlayerTrade.Accept(from))
        {
            return;
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

    /// <summary>The work question's answer: the deed the bot is about, in its own words, or the honest nothing.</summary>
    private static string WorkLine(BotMobile body)
    {
        var stage = body.Resolve.Deed?.Stage;

        return stage == null ? "Nothing in particular at the moment." : $"I am {stage}.";
    }

    /// <summary>
    /// The bot asks the player into the engine's own party. Only when neither is spoken for — a bot already leading one,
    /// a player invited or in one — and once in <see cref="InviteEveryMs"/> for the pair. What becomes of the ask is the
    /// tracker's, not the asker's word: joined is read off the party itself.
    /// </summary>
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

    /// <summary>What came of the asks, read off the party and the invitation's own timer, never assumed.</summary>
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
                // Declined, lapsed by the engine's own timer, or gone: the ask was not taken up, and nothing is claimed.
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

    /// <summary>Whether the line opens with this name, and the name ends there — "Lorelei" does not answer to "Loreleison".</summary>
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
