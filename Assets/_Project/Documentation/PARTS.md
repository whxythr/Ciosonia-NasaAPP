# PARTS — каталог деталей и контракт Core

Все значения в СИ. Формулы только в Core. Id деталей записаны в сохранениях игроков: **id не менять**.

## 1. Устройство каталога

| Что | Где | Как пользоваться |
|---|---|---|
| Категории (вкладки) | `ModuleCategory`: Cabin, Engine, FuelTank, Aerodynamics, Science, Auxiliary, Communication, Mechanics, Cargo | `ModuleCatalog.Categories` — порядок показа, `ModuleCatalog.CategoryName(c)` — подпись |
| Подгруппы (подвкладки) | `ModuleDefinition.Subgroup` → `ModuleSubgroup` | единственное свойство для UI; `ModuleCatalog.SubgroupsOf(c)` — порядок (пусто = подвкладок нет), `InSubgroup(c, s)`, `SubgroupName(s)` |
| Куда ставится | `ModuleDefinition.mount` → `ModuleMount`: StageEngine, StageTank, StageSide, Payload | `IsEngine`, `IsTank`, `IsPayload`, `IsStageAttachment`, `IsCabin`, `IsWrap`, `IsSampleCollector` |

Подгруппы заполняются из `cabinKind` (RemoteControl/Crewed), `engineKind` (LiquidChemical/Solid/Nuclear/Electric) и `aeroKind` (SideFin/TopFin/Rcs); у баков одна подгруппа LiquidFuel. У науки, вспомогательного, коммуникации, механики и грузовых подгрупп нет: вид детали внутри категории смотрите по `auxiliaryKind` (Parachute, LandingLegs, Shield, HeatShield, Light, PowerSource), `powerSource` (Solar, Steady, Battery), `mechanismKind` (Wheel, RoverChassis, RoverDrive, Drill, Manipulator, SampleCollector).

Деталь с креплением **StageSide** — это один комплект на всю ступень (четыре киля, четыре опоры, набор блоков РСУ), а не одна штука. Числа `finArea`, `rcsThrust`, `legCount` относятся ко всему комплекту.

Все новые детали бесплатны (`requiresPurchase = false`, `price = 0`). Чтобы продавать деталь, нужны цена, флаг и строка в `Shop.Items`.

## 2. Все детали

Isp в таблице: вакуум/у поверхности. Масса — сухая.

