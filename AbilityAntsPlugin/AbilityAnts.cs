using AbilityAnts;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Action = Lumina.Excel.Sheets.Action;
using Dalamud.Logging;
using ImGuiNET;

namespace AbilityAntsPlugin
{
    public sealed class AbilityAnts : IDalamudPlugin
    {
        [return : MarshalAs(UnmanagedType.U1)]
        private delegate bool OnDrawAntsDetour(IntPtr self, int at, uint ActionID);
        public string Name => "Ability Ants Plugin";

        private const string commandName = "/pants";

        private IDalamudPluginInterface PluginInterface { get; init; }
        private Configuration Configuration { get; init; }
        private AbilityAntsUI PluginUi { get; init; }

        private Hook<OnDrawAntsDetour> DrawAntsHook;
        private unsafe ActionManager* AM;
        public IClientState ClientState => Services.ClientState;
        public ICondition Condition => Services.Condition;
        public IFramework Framework => Services.Framework;
        public ISigScanner Scanner => Services.Scanner;
        private ICommandManager CommandManager => Services.CommandManager;
        private IGameInteropProvider GameInteropProvider => Services.GameInteropProvider;

        private bool InCombat => Condition[ConditionFlag.InCombat];

        private Dictionary<uint, Action> CachedActions;

        public AbilityAnts(
            IDalamudPluginInterface pluginInterface,
            IClientState clientState,
            ICommandManager commandManager)
        {
            Services.Initialize(pluginInterface);
            this.PluginInterface = pluginInterface;


            this.Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(this.PluginInterface);

            this.PluginUi = new AbilityAntsUI(this.Configuration);

            CommandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Draw ants border around specific abilities. /pants to configure."
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            ClientState.Login += OnLogin;
            ClientState.Logout += OnLogout;

            if (ClientState.IsLoggedIn)
                OnLogin();

            AbilityAntsAddressResolver.Setup64Bit(Scanner);
            DrawAntsHook = GameInteropProvider.HookFromAddress<OnDrawAntsDetour>(AbilityAntsAddressResolver.ShouldDrawAnts, HandleAntCheck);

            CacheActions();

            Enable();
        }

        private void Enable()
        {
            DrawAntsHook.Enable();
        }

        public void Dispose()
        {
            DrawAntsHook.Dispose();
            this.PluginUi.Dispose();
            this.CommandManager.RemoveHandler(commandName);
        }

        private void OnCommand(string command, string args)
        {
            this.PluginUi.Visible = true;
        }

        private void DrawUI()
        {
            this.PluginUi.Draw();
        }

        private void DrawConfigUI()
        {
            this.PluginUi.Visible = true;
        }

        public unsafe void OnLogin()
        {
            try
            {
                AM = ActionManager.Instance();
            }
            catch (Exception)
            {
                
            }
        }

        public unsafe void OnLogout(int _, int __)
        {
            AM = null;
        }

        [return : MarshalAs(UnmanagedType.U1)]
        private unsafe bool HandleAntCheck(IntPtr self, int actionType, uint actionID)
        {
            if (AM == null || ClientState.LocalPlayer == null) return false;
            bool ret = DrawAntsHook.Original(self, actionType, actionID);
            if (ret)
                return ret;
            ActionType at = (ActionType)actionType;
            if (at != ActionType.Action)
                return ret;
            if (Configuration.ShowOnlyInCombat && !InCombat)
                return ret;
            if (Configuration.ActiveActions.ContainsKey(actionID))
            {
                if (!CachedActions.ContainsKey(actionID)) 
                    return ret;
                
                bool recastActive = AM->IsRecastTimerActive(at, actionID);
                var action = CachedActions[actionID];
                float timeLeft;
                float recastTime = AM->GetRecastTime(at, actionID);
                float recastElapsed = AM->GetRecastTimeElapsed(at, actionID);
                var maxCharges = ActionManager.GetMaxCharges((uint)actionID, ClientState.LocalPlayer.Level);

                if (Configuration.ShowOnlyUsableActions && 
                    action.ClassJobLevel > ClientState.LocalPlayer.Level)
                    return false;

                if (!recastActive && maxCharges == 0)
                    return true;

                if (maxCharges > 0)
                {                     
                    if (!Configuration.AntOnFinalStack)
                    {
                        if (AvailableCharges(action, maxCharges) > 0) return true;
                        recastTime /= maxCharges;
                    }
                }
                timeLeft = recastTime - recastElapsed;
                
                return timeLeft <= Configuration.ActiveActions[actionID] / 1000;
            }
            return ret;

        }

        private unsafe int AvailableCharges(Action action, ushort maxCharges)
        {
            if (maxCharges == 0) return 0;
            RecastDetail* timer;
            // Kinda janky, I think
            var tmp = AM->GetRecastGroup(1, action.RowId);
            if (action.CooldownGroup == 58)
                timer = AM->GetRecastGroupDetail(action.AdditionalCooldownGroup);
            else
                timer = AM->GetRecastGroupDetail((byte)tmp);
            if (timer->IsActive == 0) return maxCharges; 
            return (int)(maxCharges * (timer->Elapsed / timer->Total));
        }
        
        private void CacheActions()
        {
            CachedActions = new();
    
            var actions = Services.DataManager.GetExcelSheet<Action>()!
                .Where(a => !a.IsPvP && a.ClassJob.ValueNullable?.Unknown6 > 0 && a.IsPlayerAction && 
                            (a.ActionCategory.RowId == 4 || a.Recast100ms > 100))
                .ToList();
    
            foreach (var action in actions)
            {
                if (!CachedActions.ContainsKey(action.RowId))
                {
                    CachedActions[action.RowId] = action;
                }
            }

            var roleActions = Services.DataManager.GetExcelSheet<Action>()!
                .Where(a => a.IsRoleAction && a.ClassJobLevel != 0)
                .ToList();
    
            foreach (var ra in roleActions)
            {
                if (!CachedActions.ContainsKey(ra.RowId))
                {
                    CachedActions[ra.RowId] = ra;
                }
            }
        }
    }
}
