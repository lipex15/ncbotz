using System.IO;

namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    private readonly StatisticsStore _statistics = new(Path.GetDirectoryName(database.DatabasePath)!);
    public string StatisticsSessionId { get; private set; } = "not-started";

    private async Task RecordStatisticAsync(ClientSession session, string kind, string description,
        string? id = null, long? gold = null, double quantity = 1, string evidence = "")
    {
        try
        {
            await _statistics.RecordAsync(new(id ?? Guid.NewGuid().ToString("N"), ActivationService.ProfileKey,
                session.Options.Label.EndsWith("2", StringComparison.Ordinal) ? 2 : 1,
                StatisticsSessionId, DateTimeOffset.UtcNow, kind, description, gold, quantity, evidence));
            if (kind != "farm") WritePersistentOnly(session, $"statistics kind={kind}; description={description}; gold={gold}; quantity={quantity}; {evidence}");
        }
        catch (Exception exception)
        {
            // Statistics must never interrupt protection, movement or purchases.
            WritePersistentOnly(session, $"statistics_write_failed: {exception.Message}");
        }
    }

    private async Task<GoldPriceReading> ReadStatisticsPriceAsync(ClientSession session,
        string layout, CancellationToken token)
    {
        PixelFrame? frame = null;
        try
        {
            frame = VisualRecognitionService.NormalizeForReferenceMatching(await CaptureClientFrameAsync(session, token));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            var reading = layout == "articles" && !(await recognition.FindAsync("statistics_article_title", frame, timeout.Token)).Found
                ? new GoldPriceReading(null, "popup de preço não confirmado")
                : await GoldPriceReader.ReadAsync(recognition, frame, layout, timeout.Token);
            WritePersistentOnly(session, $"statistics_price layout={layout}; value={reading.Value}; {reading.Evidence}");
            if (reading.Value is null)
            {
                await SavePriceEvidenceAsync(session, layout, frame, reading.Evidence);
            }
            return reading;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            WritePersistentOnly(session, $"statistics_price_failed layout={layout}; error={exception.GetType().Name}; detail={exception.Message}; valueNotInvented=true");
            if (frame is not null) await SavePriceEvidenceAsync(session, layout, frame, exception.Message);
            return new(null, $"preço não apurado: {exception.Message}");
        }
    }

    private async Task SavePriceEvidenceAsync(ClientSession session, string layout, PixelFrame frame, string evidence)
    {
        try
        {
            if (DateTime.UtcNow - session.LastPriceDiagnosticAt < TimeSpan.FromSeconds(5))
            {
                WritePersistentOnly(session, $"statistics_price_unresolved layout={layout}; diagnosticSkipped=rate_limit_5s; {evidence}");
                return;
            }
            session.LastPriceDiagnosticAt = DateTime.UtcNow;
            var diagnostic = await recognition.SaveDiagnosticAsync($"price_{layout}_client{session.Options.Priority}", frame, DiagnosticState(session));
            WritePersistentOnly(session, $"statistics_price_unresolved layout={layout}; diagnostic={diagnostic}; frame={frame.Width}x{frame.Height}; {evidence}");
        }
        catch (Exception error) { WritePersistentOnly(session, $"statistics_price_diagnostic_failed: {error.Message}"); }
    }

    private Task RecordExpenseAsync(ClientSession session, string description, GoldPriceReading reading, string? id = null) =>
        RecordStatisticAsync(session, "expense", description, id, reading.Value, evidence: reading.Evidence);

    private async Task RecordPendingDailyTeleportAsync(ClientSession session, string id, GoldPriceReading price)
    {
        session.StatisticsDailyPrice = price;
        session.StatisticsDailyPriceId = id;
        session.StatisticsDailyPriceAt = DateTime.UtcNow;
        await RecordStatisticAsync(session, "pending", "Teleporte para Diárias", id, price.Value,
            evidence: "Popup fechou; aguardando Campanha automática. " + price.Evidence);
    }

    private async Task TryRecordDailyExpenseAsync(ClientSession session, CancellationToken token)
    {
        if (session.StatisticsDailyPriceId is not { } id || session.StatisticsDailyPrice is not { } price) return;
        try
        {
            if (DateTime.UtcNow - session.StatisticsDailyPriceAt > TimeSpan.FromSeconds(60))
            { session.StatisticsDailyPriceId = null; return; }
            var frame = await CaptureClientFrameAsync(session, token);
            if (!(await recognition.FindAsync("daily_automatic", frame, token)).Found) return;
            await RecordExpenseAsync(session, "Teleporte para Diárias", price, id);
            session.StatisticsDailyPriceId = null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) { WritePersistentOnly(session, $"statistics_daily_pending: {exception.Message}"); }
    }

    private Task RecordEmergencyCommandAsync(ClientSession session) =>
        RecordStatisticAsync(session, "emergency", "Proteção acionada · comando de TP enviado",
            $"{StatisticsSessionId}.emergency.{Interlocked.Read(ref session.EmergencyClaimUntilTicks)}");
}