| id | Название | Категория | Подгруппа | Крепление | Ключевые числа |
|---|---|---|---|---|---|
| `core-probe` | Зонд (ядро) | Кабины | Дистанционного управления | Payload | 1 т; без экипажа; выдаёт 600 Вт; потребляет 150 Вт; 2000 км при 1 Вт |
| `cab-rc-small` | Зонд Ласточка | Кабины | Дистанционного управления | Payload | 150 кг; без экипажа; выдаёт 120 Вт; потребляет 40 Вт; 2000 км при 1 Вт |
| `cab-crew-1` | Капсула Чайка-1 | Кабины | С экипажем | Payload | 1,8 т; экипаж 1; выдаёт 400 Вт; потребляет 300 Вт; 10000 км при 5 Вт |
| `cab-crew-3` | Капсула Чайка-3 | Кабины | С экипажем | Payload | 5,8 т; экипаж 3; выдаёт 1,2 кВт; потребляет 900 Вт; 10000 км при 5 Вт |
| `cab-crew-7` | Кабина Альбатрос-7 | Кабины | С экипажем | Payload | 11 т; экипаж 7; выдаёт 3 кВт; потребляет 2,2 кВт; 10000 км при 5 Вт |
| `eng-sokol-1` | Сокол-1 | Двигатели | Жидкостные | StageEngine | 2,5 т; F 1,3 МН; Isp 330/295 с; керосин + O₂ |
| `eng-sokol-1m` | Сокол-1М | Двигатели | Жидкостные | StageEngine | 2,4 т; F 1,5 МН; Isp 338/305 с; керосин + O₂; магазин |
| `eng-vega-v` | Вега-В | Двигатели | Жидкостные | StageEngine | 1,6 т; F 700 кН; Isp 350/240 с; керосин + O₂ |
| `eng-hydra` | Гидра | Двигатели | Жидкостные | StageEngine | 300 кг; F 110 кН; Isp 450/280 с; H₂ + O₂ |
| `eng-iskra` | Искра | Двигатели | Жидкостные | StageEngine | 100 кг; F 30 кН; Isp 315/250 с; гиперголь |
| `eng-solid-s` | Стриж-Т | Двигатели | Твердотопливные | StageEngine | 140 кг; F 68 кН; Isp 292/250 с; твёрдое; заряд 2,01 т |
| `eng-solid-m` | Кречет-Т | Двигатели | Твердотопливные | StageEngine | 4,1 т; F 1,65 МН; Isp 280/250 с; твёрдое; заряд 49 т |
| `eng-solid-l` | Беркут-Т | Двигатели | Твердотопливные | StageEngine | 11 т; F 4,5 МН; Isp 279/245 с; твёрдое; заряд 142 т |
| `eng-nuc-70` | Атом-70 | Двигатели | Ядерные | StageEngine | 3,5 т; F 73 кН; Isp 850/360 с; жидкий H₂ |
| `eng-nuc-250` | Атом-250 | Двигатели | Ядерные | StageEngine | 18 т; F 246 кН; Isp 850/380 с; жидкий H₂ |
| `eng-el-hall` | Луч-Х | Двигатели | Электрические | StageEngine | 110 кг; F 0,6 Н; Isp 2800/0 с; ксенон; η 0,6; 13,7 кВт |
| `eng-el-ion` | Луч-И | Двигатели | Электрические | StageEngine | 100 кг; F 0,236 Н; Isp 4150/0 с; ксенон; η 0,7; 6,86 кВт |
| `eng-el-mpd` | Луч-М | Двигатели | Электрические | StageEngine | 620 кг; F 5,7 Н; Isp 4900/0 с; ксенон; η 0,7; 196 кВт |
| `tank-k100` | Бак K-100 | Топливные баки | Жидкое топливо | StageTank | 4 т; 100 т керосин + O₂ |
| `tank-k140` | Расширенный бак K-140 | Топливные баки | Жидкое топливо | StageTank | 5,2 т; 140 т керосин + O₂; магазин |
| `tank-k40` | Бак K-40 | Топливные баки | Жидкое топливо | StageTank | 2,4 т; 40 т керосин + O₂ |
| `tank-k10` | Бак K-10 | Топливные баки | Жидкое топливо | StageTank | 800 кг; 10 т керосин + O₂ |
| `tank-h20` | Бак H-20 | Топливные баки | Жидкое топливо | StageTank | 2 т; 20 т H₂ + O₂ |
| `tank-n2` | Бак N-2 | Топливные баки | Жидкое топливо | StageTank | 200 кг; 2 т гиперголь |
| `tank-lh8` | Бак LH-8 | Топливные баки | Жидкое топливо | StageTank | 1,1 т; 8 т жидкий H₂ |
| `tank-lh25` | Бак LH-25 | Топливные баки | Жидкое топливо | StageTank | 3,2 т; 25 т жидкий H₂ |
| `tank-xe1` | Бак Xe-1 | Топливные баки | Жидкое топливо | StageTank | 120 кг; 1 т ксенон |
| `tank-xe8` | Бак Xe-8 | Топливные баки | Жидкое топливо | StageTank | 800 кг; 8 т ксенон |
| `fin-side-s` | Стабилизаторы КС-1 | Аэродинамика | Боковые стабилизаторы | StageSide | 60 кг; 1,6 м² |
| `fin-side-l` | Стабилизаторы КС-2 | Аэродинамика | Боковые стабилизаторы | StageSide | 320 кг; 6 м² |
| `fin-top-s` | Решётчатые рули РР-1 | Аэродинамика | Верхние стабилизаторы | StageSide | 90 кг; 1,2 м² |
| `fin-top-l` | Решётчатые рули РР-2 | Аэродинамика | Верхние стабилизаторы | StageSide | 900 кг; 6,4 м² |
| `rcs-100` | Блок РСУ-100 | Аэродинамика | РСУ | StageSide | 16 кг; 100 Н |
| `rcs-1800` | Блок РСУ-1800 | Аэродинамика | РСУ | StageSide | 90 кг; 1,8 кН |
| `sci-sp1` | Спектрометр СП-1 | Наука | — | Payload | 80 кг; минералогия; потребляет 40 Вт |
| `sci-meteo` | Метеокомплекс МЕТЕО | Наука | — | Payload | 25 кг; температура, давление, состав атмосферы; потребляет 10 Вт |
| `sci-therm` | Термометр ТМ-1 | Наука | — | Payload | 2 кг; температура; потребляет 2 Вт |
| `sci-baro` | Барометр БМ-1 | Наука | — | Payload | 2 кг; давление; потребляет 2 Вт |
| `sci-grav` | Гравиметр ГР-1 | Наука | — | Payload | 30 кг; гравиметр; потребляет 15 Вт |
| `sci-seis` | Сейсмометр СМ-1 | Наука | — | Payload | 25 кг; сейсмометр; потребляет 8 Вт |
| `sci-dosi` | Дозиметр ДЗ-1 | Наука | — | Payload | 5 кг; радиация; потребляет 4 Вт |
| `sci-magn` | Магнитометр МГ-1 | Наука | — | Payload | 5 кг; магн. поле; потребляет 6 Вт |
| `sci-cam` | Камера КМ-1 | Наука | — | Payload | 20 кг; съёмка; потребляет 20 Вт |
| `prot-rad1` | Радиационная защита Р-1 | Вспомогательное оборудование | — | Payload | 450 кг; радиация 1; магазин |
| `prot-rad2` | Радиационная защита Р-2 | Вспомогательное оборудование | — | Payload | 900 кг; радиация 2; магазин |
| `prot-acid1` | Кислотостойкое покрытие К-1 | Вспомогательное оборудование | — | Payload | 300 кг; кислота 1; магазин |
| `chute-10` | Парашют ПС-10 | Вспомогательное оборудование | — | Payload | 15 кг; A 78,5 м², Cd 0,8 |
| `chute-25` | Парашют ПС-25 | Вспомогательное оборудование | — | Payload | 120 кг; A 490 м², Cd 0,8 |
| `chute-45` | Парашют ПС-45 | Вспомогательное оборудование | — | Payload | 400 кг; A 1590 м², Cd 0,8 |
| `leg-light` | Посадочные опоры ОП-1 | Вспомогательное оборудование | — | StageSide | 80 кг; 4 опоры, до 3 м/с |
| `leg-heavy` | Посадочные опоры ОП-2 | Вспомогательное оборудование | — | StageSide | 2 т; 4 опоры, до 6 м/с |
| `hs-light` | Термощит ТЩ-1 | Вспомогательное оборудование | — | Payload | 200 кг; 1,5 МВт/м² |
| `hs-heavy` | Термощит ТЩ-5 | Вспомогательное оборудование | — | Payload | 850 кг; 5 МВт/м² |
| `light-s` | Фонарь ФН-1 | Вспомогательное оборудование | — | Payload | 1,5 кг; 1500 лм; потребляет 15 Вт |
| `light-l` | Прожектор ПЖ-20 | Вспомогательное оборудование | — | Payload | 8 кг; 20000 лм; потребляет 200 Вт |
| `pwr-sol-2` | Солнечная панель СБ-2 | Вспомогательное оборудование | — | Payload | 25 кг; солнечная 2 кВт |
| `pwr-sol-20` | Солнечная панель СБ-20 | Вспомогательное оборудование | — | Payload | 220 кг; солнечная 20 кВт |
| `pwr-sol-200` | Солнечная панель СБ-200 | Вспомогательное оборудование | — | Payload | 2 т; солнечная 200 кВт |
| `pwr-rtg` | РИТЭГ-300 | Вспомогательное оборудование | — | Payload | 57 кг; 300 Вт |
| `pwr-reactor` | Реактор РК-40 | Вспомогательное оборудование | — | Payload | 2,5 т; 40 кВт |
| `pwr-bat-10` | Батарея АБ-10 | Вспомогательное оборудование | — | Payload | 65 кг; разряд 10 кВт; 10 кВт·ч |
| `pwr-bat-100` | Батарея АБ-100 | Вспомогательное оборудование | — | Payload | 600 кг; разряд 100 кВт; 100 кВт·ч |
| `nav-nk1` | Навигационный компьютер НК-1 | Коммуникация | — | Payload | 120 кг; потребляет 30 Вт |
| `nav-sd2` | Звёздный датчик СД-2 | Коммуникация | — | Payload | 60 кг; потребляет 15 Вт |
| `ant-omni` | Антенна АН-1 | Коммуникация | — | Payload | 3 кг; потребляет 10 Вт; 10000 км при 5 Вт |
| `ant-dish-s` | Тарелка ТР-1 | Коммуникация | — | Payload | 12 кг; потребляет 40 Вт; 50 млн км при 15 Вт |
| `ant-dish-l` | Тарелка ТР-4 | Коммуникация | — | Payload | 55 кг; потребляет 80 Вт; 5000 млн км при 25 Вт |
| `ant-relay` | Ретранслятор РТ-2 | Коммуникация | — | Payload | 35 кг; потребляет 110 Вт; 200 млн км при 30 Вт; ретранслятор |
| `col-pb1` | Пробоотборник ПБ-1 | Механика | — | Payload | 150 кг; потребляет 40 Вт; 10 кг образцов: камни, грунт |
| `col-pb2` | Пробоотборник ПБ-2 | Механика | — | Payload | 260 кг; потребляет 60 Вт; 30 кг образцов: камни, грунт, жидкости, кислоты, редкие; магазин |
| `wheel-s` | Колесо КЛ-40 | Механика | — | Payload | 9 кг; до 1,2 кН, 3 м/с |
| `wheel-l` | Колесо КЛ-100 | Механика | — | Payload | 45 кг; до 8 кН, 5 м/с |
| `rover-chassis` | Шасси ШС-1 | Механика | — | Payload | 180 кг |
| `rover-drive` | Привод ПД-1 | Механика | — | Payload | 30 кг; потребляет 300 Вт |
| `drill-s` | Бур БУ-1 | Механика | — | Payload | 20 кг; потребляет 150 Вт; глубина 0,15 м; 2 кг образцов: грунт |
| `drill-l` | Бур БУ-2 | Механика | — | Payload | 90 кг; потребляет 500 Вт; глубина 2 м; 8 кг образцов: камни, грунт |
| `arm-mn1` | Манипулятор МН-1 | Механика | — | Payload | 45 кг; потребляет 60 Вт |
| `cargo-2` | Контейнер КГ-2 | Грузовые модули | — | Payload | 80 кг; 2 м³ |
| `cargo-12` | Отсек КГ-12 | Грузовые модули | — | Payload | 450 кг; 12 м³ |
| `cargo-40` | Отсек КГ-40 | Грузовые модули | — | Payload | 1,2 т; 40 м³ |
| `cargo-press-20` | Гермоотсек ГО-20 | Грузовые модули | — | Payload | 2,5 т; потребляет 300 Вт; 20 м³, герметичный |
Всего деталей: 81

