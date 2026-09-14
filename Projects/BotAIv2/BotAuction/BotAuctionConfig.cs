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
    public double? RaiseStep { get; set; }

    public double? CutStep { get; set; }

    public int? BriskMs { get; set; }

    public int? StaleMs { get; set; }

    public double? MostMultiple { get; set; }

    public double? LeastMultiple { get; set; }

    public int? ForgetMs { get; set; }

    public int? BeatMs { get; set; }

    public int? MaxListings { get; set; }

    public int? MaxWants { get; set; }

    public int? Slice { get; set; }

    public int? SliceMs { get; set; }

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
