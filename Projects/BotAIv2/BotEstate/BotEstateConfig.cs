using System.Collections.Generic;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What <c>Configuration/bot-estate.json</c> is allowed to say. Everything optional.
///
/// <para>
/// What is not here: where a house may stand. That is the engine's — <c>HousePlacement.Check</c> — and a
/// file able to disagree with it would be a file able to promise a guild a hall that cannot be built.
/// </para>
/// </summary>
public sealed class BotEstateSettings
{
    public bool? Running { get; set; }

    public int? Price { get; set; }

    public int? Keep { get; set; }

    public bool? DuesRunning { get; set; }

    public double? Dues { get; set; }

    public int? DuesCeiling { get; set; }

    public bool? DuesSaving { get; set; }

    public int? MaxHalls { get; set; }

    public int? MultiID { get; set; }

    public int? PlotNear { get; set; }

    public int? PlotFar { get; set; }

    public int? PlotApart { get; set; }

    public int? PlotClearance { get; set; }

    public int? PlotBudget { get; set; }

    public int? LookMs { get; set; }

    public double? LookAt { get; set; }

    public double? Expects { get; set; }

    public bool? Fittings { get; set; }

    public int? ShelfDepth { get; set; }

    public int? ShelfBatch { get; set; }

    public int? ShelfSpend { get; set; }

    public double? ShelfMarkup { get; set; }

    public int? ShelfFloat { get; set; }

    public bool? Lands { get; set; }

    public int? LandReach { get; set; }

    public double? LandHome { get; set; }

    public double? LandAbroad { get; set; }

    public bool? Standing { get; set; }

    public bool? War { get; set; }

    public double? WarBelow { get; set; }

    public double? PeaceAbove { get; set; }

    public double? Mend { get; set; }

    public int? WarKills { get; set; }

    public int? WarLoot { get; set; }

    public int? WarLeastMinutes { get; set; }

    public int? WarLongestMinutes { get; set; }

    public int? TruceMinutes { get; set; }

    public int? DeclareEveryMinutes { get; set; }

    public int? MostWars { get; set; }

    public int? Defend { get; set; }

    public Dictionary<string, int[]> Seats { get; set; }

    public int? SeatSettled { get; set; }

    public double? SeatAbroadShare { get; set; }

    public double? Comradeship { get; set; }

    public double? Aid { get; set; }

    public double? Alliance { get; set; }

    public double? Estrangement { get; set; }

    public int? MostAllies { get; set; }
}

public static class BotEstateConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotEstateConfig));

    private const string ConfigPath = "Configuration/bot-estate.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotEstateSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotEstateSettings());

            logger.Information(
                "Wrote a starter estate file to {Path}; every number stays as the code has it",
                ConfigPath
            );

            return;
        }

        BotEstate.Running = settings.Running ?? BotEstate.Running;
        BotEstate.Price = settings.Price ?? BotEstate.Price;
        BotEstate.Keep = settings.Keep ?? BotEstate.Keep;
        BotDues.Running = settings.DuesRunning ?? BotDues.Running;
        BotDues.Share = settings.Dues ?? BotDues.Share;
        BotDues.Ceiling = settings.DuesCeiling ?? BotDues.Ceiling;
        BotDues.Saving = settings.DuesSaving ?? BotDues.Saving;
        BotEstate.MaxHalls = settings.MaxHalls ?? BotEstate.MaxHalls;

        BotPlot.MultiID = settings.MultiID ?? BotPlot.MultiID;
        BotPlot.Near = settings.PlotNear ?? BotPlot.Near;
        BotPlot.Far = settings.PlotFar ?? BotPlot.Far;
        BotPlot.Apart = settings.PlotApart ?? BotPlot.Apart;
        BotPlot.Clearance = settings.PlotClearance ?? BotPlot.Clearance;
        BotPlot.Budget = settings.PlotBudget ?? BotPlot.Budget;

        BotSteward.LookMs = settings.LookMs ?? BotSteward.LookMs;
        BotSteward.LookAt = settings.LookAt ?? BotSteward.LookAt;
        BotHall.Prior = settings.Expects ?? BotHall.Prior;

        BotFittings.Running = settings.Fittings ?? BotFittings.Running;

        BotSupplier.Shelf = settings.ShelfDepth ?? BotSupplier.Shelf;
        BotSupplier.Batch = settings.ShelfBatch ?? BotSupplier.Batch;
        BotSupplier.Spend = settings.ShelfSpend ?? BotSupplier.Spend;
        BotShelf.Markup = settings.ShelfMarkup ?? BotShelf.Markup;
        BotShelf.Float = settings.ShelfFloat ?? BotShelf.Float;

        BotLand.Running = settings.Lands ?? BotLand.Running;
        BotLand.Reach = settings.LandReach ?? BotLand.Reach;
        BotLand.Home = settings.LandHome ?? BotLand.Home;
        BotLand.Abroad = settings.LandAbroad ?? BotLand.Abroad;

        BotRegard.Running = settings.Standing ?? BotRegard.Running;
        BotRegard.Warring = settings.War ?? BotRegard.Warring;
        BotRegard.Enmity = settings.WarBelow ?? BotRegard.Enmity;
        BotRegard.Amity = settings.PeaceAbove ?? BotRegard.Amity;
        BotRegard.Mend = settings.Mend ?? BotRegard.Mend;

        BotWar.Kills = settings.WarKills ?? BotWar.Kills;
        BotWar.Loot = settings.WarLoot ?? BotWar.Loot;
        BotWar.LeastMs = settings.WarLeastMinutes * 60000 ?? BotWar.LeastMs;
        BotWar.LongestMs = settings.WarLongestMinutes * 60000 ?? BotWar.LongestMs;
        BotWar.TruceMs = settings.TruceMinutes * 60000 ?? BotWar.TruceMs;
        BotWar.DeclareEveryMs = settings.DeclareEveryMinutes * 60000 ?? BotWar.DeclareEveryMs;
        BotWar.MostWars = settings.MostWars ?? BotWar.MostWars;
        BotFeud.Defend = settings.Defend ?? BotFeud.Defend;

        BotRegard.Comradeship = settings.Comradeship ?? BotRegard.Comradeship;
        BotRegard.Aid = settings.Aid ?? BotRegard.Aid;
        BotRegard.Alliance = settings.Alliance ?? BotRegard.Alliance;
        BotRegard.Estrangement = settings.Estrangement ?? BotRegard.Estrangement;
        BotRegard.MostAllies = settings.MostAllies ?? BotRegard.MostAllies;

        BotSeat.Configure(settings.Seats);
        BotSeat.Settled = settings.SeatSettled ?? BotSeat.Settled;
        BotSeat.AbroadShare = settings.SeatAbroadShare ?? BotSeat.AbroadShare;
    }
}
