using System;
using System.Collections.Generic;

namespace SpaceSim.Multiplayer
{
    /// <summary>One line of the crew chat.</summary>
    public sealed class CrewChatMessage
    {
        public CrewChatMessage(int fromId, int channel, string text, int sequence)
        {
            FromId = fromId;
            Channel = channel;
            Text = text;
            Sequence = sequence;
        }

        /// <summary>Who wrote it.</summary>
        public int FromId { get; }
        /// <summary>The thread: the id of the non-captain player of the conversation.</summary>
        public int Channel { get; }
        public string Text { get; }
        /// <summary>Arrival order on this computer.</summary>
        public int Sequence { get; }
    }

    /// <summary>
    /// The team-mission chat as one computer sees it. Rule of the game: every player writes only to the captain, the captain writes
    /// to each of them, players never write to each other. So there is one thread per non-captain player (<see cref="CrewChatMessage.Channel"/>
    /// = that player's id); the captain sees all of them, everybody else sees only their own thread with the captain.
    /// Messages that break the rule (for example sent by a modified client) are dropped on arrival. No Unity: covered by EditMode tests.
    /// </summary>
    public sealed class CrewChat
    {
        public const int MaxLength = 240;
        /// <summary>Oldest lines are dropped beyond this per thread.</summary>
        public const int MaxPerThread = 120;

        readonly Dictionary<int, List<CrewChatMessage>> threads = new Dictionary<int, List<CrewChatMessage>>();
        readonly Dictionary<int, int> unread = new Dictionary<int, int>();
        readonly HashSet<int> members;
        int sequence;

        /// <param name="localId">The player of this computer.</param>
        /// <param name="captainId">The player holding <see cref="CrewRole.Captain"/>.</param>
        /// <param name="memberIds">Everybody in the crew (captain included).</param>
        public CrewChat(int localId, int captainId, IEnumerable<int> memberIds)
        {
            LocalId = localId;
            CaptainId = captainId;
            members = new HashSet<int>(memberIds ?? Array.Empty<int>());
            foreach (int id in members)
                if (id != captainId) threads[id] = new List<CrewChatMessage>();
        }

        public int LocalId { get; }
        public int CaptainId { get; }
        public bool LocalIsCaptain => LocalId == CaptainId;

        /// <summary>A line was added (to any thread this computer sees).</summary>
        public event Action<CrewChatMessage> Added;

        /// <summary>The threads this computer sees, in id order: all non-captain players for the captain, else only its own.</summary>
        public IReadOnlyList<int> VisibleChannels
        {
            get
            {
                var list = new List<int>();
                if (LocalIsCaptain) list.AddRange(threads.Keys);
                else if (threads.ContainsKey(LocalId)) list.Add(LocalId);
                list.Sort();
                return list;
            }
        }

        /// <summary>True when the chat rule lets <paramref name="fromId"/> write to <paramref name="toId"/>.</summary>
        public bool Allowed(int fromId, int toId)
        {
            if (fromId == toId || !members.Contains(fromId) || !members.Contains(toId)) return false;
            return (fromId == CaptainId) != (toId == CaptainId); // exactly one side is the captain
        }

        /// <summary>Who this computer writes to in a thread (the captain writes to the thread's player, others to the captain).</summary>
        public int RecipientOf(int channel) => LocalIsCaptain ? channel : CaptainId;

        /// <summary>Trims and shortens a line; null when nothing is left to send.</summary>
        public static string Clean(string text)
        {
            if (text == null) return null;
            var chars = new char[Math.Min(text.Length, MaxLength * 2)];
            int n = 0;
            foreach (char c in text)
            {
                if (n >= chars.Length) break;
                chars[n++] = char.IsControl(c) ? ' ' : c; // no line breaks or control characters in a chat line
            }
            string s = new string(chars, 0, n).Trim();
            if (s.Length > MaxLength) s = s.Substring(0, MaxLength).TrimEnd();
            return s.Length == 0 ? null : s;
        }

        /// <summary>
        /// Adds a line written by <paramref name="fromId"/> to <paramref name="toId"/> (an own line just sent, or one received).
        /// Returns it, or null when the rule forbids it, the text is empty or this computer is not part of the conversation.
        /// </summary>
        public CrewChatMessage Post(int fromId, int toId, string text)
        {
            string clean = Clean(text);
            if (clean == null || !Allowed(fromId, toId)) return null;
            if (fromId != LocalId && toId != LocalId) return null; // not ours to see
            int channel = fromId == CaptainId ? toId : fromId;
            List<CrewChatMessage> thread = threads[channel];
            var m = new CrewChatMessage(fromId, channel, clean, ++sequence);
            thread.Add(m);
            if (thread.Count > MaxPerThread) thread.RemoveRange(0, thread.Count - MaxPerThread);
            if (fromId != LocalId) unread[channel] = Unread(channel) + 1;
            Added?.Invoke(m);
            return m;
        }

        public IReadOnlyList<CrewChatMessage> Thread(int channel) =>
            threads.TryGetValue(channel, out List<CrewChatMessage> t) ? t : (IReadOnlyList<CrewChatMessage>)Array.Empty<CrewChatMessage>();

        public int Unread(int channel) => unread.TryGetValue(channel, out int n) ? n : 0;

        public int TotalUnread
        {
            get
            {
                int sum = 0;
                foreach (int n in unread.Values) sum += n;
                return sum;
            }
        }

        public void MarkRead(int channel) => unread.Remove(channel);
    }
}
