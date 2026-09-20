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
    Ta2,
    Ta3
}

public sealed record FarmCoordinate(int X, int Y);

public sealed record AutomationClientOptions(
    string Label,
    GameWindowTarget Target,
    TaDestination Destination,
    bool UseSapheras,
    int Priority,
    FarmCoordinate? CustomFarmCoordinate,
    bool UseAbbey = false,
    int AbbeyReturnLimit = 0,
    FarmCoordinate? AbbeyCustomFarmCoordinate = null);

public sealed record BotRunOptions(
    SapherasOptions Sapheras,
    AntiOverkillOptions AntiOverkill,
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