## 3. Поля `ModuleDefinition`

| Группа | Поля (единицы) |
|---|---|
| Общие | `id`, `name`, `description`, `category`, `mount`, `dryMass` [кг], `length` [м], `diameter` [м, только для 3D], `price`, `requiresPurchase` |
| Кабина | `cabinKind`, `crewCapacity` [чел.] |
| Двигатель | `engineKind`, `propellant`, `vacuumThrust` [Н], `vacuumIsp`, `seaLevelIsp` [с], `solidPropellantMass` [кг], `electricEfficiency` [0..1] |
| Бак | `propellant`, `propellantCapacity` [кг] |
| Аэродинамика | `aeroKind`, `finArea` [м²], `rcsThrust` [Н] |
| Вспомогательное | `auxiliaryKind`, `parachuteArea` [м²], `parachuteCd`, `legCount` [шт.], `legTouchdownSpeed` [м/с], `heatFluxLimit` [Вт/м²], `lightOutput` [лм], `radiationShielding`, `acidResistance` |
| Питание | `powerSource`, `powerOutput` [Вт; у батареи — предельный разряд], `powerDraw` [Вт], `batteryEnergy` [Дж] |
| Связь | `antennaRange` [м], `antennaPower` [Вт], `relay` |
| Механика | `mechanismKind`, `wheelLoad` [Н на колесо], `wheelMaxSpeed` [м/с], `drillDepth` [м], `sampleCapacity` [кг], `sampleKinds` |
| Груз | `cargoVolume` [м³], `pressurized` |
| Наука | `measurements` (флаги `ScienceMeasurement`), `capabilities`, `researchMultiplier` |

