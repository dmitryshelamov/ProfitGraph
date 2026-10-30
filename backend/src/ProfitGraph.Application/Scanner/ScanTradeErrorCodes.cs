namespace ProfitGraph.Application.Scanner;

public static class ScanTradeErrorCodes
{
    public const string NoSettings = "scan_trade_settings_missing";
    public const string LiveTradeExists = "scan_trade_live";
    public const string EntryMoved = "scan_trade_entry_moved";
    public const string PairNotScanned = "scan_trade_pair_not_scanned";
    public const string WrongVenue = "scan_trade_wrong_venue";
    public const string StartFailed = "scan_trade_start_failed";
    public const string EntryEnded = "scan_trade_entry_ended";
    public const string ShortUnsupported = "scan_trade_short_unsupported";
    public const string StopTooTight = "scan_trade_stop_too_tight";
    public const string EntryBeyondLevels = "scan_trade_entry_beyond_levels";
    public const string EntryPriceUnknown = "scan_trade_entry_price_unknown";
}