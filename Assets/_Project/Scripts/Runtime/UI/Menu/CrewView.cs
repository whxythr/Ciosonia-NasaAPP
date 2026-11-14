using System;
using System.Collections.Generic;
using SpaceSim.Crew;
using SpaceSim.Economy;
using SpaceSim.Localization;
using SpaceSim.Units;
using UnityEngine;
using UnityEngine.UIElements;
using static SpaceSim.Runtime.Menu.MenuKit;

namespace SpaceSim.Runtime.Menu
{
    /// <summary>
    /// The crew roster screen (Tab in flight, «Экипаж» in the main menu): filter tabs, the table of astronauts (role, level, flights,
    /// status) and the selected one's card — skills, flight statistics, current assignment — with «На обучение» and hiring.
    /// Data and rules come from <see cref="CrewRoster"/>; the host supplies the profile and saves the changes.
    /// </summary>
    public sealed class CrewView
    {
        public interface IHost
        {
            PlayerProfile Profile { get; }
            bool InFlight { get; }
            /// <summary>"FLT 13 · Стартовая ракета".</summary>
            string FlightTitle { get; }
            /// <summary>"T+ 00:03:12" or "".</summary>
            string FlightClock { get; }
            bool SendToTraining(string id);
            bool Hire(string id);
            void Close();
        }

        enum Filter { All, Available, OnMission, Training, Applicants }

        static readonly string[] FilterNames = { "ВСЕ", "СВОБОДНЫ", "В ПОЛЁТЕ", "НА ОБУЧЕНИИ", "КАНДИДАТЫ" };
        static Texture2D[] helmets;
        static Texture2D planet;

        readonly IHost host;
        readonly VisualElement root, rows, detail;
        readonly List<Button> tabs = new List<Button>();
        readonly Button hire;
        Filter filter;
        string selected;
        string message = "";

        public CrewView(VisualElement screen, IHost owner)
        {
            host = owner;
            root = screen;

            var head = Add(screen, El("screen-head"));
            head.Add(Text("ЭКИПАЖ", "screen-title"));

            var tabRow = Add(screen, El("hz-utabs"));
            for (int i = 0; i < FilterNames.Length; i++)
            {
                var f = (Filter)i;
                var b = new Button(() => SetFilter(f));
                b.AddToClassList("hz-utab");
                b.AddToClassList("t-mono");
                tabs.Add(b);
                tabRow.Add(b);
            }

            var table = Add(screen, El("crew-table"));
            var th = Add(table, El("crew-head"));
            Add(th, Text("ИМЯ", "crew-th", "crew-col-name", "t-mono")).style.paddingLeft = 54;
            th.Add(Text("РОЛЬ", "crew-th", "crew-col-role", "t-mono"));
            th.Add(Text("УРОВЕНЬ", "crew-th", "crew-col-level", "t-mono"));
            th.Add(Text("ПОЛЁТЫ", "crew-th", "crew-col-flights", "t-mono"));
            th.Add(Text("СТАТУС", "crew-th", "crew-col-status", "t-mono"));
            rows = Add(table, El());

            detail = Add(screen, El("crew-detail"));

            hire = new Button(HireClicked);
            hire.AddToClassList("crew-hire");
            hire.AddToClassList("t-mono");
            screen.Add(hire);

            var foot = Add(screen, El("hz-foot-right"));
            VisualElement back = KeyHint(foot, "ESC", Loc.T("НАЗАД"));
            back.AddToClassList("key-hint--button");
            back.RegisterCallback<ClickEvent>(_ => host.Close());
        }

