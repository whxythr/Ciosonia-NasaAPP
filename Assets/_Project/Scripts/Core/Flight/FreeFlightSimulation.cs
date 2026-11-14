using System;
using System.Collections.Generic;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Simulation;

namespace SpaceSim.Flight
{
    /// <summary>Record of the last contact with the surface.</summary>
    public struct ImpactRecord
    {
        public double Time;
        /// <summary>Speed relative to the ground at contact [m/s].</summary>
        public double SurfaceRelativeSpeed;
        /// <summary>Vertical (radial) component at contact [m/s], negative = downward.</summary>
        public double VerticalSpeed;
        public double Latitude;
        public double Longitude;
    }

    /// <summary>
    /// Unpowered point-mass motion under gravity (+ optional drag) with surface contact. No engine, no thrust,
    /// no automatic orbit insertion: the body only moves according to its initial conditions and the forces.
    /// </summary>
    public sealed class FreeFlightSimulation : IAccelerationModel
    {
        PlanetModel planet;

        public FreeFlightSimulation(PlanetModel planet, GravityField gravity)
        {
            this.planet = planet ?? throw new ArgumentNullException(nameof(planet));
            Gravity = gravity ?? throw new ArgumentNullException(nameof(gravity));
        }

        /// <summary>Primary body for contact, atmosphere and rotation. Can be swapped when planet data changes.</summary>
        public PlanetModel Planet
        {
            get => planet;
            set
            {
                planet = value ?? throw new ArgumentNullException(nameof(value));
                if (IsLanded)
                {
                    // Radius or terrain may have changed: keep the body resting on the new surface.
                    Vector3d dir = LandedFixedPosition.Normalized;
                    LandedFixedPosition = dir * (planet.SurfaceRadius(dir) + ContactRadius);
                    State = LandedState(State.Time);
                }
            }
        }

        public GravityField Gravity { get; set; }

        public BodyState State;
        public BodyState PreviousState;

        public bool IsLanded { get; private set; }
        /// <summary>Contact point in the planet-fixed frame while landed [m].</summary>
        public Vector3d LandedFixedPosition { get; private set; }

        public double Mass = 1000.0;
        public DragProperties Drag = new DragProperties(true, 1.0, 1.0);
        /// <summary>Distance from the body's reference point to the ground when resting [m].</summary>
        public double ContactRadius = 1.0;

        /// <summary>Integrator for unpowered (conservative) flight: symplectic Velocity Verlet keeps orbits stable.</summary>
        public IntegrationMethod Method = IntegrationMethod.VelocityVerlet;
        /// <summary>Integrator while an engine burns: thrust is non-conservative and attitude/time dependent → RK4.</summary>
        public IntegrationMethod PoweredMethod = IntegrationMethod.RungeKutta4;

        /// <summary>Level B statistics of the current flight (losses, maxima, distance).</summary>
        public FlightRecorder Recorder { get; } = new FlightRecorder();

        IntegrationMethod ActiveMethod => Rocket != null && Rocket.IsBurning ? PoweredMethod : Method;
        /// <summary>η in Δt ≤ η·√(r³/μ). 0.002 ≈ 3100 steps per orbit — far finer than needed in LEO with Δt = 0.02 s.</summary>
        public double AccuracyFactor = 0.002;
        /// <summary>Drag stiffness limit: Δt ≤ k·v/|a_drag|.</summary>
        public double DragStepFactor = 0.05;
        public int MaxSubsteps = 1000;

        public ImpactRecord LastImpact { get; private set; }
        public int ImpactCount { get; private set; }
        /// <summary>Sub-steps used during the last <see cref="Step"/> (diagnostics).</summary>
        public int LastSubstepCount { get; private set; }

        /// <summary>Optional rocket. While it has stages its mass, drag and thrust replace the bare body's.</summary>
        public RocketState Rocket { get; set; }

