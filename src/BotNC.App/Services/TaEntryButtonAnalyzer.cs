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

    public static TaEntryButtonReading Read(PixelFrame frame, TaDestination destination, RecognitionResult[]? locatedButtons = null)
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
        var values = LabelX.Select((x, buttonIndex) => locatedButtons is not null
            ? MeasureLabelInk(frame, locatedButtons[buttonIndex].X - 37, locatedButtons[buttonIndex].Y - 12, 75, 25)
            : MeasureLabelInk(
            frame,
            (int)Math.Round(x * scaleX),
            (int)Math.Round(757 * scaleY),
            Math.Max(1, (int)Math.Round(75 * scaleX)),
            Math.Max(1, (int)Math.Round(32 * scaleY)))).ToArray();
        var target = values[index];
        var brightestOther = values.Where((_, otherIndex) => otherIndex != index).Max();

        if (target is >= 45 and <= 155 && brightestOther >= 180 && target <= brightestOther * 0.72)
            return new(TaEntryButtonState.Disabled, target, brightestOther);
        if (target >= 180 && target + 30 >= brightestOther)
            return new(TaEntryButtonState.Active, target, brightestOther);
        return new(TaEntryButtonState.Unknown, target, brightestOther);
    }

    // A tinta das letras distingue os estados mesmo quando a borda dourada,
    // o fundo ou alguns pixels de deslocamento mudam a média do retângulo.
    internal static double MeasureLabelInk(PixelFrame frame, int x, int y, int width, int height)
    {
        var histogram = new int[256];
        var count = 0;
        for (var row = Math.Max(0, y); row < Math.Min(frame.Height, y + height); row++)
        for (var column = Math.Max(0, x); column < Math.Min(frame.Width, x + width); column++)
        {
            var offset = row * frame.Stride + column * 4;
            var value = (frame.Pixels[offset + 2] * 77 + frame.Pixels[offset + 1] * 150 + frame.Pixels[offset] * 29) >> 8;
            histogram[value]++;
            count++;
        }
        var accumulated = 0;
        for (var value = 0; value < histogram.Length; value++)
        {
            accumulated += histogram[value];
            if (accumulated >= count * 0.90) return count == 0 ? 0 : value;
        }
        return 0;
    }
}