        /// <summary>Re-reads the roster (call when the screen is shown and after every change).</summary>
        public void Refresh()
        {
            PlayerProfile p = host.Profile;
            rows.Clear();
            detail.Clear();
            if (p == null || p.crew == null)
            {
                detail.Add(Text("Профиль игрока ещё не создан — начните игру, и экипаж появится.", "hz-note"));
                hire.style.display = DisplayStyle.None;
                return;
            }
            CrewRoster.EnsureRoster(p.crew);
            CrewRosterData d = p.crew;

            int[] counts =
            {
                d.members.Count - CrewRoster.Count(d, CrewStatus.Applicant), CrewRoster.Count(d, CrewStatus.Available),
                CrewRoster.Count(d, CrewStatus.OnMission), CrewRoster.Count(d, CrewStatus.Training), CrewRoster.Count(d, CrewStatus.Applicant),
            };
            for (int i = 0; i < tabs.Count; i++)
            {
                tabs[i].text = Loc.T(FilterNames[i]) + " " + counts[i];
                tabs[i].EnableInClassList("hz-utab--on", i == (int)filter);
            }

            List<CrewMember> shown = d.members.FindAll(Matches);
            if (shown.Count > 0 && (selected == null || !shown.Exists(m => m.id == selected))) selected = shown[0].id;
            foreach (CrewMember m in shown) rows.Add(Row(m));
            if (shown.Count == 0) rows.Add(Text("Здесь никого нет.", "hz-note"));

            CrewMember sel = selected != null ? CrewRoster.Find(d, selected) : null;
            if (sel != null && Matches(sel)) BuildDetail(p, sel);

            int applicants = counts[4];
            hire.style.display = applicants > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            hire.text = "+  " + Loc.T("НАНЯТЬ КАНДИДАТА") + "  ·  " + HireText(p) +
                        "   (" + Loc.T("у вас ") + UnitFormatter.Number(p.credits, 0) + Loc.T(" кр") + ")";
            ApplyFonts(root);
        }

        bool Matches(CrewMember m)
        {
            switch (filter)
            {
                case Filter.Available: return m.status == CrewStatus.Available;
                case Filter.OnMission: return m.status == CrewStatus.OnMission;
                case Filter.Training: return m.status == CrewStatus.Training;
                case Filter.Applicants: return m.status == CrewStatus.Applicant;
                default: return m.status != CrewStatus.Applicant;
            }
        }

        void SetFilter(Filter f)
        {
            filter = f;
            selected = null;
            message = "";
            Refresh();
        }

        VisualElement Row(CrewMember m)
        {
            var row = new Button(() =>
            {
                selected = m.id;
                message = "";
                Refresh();
            });
            row.AddToClassList("crew-row");
            if (m.id == selected) row.AddToClassList("crew-row--on");

            var name = Add(row, El("crew-col-name"));
            Add(name, El("crew-avatar")).style.backgroundImage = new StyleBackground(Helmet(m.role));
            name.Add(Text(m.name, "crew-name"));
            row.Add(Text(RoleName(m.role).ToUpperInvariant(), "crew-cell", "crew-col-role", "t-mono"));
            var level = Add(row, El("crew-col-level"));
            for (int i = 0; i < CrewRoster.MaxLevel; i++) level.Add(El("crew-seg", i < m.Level ? "crew-seg--on" : null));
            row.Add(Text(m.flights.ToString(), "crew-cell", "crew-col-flights", "t-mono"));
            var status = Add(row, El("crew-col-status"));
            Color c = StatusColor(m.status);
            Add(status, El("crew-dot")).style.backgroundColor = c;
            Add(status, Text(StatusText(m), "crew-status", "t-mono")).style.color = c;
            return row;
        }

        /// <summary>Hiring price for the profile's game mode ("3 000 кр", Sandbox: "бесплатно").</summary>
        static string HireText(PlayerProfile p)
        {
            int cost = CrewRoster.HireCostFor(p);
            return cost > 0 ? UnitFormatter.Number(cost, 0) + Loc.T(" кр") : Loc.T("бесплатно");
        }

