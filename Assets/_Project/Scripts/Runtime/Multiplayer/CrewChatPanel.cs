using System.Collections.Generic;
using SpaceSim.Localization;
using SpaceSim.Multiplayer;
using UnityEngine;

namespace SpaceSim.Runtime.Multiplayer
{
    /// <summary>
    /// The crew chat on the right edge of the screen during an online team mission (hangar, flight, surface — it survives scene
    /// loads). The captain sees one thread per player, headed by that player's roles («ИНЖЕНЕР, ПИЛОТ») and username; everybody
    /// else sees only their thread with the captain. Folds into a tab with an unread counter. Created by <see cref="CrewSession"/>.
    /// </summary>
    public sealed class CrewChatPanel : MonoBehaviour
    {
        const string InputControl = "crewchat-input";
        const float Width = 340f, Margin = 16f, Top = 190f, HeaderH = 34f, ThreadRowH = 46f, InputH = 34f, CollapsedW = 168f;

        static CrewChatPanel instance;

        /// <summary>Screen area the panel took last frame, in GUI units (for the hangar and the map: clicks there are not theirs).</summary>
        static Rect lastRect;

        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("CrewChatPanel");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<CrewChatPanel>();
        }

        public static void Remove()
        {
            if (instance != null) Destroy(instance.gameObject);
            instance = null;
            lastRect = Rect.zero;
        }

        /// <summary>True when the mouse is over the chat (the 3D view and the map ignore clicks and scroll there).</summary>
        public static bool MouseOver
        {
            get
            {
                if (instance == null || lastRect.width <= 0f) return false;
                Vector2 mouse = FlightInput.MousePosition;
                float s = HudStyle.Scale;
                return lastRect.Contains(new Vector2(mouse.x / s, (Screen.height - mouse.y) / s));
            }
        }

        bool expanded = true;
        int selected = -1;
        string draft = "";
        Vector2 scroll;
        bool stickToBottom = true;
        CrewChat hooked;
        bool focusInput, inputFocused;

        /// <summary>The chat's text field has the keyboard (Esc leaves the field instead of opening the pause menu).</summary>
        public static bool Typing => instance != null && instance.inputFocused;

        void OnDestroy()
        {
            if (hooked != null) hooked.Added -= OnAdded;
            if (instance == this) instance = null;
        }

        void Hook(CrewChat chat)
        {
            if (hooked == chat) return;
            if (hooked != null) hooked.Added -= OnAdded;
            hooked = chat;
            if (chat != null) chat.Added += OnAdded;
            selected = -1;
        }

        void OnAdded(CrewChatMessage m)
        {
            if (m.Channel == selected) stickToBottom = true;
            // A player who is not the captain has one thread: a new line from the captain opens the panel's tab badge only.
        }

        void OnGUI()
        {
            CrewChat chat = CrewSession.Chat;
            Hook(chat);
            if (Event.current.type == EventType.Layout) inputFocused = false; // set again by DrawInput when the field is drawn
            if (chat == null || FlightInput.PauseMenuOpen)
            {
                lastRect = Rect.zero;
                return;
            }
            HudStyle.Begin();
            GUI.depth = -40; // over the HUD and the hangar, under the pause menu and the robot blackout
            Vector2 screen = HudStyle.VirtualScreen;

            IReadOnlyList<int> channels = chat.VisibleChannels;
            if (channels.Count == 0)
            {
                lastRect = Rect.zero;
                return;
            }
            if (selected < 0 || !Contains(channels, selected)) selected = channels[0];

            if (!expanded)
            {
                DrawCollapsed(chat, screen);
                return;
            }

            float height = Mathf.Clamp(screen.y - Top - 120f, 260f, 620f);
            var panel = new Rect(screen.x - Width - Margin, Top, Width, height);
            lastRect = panel;
            HudStyle.Card(panel, 0.92f);

            float y = panel.y;
            DrawHeader(chat, new Rect(panel.x, y, panel.width, HeaderH));
            y += HeaderH;

            if (chat.LocalIsCaptain)
            {
                foreach (int ch in channels)
                {
                    DrawThreadRow(chat, ch, new Rect(panel.x, y, panel.width, ThreadRowH));
                    y += ThreadRowH;
                }
                HudStyle.HLine(panel.x, y, panel.width);
            }
            else
            {
                DrawCaptainLine(chat, new Rect(panel.x, y, panel.width, 40f));
                y += 40f;
            }

            chat.MarkRead(selected);
            var messages = new Rect(panel.x + 10f, y + 6f, panel.width - 20f, panel.yMax - InputH - 16f - (y + 6f));
            DrawMessages(chat, messages);
            DrawInput(chat, new Rect(panel.x + 10f, panel.yMax - InputH - 8f, panel.width - 20f, InputH));
        }

