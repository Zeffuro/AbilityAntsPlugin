using Dalamud.Game;
using Dalamud.Plugin.Services;
using Dalamud.IoC;
using Dalamud.Plugin;

namespace AbilityAnts
{
    public class Services
    {
        [PluginService] public static IClientState ClientState { get; private set; } = null!;
        [PluginService] public static ICondition Condition { get; private set; } = null!;
        [PluginService] public static IFramework Framework { get; private set; } = null!;
        [PluginService] public static ISigScanner Scanner { get; private set; } = null!;
        [PluginService] public static IDataManager DataManager { get; private set; } = null!;
        [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
        [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;

        public static void Initialize(IDalamudPluginInterface pi)
        {
            pi.Create<Services>();
        }
    }
}