Новые `PropellantType`: Solid, LiquidHydrogen, Xenon. Существующие значения не менялись.

## 4. Чертёж

`StageBlueprint.attachments` — список id деталей StageSide. Они прибавляют массу своей ступени и улетают с ней, длину ступени не меняют. В старых сохранениях поля нет: `ProfileMigration.Migrate` вызывает `RocketBlueprint.EnsureLists()` и получает пустой список; `BlueprintCompiler` и `Clone()` также терпят null.

Деталь не на своём месте даёт `WrongMount` и пропускается (масса не считается).

Кабина: нужна хотя бы одна любого типа. Если кабин нет совсем, компилятор ставит `core-probe` и сообщает `MissingCore`. Несколько кабин разрешены.

Раскладка (`RocketLayout`, только визуал): комплект StageSide даёт по слоту на каждую штуку (`PartRole.SideFin`, `GridFin`, `RcsBlock`, `LandingLeg`; `Index` — позиция в `attachments`, `Element` — номер штуки). Кили и опоры внизу ступени, решётчатые рули и РСУ вверху. Головной отсек снизу вверх: переходник, ровер (`RoverChassis`, `RoverDrive`, `Wheel`), зонд (`ProbeBus`) с оболочками (`Wrap`), грузовые отсеки (`CargoBay`), термощит (`HeatShield`), капсулы с экипажем (`Capsule`), парашюты (`Parachute`) на носу. Остальное — `Instrument` снаружи корпуса; наука, навигация и батареи могут быть внутри открытого отсека (`PartPlacement.Inside`). `PreviewSlot(m)` — слот одной детали для миниатюр, `DeployedLegDrop(leg)` — насколько развёрнутая опора уходит ниже низа ступени.

