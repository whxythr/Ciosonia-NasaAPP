using System;
using System.Collections.Generic;
using SpaceSim.Exploration;
using SpaceSim.Mathematics;
using SpaceSim.Multiplayer;
using SpaceSim.Physics;
using SpaceSim.Planets;
using UnityEngine;

namespace SpaceSim.Runtime.Multiplayer
{
    /// <summary>
    /// The robot of a team mission on every computer of the room. The robot operator's computer sends a
    /// <see cref="CrewRobotSnapshot"/> ten times a second; the others
    ///   • draw a copy of the robot: a walker of their own on the same ground, stepped with the operator's input every frame
    ///     (so it strides, sprints and jumps like the original) and put on the sent point when it drifts (<see cref="CrewRobotSync"/>);
    ///   • move the shared rocket to where the operator's rover is while it drives (<see cref="RoverDrivenRemotely"/> keeps
    ///     <see cref="CrewFlightLink"/> from pulling it back to the pilot's lagging landed point meanwhile);
    ///   • hide the samples the operator took, so nobody sees or takes them twice.
    /// Lives on the bootstrap for the whole mission; the copy is rebuilt with the flight stack.
    /// </summary>
    public sealed class CrewRobotLink : MonoBehaviour
    {
        const float SendInterval = 0.1f;

        SpaceSimBootstrap boot;
        ICrewNetwork net;
        int sequence;
        float nextSend;

        CrewRobotSnapshot latest;
        bool fresh;
        float latestAt = -100f;
        int lastSequence = -1;

        SurfaceExplorer copyOwner;
        SurfaceWalker copyWalker;
        IExplorerBody copyBody;
        int seenJumps;
        readonly HashSet<string> hidden = new HashSet<string>();

        /// <summary>The operator's rover is driving the shared rocket right now (seen in a recent snapshot).</summary>
        public static bool RoverDrivenRemotely { get; private set; }
        /// <summary>Photos the robot operator reports (their computer counts them; others read it here).</summary>
        public static int RemotePhotos { get; private set; }
        /// <summary>The operator's robot is outside (from a recent snapshot).</summary>
        public static bool RemoteEva { get; private set; }

        public void Initialize(SpaceSimBootstrap bootstrap)
        {
            boot = bootstrap;
            net = CrewSession.Network;
            if (net != null) net.RobotReceived += OnRobot;
            RemotePhotos = 0; // statics: a new mission starts from zero
            RemoteEva = false;
        }

        void OnDestroy()
        {
            if (net != null) net.RobotReceived -= OnRobot;
            RemoveCopy();
            RoverDrivenRemotely = false;
        }

        static bool IsOperator => CrewSession.Can(CrewAction.WorkOnSurface);

        void Update()
        {
            if (net == null || !CrewSession.Active || boot == null || boot.Body == null || boot.Explorer == null)
            {
                RoverDrivenRemotely = false;
                return;
            }
            if (IsOperator)
            {
                RoverDrivenRemotely = false;
                SendIfDue();
                return;
            }
            bool current = latest != null && Time.unscaledTime - latestAt < CrewRobotSync.StaleAfter;
            RoverDrivenRemotely = current && latest.RoverActive && latest.Frame == boot.Body.Simulation.FrameBody;
            if (latest != null) RemotePhotos = Math.Max(RemotePhotos, latest.Photos);
            RemoteEva = current && latest.Eva;
            if (fresh)
            {
                fresh = false;
                ApplySnapshot(latest);
            }
            StepCopy(current);
        }

        // ---- Robot operator: snapshots -------------------------------------------------------------------------------

        void SendIfDue()
        {
            if (Time.unscaledTime < nextSend) return;
            nextSend = Time.unscaledTime + SendInterval;
            SurfaceExplorer ex = boot.Explorer;
            var s = new CrewRobotSnapshot
            {
                Sequence = ++sequence,
                Frame = boot.Body.Simulation.FrameBody,
                Eva = ex.Eva,
                Yaw = ex.Yaw,
                Forward = ex.Eva ? ex.LastForward : 0.0,
                Right = ex.Eva ? ex.LastRight : 0.0,
                Sprint = ex.Eva && ex.LastSprint,
                Jumps = ex.JumpCount,
                Photos = CrewMissionTasks.LocalPhotos,
                RoverActive = boot.Rover != null && boot.Rover.Active,
                RoverFixed = boot.Body.Simulation.LandedFixedPosition,
            };
            if (ex.Eva)
            {
                s.Position = ex.Walker.Position;
                s.Velocity = ex.Walker.Velocity;
            }
            IReadOnlyList<string> taken = ex.CollectedSites;
            s.Collected = new string[taken.Count];
            for (int i = 0; i < taken.Count; i++) s.Collected[i] = taken[i];
            net.SendRobot(s.Encode());
        }

