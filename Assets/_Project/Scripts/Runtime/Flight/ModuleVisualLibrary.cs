using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Which 3D model represents each rocket module — in the hangar and in flight. Modules without a model (or without an entry)
    /// get a procedural placeholder of the right size. The asset lives at Resources/SpaceSim/ModuleVisualLibrary
    /// (SpaceSim ▸ Module Models Library creates it with one entry per catalog module); drag a prefab or an imported model
    /// into <see cref="Entry.model"/> and it appears on the rocket in its slot. See Documentation/HANGAR.md.
    /// Purely visual: physics never reads this asset.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceSim/Module Models Library", fileName = "ModuleVisualLibrary")]
    public sealed class ModuleVisualLibrary : ScriptableObject
    {
        public const string ResourcePath = "SpaceSim/ModuleVisualLibrary";

        public enum FitMode
        {
            /// <summary>Uniform scale so the model fits inside its slot (keeps proportions). Engines and instruments.</summary>
            FitInside,
            /// <summary>Scaled on each axis to fill the slot exactly. Tanks and shells, whose slot follows the design.</summary>
            Stretch,
            /// <summary>The model's own size (metres) × <see cref="Entry.scale"/>, bottom centre on the slot's bottom centre.</summary>
            AsIs
        }

        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Module id from ModuleCatalog, e.g. eng-sokol-1.")]
            public string moduleId = "";
            [Tooltip("The module's 3D model (prefab or imported FBX/OBJ). Empty → procedural placeholder.")]
            public GameObject model;
            [Tooltip("How the model is sized to its slot (slot = diameter × height computed from the design).")]
            public FitMode fit = FitMode.FitInside;
            [Tooltip("Rotation [deg] applied before fitting — fixes models exported with another up axis (e.g. X = -90 for Z-up).")]
            public Vector3 rotation;
            [Tooltip("Shift [m] after fitting, in the slot frame: +Y up the rocket axis, +Z away from the axis (instruments).")]
            public Vector3 offset;
            [Tooltip("Extra uniform scale after fitting.")]
            public float scale = 1f;
        }

        [Tooltip("One entry per module. Model conventions: +Y = up the rocket; engines — mount on top, nozzle down; " +
                 "instruments — back (−Z) against the hull, face (+Z) outward. Stage-side sets (fins, legs, RCS) — model ONE piece: " +
                 "root / hinge on the hull side (−Z), it is repeated around the stage; a leg is modelled stowed, pointing up from its " +
                 "hinge at the bottom. Wheels — axle along X (the model is centred on the wheel centre). Parachutes — the container " +
                 "only, the canopy stays procedural.")]
        public List<Entry> entries = new List<Entry>();

        [Tooltip("Optional model of the hangar building (replaces the procedural walls; lights and the bay stay). " +
                 "Pivot = floor centre, metres, +Z = towards the door and the launch pad.")]
        public GameObject hangarModel;

        /// <summary>
        /// How a new entry fits its model by default: stretched for parts whose slot follows the design (tanks, shells, cargo bays,
        /// heat shields), kept in proportion for everything else.
        /// </summary>
        public static FitMode DefaultFit(SpaceSim.Vehicles.ModuleDefinition m)
        {
            if (m == null) return FitMode.FitInside;
            bool stretch = m.IsTank || m.IsWrap || m.category == SpaceSim.Vehicles.ModuleCategory.Cargo ||
                           m.auxiliaryKind == SpaceSim.Vehicles.AuxiliaryKind.HeatShield;
            return stretch ? FitMode.Stretch : FitMode.FitInside;
        }

        /// <summary>Entry of a module that has a model assigned (null otherwise).</summary>
        public Entry Find(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId) || entries == null) return null;
            foreach (Entry e in entries)
                if (e != null && e.model != null && e.moduleId == moduleId) return e;
            return null;
        }

        static ModuleVisualLibrary active;
        static bool loaded;

        /// <summary>The project's library from Resources (null when there is none — everything is procedural then).</summary>
        public static ModuleVisualLibrary Active
        {
            get
            {
                if (!loaded)
                {
                    active = Resources.Load<ModuleVisualLibrary>(ResourcePath);
                    loaded = true;
                }
                return active;
            }
        }

        /// <summary>Tests and tools: use another library (null → none) until <see cref="ResetActive"/>.</summary>
        public static void SetActive(ModuleVisualLibrary library)
        {
            active = library;
            loaded = true;
        }

        public static void ResetActive()
        {
            active = null;
            loaded = false;
        }
    }
}
