using BotNC.App.Models;

namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    private async Task LeaveSapherasSpawnAsync(ClientSession session, PauseController pause, CancellationToken token)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await ExitRestIfNeededAsync(session, pause, token);
            await input.HoldKeyAsync(KeyW, TimeSpan.FromMilliseconds(attempt == 1 ? 3500 : 1500), token);
            await TryOpenRestPanelAsync(session, pause, token);
            var first = await FindRestStateAsync(session, token);
            await Task.Delay(350, token);
            var second = await FindRestStateAsync(session, token);
            var clear = first is not null && second is not null &&
                first.Value.ReferenceId is "descanso_aguardando_spot" or "caca_automatica" &&
                second.Value.ReferenceId is "descanso_aguardando_spot" or "caca_automatica";
            WritePersistentOnly(session, $"sapheras_spawn attempt={attempt}; first={first?.ReferenceId}; second={second?.ReferenceId}; exited={clear}");
            await ExitRestIfNeededAsync(session, pause, token);
            if (clear) return;
        }
        throw new InvalidOperationException("Sapheras: saída do ponto fixo ainda não confirmada após três movimentos W; retomada pendente.");
    }

    private async Task TravelToSapherasPointAsync(ClientSession session, FarmCoordinate coordinate, PauseController pause, CancellationToken token)
    {
        await input.PressKeyAsync(KeyM, cancellationToken: token);
        await WaitForReferenceAsync("sapheras_map", "mapa de Sapheras", TimeSpan.FromSeconds(10), pause, token);
        var point = gameWindows.MapReferencePoint(session.Options.Target, coordinate.X, coordinate.Y);
        await input.MoveAndClickAsync(point.X, point.Y, TimeSpan.FromMilliseconds(300), token);
        string[] references = ["botao_ir", "botao_ir_ta2", "botao_ir_legado"];
        var x = Math.Max(0, point.X - 220);
        var y = Math.Max(0, point.Y - 210);
        var go = await WaitForAnyReferenceInRegionAsync(references, x, y, 470, 230, TimeSpan.FromSeconds(8), pause, token);
        if (go is null) throw new InvalidOperationException("Sapheras: ponto selecionado, mas botão Ir não confirmado; nenhum clique adicional no mapa.");
        await ClickGoButtonWithConfirmationAsync(session, references, x, y, go.X, go.Y, pause, token);
        await CloseMapAfterGoAsync(session, pause, token);
        await OpenRestForTravelAsync(session, pause, token);
        await WaitForFarmArrivalAsync(session, pause, token, "Sapheras");
    }
}
