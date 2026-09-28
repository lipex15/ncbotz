namespace BotNC.App.Models;

public sealed record SapherasOptions(
    DateTime ScheduledAt,
    TimeSpan Duration,
    TimeSpan DirectSapherasWindow,
    int TeleportVirtualKey,
    string TeleportKeyName,
    int EmergencyTeleportVirtualKey,
    string EmergencyTeleportKeyName);

public sealed record AntiOverkillOptions(
    int DeathThreshold,
    TimeSpan DeathWindow,
    TimeSpan AgendaDuration);

public enum TaDestination
{
    Ta1Codex,
    Ta2,
    Ta3
}

public enum GuildDirectiveArea
{
    OpenMap,
    Ta,
    Dungeon
}

public sealed record DailyRoutineOptions(
    bool EnableDailyMissions,
    TimeSpan DailyMissionsAt,
    bool EnableGuildDirective,
    TimeSpan GuildDirectiveAt,
    GuildDirectiveArea GuildDirectiveArea,
    bool EnableDailyShop,
    TimeSpan DailyShopAt,
    bool EnableLoveBoss = false,
    bool EnableGuildCheckin = false,
    TimeSpan GuildCheckinAt = default);

public enum FarmScheduleDestination
{
    Abbey,
    // T.A 1 is supported again using its captured coordinate, without Favorites.
    // T.A 2/3 remain legacy values and are filtered from scheduled destinations.
    Ta1,
    Ta2,
    Ta3,
    AnonymousDungeon
}

public sealed record FarmScheduleStep(
    FarmScheduleDestination Destination,
    TimeSpan Duration,
    int AnonymousDungeonLevel = 97);

public sealed record FarmScheduleOptions(
    bool Enabled,
    IReadOnlyList<FarmScheduleStep> Client1,
    IReadOnlyList<FarmScheduleStep> Client2);

public sealed record FarmCoordinate(int X, int Y);

public sealed record AutomationClientOptions(
    string Label,
    GameWindowTarget Target,
    TaDestination Destination,
    bool UseSapheras,
    int Priority,
    FarmCoordinate? CustomFarmCoordinate,
    bool UseAbbey = false,
    int WeeklyAgendaEntryLimit = 1,
    FarmCoordinate? AbbeyCustomFarmCoordinate = null,
    bool UseFarmSchedule = false,
    bool EnableDailyMissions = true,
    bool EnableGuildDirective = true,
    bool EnableMail = true,
    bool EnableAntiOverkill = true,
    bool EnableDailyShop = false,
    IReadOnlyDictionary<TaDestination, FarmCoordinate>? CustomFarmCoordinates = null,
    bool EnableLoveBoss = false,
    bool EnableGuildCheckin = false,
    bool UseAnonymousDungeon = false,
    int IndividualAnonymousDungeonLevel = 97,
    int WeeklyAnonymousEntryLimit = 1,
    FarmCoordinate? SapherasCustomFarmCoordinate = null,
    bool EnableAutoStorage = false,
    int AutoStorageIntervalMinutes = 120,
    FarmCoordinate? AnonymousDungeonCustomFarmCoordinate = null,
    PartyOptions? Party = null,
    bool KeepRestMode = true,
    bool EnableBoostBuff = false,
    GlobalDungeonOptions? GlobalDungeon = null);

public sealed record GlobalDungeonOptions(bool Enabled, int DurationMinutes, FarmCoordinate? Coordinate);

public enum PartyRole { Disabled, Leader, Receiver }
public sealed record PartyOptions(PartyRole Role, IReadOnlyList<string> InviteNames, string PreferredInviter = "");

public sealed record BotRunOptions(
    SapherasOptions Sapheras,
    AntiOverkillOptions AntiOverkill,
    DailyRoutineOptions DailyRoutines,
    FarmScheduleOptions FarmSchedule,
    IReadOnlyList<AutomationClientOptions> Clients);

public enum BotRunState
{
    Stopped,
    Waiting,
    Running,
    Paused,
    Completed,
    Failed
}

public sealed record VisualReference(
    string Id,
    string DisplayName,
    byte[] Image,
    int SourceX,
    int SourceY,
    int SourceWidth,
    int SourceHeight,
    int SearchX,
    int SearchY,
    int SearchWidth,
    int SearchHeight,
    double Threshold);

public sealed record RecognitionResult(
    bool Found,
    double Confidence,
    int X,
    int Y);

public sealed record AudioClientStatus(
    int Priority,
    string Label,
    bool IsHealthy,
    bool IsArmed,
    double CurrentConfidence,
    double RecentPeakConfidence,
    DateTime? LastAlertAt,
    long CapturedBufferCount);
