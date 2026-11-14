# Surface robot (EVA)

The EVA is done by a robot instead of a suited astronaut.

## Model

- Asset Store package **Robot Sphere** (content id 136226). It is not in the repository: import it through
  *Window ▸ Package Manager ▸ My Assets ▸ Robot Sphere ▸ Download ▸ Import*.
- After the import, `RobotSphereSetup` (Editor) runs once by itself, or run *SpaceSim ▸ Robot ▸ Set up Robot Sphere model*:
  - finds the robot prefab wherever the package landed (outside `Assets/_Project`);
  - converts the package's built-in Standard/Legacy materials to URP Lit (colour, albedo, normal, metallic/gloss,
    occlusion, emission, cutout/transparent);
  - reads the Animator controller's speed/grounded parameters and idle/walk/run/jump states;
  - writes everything to `Assets/_Project/Resources/Robot/RobotModelSettings.asset` (height, eye height, yaw offset,
    first-person hide radius and an optional rolling part can be tuned there).
- `RobotBodyModel` (Runtime) builds the robot from that asset: vendor scripts/physics stripped, materials converted at
  runtime as a safety net, shadows, first-person head hiding, lean tan θ = a/g, stride bob, landing dip, animator states
  paced to the walking speed.
- Without the asset (or with no prefab in it, or on any error) the EVA uses the procedural `AstronautModel`.
  *SpaceSim ▸ Robot ▸ Use procedural astronaut* switches back by hand.

## Status (`Scripts/Core/Exploration/RobotStatus.cs`)

Four independent values in [0, 1]; every limit and rate is data in `RobotDamageRates`:

| Value | Worn by | Default rate |
|---|---|---|
| Hull (integrity) | pressure above 2 atm; touchdowns/collisions above 3 m/s; hard landing or crash of the rocket with the robot aboard | 7·10⁻⁹ /(Pa·s); 6 %/(m/s) |
| Thermal control | surface temperature outside 90–480 K | 7·10⁻⁵ /(K·s) |
| Chemical protection | acid level at the surface | level 1 ≈ 5 min, level 2 ≈ 1.5 min |
| Visor | radiation reaching the ground; half the chemical rate; half of each impact | level 1 ≈ 1 h, level 2 ≈ 5 min |

Surface radiation/acid follow `HazardModel` (radiation only without air, acid only inside the air). Hull, thermal or
chemical at 0 disables the robot: the tether pulls it in and it cannot go out until a new rocket. Visor at 0 blinds it:
the view is black and only the status panel remains.

Exit limits (`RobotEva.CanExit`): ≤ 10 atm, 60–700 K, acid ≤ 1, radiation ≤ 2. Between the damage-free envelope and these
limits the robot may go out but wears. The suit rules (`SampleCollection.CanExit`) are unchanged.

## HUD

`RobotStatusPanel` (four bars with loss per minute, surroundings, time to failure/blindness) and `RobotVisorOverlay`
(vignette, cracks, sensor noise, darkening, flicker, blackout at 0 %).