        void BuildDetail(PlayerProfile p, CrewMember m)
        {
            var head = Add(detail, El("crew-detail__head"));
            Add(head, El("crew-detail__avatar")).style.backgroundImage = new StyleBackground(Helmet(m.role));
            var title = Add(head, El("grow"));
            title.Add(Text(RoleName(m.role).ToUpperInvariant() + "  ·  " + Loc.T("УРОВЕНЬ") + " " + m.Level, "eyebrow", "t-mono"));
            title.Add(Text(m.name, "crew-detail__name", "t-strong"));

            detail.Add(Text("НАВЫКИ", "crew-section", "t-mono"));
            Skill("Пилотирование", m.piloting);
            Skill("Инженерия", m.engineering);
            Skill("Выдержка", m.composure);
            Skill("Наука", m.science);
            Add(detail, Text("Навыки — характеристика персонажа: на физику полёта они не влияют.", "hz-note")).style.marginTop = 4;

            var stats = Add(detail, El());
            stats.style.marginTop = 10;
            Kv(stats, "Полёты", m.flights.ToString());
            Kv(stats, "Время в космосе", m.secondsInSpace > 0.0 ? UnitFormatter.Duration(m.secondsInSpace) : "—");
            Kv(stats, "Выходы на поверхность", m.evas.ToString());
            Kv(stats, "Посещённые тела", m.bodiesVisited.Count > 0 ? string.Join(", ", m.bodiesVisited.ConvertAll(BodyName)) : "—");
            Kv(stats, "Курсов обучения", m.trainings.ToString());

            detail.Add(Text("ТЕКУЩЕЕ НАЗНАЧЕНИЕ", "crew-section", "t-mono"));
            var assign = Add(detail, El("crew-assign"));
            Add(assign, El("crew-assign__planet")).style.backgroundImage = new StyleBackground(Planet());
            var at = Add(assign, El("grow"));
            switch (m.status)
            {
                case CrewStatus.OnMission:
                    at.Add(Text(host.InFlight ? host.FlightTitle : "FLT " + m.missionFlight, "crew-assign__title"));
                    at.Add(Text(Loc.T("НА БОРТУ") + (string.IsNullOrEmpty(host.FlightClock) ? "" : "  ·  " + host.FlightClock), "crew-assign__sub", "t-mono"));
                    break;
                case CrewStatus.Training:
                    at.Add(Text("Курс подготовки", "crew-assign__title"));
                    at.Add(Text("ПРОПУСТИТ СЛЕДУЮЩИЙ ПУСК", "crew-assign__sub", "t-mono"));
                    break;
                case CrewStatus.Applicant:
                    at.Add(Text("Кандидат в отряд", "crew-assign__title"));
                    at.Add(Text(Loc.T("НАЙМ") + " · " + HireText(p), "crew-assign__sub", "t-mono"));
                    break;
                default:
                    at.Add(Text("Без назначения", "crew-assign__title"));
                    at.Add(Text("ПОЛЕТИТ, ЕСЛИ В КАБИНЕ ХВАТИТ МЕСТ", "crew-assign__sub", "t-mono"));
                    break;
            }

            if (!string.IsNullOrEmpty(message)) Add(detail, Text(message, "hz-note", "hz-note--warn"));

            var buttons = Add(detail, El("hz-detail__buttons"));
            if (m.status == CrewStatus.Applicant)
                buttons.Add(ButtonEl(Loc.T("НАНЯТЬ") + " · " + HireText(p), () => DoHire(m.id), primary: true));
            else
            {
                Button train = ButtonEl("НА ОБУЧЕНИЕ", () =>
                {
                    message = host.SendToTraining(m.id) ? Loc.T("Курс начат: пропустит следующий пуск, затем вернётся с навыком +") + CrewRoster.TrainingGain + "."
                        : Loc.T("Отправить на обучение можно только свободного космонавта (не в полёте).");
                    Refresh();
                }, disabled: m.status != CrewStatus.Available);
                buttons.Add(train);
                if (host.InFlight) buttons.Add(ButtonEl(Loc.T("К ПОЛЁТУ") + "  →", host.Close, primary: true));
            }
        }

        void HireClicked()
        {
            CrewMember sel = selected != null && host.Profile != null ? CrewRoster.Find(host.Profile.crew, selected) : null;
            if (sel != null && sel.status == CrewStatus.Applicant) DoHire(sel.id);
            else SetFilter(Filter.Applicants);
        }

        void DoHire(string id)
        {
            message = host.Hire(id) ? Loc.T("Нанят: теперь в отряде и может лететь.") : Loc.T("Не хватает кредитов на найм.");
            Refresh();
        }

        void Skill(string name, int value)
        {
            var row = Add(detail, El("crew-skill"));
            row.Add(Text(name, "crew-skill__name"));
            var track = Add(row, El("crew-skill__track"));
            Add(track, El("crew-skill__fill")).style.width = Length.Percent(Mathf.Clamp(value, 0, 100));
            row.Add(Text(value.ToString(), "crew-skill__value", "t-mono"));
        }

        static void Kv(VisualElement parent, string k, string v)
        {
            var row = Add(parent, El("hz-kv"));
            row.Add(Text(k, "hz-kv__k"));
            row.Add(Text(v, "hz-kv__v", "t-mono"));
        }