        /// <summary>
        /// Optional equipment in flight (legs, parachutes, RCS, fins, heat shield). Null = none of it: the flight is exactly as without
        /// these parts.
        /// </summary>
        public FlightSystems Systems { get; set; }

        double pendingHeatDamage;
        /// <summary>Equipment integrity lost to aerodynamic heating since the last call (the caller applies it to the equipment).</summary>
        public double TakeHeatDamage()
        {
            double d = pendingHeatDamage;
            pendingHeatDamage = 0.0;
            return d;
        }

        /// <summary>Current mass [kg] (rocket mass while a rocket is attached).</summary>
        public double CurrentMass => Rocket != null && Rocket.HasStages ? Rocket.Mass : (Rocket != null ? Rocket.Configuration.payloadMass : Mass);

        // ---- IAccelerationModel -------------------------------------------------------------

        public Vector3d Acceleration(double time, Vector3d position, Vector3d velocity) =>
            GravityAcceleration(time, position) + DragAcceleration(position, velocity) + ThrustAcceleration(position);

        // ---- Patched conics (interplanetary flight) -------------------------------------------

        /// <summary>
        /// Star system for patched-conic gravity and coasting; null = only <see cref="Gravity"/> (single-body tests).
        /// The simulation frame is centred on body <see cref="FrameBody"/> of this system.
        /// </summary>
        public PatchedConicSystem Navigation { get; private set; }
        /// <summary>Index (in <see cref="Navigation"/>) of the body the simulation frame is centred on.</summary>
        public int FrameBody { get; private set; } = PatchedConicSystem.Star;
        /// <summary>Body whose gravity acts now (smallest sphere of influence containing the vehicle; −1 = the star).</summary>
        public int Primary { get; private set; } = PatchedConicSystem.Star;

        public void SetNavigation(PatchedConicSystem system, int frameBody)
        {
            Navigation = system;
            FrameBody = frameBody;
            Primary = frameBody;
            UpdatePrimary();
        }

        /// <summary>
        /// Gravity at a point of the simulation frame [m/s²]. Inside the frame body's sphere of influence — its own field
        /// (<see cref="Gravity"/>); elsewhere — the patched-conic primary plus the frame's own acceleration
        /// (<see cref="PatchedConicSystem.GravityInFrame"/>).
        /// </summary>
        public Vector3d GravityAcceleration(double time, Vector3d position)
        {
            if (Navigation == null || FrameBody < 0 || Primary == FrameBody) return Gravity.AccelerationAt(position);
            return Navigation.GravityInFrame(FrameBody, Primary, position, time);
        }

        void UpdatePrimary()
        {
            if (Navigation == null || FrameBody < 0) return;
            Primary = IsLanded ? FrameBody : Navigation.PrimaryAt(State.Position + Navigation.Position(FrameBody, State.Time), State.Time, Primary);
        }

        CoastState ToCoast(BodyState s)
        {
            Vector3d r = s.Position, v = s.Velocity;
            Navigation.Transform(FrameBody, Primary, s.Time, ref r, ref v);
            return new CoastState(Primary, r, v, s.Time);
        }

        BodyState FromCoast(CoastState c)
        {
            Vector3d r = c.Position, v = c.Velocity;
            Navigation.Transform(c.Primary, FrameBody, c.Time, ref r, ref v);
            return new BodyState(r, v, c.Time);
        }

        /// <summary>Current state relative to the primary (for prediction and planning).</summary>
        public CoastState CoastStateNow() => ToCoast(State);

