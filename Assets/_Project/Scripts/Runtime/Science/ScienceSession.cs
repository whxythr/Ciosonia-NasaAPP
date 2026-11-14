using System.Collections.Generic;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Science;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Runs the vehicle's science instruments every frame (Core: <see cref="OrbitalScanner"/>) and keeps the science maps in the
    /// player profile. Lives outside the flight stack, so mapping survives sphere-of-influence handoffs and body switches; it
    /// always follows <see cref="FlightBody.Current"/>.
    ///
    /// Each frame covers the simulated interval since the previous frame: under time warp the scanner re-samples the ground track
    /// on the conic through the previous state, so nothing is skipped. Instruments need electric power (generators covering the
    /// systems, or charge left in the batteries) and go offline with the rest of the payload when hazards degrade it.
    /// Landed instruments that measure in situ (thermometer, barometer, weather kit, camera) record the cell under the vehicle.
    /// </summary>
    [DefaultExecutionOrder(160)]
    public sealed class ScienceSession : MonoBehaviour
    {
        /// <summary>Real seconds between copying the maps into the profile (JSON) and between profile writes.</summary>
        const float SyncInterval = 5f, SaveInterval = 60f;
        /// <summary>Longest simulated gap bridged by ground-track interpolation [s]; longer gaps restart the track.</summary>
        const double MaxBridgedInterval = 30.0 * 86400.0;

        PlayerSession session;
        StarSystemRuntime system;
        readonly OrbitalScanner scanner = new OrbitalScanner { MeasureBudget = 500 };
        readonly Dictionary<string, KeyValuePair<PlanetModel, PlanetScienceModel>> models = new Dictionary<string, KeyValuePair<PlanetModel, PlanetScienceModel>>();

        FlightBody previousBody;
        string previousBodyId;
        Vector3d previousR, previousV;
        double previousT;
        bool hasPrevious;
        float lastSync, lastSave;
        bool unsaved;

        /// <summary>All science maps (the window reads them).</summary>
        public ScienceMapStore Store { get; private set; } = new ScienceMapStore();
        /// <summary>The last update of the vehicle's instruments (null before the first one).</summary>
        public ScanReport LastReport { get; private set; }
        /// <summary>Body the vehicle is mapping now ("" when there is no vehicle).</summary>
        public string ActiveBodyId { get; private set; } = "";
        /// <summary>Instruments of the vehicle in the order of <see cref="ScanReport.Status"/>.</summary>
        public IReadOnlyList<ScannerSpec> ActiveScanners { get; private set; } = new List<ScannerSpec>();
        /// <summary>Player switches per layer (all on by default).</summary>
        public readonly bool[] LayerOn = { true, true, true };
        /// <summary>The instruments have electric power.</summary>
        public bool Powered { get; private set; } = true;

        public void Initialize(PlayerSession playerSession, StarSystemRuntime starSystem)
        {
            session = playerSession;
            system = starSystem;
            Store = session != null && session.Profile != null ? ScienceArchiveCodec.Read(session.Profile.science) : new ScienceMapStore();
            lastSync = lastSave = Time.unscaledTime;
        }

        /// <summary>The planet model the instruments read (cached per body; rebuilt when the body's physics data changes).</summary>
        public PlanetScienceModel ModelFor(string bodyId, PlanetModel model)
        {
            if (model == null) return null;
            if (models.TryGetValue(bodyId, out var cached) && ReferenceEquals(cached.Key, model)) return cached.Value;
            var science = new PlanetScienceModel(bodyId, model, SurfaceClimate.ReferenceFor(model.Properties.atmosphere, EquilibriumTemperature(bodyId)));
            models[bodyId] = new KeyValuePair<PlanetModel, PlanetScienceModel>(model, science);
            return science;
        }

        /// <summary>Radiative equilibrium temperature of a body at its current distance from the star (0 when unknown).</summary>
        double EquilibriumTemperature(string bodyId)
        {
            if (system == null || system.Star == null) return 0.0;
            int i = system.IndexOf(bodyId);
            if (i < 0 || system.Bodies[i].Runtime == null || system.Bodies[i].Runtime.Model == null) return 0.0;
            double d = (system.Bodies[i].Runtime.Model.Position - system.StarPosition).Magnitude;
            return SurfaceClimate.EquilibriumTemperature(system.Star.surfaceTemperature, system.Star.radius, d, SurfaceClimate.DefaultBondAlbedo);
        }

        static bool HasPower(FlightBody body)
        {
            var rocket = body.Rocket;
            if (rocket == null) return true; // the bare test probe has no electric model
            PowerBalance e = rocket.Electrics;
            return e.SystemsCovered || e.BatteryEnergy > 0.0;
        }

        void Update()
        {
            Step();
            float now = Time.unscaledTime;
            if (Store.Dirty && now - lastSync > SyncInterval) Sync();
            if (unsaved && now - lastSave > SaveInterval) SaveNow();
        }

        void Step()
        {
            FlightBody body = FlightBody.Current;
            if (body == null || body.Simulation == null || body.Planet == null || body.Planet.Definition == null)
            {
                hasPrevious = false;
                ActiveBodyId = "";
                return;
            }
            var sim = body.Simulation;
            PlanetModel model = sim.Planet;
            string id = body.Planet.Definition.planetId;
            if (!ReferenceEquals(body, previousBody) || id != previousBodyId) hasPrevious = false;
            previousBody = body;
            previousBodyId = id;
            ActiveBodyId = id;

            VehicleCapabilities caps = body.Capabilities; // offline (empty) when hazards degraded the payload
            ActiveScanners = caps.Scanners;
            bool inSitu = sim.IsLanded && (caps.CanMeasure(ScienceMeasurement.Temperature) || caps.CanMeasure(ScienceMeasurement.Pressure) ||
                                           caps.CanMeasure(ScienceMeasurement.Imaging));
            Powered = HasPower(body);

            Vector3d r = sim.State.Position - model.Position, v = sim.State.Velocity;
            double t = sim.State.Time;
            if (hasPrevious && (t < previousT || t - previousT > MaxBridgedInterval || Teleported(model, r, t))) hasPrevious = false;
            if (!hasPrevious)
            {
                previousR = r;
                previousV = v;
                previousT = t;
                hasPrevious = true;
            }

            bool haveMap = Store.TryGet(id, out ScienceBodyMap map);
            if (!haveMap && caps.Scanners.Count == 0 && !inSitu)
            {
                LastReport = null;
                Remember(r, v, t);
                return;
            }
            if (!haveMap) map = Store.GetOrCreate(id, body.Planet.Definition.displayName);
            map.DisplayName = body.Planet.Definition.displayName;
            map.Radius = model.Radius;
            map.Airless = model.Atmosphere is VacuumAtmosphere;
            PlanetScienceModel env = ModelFor(id, model);

            bool working = Powered && !body.Destroyed;
            LastReport = scanner.Scan(Store, map, env, caps.Scanners, previousR, previousV, previousT, r, t, working, LayerOn);

            if (working && sim.IsLanded)
            {
                Vector3d spot = sim.LandedFixedPosition;
                if (caps.CanMeasure(ScienceMeasurement.Temperature) && LayerOn[(int)ScienceLayer.Temperature])
                    scanner.MeasureInSitu(Store, map, env, ScienceLayer.Temperature, spot);
                if (caps.CanMeasure(ScienceMeasurement.Pressure) && LayerOn[(int)ScienceLayer.Pressure])
                    scanner.MeasureInSitu(Store, map, env, ScienceLayer.Pressure, spot);
                if (caps.CanMeasure(ScienceMeasurement.Imaging) && LayerOn[(int)ScienceLayer.Terrain])
                    scanner.MeasureInSitu(Store, map, env, ScienceLayer.Terrain, spot);
            }
            Remember(r, v, t);
        }

        void Remember(Vector3d r, Vector3d v, double t)
        {
            previousR = r;
            previousV = v;
            previousT = t;
        }

        /// <summary>
        /// The state jumped (test tools, a new rocket on the pad): the coast from the previous state does not reach the new one, so the
        /// ground track must not be interpolated across the jump.
        /// </summary>
        bool Teleported(PlanetModel model, Vector3d r, double t)
        {
            double dt = t - previousT;
            if (dt <= 0.0) return (r - previousR).Magnitude > 1000.0;
            if (previousR.Magnitude < model.Radius * 0.5 || !previousV.IsFinite) return true;
            ConicPropagator.Propagate(model.GravitationalParameter, previousR, previousV, dt, out Vector3d predicted, out _);
            double tolerance = 0.02 * r.Magnitude + 20000.0 + 50.0 * dt; // thrust and drag within one frame stay well inside
            return !predicted.IsFinite || (predicted - r).Magnitude > tolerance;
        }

        /// <summary>Copies the maps into the profile (the next profile save writes them).</summary>
        void Sync()
        {
            lastSync = Time.unscaledTime;
            if (session == null || session.Profile == null) return;
            session.Profile.science = ScienceArchiveCodec.Write(Store);
            Store.Dirty = false;
            unsaved = true;
        }

        void SaveNow()
        {
            lastSave = Time.unscaledTime;
            unsaved = false;
            if (session != null) session.Save();
        }

        /// <summary>Writes the maps to disk now (leaving the scene, quitting).</summary>
        public void Flush()
        {
            if (Store.Dirty) Sync();
            if (unsaved) SaveNow();
        }

        void OnApplicationQuit() => Flush();

        void OnDestroy()
        {
            if (session != null) Flush();
        }
    }
}
