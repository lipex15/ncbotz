namespace BotNC.App;

internal static class AppIdentity
{
#if TEST_CHANNEL
    public static bool IsTesting => true;
    public const string DisplayName = "PEXBOT Teste";
    public const string DataDirectoryName = "PEXBOT-Teste";
    public const string Repository = "lipex15/ncbotz-testing";
    public const string InstallerBaseName = "PEXBOT-Teste-Setup-v";
    public const string MutexName = "PEXBOT.ByLIPEX.Test.AppRunning";
#else
    public static bool IsTesting => false;
    public const string DisplayName = "PEXBOT";
    public const string DataDirectoryName = "PEXBOT";
    public const string Repository = "lipex15/ncbotz";
    public const string InstallerBaseName = "PEXBOT-Setup-v";
    public const string MutexName = "PEXBOT.ByLIPEX.AppRunning";
#endif
}