        static string BodyName(string id)
        {
            foreach (SpaceSim.Planets.CelestialBodyPreset b in SpaceSim.Planets.HomeSystemCatalog.Bodies())
                if (b.Id == id) return Loc.T(b.DisplayName);
            return id;
        }

        public static string RoleName(CrewRole r) =>
            r == CrewRole.Pilot ? Loc.T("Пилот") : r == CrewRole.Engineer ? Loc.T("Инженер") : Loc.T("Учёный");

        static Color StatusColor(CrewStatus s)
        {
            switch (s)
            {
                case CrewStatus.OnMission: return new Color32(98, 140, 255, 255);
                case CrewStatus.Training: return new Color32(240, 168, 64, 255);
                case CrewStatus.Applicant: return new Color32(134, 131, 124, 255);
                default: return new Color32(170, 226, 110, 255);
            }
        }

        string StatusText(CrewMember m)
        {
            switch (m.status)
            {
                case CrewStatus.OnMission: return Loc.T("В ПОЛЁТЕ") + " · FLT " + m.missionFlight;
                case CrewStatus.Training: return Loc.T("НА ОБУЧЕНИИ");
                case CrewStatus.Applicant: return Loc.T("КАНДИДАТ");
                default: return Loc.T("СВОБОДЕН");
            }
        }

        // ─────────────────────────────── procedural pictures ───────────────────────────────

        /// <summary>A round astronaut helmet: white shell, dark visor with a reflection, a ring in the role colour.</summary>
        static Texture2D Helmet(CrewRole role)
        {
            if (helmets == null) helmets = new Texture2D[3];
            int i = (int)role;
            if (helmets[i] != null) return helmets[i];
            const int n = 96;
            Color ring = role == CrewRole.Pilot ? new Color32(98, 140, 255, 255) : role == CrewRole.Engineer ? new Color32(240, 168, 64, 255) : new Color32(170, 226, 110, 255);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f; // v up
                float r = Mathf.Sqrt(u * u + v * v);
                Color c = Color.clear;
                if (r < 0.98f)
                {
                    float shade = 0.78f + 0.22f * Mathf.Clamp01(0.6f - u * 0.5f + v * 0.4f);
                    c = new Color(0.93f * shade, 0.91f * shade, 0.87f * shade, 1f);
                    if (r > 0.86f) c = Color.Lerp(c, ring, 0.85f);
                    // Visor: a wide ellipse slightly above the centre.
                    float vx = u / 0.62f, vy = (v - 0.08f) / 0.46f;
                    if (vx * vx + vy * vy < 1f)
                    {
                        c = new Color(0.07f, 0.08f, 0.11f, 1f);
                        float hx = (u + 0.25f) / 0.16f, hy = (v - 0.22f) / 0.09f;
                        if (hx * hx + hy * hy < 1f) c = new Color(0.55f, 0.62f, 0.78f, 1f);
                    }
                    c.a = Mathf.Clamp01((0.98f - r) * n * 0.5f);
                }
                px[y * n + x] = c;
            }
            helmets[i] = Upload(px, n);
            return helmets[i];
        }

        /// <summary>Small shaded ochre planet for the assignment line.</summary>
        static Texture2D Planet()
        {
            if (planet != null) return planet;
            const int n = 64;
            var px = new Color32[n * n];
            var light = new Vector3(-0.5f, 0.5f, 0.7f).normalized;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r2 = u * u + v * v;
                if (r2 > 1f) { px[y * n + x] = new Color32(0, 0, 0, 0); continue; }
                var normal = new Vector3(u, v, Mathf.Sqrt(1f - r2));
                float band = 0.5f + 0.5f * Mathf.Sin(v * 7f + Mathf.Sin(u * 4f) * 1.4f);
                Color baseColor = Color.Lerp(new Color(0.62f, 0.45f, 0.18f), new Color(0.92f, 0.74f, 0.36f), band);
                Color c = baseColor * (Mathf.Clamp01(Vector3.Dot(normal, light)) * 0.9f + 0.08f);
                c.a = Mathf.Clamp01((1f - Mathf.Sqrt(r2)) * n * 0.5f);
                px[y * n + x] = c;
            }
            planet = Upload(px, n);
            return planet;
        }

        static Texture2D Upload(Color32[] px, int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }
    }
}
