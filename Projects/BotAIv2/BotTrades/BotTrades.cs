namespace Server.BotAI.V2;

public static class BotTrades
{

    public static bool Running { get; set; } = true;

    public static bool Taverns { get; set; } = true;

    public static bool Lenders { get; set; } = true;

    public static bool Libraries { get; set; } = true;

    public static bool Ragmen { get; set; } = true;

    public static bool TavernsOn => Running && Taverns;

    public static bool LendersOn => Running && Lenders;

    public static bool LibrariesOn => Running && Libraries;

    public static bool RagmenOn => Running && Ragmen;

    public static string Clause() =>
        $"; establishments: {(TavernsOn ? "" : "taverns OFF, ")}{BotTaverns.Describe()}; {(LendersOn ? "" : "lending OFF, ")}{BotLoans.Describe()}; {(BotInfirmaries.On ? "" : "infirmaries OFF, ")}{BotInfirmaries.Describe()}"
        + $"; {(LibrariesOn ? "" : "libraries OFF, ")}{BotLibraries.Describe()}; {(RagmenOn ? "" : "ragmen OFF, ")}{BotRagmen.Describe()}";

    public static void Forget()
    {
        BotTaverns.Forget();
        BotTaverner.Forget();
        BotSupperer.Forget();
        BotLoans.Forget();
        BotBorrower.Forget();
        BotInfirmaries.Forget();
        BotInfirmarer.Forget();
        BotPatienter.Forget();
        BotLibraries.Forget();
        BotLibrarian.Forget();
        BotReader.Forget();
        BotRagmen.Forget();
        BotRagman.Forget();
        BotRagSeller.Forget();
        BotRagHauler.Forget();
    }
}