        // ---- Everybody else: the copy --------------------------------------------------------------------------------

        void OnRobot(int sender, string text)
        {
            if (sender != CrewSession.HolderIdOf(CrewRole.RobotOperator)) return;
            CrewRobotSnapshot s = CrewRobotSnapshot.Decode(text);
            if (s == null || s.Sequence <= lastSequence) return;
            lastSequence = s.Sequence;
            latest = s;
            latestAt = Time.unscaledTime;
            fresh = true;
        }

        void ApplySnapshot(CrewRobotSnapshot s)
        {
            SurfaceExplorer ex = boot.Explorer;
            foreach (string id in s.Collected)
                if (hidden.Add(id)) ex.MarkCollectedByCrew(id);

            FlightBody body = boot.Body;
            bool sameWorld = s.Frame == body.Simulation.FrameBody;
            if (sameWorld && body.Simulation.IsLanded && CrewRobotSync.RoverNeedsMove(s, body.Simulation.LandedFixedPosition))
                body.DriveTo(s.RoverFixed.Normalized);

            if (!s.Eva || !sameWorld)
            {
                RemoveCopy();
                return;
            }
            if (!ReferenceEquals(copyOwner, ex)) RemoveCopy(); // a new flight stack: the old walker belongs to another world
            if (copyWalker == null)
            {
                copyOwner = ex;
                copyWalker = ex.CreateCrewWalker(s.Position);
                copyBody = ex.CreateCrewBody();
                seenJumps = s.Jumps;
                return;
            }
            if (CrewRobotSync.RobotNeedsPlace(s, copyWalker.Position, net.LatencySeconds)) copyWalker.PlaceAt(s.Position);
        }

        void StepCopy(bool current)
        {
            if (copyWalker == null || latest == null) return;
            if (!ReferenceEquals(copyOwner, boot.Explorer))
            {
                RemoveCopy();
                return;
            }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            // A stale snapshot (the operator dropped out) no longer walks the copy: it stands where it was last seen.
            double forward = current ? latest.Forward : 0.0, right = current ? latest.Right : 0.0;
            bool jump = current && CrewRobotSync.StartsJump(seenJumps, latest);
            seenJumps = latest.Jumps;
            copyWalker.Step(dt, forward, right, latest.Yaw, current && latest.Sprint, jump);
            if (copyBody != null)
            {
                Axes(out _, out Vector3d f, out Vector3d r);
                copyBody.Animate(copyWalker, f, r, dt);
            }
        }

        void LateUpdate()
        {
            if (copyWalker == null || copyBody == null || boot == null || boot.Planet == null || boot.Planet.Model == null || boot.Origin == null) return;
            // Feet on the copy's ground point, after the camera moved the floating origin (like SurfaceExplorer does for its own body).
            PlanetModel model = boot.Planet.Model;
            double t = boot.Simulation.RenderTime;
            Axes(out Vector3d up, out Vector3d forward, out _);
            Vector3d feet = model.ToInertial(copyWalker.Position, t);
            Vector3 fw = CoordinateSystem.ToUnityVector(model.Rotation.FixedToInertial(forward, t));
            Vector3 u = CoordinateSystem.ToUnityVector(model.Rotation.FixedToInertial(up, t));
            copyBody.Root.SetPositionAndRotation(boot.Origin.ToUnityPosition(feet), Quaternion.LookRotation(fw, u));
        }

        /// <summary>The copy's horizontal axes in the planet-fixed frame from the operator's heading.</summary>
        void Axes(out Vector3d up, out Vector3d forward, out Vector3d right)
        {
            up = copyWalker.Up;
            PlanetRotation.LocalBasis(up, out Vector3d east, out Vector3d north);
            double y = (latest != null ? latest.Yaw : 0.0) * MathD.DegToRad;
            forward = north * Math.Cos(y) + east * Math.Sin(y);
            right = east * Math.Cos(y) - north * Math.Sin(y);
        }

        void RemoveCopy()
        {
            if (copyBody != null) copyBody.Destroy();
            copyBody = null;
            copyWalker = null;
            copyOwner = null;
        }
    }
}