## 5. Двигатели в полёте и анализе

`RocketStage`: новые `engineKind`, `electricPowerDemand` [Вт]; `IsSolid`, `IsElectric`. `vacuumThrust` остаётся **полной** тягой; реальную даёт `RocketState` и `StageReport.VacuumThrust`.

`RocketConfiguration`: `solarPower`, `steadyPower`, `systemsPower`, `batteryPower` [Вт], `batteryEnergy` [Дж], `ElectricBalance(stage, energy, flux)`, `BurnablePropellant(stage, propellant, ref energy, out burnTime, flux)`. Эти две функции считают ограничение и для `RocketState`, и для `RocketDesignAnalysis`, поэтому ангар и полёт совпадают.

`RocketState`:

| Член | Смысл |
|---|---|
| `CanThrottle` | false у твердотопливной ступени |
| `Throttle` | у твердотопливной читается 1, запись игнорируется |
| `EngineOn = false` | у горящего твердотопливного двигателя игнорируется |
| `IsSolidBurning` | твердотопливный двигатель зажжён и заряд не кончился |
| `Extinguish()` | останавливает любой двигатель (авария, не действие пилота) |
| `PowerFraction` | доля полной тяги, которую даёт электропитание, 0..1 |
| `Electrics` | `PowerBalance` текущей ступени |
| `BatteryEnergy` | остаток в батареях [Дж] |
| `SolarFluxRatio` | поток от звезды относительно родной планеты; по умолчанию 1 |
| `UpdateBatteries(dt)` | разряд и заряд батарей; во время работы электродвигателя вызывается из `Burn` |

