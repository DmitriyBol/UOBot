using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>The numbers of <c>Configuration/bot-character.json</c>. A missing key keeps the code's value; keys are PascalCase.</summary>
public sealed class BotCharacterSettings
{
    public bool? Traits { get; set; }

    public double? Lean { get; set; }

    public double? Least { get; set; }

    public double? Most { get; set; }

    public int? VeteranKills { get; set; }

    public bool? Biography { get; set; }

    public double? TaleChance { get; set; }

    public bool? Ties { get; set; }

    public double? DuelAt { get; set; }

    public double? FriendAt { get; set; }

    public bool? Duels { get; set; }
}

/// <summary>
/// A bot's character as a module: reads its numbers, puts its words into the voice's bank, offers the seeking out of an enemy
/// to the auction, runs the clock that challenges, judges duels, lets ties fade and writes the <c>Character:</c> line, and says at
/// boot how the population's natures fall. Patrick's idea of 07.10.2026 (night 17, "a livelier world"). See <see cref="BotTraits"/>,
/// <see cref="BotBiography"/>, <see cref="BotTies"/> and <see cref="BotGrudges"/>.
///
/// <para>
/// <b>Registers itself</b> from its own <c>Configure</c>, after <c>BotCore.Configure</c> (priority 41 against 40), rather than from
/// the list in <c>BotCore</c>: four agents built new modules on the same night and each one line in the same list would have been
/// four patches colliding in one hunk. A World-phase module needs nothing from the Settings phase, so the order is harmless.
/// </para>
/// </summary>
public sealed class BotCharacterModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCharacterModule));

    private const string ConfigPath = "Configuration/bot-character.json";

    public static int TickMs { get; set; } = 2000;

    private static CharacterTimer _timer;

    private static long _summaryTick;

    private static long _refreshTick;

    public static bool Started { get; private set; }

    [CallPriority(41)]
    public static void Configure()
    {
        if (BotCore.Enabled)
        {
            BotModules.Register(new BotCharacterModule());
        }
    }

    public override string Name => "Character";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Population", "Will", "Voice"];

    public override void Start()
    {
        BotPolitics.TiesRegard = static (a, b) => BotTies.Regard(a, b);
        BotPolitics.Veteran = static bot => BotTraits.Veteran(bot);

        Load();

        var words = Phrases();

        BotWill.Offer(new BotGrudgeProposer());

        BotLives.Refresh();

        var now = Core.TickCount;

        _summaryTick = now;
        _refreshTick = now;
        Started = true;

        _timer?.Stop();
        _timer = new CharacterTimer(TimeSpan.FromMilliseconds(Math.Max(250, TickMs)));
        _timer.Start();

        logger.Information(
            "Character is {State}: natures {Traits} (×{Down:F2}–×{Up:F2} a trait, held in ×{Least:F2}–×{Most:F2}; daring ×{CD:F2}/×{RD:F2}, flight ×{CF:F2}/×{RF:F2} cautious/reckless, never under {Floor:P0}); "
            + "veterans at {Veteran} kills; biographies {Bio} ({Tales} tales a bot, one fire line in {Chance:P0} a tale); ties {Ties} (friend at {Friend}, enemy at {Enemy}, at most {Most2} a bot, fading {Decay:P0} each {Every} min); "
            + "grudge duels {Duels} within {Reach} tiles, sought up to {Seek}; {Words} occasions added to the voice's bank. The population: {Distribution}",
            BotTraits.On || BotBiography.On || BotTies.On ? "on" : "off",
            BotTraits.On ? "on" : "off",
            1.0 - BotTraits.Lean,
            1.0 + BotTraits.Lean,
            BotTraits.Least,
            BotTraits.Most,
            BotTraits.CautiousDaring,
            BotTraits.RecklessDaring,
            BotTraits.CautiousFlee,
            BotTraits.RecklessFlee,
            BotTraits.FleeFloor,
            BotTraits.VeteranKills,
            BotBiography.On ? "on" : "off",
            BotBiography.MostTales,
            BotBiography.TaleChance,
            BotTies.On ? "on" : "off",
            BotTies.FriendAt,
            BotTies.DuelAt,
            BotTies.MostTies,
            BotTies.DecayShare,
            BotTies.DecayEveryMs / 60000,
            BotGrudges.On ? "on" : "off",
            BotGrudges.Reach,
            BotGrudges.SeekReach,
            words,
            BotTraits.Distribution()
        );
    }

    public static void Tick(long now)
    {
        if (now - _refreshTick >= Math.Max(5000, BotGrudges.LookEveryMs))
        {
            _refreshTick = now;
            BotLives.Refresh();
        }

        BotGrudges.Tick(now);

        var forgotten = BotTies.Decay(now);

        if (forgotten > 0)
        {
            logger.Information("Ties faded: {Forgotten} regards fell under one and were forgotten", forgotten);
        }

        if (now - _summaryTick >= Math.Max(60000, BotBeat.SummaryMs))
        {
            _summaryTick = now;

            logger.Information("Character: {Traits}; {Bio}; ties: {Ties}; {Grudges}", BotTraits.Describe(), BotBiography.Describe(), BotTies.Describe(), BotGrudges.Describe());
        }
    }

    public static Dictionary<string, string[]> Defaults() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["camp:tale"] = ["{tale}", "Did I ever tell you? {tale}", "{tale} I won't forget it.", "{tale} True story."],
            ["camp:friend"] =
            [
                "{friend} is a good friend of mine. {why}",
                "If you ever need help, look for {friend}. {why}",
                "I'd walk through fire for {friend}. {why}"
            ],
            ["camp:enemy"] =
            [
                "If you see {enemy}, tell them I haven't forgotten.",
                "{enemy} and I have a score to settle.",
                "Don't speak to me of {enemy}."
            ],
            ["character:tale"] = ["{tale}", "I was just thinking... {tale}", "{tale} Strange, the things you remember."],
            ["character:veteran"] = ["{kills} kills now. They call me {title}.", "That's {kills}. A veteran, they say."],
            ["character:challenge"] = ["{other}! You and I have a score to settle. Draw!", "{other}. Here and now, you and me.", "I've waited for this, {other}. Draw!"],
            ["character:accept"] = ["So be it, {other}.", "If you must, {other}.", "Have it your way, {other}."],
            ["character:won"] = ["That settles it, {other}.", "We're done, {other}. No hard feelings.", "Square now, {other}."],
            ["character:lost"] = ["You have it, {other}. We're square.", "Fair fight, {other}. It's settled.", "Enough, {other}. You win this one."]
        };

    private static int Phrases()
    {
        var added = 0;

        foreach (var (key, lines) in Defaults())
        {
            if (!BotVoice.Has(key))
            {
                BotVoice.Phrases(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [key] = lines });
                added++;
            }
        }

        return added;
    }

    private static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotCharacterSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotCharacterSettings());
            logger.Information("Wrote a starter character file to {Path}; every number stays as the code has it", ConfigPath);

            return;
        }

        BotTraits.On = settings.Traits ?? BotTraits.On;
        BotTraits.Lean = settings.Lean ?? BotTraits.Lean;
        BotTraits.Least = settings.Least ?? BotTraits.Least;
        BotTraits.Most = settings.Most ?? BotTraits.Most;
        BotTraits.VeteranKills = settings.VeteranKills ?? BotTraits.VeteranKills;
        BotBiography.On = settings.Biography ?? BotBiography.On;
        BotBiography.TaleChance = settings.TaleChance ?? BotBiography.TaleChance;
        BotTies.On = settings.Ties ?? BotTies.On;
        BotTies.DuelAt = settings.DuelAt ?? BotTies.DuelAt;
        BotTies.FriendAt = settings.FriendAt ?? BotTies.FriendAt;
        BotGrudges.On = settings.Duels ?? BotGrudges.On;
    }

    public override void Reset()
    {
        logger.Information("Character, before the reload: {Traits}; {Ties}", BotTraits.Describe(), BotTies.Describe());

        _timer?.Stop();
        _timer = null;

        BotTraits.Forget();
        BotBiography.Forget();
        BotTies.Forget();
        BotGrudges.Forget();
    }

    private sealed class CharacterTimer : Timer
    {
        public CharacterTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => BotCharacterModule.Tick(Core.TickCount);
    }
}

