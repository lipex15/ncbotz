using BotNC.App.Services;

if (args.Length != 3)
    throw new ArgumentException("Use: BotNC.ActivationSmoke <url-local> <usuario-teste> <senha-teste>.");

var testDirectory = Path.Combine(Path.GetTempPath(), "pexbot-activation-smoke-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("PEXBOT_LICENSE_URL", args[0]);
Environment.SetEnvironmentVariable("PEXBOT_LICENSE_ALLOW_UNPINNED_DEV", "1");
Environment.SetEnvironmentVariable("PEXBOT_ACTIVATION_TEST_DIR", testDirectory);
var activation = new ActivationService();
if (!activation.IsRequired || activation.HasValidActivation())
    throw new InvalidOperationException("O primeiro acesso deveria exigir ativação.");

await activation.ActivateAsync(args[1], args[2], CancellationToken.None);
if (!activation.HasValidActivation())
    throw new InvalidOperationException("A licença assinada não foi aceita no mesmo computador.");

var file = Path.Combine(testDirectory, "license.dat");
var valid = await File.ReadAllBytesAsync(file);
await File.WriteAllBytesAsync(file, [1, 2, 3, 4]);
if (activation.HasValidActivation())
    throw new InvalidOperationException("Um arquivo de licença adulterado foi aceito.");
await File.WriteAllBytesAsync(file, valid);
if (!activation.HasValidActivation())
    throw new InvalidOperationException("A licença válida deixou de funcionar offline.");

Console.WriteLine("PASS: primeira ativação, uso offline e rejeição de licença adulterada.");
