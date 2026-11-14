using SpaceSim.Localization;
using SpaceSim.Rover;
using SpaceSim.Units;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// A small overlay for the rover and its equipment: speed and its limit on this grade, slope, drive power and the energy
    /// used, the batteries, the drill's progress and the lamps. Shown only on the ground and only for a vehicle that has such parts.
    /// </summary>
    public sealed class RoverHUD : MonoBehaviour
    {
        RoverController rover;
        FlightBody body;
        PlanetRuntime planet;
        GUIStyle note;

        public void Initialize(RoverController controller, FlightBody flightBody, PlanetRuntime planetRuntime)
        {
            rover = controller;
            body = flightBody;
            planet = planetRuntime;
        }

        void OnGUI()
        {
            if (rover == null || body == null || body.Simulation == null || planet == null) return;
            if (FlightInput.PauseMenuOpen || FlightInput.MapOpen || body.VisualHidden || FlightHUD.CabinViewClear) return;
            var sim = body.Simulation;
            VehicleCapabilities caps = body.DesignCapabilities;
            bool roverParts = caps.WheelCount > 0 || caps.HasRoverChassis || caps.HasRoverDrive;
            bool drill = caps.DrillCount > 0, lamps = rover.Lights != null && rover.Lights.On;
            // Only on the ground and only for a vehicle with such parts (the flight HUD owns the screen in the air).
            if (!sim.IsLanded || body.Destroyed || rover.Eva) return;
            if (!roverParts && !drill && !lamps) return;

            HudStyle.Begin();
            if (note == null) note = new GUIStyle(HudStyle.Small) { wordWrap = true };
            Vector2 screen = HudStyle.VirtualScreen;
            float height = rover.Active ? 132f : 74f;
            GUILayout.BeginArea(new Rect(screen.x * 0.5f - 290f, screen.y - 260f - height, 580f, height));
            GUILayout.BeginVertical(HudStyle.Panel);
            if (rover.Active) DrivePanel();
            else
            {
                GUILayout.Label(Loc.T("ОБОРУДОВАНИЕ"), HudStyle.Title);
                string hint = "";
                if (roverParts)
                {
                    string can = rover.CanDrive();
                    hint += can.Length == 0 ? FlightInput.KeyLabel(InputKey.Q) + Loc.T(" — режим ровера. ") : Loc.T("Ровер: ") + can + " ";
                }
                if (drill) hint += DrillLine() + " ";
                GUILayout.Label(hint + LampLine(), note);
            }
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        void DrivePanel()
        {
            RoverStep step = rover.LastStep;
            RoverSpec spec = rover.Spec;
            GUILayout.Label(Loc.T("РОВЕР · ") + Loc.T(planet.Definition.displayName), HudStyle.Title);
            GUILayout.Label(Loc.T("Скорость ") + UnitFormatter.Number(rover.Motion.Speed, 2) + Loc.T(" м/с · предел здесь ") + UnitFormatter.Number(step.MaxSpeed, 2) +
                            Loc.T(" м/с (колёса ") + UnitFormatter.Number(spec.WheelMaxSpeed, 1) + Loc.T(") · уклон по курсу ") + UnitFormatter.Number(rover.GradeDegrees, 1) +
                            Loc.T("°, склон ") + UnitFormatter.Number(rover.SlopeDegrees, 1) + Loc.T("° (подъём до ") + UnitFormatter.Number(RoverKinematics.ClimbLimitDegrees, 0) +
                            Loc.T("°, держит до ") + UnitFormatter.Number(RoverKinematics.HoldLimitDegrees, 0) + "°) · " + RoverKinematics.Describe(step.Mode, spec, rover.Gravity) +
                            (rover.Blocked.Length > 0 ? " · " + rover.Blocked : ""), note);
            var r = body.Rocket;
            string battery = r != null && r.Configuration.batteryEnergy > 0.0
                ? Loc.T(" · батареи ") + UnitFormatter.Number(r.BatteryEnergy / 3.6e6, 2) + " / " + UnitFormatter.Number(r.Configuration.batteryEnergy / 3.6e6, 2) + Loc.T(" кВт·ч")
                : Loc.T(" · батарей нет");
            GUILayout.Label(Loc.T("Привод ") + UnitFormatter.Number(step.Power, 0) + " / " + UnitFormatter.Number(spec.DrivePower * rover.Supply, 0) + Loc.T(" Вт · израсходовано ") +
                            UnitFormatter.Number(rover.Motion.EnergyUsed / 3600.0, 1) + Loc.T(" Вт·ч за ") + UnitFormatter.Number(rover.Motion.Distance, 0) + Loc.T(" м") + battery +
                            (rover.Supply < 1.0 ? Loc.T(" · питания хватает на ") + UnitFormatter.Number(rover.Supply * 100.0, 0) + " %" : ""), note);
            GUILayout.Label(Loc.T("Масса ") + UnitFormatter.Number(spec.Mass, 0) + Loc.T(" кг · вес ") + UnitFormatter.Number(spec.Mass * rover.Gravity / 1000.0, 2) +
                            Loc.T(" кН из ") + UnitFormatter.Number(spec.WheelLoad / 1000.0, 1) + Loc.T(" кН · ") + RoverController.MoveKeys() + Loc.T(" — газ/руль, ") +
                            FlightInput.KeyLabel(InputKey.E) + Loc.T(" — манипулятор, ") + FlightInput.KeyLabel(InputKey.Q) + Loc.T(" — выйти · ") + DrillLine() + " " + LampLine(), note);
        }

        string DrillLine()
        {
            DrillRig d = rover.Drill;
            if (d == null || body.DesignCapabilities.DrillCount <= 0) return "";
            if (d.Busy)
                return Loc.T("Бурение ") + UnitFormatter.Number(d.Progress * 100.0, 0) + Loc.T(" % (") + UnitFormatter.Number(d.Current.Depth, 2) + Loc.T(" м, осталось ") +
                       UnitFormatter.Number((1.0 - d.Progress) * d.Duration, 0) + Loc.T(" с).");
            return FlightInput.KeyLabel(InputKey.Alpha5) + Loc.T(" — бурить до ") + UnitFormatter.Number(body.DesignCapabilities.DrillDepth, 2) + Loc.T(" м.");
        }

        string LampLine()
        {
            VehicleLights l = rover.Lights;
            if (l == null || l.TotalFlux <= 0.0) return "";
            return Loc.T("Фонари ") + (l.On ? Loc.T("вкл") : Loc.T("выкл")) + " (" + UnitFormatter.Number(l.TotalFlux, 0) + Loc.T(" лм) — ") + FlightInput.KeyLabel(InputKey.Alpha4) + ".";
        }
    }
}
