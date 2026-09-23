using BotNC.App.Models;

namespace BotNC.App.Services;

public enum TaEntryButtonState
{
    Unknown,
    Disabled,
    Active
}

public readonly record struct TaEntryButtonReading(
    TaEntryButtonState State,
    double TargetLuma,
    double BrightestOtherLuma);

// Somente a área da palavra Entrar entra na decisão. Preço, moeda, nome do
// mapa, duração da temporada e fundo do cartão ficam fora da amostra.
public static class TaEntryButtonAnalyzer
{
    private static readonly int[] LabelX = [462, 733, 1007];

    public static TaEntryButtonReading Read(PixelFrame frame, TaDestination destination)
    {
        var index = destination switch
        {
            TaDestination.Ta1Codex => 0,
            TaDestination.Ta2 => 1,
            _ => 2
        };

        // A captura da tela inteira usada nos testes inclui a barra de tarefas;
        // a captura da janela do jogo termina em 1040 px. Nos dois casos o
        // botão permanece na mesma coordenada dentro da janela.
        var referenceHeight = frame.Width is >= 1918 and <= 1922 &&
                              frame.Height is >= 1078 and <= 1082
            ? 1040d
            : frame.Height;
        var scaleX = frame.Width / 1920d;
        var scaleY = referenceHeight / 1040d;
        var values = LabelX.Select(x => VisualRecognitionService.MeasureAverageLuma(
            frame,
            (int)Math.Round(x * scaleX),
            (int)Math.Round(757 * scaleY),
            Math.Max(1, (int)Math.Round(75 * scaleX)),
            Math.Max(1, (int)Math.Round(32 * scaleY)))).ToArray();
        var target = values[index];
        var brightestOther = values.Where((_, otherIndex) => otherIndex != index).Max();

        if (target <= 65 && brightestOther >= 75 && target + 22 <= brightestOther)
            return new(TaEntryButtonState.Disabled, target, brightestOther);
        if (target >= 75 && target + 15 >= brightestOther)
            return new(TaEntryButtonState.Active, target, brightestOther);
        return new(TaEntryButtonState.Unknown, target, brightestOther);
    }
}