        /// <summary>The flight state <see cref="TimeWarpPolicy"/> needs, measured from the body the vehicle is orbiting now.</summary>
        public TimeWarpSituation WarpSituation()
        {
            var s = new TimeWarpSituation { Landed = IsLanded, EngineBurning = Rocket != null && Rocket.IsBurning };
            if (IsLanded) return s;
            if (s.EngineBurning) s.ThrustAcceleration = ThrustAcceleration(State.Position).Magnitude;

            double mu, zoneRadius;
            Vector3d r, v;
            if (Navigation != null && FrameBody >= 0)
            {
                CoastState c = ToCoast(State);
                if (c.Primary < 0)
                {
                    s.Interplanetary = true;
                    return s;
                }
                NavBody body = Navigation[c.Primary];
                mu = body.Mu;
                s.BodyRadius = body.Radius;
                zoneRadius = body.ZoneRadius;
                r = c.Position;
                v = c.Velocity;
            }
            else
            {
                mu = planet.GravitationalParameter;
                s.BodyRadius = planet.Radius;
                zoneRadius = planet.Radius + PatchedConicSystem.NumericZoneAltitude(planet.Atmosphere, planet.Surface.MaxSurfaceHeight);
                r = State.Position - planet.Position;
                v = State.Velocity;
            }
            s.Altitude = r.Magnitude - s.BodyRadius;
            s.ZoneAltitude = zoneRadius - s.BodyRadius;
            OrbitalElements orbit = OrbitalMechanics.ComputeElements(mu, r, v, planet.Rotation.SpinAxis);
            // A closed orbit must clear the zone at periapsis; an open trajectory is also safe once it is on its way out.
            s.SafeTrajectory = orbit.PeriapsisRadius > zoneRadius || (!orbit.IsBound && Vector3d.Dot(r, v) > 0.0);
            return s;
        }

        /// <summary>
        /// How long the vehicle can coast analytically ("on rails") [s] — 0 while the engine burns, inside an atmosphere or
        /// near the ground. Resting on the ground is exact at any step.
        /// </summary>
        public double RailsHorizon(double maxDt)
        {
            if (Navigation == null || FrameBody < 0) return 0.0;
            if (Rocket != null && Rocket.IsBurning)
            {
                // A low-thrust burn on a safe trajectory goes in long steps that are still integrated numerically (see RailsStep):
                // never longer than the sub-step limit allows at the orbit's accuracy step.
                if (IsLanded || !LowThrustBurnOnSafeTrajectory()) return 0.0;
                double limit = 0.9 * MaxSubsteps * MaxStableStep(State);
                return Math.Min(Navigation.Horizon(ToCoast(State), maxDt), limit);
            }
            if (IsLanded) return maxDt;
            return Navigation.Horizon(ToCoast(State), maxDt);
        }

        /// <summary>The engine is a low-thrust one and the trajectory is safe (or around the star): see <see cref="TimeWarpPolicy.LowThrustWarp"/>.</summary>
        public bool LowThrustBurnOnSafeTrajectory()
        {
            TimeWarpSituation s = WarpSituation();
            if (!TimeWarpPolicy.IsLowThrust(s)) return false;
            return s.Interplanetary || (s.SafeTrajectory && s.Altitude >= s.ZoneAltitude);
        }

        /// <summary>
        /// Coasts analytically by <paramref name="dt"/> with exact two-body motion around each primary (same physics as the
        /// integrator, no drag — only used where drag is negligible). <paramref name="renderLag"/>: the previous state is
        /// taken that much before the end, for smooth interpolation.
        /// </summary>
        public void RailsStep(double dt, double renderLag, List<PatchTransition> transitions = null)
        {
            if (!IsLanded && Rocket != null && Rocket.IsBurning)
            {
                // Low-thrust burn: the same numerical integration as Step, in long sub-stepped pieces (thrust is not a Kepler arc).
                double lagPowered = Math.Min(renderLag, dt);
                if (dt - lagPowered > 0.0) Step(dt - lagPowered);
                if (lagPowered > 0.0) Step(lagPowered);
                return;
            }
            UpdateEquipment(dt, false);
            if (IsLanded)
            {
                State = LandedState(State.Time + dt);
                PreviousState = LandedState(State.Time - Math.Min(renderLag, dt));
                LastSubstepCount = 0;
                return;
            }
            double lag = Math.Min(renderLag, dt);
            CoastState c = ToCoast(State);
            Navigation.Coast(ref c, dt - lag, transitions);
            Primary = c.Primary;
            PreviousState = FromCoast(c);
            Navigation.Coast(ref c, lag, transitions);
            Primary = c.Primary;
            State = FromCoast(c);
            Recorder.RecordCoast(dt, State.Velocity.Magnitude, planet.AltitudeAboveSurface(State.Position, State.Time));
            LastSubstepCount = 0;
        }

