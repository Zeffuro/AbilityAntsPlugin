using AbilityAnts;
using ImGuiNET;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Action = Lumina.Excel.Sheets.Action;
using Dalamud.Interface.Textures.TextureWraps;

namespace AbilityAntsPlugin
{
    // It is good to have this be disposable in general, in case you ever need it
    // to do any cleanup
    class AbilityAntsUI : IDisposable
    {
        private Configuration Configuration;

        // this extra bool exists for ImGui, since you can't ref a property
        private bool visible = false;
        public bool Visible
        {
            get { return this.visible; }
            set { this.visible = value; }
        }

        private bool settingsVisible = false;
        public bool SettingsVisible
        {
            get { return this.settingsVisible; }
            set { this.settingsVisible = value; }
        }

        private int PreAntTimeMs;
        private int ExistingAntTimeMs = 0;

        private List<ClassJob> Jobs;
        private ClassJob? SelectedJob = null;
        private Dictionary<uint, List<Action>> JobActions;
        private List<Action> RoleActions;
        private Dictionary<uint, IDalamudTextureWrap?> LoadedIcons;

        // passing in the image here just for simplicity
        public AbilityAntsUI(Configuration configuration)
        {
            this.Configuration = configuration;

            PreAntTimeMs = Configuration.PreAntTimeMs;

            Jobs = Services.DataManager.GetExcelSheet<ClassJob>()!.Where(j => j.Role > 0 && j.ItemSoulCrystal.Value.RowId > 0).ToList();
            Jobs.Sort((lhs, rhs) => lhs.Name.ExtractText().CompareTo(rhs.Name.ExtractText()));
            JobActions = new();
            foreach(var job in Jobs)
            {
                // SMN
                if (job.RowId == 27)
                {
                    JobActions[job.RowId] = Services.DataManager.GetExcelSheet<Action>()!.
                    Where(a => !a.IsPvP && (a.ClassJob.RowId == 26 || a.ClassJob.RowId == 27) && a.IsPlayerAction && (a.ActionCategory.RowId == 4 || a.Recast100ms > 100)).ToList();
                    JobActions[job.RowId].Sort((lhs, rhs) => lhs.Name.ExtractText().CompareTo(rhs.Name.ExtractText()));
                }
                // SCH
                else if (job.RowId == 28)
                {
                    JobActions[job.RowId] = Services.DataManager.GetExcelSheet<Action>()!.
                    Where(a => !a.IsPvP && a.ClassJob.RowId == 28 && a.IsPlayerAction && (a.ActionCategory.RowId == 4 || a.Recast100ms > 100)).ToList();
                    JobActions[job.RowId].Sort((lhs, rhs) => lhs.Name.ExtractText().CompareTo(rhs.Name.ExtractText()));
                }
                else
                {
                    JobActions[job.RowId] = Services.DataManager.GetExcelSheet<Action>()!.
                        Where(a => !a.IsPvP && a.ClassJob.RowId == job.RowId  && a.IsPlayerAction && (a.ActionCategory.RowId == 4 || a.Recast100ms > 100) && a.RowId != 29581).ToList();
                    JobActions[job.RowId].Sort((lhs, rhs) => lhs.Name.ExtractText().CompareTo(rhs.Name.ExtractText()));
                }
            }
            RoleActions = Services.DataManager.GetExcelSheet<Action>()!.Where(a => a.IsRoleAction && a.ClassJobLevel != 0).ToList();
            RoleActions.Sort((lhs, rhs) => lhs.Name.ExtractText().CompareTo(rhs.Name.ExtractText()));
        }

        public void Dispose()
        {
        }

        public void Draw()
        {
            DrawMainWindow();
        }