Твердотопливный: заряд входит в топливо ступени, баки не нужны; бак на такой ступени — мёртвый груз (`IncompatiblePropellant`).

Электрический: `P = F·g₀·Isp/(2·η)` (`RocketEquations.ElectricPower`). Реальная тяга = полная × min(дроссель, `PowerFraction`); Isp не меняется, поэтому Δv от мощности не зависит, меняется только время работы. Батареи тратятся до нуля, затем остаются только генераторы; если их нет, горение прекращается, остаток топлива остаётся в баке.

`StageReport`: новые `EngineKind`, `IsSolid`, `NominalVacuumThrust`, `ElectricPowerDemand`, `PowerFraction`, `BurnablePropellant`.

## 6. Чистые функции `SpaceSim.Physics.PartPhysics`

| Функция | Формула |
|---|---|
| `ParachuteDescentSpeed(m, g, ρ, Cd, A)`, `(m, g, ρ, CdA)` | `v = √(2mg/(ρ·Cd·A))`, без воздуха или купола +∞ |
| `AssessTouchdown(v↓, legCount, legSpeed)` → `TouchdownCheck` | Soft/Hard/Crash; меньше 3 опор — корпус, 1,5 м/с; авария = предел × 2,5 |
| `FinNormalForceSlope(S, A)` | `C_Nα = (S/A)·½·2π·AR/(2+√(4+AR²))`, AR = 2 |
| `CenterOfPressure(xКорпус, xКили, S, A)` | центр давления, взвешенный по нормальной силе |
| `StaticMarginCalibers(xМасс, xКорпус, xКили, S, A, D)` | `(xМасс − xДавл)/D`; устойчиво от `StableMarginCalibers` = 1 |
| `FinRestoringTorque(q, S, A, α, плечо)` | `M = q·A·C_Nα·α·плечо` |
| `RcsTorque(F, r)` | `τ = F·r` |
| `CommRange(R₀, P₀, P)` | `R = R₀·√(P/P₀)` |
| `SignalStrength(R, d)` | `min(1, (R/d)²)` |
| `CombineRange(a, b)` | `√(a²+b²)` |
| `ElectricBalance(...)` → `PowerBalance` | `Generation`, `Consumption`, `NetGeneration`, `AvailableForEngines`, `EngineFraction`, `SystemsCovered` |
| `SolarFluxRatio(d, d₀)` | `(d₀/d)²` |
| `WheelsCarry(нагрузка, m, g)` | `m·g ≤ нагрузка` |

## 7. `VehicleCapabilities`

