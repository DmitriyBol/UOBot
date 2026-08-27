using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What <c>Configuration/bot-auction.json</c> is allowed to say. Everything optional.
///
/// <para>
/// What is deliberately <b>not</b> here: prices. Not one number in this file says what anything is worth —
/// they say how fast a bot changes its mind and how far it may go. A configuration file that could set the
/// price of an ingot would be a configuration file that decides the economy, and then the market would be
/// decoration.
/// </para>
/// </summary>
public sealed class BotAuctionSettings
{
    /// <summary>How much a price goes up when the same goods sell again soon.</summary>
    public double? RaiseStep { get; set; }

    /// <summary>How much it comes down after sitting untouched.</summary>
    public double? CutStep { get; set; }

    /// <summary>How soon after a sale another sale counts as brisk.</summary>
    public int? BriskMs { get; set; }

    /// <summary>How long a stall may sit untouched before the price comes down.</summary>
    public int? StaleMs { get; set; }

    /// <summary>The most a price may become, as a multiple of the opening ask.</summary>
    public double? MostMultiple { get; set; }

    /// <summary>The least it may become, on the same terms.</summary>
    public double? LeastMultiple { get; set; }

    /// <summary>How long an empty stall keeps its remembered price before being forgotten.</summary>
    public int? ForgetMs { get; set; }

    /// <summary>How often the market looks at itself.</summary>
    public int? BeatMs { get; set; }

    /// <summary>How many stalls the market may hold at once.</summary>
    public int? MaxListings { get; set; }

    /// <summary>How many wants the market may hold at once.</summary>
    public int? MaxWants { get; set; }

    /// <summary>The most units one supplier may deliver against one want at a time.</summary>
    public int? Slice { get; set; }

    /// <summary>How long a want holds a supplier off before taking from the same one again.</summary>
    public int? SliceMs { get; set; }

    /// <summary>
    /// Whether produced goods go to the market rather than into the bank box.
    ///
    /// Off, the population still works and still banks metal — it simply has nothing to sell, and the
    /// auction stays empty. That is the A/B for any question of the form "is this the market or is this the
    /// trade that feeds it".
    /// </summary>
    public bool? ListGoods { get; set; }
}

/// <summary>Reads the market file and moves the numbers it names.</summary>
public static class BotAuctionConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotAuctionConfig));

    private const string ConfigPath = "Configuration/bot-auction.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotAuctionSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotAuctionSettings());

            logger.Information(
                "Wrote a starter market file to {Path}; every number stays as the code has it",
                ConfigPath
            );

            return;
        }

        BotAuction.RaiseStep = settings.RaiseStep ?? BotAuction.RaiseStep;
        BotAuction.CutStep = settings.CutStep ?? BotAuction.CutStep;
        BotAuction.BriskMs = settings.BriskMs ?? BotAuction.BriskMs;
        BotAuction.StaleMs = settings.StaleMs ?? BotAuction.StaleMs;
        BotAuction.MostMultiple = settings.MostMultiple ?? BotAuction.MostMultiple;
        BotAuction.LeastMultiple = settings.LeastMultiple ?? BotAuction.LeastMultiple;
        BotAuction.ForgetMs = settings.ForgetMs ?? BotAuction.ForgetMs;
        BotAuction.BeatMs = settings.BeatMs ?? BotAuction.BeatMs;
        BotAuction.MaxListings = settings.MaxListings ?? BotAuction.MaxListings;
        BotAuction.MaxWants = settings.MaxWants ?? BotAuction.MaxWants;
        BotAuction.Slice = settings.Slice ?? BotAuction.Slice;
        BotAuction.SliceMs = settings.SliceMs ?? BotAuction.SliceMs;

        BotDig.ListGoods = settings.ListGoods ?? BotDig.ListGoods;
    }
}