        public Vector3d DragAcceleration(Vector3d position, Vector3d velocity)
        {
            double m = CurrentMass;
            if (!Drag.enabled || m <= 0.0) return Vector3d.Zero;
            double rho = planet.DensityAt(position);
            if (rho <= 0.0) return Vector3d.Zero;
            Vector3d vRel = velocity - planet.AirVelocityAt(position);
            bool rocket = Rocket != null && Rocket.HasStages;
            double cd = rocket ? Rocket.Configuration.dragCoefficient : Drag.dragCoefficient;
            double area = rocket ? Rocket.Configuration.ReferenceArea : Drag.referenceArea;
            Vector3d force = AtmosphericDrag.DragForce(rho, vRel, cd, area);
            // Released canopies: their own C_d·A, scaled by how far they have filled.
            double chutes = Systems != null ? Systems.ParachuteDragArea : 0.0;
            if (chutes > 0.0) force += AtmosphericDrag.DragForce(rho, vRel, chutes, 1.0);
            return force / m;
        }

        /// <summary>Thrust acceleration [m/s²] along the commanded attitude (local horizon frame at the position).</summary>
        public Vector3d ThrustAcceleration(Vector3d position)
        {
            if (Rocket == null || !Rocket.IsBurning) return Vector3d.Zero;
            double rho0 = planet.Atmosphere.ReferenceDensity;
            double h = planet.AltitudeAboveReference(position);
            double p0 = planet.Atmosphere.Pressure(0.0);
            double pressureRatio = rho0 > 0.0 && p0 > 0.0 ? planet.Atmosphere.Pressure(h) / p0 : 0.0;
            double thrust = Rocket.Thrust(pressureRatio);
            return ThrustDirection(position) * (thrust / Rocket.Mass);
        }

        /// <summary>Unit thrust direction in the inertial frame from the rocket's pitch/heading.</summary>
        public Vector3d ThrustDirection(Vector3d position)
        {
            Vector3d up = (position - planet.Position).Normalized;
            Vector3d e = Vector3d.Cross(planet.Rotation.SpinAxis, up);
            if (e.SqrMagnitude < 1e-20) e = Vector3d.Cross(Vector3d.UnitX, up);
            Vector3d east = e.Normalized;
            Vector3d north = Vector3d.Cross(up, east);
            if (Rocket == null) return up;
            Rocket.LocalDirection(out double cu, out double cn, out double ce);
            return (up * cu + north * cn + east * ce).Normalized;
        }

        /// <summary>
        /// Pitch/heading [deg] that would point the thrust along <paramref name="direction"/> at <paramref name="position"/> — the
        /// inverse of <see cref="ThrustDirection"/>. Display aid for manoeuvres; the player still steers.
        /// </summary>
        public void AttitudeFor(Vector3d direction, Vector3d position, out double pitchDegrees, out double headingDegrees)
        {
            Vector3d up = (position - planet.Position).Normalized;
            Vector3d e = Vector3d.Cross(planet.Rotation.SpinAxis, up);
            if (e.SqrMagnitude < 1e-20) e = Vector3d.Cross(Vector3d.UnitX, up);
            Vector3d east = e.Normalized;
            Vector3d north = Vector3d.Cross(up, east);
            Vector3d d = direction.Normalized;
            pitchDegrees = Math.Acos(MathD.Clamp(Vector3d.Dot(d, up), -1.0, 1.0)) * MathD.RadToDeg;
            headingDegrees = Math.Atan2(Vector3d.Dot(d, east), Vector3d.Dot(d, north)) * MathD.RadToDeg;
            if (headingDegrees < 0.0) headingDegrees += 360.0;
        }

