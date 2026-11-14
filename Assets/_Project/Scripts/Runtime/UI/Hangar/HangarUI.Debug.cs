using System;
using System.Collections.Generic;
using System.Text;
using SpaceSim.Vehicles;

namespace SpaceSim.Runtime
{
    // Test hooks for the editor bridge and tests: browse the catalog, show a part's card and handle projects without the mouse.
    public sealed partial class HangarUI
    {
        /// <summary>A module id drawn as hovered (its tile highlighted and its card shown) while the mouse is elsewhere; null — off.</summary>
        public string DebugHover { get; set; }

        /// <summary>
        /// cat N | sub N | search TEXT | sort name|mass|price|size|catalog | hover ID|off | projects | closeprojects | save | open NAME |
        /// new | default | remove ROLE STAGE INDEX | state — returns the catalog state and the first shown tiles.
        /// </summary>
        public string DebugCommand(string[] a)
        {
            string sub = a.Length > 0 ? a[0].ToLowerInvariant() : "state";
            string arg = a.Length > 1 ? string.Join(" ", a, 1, a.Length - 1) : "";
            switch (sub)
            {
                case "cat":
                {
                    int i = int.Parse(arg);
                    if (i < 0 || i >= Categories.Length) return "ERROR category 0.." + (Categories.Length - 1);
                    search = "";
                    SelectCategory(Categories[i]);
                    break;
                }
                case "sub":
                {
                    IReadOnlyList<ModuleSubgroup> groups = ModuleCatalog.SubgroupsOf(category);
                    int i = int.Parse(arg);
                    subgroup = i < 0 || groups == null || i >= groups.Count ? ModuleSubgroup.None : groups[i];
                    gridScroll = UnityEngine.Vector2.zero;
                    break;
                }
                case "search": search = arg; gridScroll = UnityEngine.Vector2.zero; break;
                case "sort":
                {
                    SortKey k = (SortKey)Enum.Parse(typeof(SortKey), a[1], true);
                    if (sortKey == k) sortDescending = !sortDescending;
                    else
                    {
                        sortKey = k;
                        sortDescending = false;
                    }
                    break;
                }
                case "hover": DebugHover = arg == "off" || arg.Length == 0 ? null : arg; break;
                case "projects": projectsOpen = true; deleteArmed = null; break;
                case "closeprojects": projectsOpen = false; break;
                case "save": SaveProject(); break;
                case "open": OpenProject(arg); Refresh(); break;
                case "new": NewProject(); Refresh(); break;
                case "default": ResetToDefault(); Refresh(); break;
                case "remove":
                {
                    if (a.Length < 4) return "ERROR remove ROLE STAGE INDEX";
                    var role = (PartRole)Enum.Parse(typeof(PartRole), a[1], true);
                    var p = new PartPlacement { Role = role, Stage = int.Parse(a[2]), Index = int.Parse(a[3]) };
                    // The module id is only needed for payload modules.
                    if (p.Stage == -1 && design != null && p.Index >= 0 && p.Index < design.payloadModules.Count) p.ModuleId = design.payloadModules[p.Index];
                    if (!CanRemove(p)) return "ERROR cannot remove " + role + " " + p.Stage + " " + p.Index;
                    RemoveSelectedPart(p);
                    Refresh();
                    break;
                }
                case "state": break;
                default: return "ERROR unknown hangar ui command " + sub;
            }
            List<ModuleDefinition> list = ShownModules();
            var sb = new StringBuilder();
            sb.Append("category=").Append(category).Append(" subgroup=").Append(subgroup).Append(" search='").Append(search).Append("' sort=")
              .Append(sortKey).Append(sortDescending ? " desc" : " asc").Append(" shown=").Append(list.Count).Append(" projectsOpen=").Append(projectsOpen)
              .Append(" status='").Append(status).Append("' tiles:");
            for (int i = 0; i < Math.Min(12, list.Count); i++) sb.Append(' ').Append(list[i].id);
            return sb.ToString();
        }
    }
}
