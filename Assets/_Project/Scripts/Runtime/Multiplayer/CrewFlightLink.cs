using SpaceSim.Flight;
using SpaceSim.Localization;
using SpaceSim.Mathematics;
using SpaceSim.Multiplayer;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime.Multiplayer
{
    /// <summary>
    /// Keeps the one rocket of a team mission the same on every computer of the room.
    ///   • The engineer's rolled-out design goes into the room (<see cref="ICrewNetwork.PublishDesign"/>); every other computer
    ///     puts it on its pad as soon as its rocket stands there.
    ///   • The pilot's computer is the authority over the flight and the clock: ten times a second it sends a
    ///     <see cref="CrewFlightSnapshot"/>. The others take its warp and pause, jump their clock when it is too far off, copy the
    ///     controls, stage, propellant, legs, RCS and parachutes, and put their rocket on the pilot's state when it has drifted
    ///     (<see cref="CrewFlightSync"/>). Between snapshots they run the same physics with the same controls.
    /// Lives on the bootstrap for the whole mission (the flight stack is rebuilt at sphere changes; the body is re-read every frame).
    /// The robot and the rover are CrewRobotLink's.
    /// </summary>
    public sealed class CrewFlightLink : MonoBehaviour
    {
        const float SendInterval = 0.1f;

        SpaceSimBootstrap boot;
        ICrewNetwork net;
        FlightBody hookedBody;

        // Pilot
        int sequence, resets;
        float nextSend;

        // Follower
        CrewFlightSnapshot latest;
        bool pending;
        int lastSequence = -1, appliedResets = -1;
        bool pilotGoneNoticed;

        // Design
        string appliedDesign, waitingDesign;

        public void Initialize(SpaceSimBootstrap bootstrap)
        {
            boot = bootstrap;
            net = CrewSession.Network;
            if (net != null) net.FlightReceived += OnFlight;
        }

        void OnDestroy()
        {
            if (net != null) net.FlightReceived -= OnFlight;
            Hook(null);
        }

        static bool IsPilot => CrewSession.Can(CrewAction.FlyRocket);
        static bool IsEngineer => CrewSession.Can(CrewAction.BuildRocket);

        void Notify(string text)
        {
            if (boot != null && boot.FlightState != null) boot.FlightState.Notify(text);
        }

        void Update()
        {
            if (net == null || !CrewSession.Active || boot == null || boot.Body == null || boot.Simulation == null) return;
            if (!ReferenceEquals(hookedBody, boot.Body)) Hook(boot.Body);
            SyncDesign();
            if (IsPilot) SendIfDue();
            else ApplyLatest();
        }

        // ---- Body events ---------------------------------------------------------------------------------------------

        void Hook(FlightBody body)
        {
            if (hookedBody != null)
            {
                hookedBody.BlueprintApplied -= OnBlueprintApplied;
                hookedBody.RocketReset -= OnRocketReset;
            }
            bool fresh = body != null && hookedBody != null && body.AwaitingLaunch;
            hookedBody = body;
            if (body == null) return;
            body.BlueprintApplied += OnBlueprintApplied;
            body.RocketReset += OnRocketReset;
            // A new flight stack with the rocket on a pad (back home) is a new flight too; a sphere change carries the flight on.
            if (fresh) OnRocketReset();
        }

        void OnRocketReset()
        {
            if (IsPilot) resets++;
        }

        void OnBlueprintApplied(RocketBlueprint design)
        {
            if (!IsEngineer || design == null) return;
            string json = JsonUtility.ToJson(design);
            appliedDesign = json;
            net.PublishDesign(json);
        }

        // ---- Rocket design -------------------------------------------------------------------------------------------

        void SyncDesign()
        {
            if (IsEngineer) return; // the engineer's own rollout is the source
            string shared = net.SharedDesign;
            if (string.IsNullOrEmpty(shared) || shared == appliedDesign) return;
            FlightBody body = boot.Body;
            if (!body.IsOnLaunchPad || (body.Rocket != null && body.Rocket.IsBurning))
            {
                if (waitingDesign != shared)
                {
                    waitingDesign = shared;
                    Notify(Loc.T("Инженер собрал новую ракету: она встанет на старт, когда эта вернётся на площадку."));
                }
                return;
            }
            RocketBlueprint design;
            try
            {
                design = JsonUtility.FromJson<RocketBlueprint>(shared);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[SpaceSim] Crew rocket design unreadable: " + e.Message);
                appliedDesign = shared;
                return;
            }
            if (design == null || design.stages == null)
            {
                appliedDesign = shared;
                return;
            }
            design.EnsureLists();
            if (!body.ApplyBlueprint(design)) return;
            appliedDesign = shared;
            waitingDesign = null;
            string engineer = CrewSession.HolderOf(CrewRole.Engineer);
            Notify(Loc.T("На старте ракета инженера") + (string.IsNullOrEmpty(engineer) ? "" : " (" + engineer + ")") + ": «" + Loc.T(design.name) + "».");
        }

        // ---- Pilot: snapshots ----------------------------------------------------------------------------------------

        void SendIfDue()
        {
            if (Time.unscaledTime < nextSend) return;
            nextSend = Time.unscaledTime + SendInterval;
            net.SendFlight(Capture().Encode());
        }

        CrewFlightSnapshot Capture()
        {
            FlightBody body = boot.Body;
            SimulationController clock = boot.Simulation;
            FreeFlightSimulation sim = body.Simulation;
            var s = new CrewFlightSnapshot
            {
                Sequence = ++sequence,
                Time = clock.SimulationTime,
                Warp = clock.TimeWarp,
                Rate = clock.Paused ? 0.0 : clock.EffectiveWarp,
                Paused = clock.Paused,
                Frame = sim.FrameBody,
                Landed = sim.IsLanded,
                Position = sim.State.Position,
                Velocity = sim.State.Velocity,
                LandedFixed = sim.LandedFixedPosition,
                Resets = resets,
            };
            RocketState r = sim.Rocket;
            if (r != null)
            {
                s.StageCount = r.StageCount;
                s.StageIndex = r.StageIndex;
                s.EngineOn = r.EngineOn;
                s.Throttle = r.Throttle;
                s.Pitch = r.PitchDegrees;
                s.Heading = r.HeadingDegrees;
                s.Propellant = (double[])r.Propellant.Clone();
            }
            FlightSystems sys = sim.Systems;
            if (sys != null)
            {
                s.Legs = sys.LegsDeployed;
                s.Rcs = sys.RcsEnabled;
                s.Parachutes = sys.ParachuteCount > 0 && sys.CanopiesIn(CanopyState.Stowed) < sys.ParachuteCount;
            }
            return s;
        }

        // ---- Follower: mirror the pilot ------------------------------------------------------------------------------

        void OnFlight(int sender, string text)
        {
            if (sender != CrewSession.HolderIdOf(CrewRole.Pilot)) return; // only the pilot's computer flies the rocket
            CrewFlightSnapshot s = CrewFlightSnapshot.Decode(text);
            if (s == null || s.Sequence <= lastSequence) return; // garbage, late or repeated
            lastSequence = s.Sequence;
            latest = s;
            pending = true;
        }

        void ApplyLatest()
        {
            int pilot = CrewSession.HolderIdOf(CrewRole.Pilot);
            if (pilot >= 0 && !CrewSession.IsConnected(pilot))
            {
                if (!pilotGoneNoticed) Notify(Loc.T("Пилот вышел из миссии: полёт больше не повторяет его ракету."));
                pilotGoneNoticed = true;
                return;
            }
            if (!pending || latest == null) return;
            pending = false;
            CrewFlightSnapshot s = latest;

            FlightBody body = boot.Body;
            FreeFlightSimulation sim = body.Simulation;
            SimulationController clock = boot.Simulation;

            // A landed pilot in another body's frame (back home on a new pad): this computer moves there too; the next snapshot places it.
            if (s.Frame != sim.FrameBody && s.Landed)
            {
                boot.SwitchToBody(s.Frame);
                return;
            }

            // A new flight on the pilot's computer: a fresh rocket on the pad here too (the first snapshot only sets the count).
            if (appliedResets < 0) appliedResets = s.Resets;
            else if (s.Resets != appliedResets)
            {
                appliedResets = s.Resets;
                body.ResetToLaunchSite();
            }

            // Clock: the pilot's warp and pause; a jump when the difference is past the tolerance.
            if (clock.Paused != s.Paused) clock.SetPaused(s.Paused);
            if (System.Math.Abs(clock.TimeWarp - s.Warp) > 1e-9) clock.SetWarp(s.Warp);
            double predicted = CrewFlightSync.PredictedTime(s, net.LatencySeconds);
            bool jumped = false;
            if (CrewFlightSync.ClockNeedsJump(clock.SimulationTime, predicted, s.Warp))
            {
                clock.JumpTo(predicted);
                jumped = true;
            }
            double now = clock.SimulationTime;

            // The rocket: stage first (a separation moves the reference point), then the controls and the tanks.
            RocketState r = sim.Rocket;
            if (r != null && s.StageCount == r.StageCount)
            {
                while (r.StageIndex < s.StageIndex && body.SeparateStage()) { }
                if (r.StageIndex == s.StageIndex)
                {
                    r.Throttle = s.Throttle;
                    r.PitchDegrees = s.Pitch;
                    r.HeadingDegrees = s.Heading;
                    r.EngineOn = s.EngineOn;
                    if (s.Propellant != null && s.Propellant.Length == r.Propellant.Length)
                        for (int i = 0; i < s.Propellant.Length; i++) r.Propellant[i] = s.Propellant[i];
                }
            }
            FlightSystems sys = sim.Systems;
            if (sys != null)
            {
                if (sys.DesignHasLegs && sys.LegsDeployed != s.Legs) body.SetLegsDeployed(s.Legs);
                sys.RcsEnabled = s.Rcs;
                if (s.Parachutes && sys.ParachuteCount > 0 && sys.CanopiesIn(CanopyState.Stowed) == sys.ParachuteCount) body.ReleaseParachutes();
            }

            // Where it is. While the robot operator's rover drives, it owns the landed position: on its own computer, and here
            // through CrewRobotLink (the pilot's landed point lags behind the rover meanwhile).
            if (boot.Rover != null && boot.Rover.Active) return;
            if (s.Landed && sim.IsLanded && CrewRobotLink.RoverDrivenRemotely) return;
            if (s.Landed)
            {
                if (!sim.IsLanded || jumped || CrewFlightSync.LandedNeedsMove(s, sim.LandedFixedPosition))
                    sim.PlaceOnSurface(s.LandedFixed.Normalized, now);
                return;
            }
            Vector3d position = s.Position, velocity = s.Velocity;
            if (s.Frame != sim.FrameBody)
            {
                // Mid sphere change (the pilot's computer has handed over, this one not yet, or the other way round).
                if (sim.Navigation == null) return;
                sim.Navigation.Transform(s.Frame, sim.FrameBody, s.Time, ref position, ref velocity);
            }
            var local = new CrewFlightSnapshot { Time = s.Time, Position = position, Velocity = velocity };
            if (sim.IsLanded || jumped || CrewFlightSync.FlightNeedsSnap(local, sim.State.Position, now))
            {
                CrewFlightSync.Extrapolate(local, now, out Vector3d at);
                sim.SetInertialState(at, velocity, now);
            }
        }
    }
}