        // ---- Initial conditions (test tools; nothing here chooses a trajectory for the player) ----

        /// <summary>Puts the body at rest on the surface along a planet-fixed direction.</summary>
        public void PlaceOnSurface(Vector3d fixedDirection, double time)
        {
            Vector3d dir = fixedDirection.Normalized;
            LandedFixedPosition = dir * (planet.SurfaceRadius(dir) + ContactRadius);
            IsLanded = true;
            State = LandedState(time);
            PreviousState = State;
            Recorder.Reset();
            UpdatePrimary();
        }

        /// <summary>
        /// Sets position by latitude/longitude [rad] and altitude above the reference sphere [m], with a velocity
        /// given in the local East-North-Up frame [m/s], either relative to the rotating ground or inertial.
        /// </summary>
        public void SetLocalState(double latitude, double longitude, double altitudeAboveReference,
            double vEast, double vNorth, double vUp, bool relativeToSurface, double time)
        {
            Vector3d up = PlanetRotation.DirectionFromLatLon(latitude, longitude);
            PlanetRotation.LocalBasis(up, out Vector3d east, out Vector3d north);
            Vector3d fixedPos = up * (planet.Radius + altitudeAboveReference);
            Vector3d fixedVel = east * vEast + north * vNorth + up * vUp;

            Vector3d r = planet.ToInertial(fixedPos, time);
            Vector3d v = planet.Rotation.FixedToInertial(fixedVel, time);
            if (relativeToSurface) v += planet.SurfaceVelocityAt(r);
            SetInertialState(r, v, time);
        }

        public void SetInertialState(Vector3d position, Vector3d velocity, double time)
        {
            IsLanded = false;
            State = new BodyState(position, velocity, time);
            PreviousState = State;
            Recorder.Reset();
            if (planet.AltitudeAboveSurface(position, time) < ContactRadius)
            {
                // Started below ground: lift to the surface, keep the velocity (it may be pointing upward).
                Vector3d f = planet.ToFixed(position, time).Normalized;
                Vector3d lifted = planet.ToInertial(f * (planet.SurfaceRadius(f) + ContactRadius), time);
                State.Position = lifted;
                PreviousState = State;
                double vUp = Vector3d.Dot(velocity - planet.SurfaceVelocityAt(lifted), (lifted - planet.Position).Normalized);
                if (vUp <= 0.0) Land(time, lifted, velocity);
            }
            UpdatePrimary();
        }

        // ---- Stepping -------------------------------------------------------------------------

        /// <summary>Advances by exactly <paramref name="dt"/> seconds (fixed step from the simulation clock).</summary>
        public void Step(double dt)
        {
            PreviousState = State;
            if (IsLanded)
            {
                if (Rocket != null && Rocket.IsBurning && ThrustExceedsWeight(State.Position))
                {
                    IsLanded = false; // lift-off: start from the co-rotating pad state
                }
                else
                {
                    Rocket?.Burn(dt); // engine held down on the pad: propellant still burns
                    State = LandedState(State.Time + dt);
                    LastSubstepCount = 0;
                    UpdateEquipment(dt, true);
                    return;
                }
            }

            double maxStep = MaxStableStep(State);
            int n = NumericalIntegrator.SubstepCount(dt, maxStep, MaxSubsteps);
            double h = dt / n;
            LastSubstepCount = n;

            for (int i = 0; i < n; i++)
            {
                BodyState before = State;
                IntegrationMethod method = ActiveMethod;
                BodyState after = NumericalIntegrator.Step(before, h, this, method);
                if (planet.AltitudeAboveSurface(after.Position, after.Time) < ContactRadius)
                {
                    ResolveImpact(before, h, method);
                    // Remaining time is spent resting on the surface.
                    double endTime = PreviousState.Time + dt;
                    State = LandedState(endTime);
                    UpdateEquipment(dt, true);
                    return;
                }
                RecordSubstep(before, after, h);
                State = after;
                Rocket?.Burn(h);
                UpdatePrimary();
            }
            UpdateEquipment(dt, true);
        }