        static bool Contains(IReadOnlyList<int> list, int value)
        {
            foreach (int v in list)
                if (v == value) return true;
            return false;
        }

        void DrawCollapsed(CrewChat chat, Vector2 screen)
        {
            int unread = chat.TotalUnread;
            var r = new Rect(screen.x - CollapsedW - Margin, Top, CollapsedW, 32f);
            lastRect = r;
            HudStyle.Card(r, 0.92f);
            if (unread > 0) HudStyle.Frame(r, HudStyle.Amber);
            string title = chat.LocalIsCaptain ? Loc.T("ЧАТЫ ЭКИПАЖА") : Loc.T("СВЯЗЬ С КАПИТАНОМ");
            GUI.Label(new Rect(r.x + 10f, r.y, r.width - 40f, r.height), title, HudStyle.Style(HudStyle.Face.Mono, 11, HudStyle.TextSoft));
            if (unread > 0)
                GUI.Label(new Rect(r.xMax - 34f, r.y, 26f, r.height), unread.ToString(),
                    HudStyle.Style(HudStyle.Face.Strong, 13, HudStyle.Amber, TextAnchor.MiddleRight));
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                expanded = true;
                stickToBottom = true;
                focusInput = true;
            }
        }

        void DrawHeader(CrewChat chat, Rect r)
        {
            string title = chat.LocalIsCaptain ? Loc.T("ЧАТЫ ЭКИПАЖА") : Loc.T("СВЯЗЬ С КАПИТАНОМ");
            GUI.Label(new Rect(r.x + 12f, r.y, r.width - 60f, r.height), title, HudStyle.Style(HudStyle.Face.Mono, 12, HudStyle.Amber));
            if (GUI.Button(new Rect(r.xMax - 38f, r.y + 5f, 28f, 24f), "—", HudStyle.Button))
            {
                expanded = false;
                GUIUtility.keyboardControl = 0;
            }
            HudStyle.HLine(r.x, r.yMax - 1f, r.width);
        }

        /// <summary>Captain's list: «ИНЖЕНЕР, ПИЛОТ» over the username, unread counter on the right.</summary>
        void DrawThreadRow(CrewChat chat, int channel, Rect r)
        {
            bool on = channel == selected;
            if (on) HudStyle.Fill(r, new Color(HudStyle.Blue.r, HudStyle.Blue.g, HudStyle.Blue.b, 0.16f));
            if (on) HudStyle.Fill(new Rect(r.x, r.y, 3f, r.height), HudStyle.Blue);

            CrewSession.Seat seat = CrewSession.SeatOf(channel);
            Color accent = RoleColor(seat);
            GUI.Label(new Rect(r.x + 14f, r.y + 6f, r.width - 70f, 18f), CrewSession.RolesText(channel),
                HudStyle.Style(HudStyle.Face.Strong, 13, accent));
            string name = seat != null ? seat.Name : "?";
            if (!CrewSession.IsConnected(channel)) name += "  ·  " + Loc.T("вышел");
            GUI.Label(new Rect(r.x + 14f, r.y + 25f, r.width - 70f, 16f), name, HudStyle.Style(HudStyle.Face.Mono, 11, HudStyle.TextMuted));

            int unread = on ? 0 : chat.Unread(channel);
            if (unread > 0)
            {
                var badge = new Rect(r.xMax - 40f, r.y + 13f, 26f, 20f);
                HudStyle.Fill(badge, HudStyle.Amber);
                GUI.Label(badge, unread > 99 ? "99+" : unread.ToString(),
                    HudStyle.Style(HudStyle.Face.Strong, 11, HudStyle.Space, TextAnchor.MiddleCenter));
            }
            if (GUI.Button(r, GUIContent.none, GUIStyle.none) && !on)
            {
                selected = channel;
                stickToBottom = true;
                focusInput = true;
            }
            HudStyle.HLine(r.x, r.yMax - 1f, r.width);
        }

        /// <summary>Non-captain view: who the captain is.</summary>
        void DrawCaptainLine(CrewChat chat, Rect r)
        {
            CrewSession.Seat captain = CrewSession.SeatOf(chat.CaptainId);
            Color c = RoleColor(CrewRole.Captain);
            GUI.Label(new Rect(r.x + 14f, r.y + 4f, r.width - 28f, 16f), CrewRoles.Info(CrewRole.Captain).Title,
                HudStyle.Style(HudStyle.Face.Strong, 12, c));
            string name = captain != null ? captain.Name : "?";
            if (!CrewSession.IsConnected(chat.CaptainId)) name += "  ·  " + Loc.T("вышел");
            GUI.Label(new Rect(r.x + 14f, r.y + 21f, r.width - 28f, 16f), name, HudStyle.Style(HudStyle.Face.Mono, 11, HudStyle.TextMuted));
            HudStyle.HLine(r.x, r.yMax - 1f, r.width);
        }

        void DrawMessages(CrewChat chat, Rect area)
        {
            IReadOnlyList<CrewChatMessage> thread = chat.Thread(selected);
            GUIStyle author = HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.TextMuted);
            var body = new GUIStyle(HudStyle.Style(HudStyle.Face.Body, 13, HudStyle.Text)) { wordWrap = true, richText = false };
            var bodyOwn = new GUIStyle(body) { alignment = TextAnchor.UpperRight };
            bodyOwn.normal.textColor = HudStyle.TextStrong;

            float inner = area.width - 16f;
            float total = 0f;
            var heights = new float[thread.Count];
            for (int i = 0; i < thread.Count; i++)
            {
                heights[i] = 16f + body.CalcHeight(new GUIContent(thread[i].Text), inner) + 8f;
                total += heights[i];
            }

            if (thread.Count == 0)
            {
                string hint = chat.LocalIsCaptain ? Loc.T("Напишите игроку: что ему нужно для миссии.")
                    : Loc.T("Здесь сообщения капитана. Ответить можно только ему.");
                var h = new GUIStyle(HudStyle.Style(HudStyle.Face.Body, 12, HudStyle.TextDim)) { wordWrap = true };
                GUI.Label(new Rect(area.x, area.y + 4f, area.width, 60f), hint, h);
                return;
            }

            if (stickToBottom && Event.current.type == EventType.Layout)
            {
                scroll.y = Mathf.Max(0f, total - area.height);
                stickToBottom = false;
            }
            scroll = GUI.BeginScrollView(area, scroll, new Rect(0f, 0f, inner, Mathf.Max(total, area.height)), false, false,
                GUIStyle.none, GUI.skin.verticalScrollbar);
            float y = 0f;
            for (int i = 0; i < thread.Count; i++)
            {
                CrewChatMessage m = thread[i];
                bool own = m.FromId == chat.LocalId;
                string who = own ? Loc.T("ВЫ") : AuthorLabel(m.FromId);
                GUI.Label(new Rect(0f, y, inner, 14f), who,
                    own ? HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.Blue, TextAnchor.MiddleRight) : author);
                GUI.Label(new Rect(0f, y + 15f, inner, heights[i] - 22f), m.Text, own ? bodyOwn : body);
                y += heights[i];
            }
            GUI.EndScrollView();
        }

        static string AuthorLabel(int id)
        {
            CrewSession.Seat seat = CrewSession.SeatOf(id);
            return (seat != null ? seat.Name : "?") + "  ·  " + CrewSession.RolesText(id);
        }

        void DrawInput(CrewChat chat, Rect r)
        {
            Event e = Event.current;
            bool focused = GUI.GetNameOfFocusedControl() == InputControl;
            if (focused) inputFocused = true;
            if (focused && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    Send();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    GUIUtility.keyboardControl = 0; // leave the field; the flight keys work again
                    e.Use();
                }
            }

            GUI.SetNextControlName(InputControl);
            draft = GUI.TextField(new Rect(r.x, r.y, r.width - 46f, r.height), draft ?? "", CrewChat.MaxLength, HudStyle.Field);
            if (focusInput && e.type == EventType.Repaint)
            {
                GUI.FocusControl(InputControl);
                focusInput = false;
            }
            bool can = CrewChat.Clean(draft) != null;
            GUI.enabled = can;
            if (GUI.Button(new Rect(r.xMax - 40f, r.y, 40f, r.height), "→", HudStyle.Primary)) Send();
            GUI.enabled = true;
        }

        void Send()
        {
            if (CrewSession.SendChat(selected, draft))
            {
                draft = "";
                stickToBottom = true;
            }
        }

        static Color RoleColor(CrewSession.Seat seat) =>
            seat != null && seat.Roles.Count > 0 ? RoleColor(seat.Roles[0]) : HudStyle.TextSoft;

        static Color RoleColor(CrewRole role)
        {
            CrewRoleInfo info = CrewRoles.Info(role);
            return new Color32(info.R, info.G, info.B, 255);
        }
    }
}
