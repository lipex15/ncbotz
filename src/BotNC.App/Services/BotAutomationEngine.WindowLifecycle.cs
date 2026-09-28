using BotNC.App.Models;
namespace BotNC.App.Services;
public sealed partial class BotAutomationEngine
{
    public event Action<string, GameWindowTarget>? WindowTargetChanged;
}
