using System.Collections.Generic;
using SpaceSim.Localization;
using SpaceSim.Physics;
using SpaceSim.Rover;
using SpaceSim.Units;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The vehicle's lamps: a Unity spot light on each <see cref="RocketModel.LightAnchors"/> mount (+Z of the anchor is the beam),
    /// switched with a key. Flux and beam come from the part data through <see cref="LampPhotometry"/>: I = Φ/Ω [cd], drawn in
    /// the scene's light units for a night-adapted eye (<see cref="AdaptedLux"/>). At most <see cref="MaxLamps"/> real-time
    /// lights (URP's per-object additional-light budget); the brightest ones are kept. Purely visual: physics never reads it.
    /// Their power draw is already counted in the systems consumption of the design.
    /// </summary>
    [DefaultExecutionOrder(114)]
    public sealed class VehicleLights : MonoBehaviour
    {
        /// <summary>Real-time spot lights per vehicle (URP forward renders 4 additional lights per object by default).</summary>
        public const int MaxLamps = 4;
        /// <summary>
        /// Illuminance [lx] drawn as light intensity 1 (the direct star at noon is ≈ 1.4): the eye adapted to a lit room at night.
        /// Physically the lamps are a thousand times weaker than daylight; without adaptation they would be invisible.
        /// </summary>
        public const float AdaptedLux = 250f;
        /// <summary>The beam is drawn up to where the ground gets this much light [lx].</summary>
        const double EdgeLux = 0.5;
        const float MaxRange = 300f;

        FlightBody body;
        FlightStateController state;
        RocketModel boundModel;
        readonly List<Light> lamps = new List<Light>();
        readonly List<GameObject> lampObjects = new List<GameObject>();

        public bool On { get; private set; }
        /// <summary>Real-time lights currently built (≤ <see cref="MaxLamps"/>).</summary>
        public int LampCount => lamps.Count;
        /// <summary>Lamp mounts on the model (may exceed <see cref="LampCount"/>).</summary>
        public int AnchorCount => boundModel != null ? boundModel.LightAnchors.Count : 0;
        /// <summary>Luminous flux of all the lamps on board [lm].</summary>
        public double TotalFlux => body != null ? body.DesignCapabilities.LightOutput : 0.0;

        public void Initialize(FlightBody flightBody, FlightStateController flightState)
        {
            body = flightBody;
            state = flightState;
        }

        void OnDestroy() => Clear();

        /// <summary>4: lamps on/off; returns the message shown to the player.</summary>
        public string Toggle() => SetOn(!On);

        public string SetOn(bool on)
        {
            if (body == null) return "";
            if (TotalFlux <= 0.0)
            {
                On = false;
                string none = Loc.T("Фонарей нет: поставьте ФН-1 или ПЖ-20 в ангаре (вспомогательное оборудование).");
                state?.Notify(none);
                return none;
            }
            On = on;
            Apply();
            string msg = On
                ? Loc.T("Фонари включены: ") + UnitFormatter.Number(TotalFlux, 0) + Loc.T(" лм") +
                  (AnchorCount == 0 ? Loc.T(" (на модели нет креплений фонарей — свет не виден)") : "")
                : Loc.T("Фонари выключены.");
            state?.Notify(msg);
            return msg;
        }

        void Update()
        {
            if (body == null) return;
            if (!ReferenceEquals(body.Model, boundModel)) Rebuild();
            if (!FlightInput.IsTyping && FlightInput.GetKeyDown(InputKey.Alpha4)) Toggle();
            Apply();
        }

        void Clear()
        {
            foreach (GameObject g in lampObjects)
                if (g != null) Destroy(g);
            lampObjects.Clear();
            lamps.Clear();
        }

        /// <summary>One spot light per mount, brightest first, from the flux of the part the mount belongs to.</summary>
        void Rebuild()
        {
            Clear();
            boundModel = body.Model;
            if (boundModel == null || boundModel.LightAnchors.Count == 0) return;
            double shared = TotalFlux / boundModel.LightAnchors.Count;
            var mounts = new List<(Transform anchor, double flux)>();
            foreach (Transform a in boundModel.LightAnchors)
            {
                if (a == null) continue;
                ModuleDefinition m = ModuleOf(boundModel, a);
                double flux = m != null && m.lightOutput > 0.0 ? m.lightOutput : shared;
                if (flux > 0.0) mounts.Add((a, flux));
            }
            mounts.Sort((x, y) => y.flux.CompareTo(x.flux));
            for (int i = 0; i < mounts.Count && i < MaxLamps; i++)
            {
                double beam = LampPhotometry.BeamAngleDegrees(mounts[i].flux);
                double candela = LampPhotometry.Intensity(mounts[i].flux, beam);
                var go = new GameObject("Lamp");
                go.transform.SetParent(mounts[i].anchor, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                Light l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = (float)beam;
                l.innerSpotAngle = (float)beam * 0.55f;
                l.intensity = (float)(candela / AdaptedLux);
                l.range = Mathf.Min(MaxRange, (float)LampPhotometry.Reach(candela, EdgeLux));
                l.color = new Color(1f, 0.95f, 0.88f); // white LED, slightly warm
                l.shadows = LightShadows.None;
                l.enabled = false;
                lamps.Add(l);
                lampObjects.Add(go);
            }
        }

        /// <summary>The catalog part a mount sits on (its slot is an ancestor of the anchor).</summary>
        internal static ModuleDefinition ModuleOf(RocketModel model, Transform anchor)
        {
            for (Transform t = anchor; t != null; t = t.parent)
                foreach (RocketModel.Part p in model.Parts)
                    if (p.Transform == t && p.Placement != null && p.Placement.IsModule) return ModuleCatalog.Find(p.Placement.ModuleId);
            return null;
        }

        void Apply()
        {
            // No generators left and the batteries empty: the lamps go dark.
            float supply = 1f;
            var r = body.Rocket;
            if (r != null) supply = (float)RoverKinematics.SupplyFraction(r.Electrics);
            bool lit = On && !body.Destroyed && supply > 0f;
            for (int i = 0; i < lamps.Count; i++)
            {
                Light l = lamps[i];
                if (l == null) continue;
                l.enabled = lit;
            }
        }
    }
}
