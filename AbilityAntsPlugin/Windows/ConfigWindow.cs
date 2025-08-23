using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;
using AbilityAnts;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Lumina.Excel.Sheets;
using Action = Lumina.Excel.Sheets.Action;

namespace AbilityAntsPlugin.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration _configuration;
    private readonly FrozenDictionary<uint, Action> _actions;
    private readonly FrozenDictionary<uint, string> _actionNames;
    private readonly FrozenDictionary<uint, string> _jobAbbreviations;
    private readonly FrozenDictionary<uint, List<uint>> _jobActionIds;
    private readonly List<uint> _roleActionIds;
    private readonly List<ClassJob> _jobs;
    private ClassJob? _selectedJob = null;

    private int _preAntTimeMs;
    private int _existingAntTimeMs = 0;

    public readonly Dictionary<uint, HashSet<int>> JobActionWhitelist = new()
    {
        { 33, [7444, 7445, 37018, 37023, 37024, 37025, 37026, 37027, 37028] },
    };

    public ConfigWindow(Configuration configuration)
        : base("Ability Ants Config", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        Size = new Vector2(375, 330);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        _configuration = configuration;

        _preAntTimeMs = _configuration.PreAntTimeMs;

        // Cache jobs
        _jobs = Services.DataManager.GetExcelSheet<ClassJob>()!
            .Where(j => j.Role > 0 && j.ItemSoulCrystal.Value.RowId > 0)
            .OrderBy(j => j.Name.ToString())
            .ToList();

        // Cache actions
        var allActions = Services.DataManager.GetExcelSheet<Action>()!.ToList();
        _actions = allActions.ToFrozenDictionary(a => a.RowId, a => a);

        // Cache action names
        _actionNames = allActions.ToFrozenDictionary(a => a.RowId, a => a.Name.ToString());

        // Cache job abbreviations
        _jobAbbreviations = _jobs.ToFrozenDictionary(j => j.RowId, j => j.Abbreviation.ToString());

        // Build job action ID lists
        var jobActionIdsBuilder = new Dictionary<uint, List<uint>>();
        foreach (var job in _jobs)
        {
            var ids = allActions
                .Where(a => !a.IsPvP && (a.ClassJob.RowId == job.RowId || a.ClassJob.RowId == job.ClassJobParent.RowId)
                            && a.IsPlayerAction && (a.ActionCategory.RowId == 4 || a.Recast100ms > 100) && a.RowId != 29581)
                .Select(a => a.RowId)
                .ToList();

            // Add whitelisted actions
            if (JobActionWhitelist.TryGetValue(job.RowId, out var whitelist))
            {
                foreach (var actionId in whitelist)
                    if (!ids.Contains((uint)actionId) && _actions.ContainsKey((uint)actionId))
                        ids.Add((uint)actionId);
            }

            // Sort by cached name
            ids.Sort((lhs, rhs) => string.Compare(_actionNames[lhs], _actionNames[rhs], StringComparison.Ordinal));
            jobActionIdsBuilder[job.RowId] = ids;
        }
        _jobActionIds = jobActionIdsBuilder.ToFrozenDictionary();

        // Cache role actions
        _roleActionIds = allActions
            .Where(a => a.IsRoleAction && a.ClassJobLevel != 0)
            .Select(a => a.RowId)
            .OrderBy(id => _actionNames[id])
            .ToList();
    }

    public override void Draw()
    {
        if (!IsOpen) return;

        bool showCombat = _configuration.ShowOnlyInCombat;
        if (ImGui.Checkbox("Only show custom ants while in combat", ref showCombat))
        {
            _configuration.ShowOnlyInCombat = showCombat;
            _configuration.Save();
        }

        bool showUsable = _configuration.ShowOnlyUsableActions;
        if (ImGui.Checkbox("Only show custom ants for actions your character can use (job level restriction).", ref showUsable))
        {
            _configuration.ShowOnlyUsableActions = showUsable;
            _configuration.Save();
        }

        bool lastCharge = _configuration.AntOnFinalStack;
        if (ImGui.Checkbox("Charged abilities only get ants for the final charge", ref lastCharge))
        {
            _configuration.AntOnFinalStack = lastCharge;
        }

        ImGui.SetNextItemWidth(75);
        if (ImGui.InputInt("##default", ref _preAntTimeMs, 0))
        {
            // This section intentionally left blank
        }
        ImGui.SameLine();
        if (ImGui.Button("Set default pre-ant time, in ms"))
        {
            _configuration.PreAntTimeMs = _preAntTimeMs;
        }

        ImGui.SetNextItemWidth(75);
        if (ImGui.InputInt("##existing", ref _existingAntTimeMs, 0))
        {
            // This section intentionally left blank
        }
        ImGui.SameLine();
        if (ImGui.Button("Set saved pre-ant times to this, in ms"))
        {
            foreach (var (k, _) in _configuration.ActiveActions)
                _configuration.ActiveActions[k] = _existingAntTimeMs;
        }


        using (ImRaii.Child("sidebar", new(ImGui.GetContentRegionAvail().X * 0.25f, ImGui.GetContentRegionAvail().Y)))
        {
            if (ImGui.Selectable("Role Actions", _selectedJob == null))
                _selectedJob = null;
            foreach (var job in _jobs)
            {
                if (ImGui.Selectable(_jobAbbreviations[job.RowId], _selectedJob?.RowId == job.RowId))
                    _selectedJob = job;
            }
        }

        ImGui.SameLine();

        using (ImRaii.Child("actions", new(-1, -1)))
        {
            var actionSheet = Services.DataManager.GetExcelSheet<Action>()!;
            List<Action> actions;

            if (_selectedJob != null)
            {
                var job = _selectedJob.Value;
                actions = actionSheet
                    .Where(a => !a.IsPvP && (a.ClassJob.RowId == job.RowId || a.ClassJob.RowId == job.ClassJobParent.RowId)
                                         && a.IsPlayerAction && (a.ActionCategory.RowId == 4 || a.Recast100ms > 100) && a.RowId != 29581)
                    .ToList();

                // Add whitelisted actions
                if (JobActionWhitelist.TryGetValue(job.RowId, out var whitelist))
                {
                    foreach (var actionId in whitelist)
                    {
                        if (actionSheet.TryGetRow((uint)actionId, out Action extra) && actions.All(a => a.RowId != (uint)actionId))
                            actions.Add(extra);
                    }
                }

                actions = actions.OrderBy(a => a.Name).ToList();
            }
            else
            {
                actions = actionSheet
                    .Where(a => a.IsRoleAction && a.ClassJobLevel != 0)
                    .OrderBy(a => a.Name.ToString())
                    .ToList();
            }

            DrawActions(actions);
        }

        ImGui.Spacing();

        ImGui.Text("Have a goat:");
        ImGui.Indent(55);
        ImGui.Unindent(55);

        _configuration.Save();
    }

    private void DrawActions(List<Action> actions)
    {
        foreach (var action in actions)
        {
            var name = action.Name.ToString();

            using (ImRaii.PushId(name))
            {
                bool active = _configuration.ActiveActions.ContainsKey(action.RowId);
                DrawIcon(action);
                ImGui.SameLine();
                ImGui.Text(name);
                ImGui.SameLine();
                if (ImGui.Checkbox("Active", ref active))
                {
                    if (active)
                        _configuration.ActiveActions[action.RowId] = _configuration.PreAntTimeMs;
                    else
                        _configuration.ActiveActions.Remove(action.RowId);
                }
                if (_configuration.ActiveActions.ContainsKey(action.RowId))
                {
                    ImGui.SameLine();
                    int preAntTime = _configuration.ActiveActions[action.RowId];
                    ImGui.SetNextItemWidth(75);
                    if (ImGui.InputInt("ms pre-ant", ref preAntTime, 0))
                        _configuration.ActiveActions[action.RowId] = preAntTime;
                }
            }
        }
    }

    public void Dispose()
    {
    }

    private void DrawIcon(Action action)
    {
        if (Services.TextureProvider.TryGetFromGameIcon((uint)action.Icon, out var texture) && texture.TryGetWrap(out var tw, out _))
            ImGui.Image(tw.Handle, tw.Size);
    }
}