        /// <summary>
        /// After a step of dt seconds: the batteries (unless <see cref="RocketState.Burn"/> already counted them for an electric stage),
        /// the parachutes, the heating at the nose and the weathervane moment of the side fins.
        /// </summary>
        void UpdateEquipment(double dt, bool burnCalled)
        {
            if (Rocket != null && !(burnCalled && Rocket.HasStages && Rocket.CurrentStage.IsElectric)) Rocket.UpdateBatteries(dt);
            if (Systems == null) return;
            if (IsLanded)
            {
                Systems.ClearHeat();
                return;
            }
            Vector3d r = State.Position;
            double rho = planet.DensityAt(r);
            Vector3d vAir = State.Velocity - planet.AirVelocityAt(r);
            double speed = vAir.Magnitude;
            Systems.UpdateParachutes(dt, rho, speed);
            double nose = Rocket != null ? Rocket.Configuration.radius : Math.Sqrt(Math.Max(0.0, Drag.referenceArea) / Math.PI);
            pendingHeatDamage += Systems.UpdateHeat(dt, rho, speed, nose);
            if (Rocket != null && Rocket.HasStages && rho > 0.0 && speed > 1.0) Weathervane(dt, r, vAir, rho);
        }

        /// <summary>
        /// Side fins turn the axis towards the airflow at <see cref="FlightSystems.WeathervaneRate"/> (passive stability, like an arrow).
        /// Without fins nothing happens: the attitude stays what the pilot holds.
        /// </summary>
        void Weathervane(double dt, Vector3d position, Vector3d airVelocity, double density)
        {
            double speed = airVelocity.Magnitude;
            Vector3d axis = ThrustDirection(position);
            Vector3d flow = airVelocity / speed;
            double q = AtmosphericDrag.DynamicPressure(density, speed);
            // Open canopies hang the vehicle from the top of its stack: they turn the axis against the airflow (nose up under the
            // canopy) and overpower the fins, which would turn it into the flow (nose first).
            bool canopy = Systems.ParachuteDragArea > 0.0;
            if (canopy) flow = -flow;
            double cos = MathD.Clamp(Vector3d.Dot(axis, flow), -1.0, 1.0);
            double aoa = Math.Acos(cos);
            if (aoa < 1e-6) return;
            double rate = canopy ? Systems.CanopyRightingRate(Rocket, q, aoa) : Systems.WeathervaneRate(Rocket, q, aoa);
            if (!(rate > 0.0)) return;
            Vector3d perp = flow - axis * cos;
            if (perp.SqrMagnitude < 1e-12)
            {
                if (!canopy) return; // exactly backwards: no preferred side
                // Nose straight down under the canopy: the slightest swing tips it over; pick any side.
                perp = Vector3d.Cross(axis, Vector3d.UnitX);
                if (perp.SqrMagnitude < 1e-12) perp = Vector3d.Cross(axis, Vector3d.UnitY);
            }
            perp = perp.Normalized;
            double turn = Math.Min(aoa, rate * dt);
            Vector3d next = axis * Math.Cos(turn) + perp * Math.Sin(turn);
            AttitudeFor(next, position, out double pitch, out double heading);
            Rocket.PitchDegrees = pitch;
            if (pitch > 1e-3) Rocket.HeadingDegrees = heading;
        }

