namespace BotNC.App.Services;

internal static class ManualInterfacePolicy
{
    // A remembered manual menu belongs to a live foreground client, not to
    // every client forever. Recent physical input is checked separately.
    internal static bool Blocks(bool rememberedBusy, bool captureFaulted, bool reconnectPending, bool foreground) =>
        rememberedBusy && !captureFaulted && !reconnectPending && foreground;

    internal static void Verify()
    {
        for (var tick = 0; tick < 1000; tick++)
        {
            if (Blocks(true, true, false, false) || Blocks(true, true, false, true) ||
                Blocks(true, false, true, true) || Blocks(true, false, false, false))
                throw new InvalidOperationException("Cliente indisponível/em login/em segundo plano bloqueou outro cliente.");
        }
        if (!Blocks(true, false, false, true) || Blocks(false, false, false, true))
            throw new InvalidOperationException("Proteção da interface manual ativa incorreta.");
    }
}