/// <summary>A bot's character on its page: <c>/api/bot/&lt;name&gt;</c> and the snapshot. Called by <c>BotWebSnapshot</c>.</summary>
public static class BotCharacterWeb
{
    public static int MostTies { get; set; } = 6;

    public static double LeastRegard { get; set; } = 10.0;

    public static void Write(Utf8JsonWriter w, BotMobile bot)
    {
        if (BotLives.Of(bot) is not { } life)
        {
            return;
        }

        w.WritePropertyName("traits");
        w.WriteStartArray();

        foreach (var trait in BotTraits.List(life.Traits))
        {
            w.WriteStringValue(trait);
        }

        w.WriteEndArray();

        w.WriteBoolean("veteran", life.Veteran);
        BotJson.StringOrNull(w, "title", life.Veteran ? BotTraits.VeteranTitle : null);
        w.WriteNumber("slain", Math.Round(life.Kills, 1));
        w.WriteNumber("deaths", life.Deaths);

        w.WritePropertyName("biography");
        w.WriteStartArray();

        for (var i = 0; i < life.Tales.Count; i++)
        {
            var tale = life.Tales[i];

            w.WriteStartObject();
            w.WriteString("kind", tale.Kind);
            BotJson.StringOrNull(w, "other", tale.Other);
            w.WriteString("text", tale.Text);
            w.WriteNumber("weight", Math.Round(tale.Weight, 1));
            w.WriteString("at", tale.At.ToString("yyyy-MM-dd HH:mm"));
            w.WriteEndObject();
        }

        w.WriteEndArray();

        w.WritePropertyName("life");
        w.WriteStartObject();
        w.WriteNumber("saved", life.SavedTimes);
        w.WriteNumber("raised", life.RaisedTimes);
        w.WriteNumber("robbed", life.Robbed);
        w.WriteNumber("warKills", life.WarKills);
        w.WriteNumber("duelsWon", life.DuelsWon);
        w.WriteNumber("duelsLost", life.DuelsLost);
        w.WriteNumber("bestSale", life.BestSale);
        w.WriteEndObject();

        w.WritePropertyName("ties");
        w.WriteStartArray();

        Span<bool> written = stackalloc bool[life.Ties.Count];

        for (var n = 0; n < MostTies; n++)
        {
            var best = -1;

            for (var i = 0; i < life.Ties.Count; i++)
            {
                if (!written[i] && Math.Abs(life.Ties[i].Regard) >= LeastRegard
                    && (best < 0 || Math.Abs(life.Ties[i].Regard) > Math.Abs(life.Ties[best].Regard)))
                {
                    best = i;
                }
            }

            if (best < 0)
            {
                break;
            }

            written[best] = true;

            var tie = life.Ties[best];

            w.WriteStartObject();
            w.WriteString("name", tie.Name);
            w.WriteNumber("regard", Math.Round(tie.Regard));
            w.WriteString("is", tie.Regard >= BotTies.FriendAt ? "friend" : tie.Regard <= BotTies.DuelAt ? "enemy" : tie.Regard < 0 ? "cool" : "warm");
            w.WriteNumber("saved", tie.Saved);
            w.WriteNumber("wronged", tie.Wronged);
            w.WriteNumber("beside", tie.Beside);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }
}
