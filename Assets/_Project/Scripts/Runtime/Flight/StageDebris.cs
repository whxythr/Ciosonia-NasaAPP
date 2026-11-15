using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// A spent stage after separation: a copy of its model that keeps flying on its own ballistic trajectory — gravity of the
    /// vehicle's frame and air drag, no thrust — tumbling slowly, until it hits the ground or falls far behind. On impact it
    /// lies where it fell for a while. Purely a consequence of the flight: it never acts on the vehicle or the simulation.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class StageDebris : MonoBehaviour
    {
        /// <summary>Removed once this far from the vehicle [m].</summary>
        public const double MaxDistance = 150000.0;
        /// <summary>How long the wreck lies on the ground before it is removed [s, real time].</summary>
        const float LieOnGround = 45f;
        /// <summary>Longest integration step [s] and the most steps per frame (at high warp the steps grow instead).</summary>
        const double MaxStep = 0.05;
        const int MaxStepsPerFrame = 400;
        const double DragCoefficient = 1.0; // a tumbling cylinder, mostly broadside

        FreeFlightSimulation sim;
        SimulationController controller;
        FloatingOrigin origin;
        FlightBody vehicle;
        Vector3d position, velocity;
        double time, mass, area, halfWidth;
        Quaternion attitude;
        Vector3 spinAxis;
        float spinRate; // [deg/s] of simulated time
        bool landed;
        Vector3d landedFixed;
        float landedAt;

        /// <summary>
        /// Starts the debris from a copy of <paramref name="stageVisual"/> exactly where it is drawn now. <paramref name="v"/> is its
        /// simulation-frame velocity at <paramref name="t"/> (render time) with the separation push already added. The copy turns
        /// and falls about its own middle.
        /// </summary>
        public static StageDebris Spawn(GameObject stageVisual, FlightBody vehicle, FreeFlightSimulation sim,
            SimulationController controller, FloatingOrigin origin, Vector3d v, double t, double mass, double radius, int seed)
        {
            Quaternion rotation = stageVisual.transform.rotation;
            Vector3 middle = stageVisual.transform.position;
            Renderer[] renderers = stageVisual.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                middle = b.center;
            }

            var go = new GameObject("SpentStage · " + stageVisual.name);
            go.transform.SetPositionAndRotation(middle, rotation);
            GameObject copy = Instantiate(stageVisual, go.transform, true); // keeps its world pose
            copy.name = stageVisual.name;
            copy.SetActive(true);

            var d = go.AddComponent<StageDebris>();
            d.vehicle = vehicle;
            d.sim = sim;
            d.controller = controller;
            d.origin = origin;
            d.position = origin.ToSimulationPosition(middle);
            d.velocity = v;
            d.halfWidth = radius;
            d.time = t;
            d.mass = System.Math.Max(1.0, mass);
            d.area = System.Math.PI * radius * radius;
            d.attitude = rotation;
            // A slow tumble set by the separation (a few degrees per second about a sideways axis).
            var rng = new System.Random(seed);
            float a = (float)(rng.NextDouble() * 360.0);
            d.spinAxis = rotation * new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad));
            d.spinRate = 2f + 4f * (float)rng.NextDouble();
            return d;
        }

        void LateUpdate()
        {
            if (vehicle == null || sim == null || controller == null || origin == null || vehicle.Planet == null || vehicle.Planet.Model == null)
            {
                Destroy(gameObject);
                return;
            }
            var planet = vehicle.Planet.Model;
            double target = controller.RenderTime;
            if (target < time - 1.0)
            {
                Destroy(gameObject); // the clock went back (new flight / reload)
                return;
            }

            if (landed)
            {
                position = planet.ToInertial(landedFixed, target);
                time = target;
                if (Time.unscaledTime - landedAt > LieOnGround) Destroy(gameObject);
            }
            else if (target > time)
            {
                double span = target - time;
                int n = (int)System.Math.Min(MaxStepsPerFrame, System.Math.Ceiling(span / MaxStep));
                double h = span / n;
                double turned = 0.0;
                for (int i = 0; i < n && !landed; i++)
                {
                    Step(planet, h);
                    turned += h;
                }
                attitude = Quaternion.AngleAxis(spinRate * (float)turned, spinAxis) * attitude;
            }

            if ((position - vehicle.Simulation.State.Position).Magnitude > MaxDistance)
            {
                Destroy(gameObject);
                return;
            }
            transform.SetPositionAndRotation(origin.ToUnityPosition(position), attitude);
        }

        /// <summary>One semi-implicit (kick-drift) step: gravity of the vehicle's frame plus drag against the co-rotating air.</summary>
        void Step(SpaceSim.Planets.PlanetModel planet, double h)
        {
            Vector3d acc = sim.GravityAcceleration(time, position);
            double rho = planet.DensityAt(position);
            if (rho > 0.0)
            {
                Vector3d vRel = velocity - planet.AirVelocityAt(position);
                Vector3d drag = AtmosphericDrag.DragForce(rho, vRel, DragCoefficient, area) / mass;
                // Never let the drag of a coarse step reverse the motion through the air.
                double vr = vRel.Magnitude, dv = drag.Magnitude * h;
                if (vr > 0.0 && dv > vr) drag *= vr / dv;
                acc += drag;
            }
            velocity += acc * h;
            position += velocity * h;
            time += h;
            if (planet.AltitudeAboveSurface(position, time) <= 0.0)
            {
                // Impact: the wreck stays where it hit, turning with the ground.
                Vector3d f = planet.ToFixed(position, time).Normalized;
                landedFixed = f * (planet.SurfaceRadius(f) + halfWidth); // lying on its side
                position = planet.ToInertial(landedFixed, time);
                velocity = Vector3d.Zero;
                landed = true;
                landedAt = Time.unscaledTime;
            }
        }
    }
}
