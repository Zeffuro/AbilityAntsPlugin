using AbilityAnts;
using Dalamud.Bindings.ImGui;
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
using AbilityAntsPlugin.Windows;
using Dalamud.Interface.Windowing;
using Action = Lumina.Excel.Sheets.Action;

namespace AbilityAntsPlugin
{
    public sealed class AbilityAnts : IDalamudPlugin
    {
        public string Name => "Ability Ants Plugin";

        private const string commandName = "/pants";

        private IDalamudPluginInterface PluginInterface { get; init; }
        private Configuration Configuration { get; init; }
        private ConfigWindow ConfigWindow { get; init; }

        private readonly Hook<ActionManager.Delegates.IsActionHighlighted> _drawAntsHook;
        private unsafe ActionManager* _am;
        private IClientState ClientState => Services.ClientState;
        private ICondition Condition => Services.Condition;
        private ICommandManager CommandManager => Services.CommandManager;
        private IGameInteropProvider GameInteropProvider => Services.GameInteropProvider;
        private IObjectTable ObjectTable => Services.ObjectTable;

        private bool InCombat => Condition[ConditionFlag.InCombat];

        private readonly WindowSystem _windowSystem = new("AbilityAntsPlugin");
        private readonly Dictionary<uint, Action> _cachedActions = new();

        public unsafe AbilityAnts(
            IDalamudPluginInterface pluginInterface)
        {
            Services.Initialize(pluginInterface);
            PluginInterface = pluginInterface;


            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(PluginInterface);

            ConfigWindow = new ConfigWindow(Configuration);
            _windowSystem.AddWindow(ConfigWindow);

            CommandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Draw ants border around specific abilities. /pants to configure."
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUI;
            PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
            ClientState.Login += OnLogin;
            ClientState.Logout += OnLogout;

            if (ClientState.IsLoggedIn)
                OnLogin();

            _drawAntsHook = GameInteropProvider.HookFromAddress<ActionManager.Delegates.IsActionHighlighted>(ActionManager.MemberFunctionPointers.IsActionHighlighted, HandleAntCheck);

            CacheActions();

            Enable();
        }

        private void Enable()
        {
            _drawAntsHook.Enable();
        }

        public void Dispose()
        {
            _drawAntsHook.Dispose();

            _windowSystem.RemoveAllWindows();
            ConfigWindow.Dispose();

            CommandManager.RemoveHandler(commandName);
        }

        private void OnCommand(string command, string args)
        {
            ToggleConfigUI();
        }

        private void DrawUI() => _windowSystem.Draw();

        private unsafe void OnLogin()
        {
            try
            {
                _am = ActionManager.Instance();
            }
            catch (Exception exception)
            {
                Services.Logger.Error("Failed to get ActionManager instance. Plugin will not function correctly.");
                Services.Logger.Error(exception, "OnLogin");
            }
        }

        private unsafe void OnLogout(int _, int __)
        {
            _am = null;
        }

        public void ToggleConfigUI() => ConfigWindow.Toggle();

        private unsafe bool HandleAntCheck(ActionManager* actionManager, ActionType actionType, uint actionID)
        {
            if (_am == null || ObjectTable.LocalPlayer == null) return false;
            bool ret = _drawAntsHook.Original(actionManager, actionType, actionID);
            if (ret || actionType != ActionType.Action || Configuration.ShowOnlyInCombat && !InCombat)
                return ret;

            if (Configuration.ActiveActions.ContainsKey(actionID))
            {
                if (!_cachedActions.TryGetValue(actionID, out Action action))
                    return ret;

                bool recastActive = _am->IsRecastTimerActive(actionType, actionID);
                float timeLeft;
                float recastTime = _am->GetRecastTime(actionType, actionID);
                float recastElapsed = _am->GetRecastTimeElapsed(actionType, actionID);
                var maxCharges = ActionManager.GetMaxCharges(actionID, ObjectTable.LocalPlayer.Level);

                if (Configuration.ShowOnlyUsableActions &&
                    action.ClassJobLevel > ObjectTable.LocalPlayer.Level)
                    return false;

                if (!recastActive && maxCharges == 0)
                    return true;

                if (maxCharges > 0)
                {
                    if (!Configuration.AntOnFinalStack)
                    {
                        if (AvailableCharges(action, maxCharges) > 0 && !recastActive) return true;
                        recastTime /= maxCharges;
                    }
                }
                timeLeft = recastTime - recastElapsed;

                return timeLeft <= Configuration?.ActiveActions[actionID] / 1000;
            }
            return ret;

        }

        private unsafe int AvailableCharges(Action action, ushort maxCharges)
        {
            if (_am == null || maxCharges == 0) return 0;
            RecastDetail* timer;
            // Kinda janky, I think
            var tmp = _am->GetRecastGroup(1, action.RowId);
            if (action.CooldownGroup == 58)
                timer = _am->GetRecastGroupDetail(action.AdditionalCooldownGroup);
            else
                timer = _am->GetRecastGroupDetail((byte)tmp);
            if (timer == null) return 0;

            if (timer->IsActive) return maxCharges;
            return (int)(maxCharges * (timer->Elapsed / timer->Total));
        }

        private void CacheActions()
        {
            var whitelistedActions = ConfigWindow.JobActionWhitelist.Values.SelectMany(hashSet => hashSet).ToList();

            var actions = Services.DataManager.GetExcelSheet<Action>()!
                .Where(a =>
                    (a is { IsPvP: false, ClassJob.ValueNullable.Unknown6: > 0 } &&
                     a.IsPlayerAction &&
                     (a.ActionCategory.RowId == 4 || a.Recast100ms > 100))
                    || whitelistedActions.Contains((int)a.RowId)) // Include whitelisted actions
                .ToList();

            foreach (var action in actions)
            {
                _cachedActions.TryAdd(action.RowId, action);
            }

            var roleActions = Services.DataManager.GetExcelSheet<Action>()!
                .Where(a => a.IsRoleAction && a.ClassJobLevel != 0)
                .ToList();

            foreach (var ra in roleActions)
            {
                _cachedActions.TryAdd(ra.RowId, ra);
            }
        }
    }
}