| Что | Поля |
|---|---|
| Управление | `CrewCapacity`, `RemoteCabins`, `CrewedCabins`, `HasCabin`, `ControlKind` |
| Аэродинамика и посадка (весь стек) | `FinArea`, `SideFinArea`, `TopFinArea`, `RcsThrust`, `LegCount`, `LegTouchdownSpeed`, `Stages[i]` (`StageHardware`) |
| После разделения | `HardwareFrom(stageIndex)` — то, что ещё прикреплено |
| Спасение | `ParachuteCount`, `ParachuteArea`, `ParachuteDragArea`, `HeatShieldFluxLimit` |
| Свет и связь | `LightOutput`, `CommRange`, `AntennaPower`, `HasRelay` |
| Питание | `SolarPower`, `SteadyPower`, `GeneratedPower`, `PowerConsumption`, `BatteryPower`, `BatteryEnergy`, `EnginePowerDemand`, `Electrics` |
| Ровер и буры | `WheelCount`, `WheelLoad`, `WheelMaxSpeed`, `HasRoverChassis`, `HasRoverDrive`, `RoverAssembled`, `RoverCanDrive(m, g)`, `DrillCount`, `DrillDepth`, `DrillKinds`, `ManipulatorCount` |
| Груз и наука | `CargoVolume`, `PressurizedVolume`, `Measurements`, `CanMeasure(flags)` |
| Как раньше | `Flags`, `RadiationShielding`, `AcidResistance`, `SampleCapacity`, `SampleKinds`, `ResearchMultiplier` |

`FlightBody.Capabilities` при повреждённых приборах отдаёт почти пустой объект; для оборудования берите `DesignCapabilities`.

Новые `BlueprintIssueCode`: `WrongMount`, `NoElectricPower`, `InsufficientElectricPower`, `PowerDeficit`, `RoverIncomplete`.

## 8. Детали в полёте

Состояние оборудования в полёте держит `SpaceSim.Flight.FlightSystems` (Core), его создаёт `FlightBody` из `DesignCapabilities` при каждой новой ракете и передаёт через смену сферы влияния (`VehicleCarry.Systems`). Пока `FreeFlightSimulation.Systems == null`, полёт идёт ровно как без этих деталей; у проекта без новых деталей результат тот же до бита (тест `Systems_OfADesignWithoutNewParts_ChangeNothingInFlight`).

| Что | Как работает | Где |
|---|---|---|
| Авария | `FlightBody` вызывает `Rocket.Extinguish()`: горящий твердотопливный двигатель тоже гаснет | `FlightBody.CheckTouchdown` |
| Отсечка и дроссель | у горящего твердотопливного X ничего не делает и сообщает, что заглушить нельзя; Shift/Ctrl/Z сообщают, что тяга не регулируется | `RocketControls.CutOff` |
| Опоры | клавиша 2; `LandingPhysics.Assess(…, designHasLegs, attachedLegCount, legSpeed, legsDeployed)`. Проект **без опор** сохраняет прежние встроенные пределы (6/15 м/с). С опорами по вертикали решает `PartPhysics.AssessTouchdown` по `HardwareFrom(StageIndex)`: выпущены → предел опор, убраны или отделены → корпус 1,5 м/с; пределы вбок, наклона и склона прежние | `FlightSystems.AssessLanding` |
| Парашюты | каждый купол отдельно: Stowed → Deployed → Torn. Наполняется линейно за `ParachuteInflationTime` = 3 с при q ≥ 5 Па; сопротивление `½ρv²·Σ(C_d·A)·opening` в `DragAcceleration`. Рвётся, если q > `parachuteMaxDynamicPressure` или v > `parachuteMaxSpeed` (новые поля детали: ПС-10 6 кПа/350 м/с, ПС-25 4 кПа/250 м/с, ПС-45 2,5 кПа/180 м/с). В вакууме купол висит пустым и не рвётся; сообщение пилоту через `FlightBody.SystemMessage` | `FlightSystems.UpdateParachutes`, `PartPhysics.ParachuteTears/Inflation/Drag` |
| Поворот без РСУ | кинематический: 12°/с по тангажу, 20°/с по курсу в реальном времени (`RocketControls`), так и осталось | — |
| РСУ | пока зажата клавиша поворота и РСУ включена: ε = F·(L/2)/I, I = m(L²/12 + r²/4); скорость поворота ω₀ + ε·1 с, не выше 60°/с. Расход ṁ = F/(220·g₀) из нижней присоединённой жидкостной ступени (не из твёрдого заряда), опустевшая ступень гаснет | `FlightSystems.CommandTurn`, `RocketState.DrawRcsPropellant` |
| Решётчатые рули | добавляют момент `FinRestoringTorque(q, S, A, 15°, L/2)` только при q ≥ 1 кПа | `PartPhysics.GridFinControlTorque` |
| Боковые стабилизаторы | флюгерная устойчивость: ось поворачивается к набегающему потоку со скоростью α·√(q·A·C_Nα·(L/2)/I) (физика, не управление); без килей ориентация не меняется | `PartPhysics.WeathervaneRate`, `FreeFlightSimulation.Weathervane` |
| Нагрев | поток Саттона–Грейвса считается в шаге физики (`FlightSystems.HeatFlux`). Допустимо `max(HullHeatFluxLimit = 1 МВт/м², щит)`. Выше — потеря целостности оборудования `0,02·(q̇/q̇_max − 1)` в секунду (`EquipmentCondition.Damage`), не разрушение. Пологий возврат с НОО (< 1 МВт/м²) без щита не меняется; крутой и межпланетный вход без щита теперь повреждает приборы | `FlightSystems.UpdateHeat`, `PartPhysics.HeatDamagePerSecond` |
| Питание | `SolarFluxRatio` по расстоянию до звезды относительно планеты `home` (у луны — её планеты), каждый шаг; `UpdateBatteries(dt)` после каждого шага и на «рельсах» (для электроступени в шаге — по-прежнему из `Burn`) | `FlightBody.UpdateSolarFlux`, `FreeFlightSimulation.UpdateEquipment` |
| HUD | тяга сейчас/макс. в мН/Н/кН/МН, расход в мг/с, г/с, кг/с; баланс мощности и батареи; опоры, парашюты, РСУ, стабилизаторы; нагрев «из допустимого» | `FlightHUD.DrawSystems` |

