using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace AbilityAntsPlugin
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;
        public int PreAntTimeMs { get; set; } = 3000;
        public bool ShowOnlyInCombat { get; set; } = true;
        public bool AntOnFinalStack { get; set; } = true;
        public bool ShowOnlyUsableActions { get; set; } = false;
        public Dictionary<uint, int> ActiveActions { get; private set; }

        // the below exist just to make saving less cumbersome

        [NonSerialized]
        private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.pluginInterface!.SavePluginConfig(this);
        }

        public Configuration()
        {
            ActiveActions = new();
        }
    }
}
