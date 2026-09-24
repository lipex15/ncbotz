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
        try
        {
            var frame = VisualRecognitionService.NormalizeForReferenceMatching(await CaptureClientFrameAsync(session, token));
            if (layout == "articles" && !(await recognition.FindAsync("statistics_article_title", frame, token)).Found)
                return new(null, "popup de preço não confirmado");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            var reading = await GoldPriceReader.ReadAsync(recognition, frame, layout, timeout.Token);
            WritePersistentOnly(session, $"statistics_price layout={layout}; value={reading.Value}; {reading.Evidence}");
            return reading;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) { return new(null, $"preço não apurado: {exception.Message}"); }
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
