using SpaceSim.Flight;
using SpaceSim.Localization;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Manual rocket controls. Everything the rocket does comes from these inputs and physics — there is no
    /// guidance or autopilot. Rates are in real time (not scaled by time warp).
    ///   W / S — pitch down towards the horizon / back up (0° = vertical, 90° = horizontal)
    ///   A / D — heading left / right (90° = east)
    ///   Left Shift / Left Ctrl — throttle up / down;  Z — full throttle;  X — engine cut-off
    ///   Enter — ignite (same as the ЗАПУСК button);  Space — separate the current stage
    ///   1 — release the parachutes;  2 — extend / retract the legs;  3 — RCS on / off
    /// Every vehicle turns at the base rates; attached RCS (burning propellant) and grid fins (in dense enough air) turn it faster.
    /// </summary>
    [DefaultExecutionOrder(-120)]
    public sealed class RocketControls : MonoBehaviour
    {
        [SerializeField] float pitchRate = 12f;     // deg/s
        [SerializeField] float headingRate = 20f;   // deg/s
        [SerializeField] float throttleRate = 0.5f; // per second

        FlightBody body;
        FlightStateController state;

        /// <summary>The keys belong to someone else now (the astronaut on foot).</summary>
        public bool Suspended { get; set; }

        public void Initialize(FlightBody flightBody, FlightStateController flightState)
        {
            if (body != null) body.SystemMessage -= OnSystemMessage;
            body = flightBody;
            state = flightState;
            if (body != null) body.SystemMessage += OnSystemMessage;
        }

        void OnSystemMessage(string message) => state?.Notify(message);

        void OnDestroy()
        {
            if (body != null) body.SystemMessage -= OnSystemMessage;
        }

        void Update()
        {
            var r = body != null ? body.Rocket : null;
            if (r == null || FlightInput.IsTyping || Suspended || body.Destroyed) return;
            float dt = Time.unscaledDeltaTime;

            bool pitching = FlightInput.GetKey(InputKey.W) || FlightInput.GetKey(InputKey.S);
            bool yawing = FlightInput.GetKey(InputKey.A) || FlightInput.GetKey(InputKey.D);
            FlightSystems systems = body.Simulation.Systems;
            double q = state != null ? state.Telemetry.DynamicPressure : 0.0;
            double pitchSpeed = pitchRate, headingSpeed = headingRate;
            if (systems != null && !body.Simulation.IsLanded)
            {
                // One RCS firing serves both axes; the extra rate scales with the base rate of each axis.
                double rate = systems.CommandTurn(r, pitchRate, q, pitching || yawing, dt);
                pitchSpeed = rate;
                headingSpeed = rate * headingRate / pitchRate;
            }
            if (FlightInput.GetKey(InputKey.W)) r.PitchDegrees += pitchSpeed * dt;
            if (FlightInput.GetKey(InputKey.S)) r.PitchDegrees -= pitchSpeed * dt;
            if (FlightInput.GetKey(InputKey.A)) r.HeadingDegrees -= headingSpeed * dt;
            if (FlightInput.GetKey(InputKey.D)) r.HeadingDegrees += headingSpeed * dt;

            if (r.CanThrottle)
            {
                if (FlightInput.GetKey(InputKey.LeftShift)) r.Throttle += throttleRate * dt;
                if (FlightInput.GetKey(InputKey.LeftCtrl)) r.Throttle -= throttleRate * dt;
                if (FlightInput.GetKeyDown(InputKey.Z)) r.Throttle = 1.0;
            }
            else if (FlightInput.GetKeyDown(InputKey.LeftShift) || FlightInput.GetKeyDown(InputKey.LeftCtrl) || FlightInput.GetKeyDown(InputKey.Z))
                state.Notify(Loc.T("Твердотопливный двигатель: тягу нельзя регулировать — он работает на полной тяге, пока не выгорит заряд."));

            if (FlightInput.GetKeyDown(InputKey.X)) CutOff();
            if (FlightInput.GetKeyDown(InputKey.Return)) Ignite();
            if (FlightInput.GetKeyDown(InputKey.Space)) Separate();
            if (FlightInput.GetKeyDown(InputKey.Alpha1)) ReleaseParachutes();
            if (FlightInput.GetKeyDown(InputKey.Alpha2)) ToggleLegs();
            if (FlightInput.GetKeyDown(InputKey.Alpha3)) ToggleRcs();
        }

        /// <summary>Engine cut-off. A burning solid motor cannot be stopped: nothing changes and the pilot is told so.</summary>
        public void CutOff()
        {
            var r = body.Rocket;
            if (r == null) return;
            if (r.IsSolidBurning)
            {
                state.Notify(Loc.T("Твердотопливный двигатель нельзя заглушить: он горит, пока не выгорит заряд."));
                return;
            }
            r.EngineOn = false;
            state.Notify(Loc.T("Двигатель выключен."));
        }

        public void Ignite()
        {
            if (Suspended)
            {
                state.Notify(Loc.T("Нельзя: астронавт снаружи. Вернитесь в ракету (E у ракеты)."));
                return;
            }
            if (body.Launch())
                state.Notify(Loc.T("Зажигание: ") + body.Rocket.CurrentStage.name + Loc.T(", тяга ") + (body.Rocket.Throttle * 100.0).ToString("0") + " %." +
                             (body.Rocket.CurrentStage.IsSolid ? Loc.T(" Твердотопливный: не выключается и не дросселируется.") : ""));
            else state.Notify(Loc.T("Нечем запускаться: ступень пуста или отделены все ступени."));
        }

        public void Separate()
        {
            string name = body.Rocket.HasStages ? body.Rocket.CurrentStage.name : null;
            if (body.SeparateStage()) state.Notify(Loc.T("Отделена ") + name + "." + (body.Rocket.HasStages && !body.Rocket.EngineOn ? Loc.T(" Включите следующую ступень (Enter).") : ""));
        }

        public void ReleaseParachutes()
        {
            switch (body.ReleaseParachutes())
            {
                case ParachuteCommand.NoParachutes:
                    state.Notify(Loc.T("Парашютов на борту нет."));
                    break;
                case ParachuteCommand.AlreadyReleased:
                    state.Notify(Loc.T("Парашюты уже выпущены."));
                    break;
                default:
                    state.Notify(Loc.T("Парашюты выпущены: купола наполнятся, когда хватит воздуха. Слишком большой напор или скорость их порвут."));
                    break;
            }
        }

        public void ToggleLegs()
        {
            FlightSystems s = body.Simulation.Systems;
            bool want = s == null || !s.LegsDeployed;
            if (body.SetLegsDeployed(want)) state.Notify(want ? Loc.T("Опоры выпущены.") : Loc.T("Опоры убраны."));
            else state.Notify(Loc.T("Посадочных опор нет (или они отделены со ступенью)."));
        }

        public void ToggleRcs()
        {
            FlightSystems s = body.Simulation.Systems;
            var r = body.Rocket;
            if (s == null || s.RcsThrust(r) <= 0.0)
            {
                state.Notify(Loc.T("Блоков РСУ нет (или они отделены со ступенью)."));
                return;
            }
            s.RcsEnabled = !s.RcsEnabled;
            state.Notify(s.RcsEnabled ? Loc.T("РСУ включена: поворот быстрее, расходуется топливо.") : Loc.T("РСУ выключена."));
        }
    }
}
