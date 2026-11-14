using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Simulation;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>Anything advanced by the fixed-step simulation clock.</summary>
    public interface ISimulatedBody
    {
        /// <summary>Advance from <paramref name="time"/> by exactly <paramref name="dt"/> simulated seconds.</summary>
        void SimulationStep(double time, double dt);
    }

    /// <summary>A body that can coast analytically over long intervals (exact two-body arcs) at high time warp.</summary>
    public interface IRailsBody
    {
        /// <summary>Longest interval [s] (≤ <paramref name="maxDt"/>) it can coast analytically from now; 0 = must be integrated.</summary>
        double RailsHorizon(double maxDt);
        /// <summary>Coast from <paramref name="time"/> by <paramref name="dt"/>; the previous state is taken <paramref name="renderLag"/> before the end.</summary>
        void RailsStep(double time, double dt, double renderLag);
    }

    /// <summary>A body whose current situation limits the time warp.</summary>
    public interface IWarpLimitedBody
    {
        TimeWarpLimit WarpLimit();
    }

    /// <summary>
    /// Drives the deterministic simulation. Runs in Update with its own accumulator, not in FixedUpdate,
    /// so the physics step and time warp are fully under our control and independent of Unity's physics.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SimulationController : MonoBehaviour
    {
        [SerializeField] SimulationSettings settings = new SimulationSettings();

        readonly SimulationClock clock = new SimulationClock();
        readonly List<ISimulatedBody> bodies = new List<ISimulatedBody>();

        public SimulationSettings Settings => settings;
        public double SimulationTime => clock.SimulationTime;
        /// <summary>Time between the last two physics steps that matches interpolated body positions.</summary>
        public double RenderTime => clock.RenderTime;
        public double InterpolationAlpha => clock.Alpha;
        public double TimeWarp => clock.TimeScale;
        public bool Paused => clock.Paused;
        public bool WarpLimited => clock.WasThrottled;
        public int StepsLastFrame { get; private set; }
        public double FixedStep => clock.FixedStep;
        /// <summary>The last frame advanced on rails (analytic coast at warp above <see cref="SimulationSettings.maxNumericWarp"/>).</summary>
        public bool OnRails { get; private set; }
        /// <summary>Warp above the numeric limit was requested but a body had to be integrated: ran at the numeric limit.</summary>
        public bool RailsBlocked { get; private set; }
        /// <summary>Warp actually achieved last frame (simulated seconds per real second).</summary>
        public double EffectiveWarp { get; private set; } = 1.0;
        /// <summary>Highest warp allowed where the vehicle is now (see <see cref="TimeWarpPolicy"/>).</summary>
        public double WarpLimit { get; private set; } = 1.0;
        /// <summary>The situation that sets <see cref="WarpLimit"/>.</summary>
        public TimeWarpZone WarpZone { get; private set; } = TimeWarpZone.Surface;
        /// <summary>How many times the warp was forced down because the limit fell below it.</summary>
        public int WarpClampCount { get; private set; }

        void Awake()
        {
            ApplySettings();
            clock.Reset(settings.startTime);
        }

        void OnValidate()
        {
            if (Application.isPlaying) ApplySettings();
        }

        void ApplySettings()
        {
            if (settings.fixedStep > 0.0 && settings.fixedStep != clock.FixedStep) clock.FixedStep = settings.fixedStep;
            clock.MaxStepsPerFrame = Mathf.Max(1, settings.maxStepsPerFrame);
            clock.MaxRealDeltaTime = settings.maxRealDeltaTime;
            if (settings.timeWarpLevels == null || settings.timeWarpLevels.Length == 0) settings.timeWarpLevels = new double[] { 1 };
            RefreshWarpLimit();
            clock.TimeScale = System.Math.Max(1.0, System.Math.Min(clock.TimeScale, WarpLimit));
        }

        public void Register(ISimulatedBody body)
        {
            if (body != null && !bodies.Contains(body)) bodies.Add(body);
        }

        public void Unregister(ISimulatedBody body) => bodies.Remove(body);

        public void SetPaused(bool paused) => clock.Paused = paused;
        public void TogglePause() => clock.Paused = !clock.Paused;

        /// <summary>Sets the warp, kept between ×1 and the limit of the current situation.</summary>
        public void SetWarp(double factor)
        {
            RefreshWarpLimit();
            clock.TimeScale = System.Math.Max(1.0, System.Math.Min(factor, WarpLimit));
        }

        // The keys step through the regular levels from whatever value the slider left.
        public void IncreaseWarp()
        {
            double[] levels = settings.timeWarpLevels;
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i] <= clock.TimeScale) continue;
                SetWarp(levels[i]);
                return;
            }
        }

        public void DecreaseWarp()
        {
            double[] levels = settings.timeWarpLevels;
            for (int i = levels.Length - 1; i >= 0; i--)
            {
                if (levels[i] >= clock.TimeScale) continue;
                SetWarp(levels[i]);
                return;
            }
        }

        public void ResetWarp() => SetWarp(1.0);

        void RefreshWarpLimit()
        {
            double[] levels = settings.timeWarpLevels;
            WarpLimit = levels[levels.Length - 1];
            for (int i = 0; i < bodies.Count; i++)
            {
                if (!(bodies[i] is IWarpLimitedBody limited)) continue;
                TimeWarpLimit limit = limited.WarpLimit();
                WarpZone = limit.Zone;
                WarpLimit = System.Math.Min(WarpLimit, limit.MaxWarp);
            }
        }

        void Update()
        {
            // The limit only ever pulls the warp down: nothing speeds up without the player asking for it.
            RefreshWarpLimit();
            if (clock.TimeScale > WarpLimit)
            {
                clock.TimeScale = WarpLimit;
                WarpClampCount++;
            }

            if (!FlightInput.IsTyping)
            {
                if (FlightInput.GetKeyDown(InputKey.Period)) IncreaseWarp();
                if (FlightInput.GetKeyDown(InputKey.Comma)) DecreaseWarp();
                if (FlightInput.GetKeyDown(InputKey.P)) TogglePause();
            }

            double real = Mathf.Min(Time.unscaledDeltaTime, (float)settings.maxRealDeltaTime);
            double startTime = clock.SimulationTime;
            OnRails = RailsBlocked = false;
            if (!clock.Paused && clock.TimeScale > settings.maxNumericWarp && real > 0.0)
            {
                if (TryRails(real * clock.TimeScale))
                {
                    EffectiveWarp = (clock.SimulationTime - startTime) / real;
                    return;
                }
                // Something must be integrated (engine, atmosphere): run at the numeric limit instead.
                RailsBlocked = true;
                double requested = clock.TimeScale;
                clock.TimeScale = settings.maxNumericWarp;
                StepsLastFrame = clock.Advance(Time.unscaledDeltaTime, Step);
                clock.TimeScale = requested;
            }
            else StepsLastFrame = clock.Advance(Time.unscaledDeltaTime, Step);
            EffectiveWarp = real > 0.0 ? (clock.SimulationTime - startTime) / real : 0.0;
        }

        bool TryRails(double request)
        {
            double horizon = request;
            for (int i = 0; i < bodies.Count; i++)
            {
                if (!(bodies[i] is IRailsBody rails)) return false;
                horizon = System.Math.Min(horizon, rails.RailsHorizon(request));
            }
            long steps = (long)System.Math.Floor(horizon / clock.FixedStep);
            if (steps < 2) return false;
            double time = clock.SimulationTime, dt = steps * clock.FixedStep;
            clock.AdvanceSteps(steps);
            for (int i = 0; i < bodies.Count; i++) ((IRailsBody)bodies[i]).RailsStep(time, dt, clock.FixedStep);
            StepsLastFrame = 1;
            OnRails = true;
            return true;
        }

        void Step(double time, double dt)
        {
            for (int i = 0; i < bodies.Count; i++) bodies[i].SimulationStep(time, dt);
        }
    }
}