        void RecordSubstep(BodyState before, BodyState after, double h)
        {
            Vector3d r = before.Position, v = before.Velocity;
            Vector3d aDrag = DragAcceleration(r, v);
            Vector3d aThrust = ThrustAcceleration(r);
            double rho = planet.DensityAt(r);
            Vector3d vAir = v - planet.AirVelocityAt(r);
            double burned = Rocket != null ? Rocket.MassFlow * h : 0.0;
            Recorder.Record(h, v, v - planet.SurfaceVelocityAt(r), GravityAcceleration(before.Time, r), aDrag, aThrust,
                planet.AltitudeAboveSurface(after.Position, after.Time),
                AtmosphericDrag.DynamicPressure(rho, vAir.Magnitude), burned);
        }

        /// <summary>
        /// Lift-off test: the upward component of thrust + gravity must exceed what is needed to keep co-rotating
        /// with the ground (the centripetal acceleration of the pad).
        /// </summary>
        bool ThrustExceedsWeight(Vector3d position)
        {
            Vector3d rel = position - planet.Position;
            Vector3d up = rel.Normalized;
            Vector3d w = planet.Rotation.AngularVelocity;
            Vector3d centripetal = -Vector3d.Cross(w, Vector3d.Cross(w, rel));
            Vector3d net = ThrustAcceleration(position) + GravityAcceleration(State.Time, position);
            return Vector3d.Dot(net, up) > Vector3d.Dot(centripetal, up);
        }

        double MaxStableStep(BodyState s)
        {
            Vector3d rel = s.Position - planet.Position;
            double limit = NumericalIntegrator.AccuracyLimitedStep(planet.GravitationalParameter, rel.Magnitude, AccuracyFactor);
            Vector3d aDrag = DragAcceleration(s.Position, s.Velocity);
            double ad = aDrag.Magnitude;
            if (ad > 0.0)
            {
                double vRel = (s.Velocity - planet.AirVelocityAt(s.Position)).Magnitude;
                limit = Math.Min(limit, DragStepFactor * vRel / ad);
            }
            return limit;
        }

        /// <summary>Bisection on the sub-step duration to find the contact time, then lands there.</summary>
        void ResolveImpact(BodyState before, double h, IntegrationMethod method)
        {
            double lo = 0.0, hi = h;
            BodyState contact = before;
            for (int k = 0; k < 24; k++)
            {
                double mid = 0.5 * (lo + hi);
                BodyState probe = NumericalIntegrator.Step(before, mid, this, method);
                if (planet.AltitudeAboveSurface(probe.Position, probe.Time) < ContactRadius) hi = mid;
                else
                {
                    lo = mid;
                    contact = probe;
                }
            }
            Land(contact.Time, contact.Position, contact.Velocity);
        }

        void Land(double time, Vector3d position, Vector3d velocity)
        {
            Vector3d surfaceVel = planet.SurfaceVelocityAt(position);
            Vector3d rel = velocity - surfaceVel;
            Vector3d up = (position - planet.Position).Normalized;
            Vector3d fixedDir = planet.ToFixed(position, time).Normalized;
            PlanetRotation.LatLonFromDirection(fixedDir, out double lat, out double lon);

            LastImpact = new ImpactRecord
            {
                Time = time,
                SurfaceRelativeSpeed = rel.Magnitude,
                VerticalSpeed = Vector3d.Dot(rel, up),
                Latitude = lat,
                Longitude = lon
            };
            ImpactCount++;

            LandedFixedPosition = fixedDir * (planet.SurfaceRadius(fixedDir) + ContactRadius);
            IsLanded = true;
            if (Navigation != null) Primary = FrameBody; // the ground belongs to the frame body
        }

        BodyState LandedState(double time)
        {
            Vector3d r = planet.ToInertial(LandedFixedPosition, time);
            return new BodyState(r, planet.SurfaceVelocityAt(r), time);
        }

        /// <summary>Exact position for rendering at an arbitrary time while landed (avoids chord interpolation).</summary>
        public Vector3d LandedPositionAt(double time) => planet.ToInertial(LandedFixedPosition, time);
    }
}