Клавиши (переназначаемые, группа «РАКЕТА»): **1** — раскрыть парашюты, **2** — выпустить/убрать опоры, **3** — РСУ вкл./выкл. Все буквы уже заняты (Q/Y/5 — ровер, T — кабина). Вид: `RocketModel.SetLegsDeployed`, `SetParachutesDeployed(released, opening)` вызываются из `FlightBody.Update` только при изменении.

Мост: `chute`, `legs [on|off]`, `rcs [on|off]`, `systems`.

Ещё не сделано: свет и связь в полёте; РСУ не даёт поступательного движения; ориентация в целом остаётся кинематической (без неустойчивости бескилевой ракеты, иначе старые проекты летали бы иначе). Значение `ОП-1` (3 м/с) ниже встроенного предела проекта без опор (6 м/с) — это вопрос баланса каталога.

## 9. Малая тяга

- `TimeWarpSituation.ThrustAcceleration`; `TimeWarpPolicy.IsLowThrust` — двигатель работает и ускорение 0 < a ≤ 0,01 м/с² (≈ 1 мg₀). На безопасной траектории выше зоны или вокруг звезды предел `LowThrustWarp` = ×10 000, зона `LowThrustFlight` («малая тяга на безопасной траектории»). Химические двигатели остаются на ×100.
- Выше ×1000 `SimulationController` идёт через «рельсы». Для малой тяги `FreeFlightSimulation.RailsHorizon` больше нуля (не длиннее `0,9·MaxSubsteps·MaxStableStep`), а `RailsStep` при работающем двигателе делает обычное численное интегрирование длинными кусками с подшагами (`Step`). Тест `Warp_LowThrustLongSteps_MatchFineSteps`: 1000 с одним вызовом против 50 000 шагов по 0,02 с — расхождение < 1 м, топливо совпадает.
- Холловский двигатель на 2 т: 1 км/с за ≈ 39 суток, при ×10 000 это около 6 мин реального времени.
- Планировщик по-прежнему считает импульс; в HUD (панель манёвра) и в окне навигации для электроступени выводится предупреждение, что расчёт приблизительный и разгон идёт по спирали.