        public void DrawMainWindow()
        {
            if (!Visible)
            {
                return;
            }

            ImGui.SetNextWindowSize(new Vector2(375, 330), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(375, 330), new Vector2(float.MaxValue, float.MaxValue));
            if (ImGui.Begin("Ability Ants Config", ref this.visible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                bool showCombat = Configuration.ShowOnlyInCombat;
                if (ImGui.Checkbox("Only show custom ants while in combat", ref showCombat))
                {
                    Configuration.ShowOnlyInCombat = showCombat;
                    Configuration.Save();
                }

                bool showUsable = Configuration.ShowOnlyUsableActions;
                if (ImGui.Checkbox("Only show custom ants for actions your character can use (job level restriction).", ref showUsable))
                {
                    Configuration.ShowOnlyUsableActions = showUsable;
                    Configuration.Save();
                }

                bool lastCharge = Configuration.AntOnFinalStack;
                if (ImGui.Checkbox("Charged abilities only get ants for the final charge", ref lastCharge))
                {
                    Configuration.AntOnFinalStack = lastCharge;
                }

                ImGui.SetNextItemWidth(75);
                if (ImGui.InputInt("##default", ref PreAntTimeMs, 0))
                {
                    // This section intentionally left blank
                }
                ImGui.SameLine();
                if (ImGui.Button("Set default pre-ant time, in ms"))
                {
                    Configuration.PreAntTimeMs = PreAntTimeMs;
                }

                ImGui.SetNextItemWidth(75);
                if (ImGui.InputInt("##existing", ref ExistingAntTimeMs, 0))
                {
                    // This section intentionally left blank
                }
                ImGui.SameLine();
                if (ImGui.Button("Set saved pre-ant times to this, in ms"))
                {
                    foreach (var (k, _) in Configuration.ActiveActions)
                        Configuration.ActiveActions[k] = ExistingAntTimeMs;
                }


                if (ImGui.BeginChild("sidebar", new(ImGui.GetContentRegionAvail().X * (float)0.25, ImGui.GetContentRegionAvail().Y)))
                {
                    if (ImGui.Selectable("Role Actions", SelectedJob == null))
                    {
                        SelectedJob = null;
                    }
                    foreach (var job in Jobs)
                    {
                        if (ImGui.Selectable(job.Abbreviation.ExtractText()))
                        {
                            SelectedJob = job;
                        }
                    }
                    ImGui.EndChild();
                }

                ImGui.SameLine();
                
                

                if (ImGui.BeginChild("testo2", new(-1, -1)))
                {
                    List<Action> actions;
                    if (SelectedJob != null)
                    {
                        ImGui.PushID(SelectedJob.Value.Abbreviation.ExtractText());
                        actions = JobActions[SelectedJob.Value.RowId];
                    }
                    else
                    {
                        ImGui.PushID("job actions");
                        actions = RoleActions;
                    }
                    DrawActions(actions);
                    ImGui.PopID();
                    ImGui.EndChild();
                }

                ImGui.Spacing();

                ImGui.Text("Have a goat:");
                ImGui.Indent(55);
                ImGui.Unindent(55);
            }
            ImGui.End();
            Configuration.Save();
        }
        void DrawActions(List<Action> actions)
        {
            foreach (var action in actions)
            {
                ImGui.PushID(action.Name.ExtractText());
                bool active = Configuration.ActiveActions.ContainsKey(action.RowId);
                DrawIcon(action);
                ImGui.SameLine();
                ImGui.Text(action.Name.ExtractText());
                ImGui.SameLine();
                if (ImGui.Checkbox("Active", ref active))
                {
                    if (active)
                    {
                        Configuration.ActiveActions.Add(action.RowId, Configuration.PreAntTimeMs);
                    }
                    else
                    {
                        Configuration.ActiveActions.Remove(action.RowId);
                    }
                }
                if (Configuration.ActiveActions.ContainsKey(action.RowId))
                {
                    ImGui.SameLine();
                    int preAntTime = Configuration.ActiveActions[action.RowId];
                    ImGui.SetNextItemWidth(75);
                    if (ImGui.InputInt("ms pre-ant", ref preAntTime, 0))
                    {
                        Configuration.ActiveActions[action.RowId] = preAntTime;
                    }
                }
                ImGui.PopID();
            }
        }

        void DrawIcon(Action action)
        {
            GameIconLookup lookup = new GameIconLookup(action.Icon);
            IDalamudTextureWrap? tw = Services.TextureProvider.GetFromGameIcon(lookup).GetWrapOrDefault();
            if(tw != null) ImGui.Image(tw.ImGuiHandle, tw.Size);
        }
    }

}
