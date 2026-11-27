using System;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Objectives;
using DMI.Xenoarch.Save;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Messages
{
    // ---------------------------------------------------------------- data (Resources/Messages/message_threads.json)
    [Serializable] public sealed class MessageChoice { public string text; public string next; }

    [Serializable]
    public sealed class MessageNode
    {
        public string id;
        /// <summary>Speaker; empty = the thread's contact.</summary>
        public string from;
        public string text;
        /// <summary>Follow-up node sent straight after this one (no reply needed).</summary>
        public string next;
        public List<MessageChoice> choices = new List<MessageChoice>();
    }

    [Serializable]
    public sealed class MessageThread
    {
        public string id, contact, glyph, subject;
        /// <summary>"start" (as soon as a game is running), "notice:&lt;NoticeKind&gt;" (first Captain's Log post of that
        /// kind) or "id:&lt;prefix&gt;" (first log post whose id starts with the prefix).</summary>
        public string trigger;
        public List<MessageNode> nodes = new List<MessageNode>();
        public MessageNode Node(string nodeId) { foreach (var n in nodes) if (n != null && n.id == nodeId) return n; return null; }
    }

    [Serializable] public sealed class MessageLibrary { public List<MessageThread> threads = new List<MessageThread>(); }

    // ---------------------------------------------------------------- saved state (messages.state)
    [Serializable]
    public sealed class MessageLine { public string from; public string text; public float time; public bool player; }

    [Serializable]
    public sealed class ThreadState
    {
        public string id;
        public string node;      // node awaiting a reply, or "" when the thread has ended
        public bool unread;
        public float updated;
        public List<MessageLine> lines = new List<MessageLine>();
    }

    [Serializable] public sealed class MessagesState { public List<ThreadState> threads = new List<ThreadState>(); }

    /// <summary>
    /// t246u: data-driven conversations (Mission Control, ship AI) with reply choices. New messages go through the
    /// notification pipeline (Captain's Log entry + toast, NoticeKind.Message) and are read and answered on the
    /// journal's Messages tab. Bootstraps itself onto the runtime object; saved as messages.state.
    /// </summary>
    public sealed class MessageService : MonoBehaviour
    {
        public const string SaveKey = "messages.state";
        public const string Resource = "Messages/message_threads";
        public static MessageService Instance { get; private set; }

        public event Action Changed;
        public MessageLibrary Library { get; private set; } = new MessageLibrary();
        public MessagesState State { get; private set; } = new MessagesState();

        readonly Queue<string> _pending = new Queue<string>();
        float _nextStart;
        bool _wasInGame;
        NotificationService _hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreate()
        {
            var rt = XenoarchRuntime.Instance;
            if (rt == null || rt.GetComponent<MessageService>() != null) return;
            rt.gameObject.AddComponent<MessageService>();
        }

        void Awake()
        {
            Instance = this;
            var ta = Resources.Load<TextAsset>(Resource);
            if (ta != null)
            {
                try { Library = JsonUtility.FromJson<MessageLibrary>(ta.text) ?? new MessageLibrary(); }
                catch (Exception e) { Debug.LogWarning($"[Xenoarch] Messages: {Resource} unreadable: {e.Message}"); }
            }
            XenoarchRuntime.Session?.Saves.Register(new JsonStateSection<MessagesState>(SaveKey, 27,
                () => State, v => { State = v ?? new MessagesState(); _pending.Clear(); Changed?.Invoke(); }));
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_hooked != null) _hooked.Posted -= OnPosted;
        }

        public MessageThread Thread(string id) { foreach (var t in Library.threads) if (t != null && t.id == id) return t; return null; }
        public ThreadState StateOf(string id) { foreach (var t in State.threads) if (t != null && t.id == id) return t; return null; }
        public bool Started(string id) => StateOf(id) != null;

        public int Unread { get { int n = 0; foreach (var t in State.threads) if (t != null && t.unread) n++; return n; } }

        /// <summary>Started threads, most recently updated first.</summary>
        public List<ThreadState> Inbox()
        {
            var l = new List<ThreadState>();
            foreach (var t in State.threads) if (t != null && Thread(t.id) != null) l.Add(t);
            l.Sort((a, b) => b.updated.CompareTo(a.updated));
            return l;
        }

        /// <summary>Reply choices for the thread's current node (empty when it has ended).</summary>
        public IReadOnlyList<MessageChoice> Choices(string threadId)
        {
            var st = StateOf(threadId); var th = Thread(threadId);
            var n = st != null && th != null && !string.IsNullOrEmpty(st.node) ? th.Node(st.node) : null;
            return n != null && n.choices != null ? n.choices : (IReadOnlyList<MessageChoice>)Array.Empty<MessageChoice>();
        }

        public void MarkRead(string threadId)
        {
            var st = StateOf(threadId);
            if (st == null || !st.unread) return;
            st.unread = false; Changed?.Invoke();
        }

        static float Now() => XenoarchRuntime.Session != null ? XenoarchRuntime.Session.PlaySeconds : 0f;

        /// <summary>Starts a thread (once): its first node and any chained follow-ups arrive as new messages.</summary>
        public bool Begin(string threadId)
        {
            var th = Thread(threadId);
            if (th == null || Started(threadId) || th.nodes == null || th.nodes.Count == 0) return false;
            var st = new ThreadState { id = threadId, node = "" };
            State.threads.Add(st);
            Deliver(th, st, th.nodes[0]);
            return true;
        }

        /// <summary>The player answers with choice <paramref name="index"/> of the current node.</summary>
        public bool Reply(string threadId, int index)
        {
            var th = Thread(threadId); var st = StateOf(threadId);
            var choices = Choices(threadId);
            if (th == null || st == null || index < 0 || index >= choices.Count) return false;
            var c = choices[index];
            st.lines.Add(new MessageLine { from = "You", text = c.text, time = Now(), player = true });
            st.node = ""; st.updated = Now();
            var next = string.IsNullOrEmpty(c.next) ? null : th.Node(c.next);
            if (next != null) Deliver(th, st, next, toast: false);
            else Changed?.Invoke();
            return true;
        }

        void Deliver(MessageThread th, ThreadState st, MessageNode n, bool toast = true)
        {
            int guard = 0;
            string last = null;
            while (n != null && guard++ < 32)
            {
                string from = string.IsNullOrEmpty(n.from) ? th.contact : n.from;
                st.lines.Add(new MessageLine { from = from, text = n.text, time = Now() });
                last = $"{from}: {n.text}";
                st.node = n.choices != null && n.choices.Count > 0 ? n.id : "";
                if (!string.IsNullOrEmpty(st.node) || string.IsNullOrEmpty(n.next)) { n = null; break; }
                n = th.Node(n.next);
            }
            st.unread = true; st.updated = Now();
            Changed?.Invoke();
            // Captain's Log + toast via the notification pipeline (deduped per thread node).
            var notes = XenoarchRuntime.Session?.Notifications;
            if (notes != null && last != null)
                notes.Post(NoticeKind.Message, $"msg.{th.id}.{st.lines.Count}", last, toast ? 6f : 0f);
        }

        // ---------------------------------------------------------------- triggers
        bool InGame()
        {
            var rt = XenoarchRuntime.Instance;
            if (rt == null || !rt.SessionActive || DMI.Xenoarch.Scenes.SceneLoader.Busy) return false;
            string sc = SceneManager.GetActiveScene().name;
            return sc != SceneNames.MainMenu && sc != SceneNames.Bootstrap;
        }

        void Update()
        {
            var notes = XenoarchRuntime.Session?.Notifications;
            if (notes != _hooked)
            {
                if (_hooked != null) _hooked.Posted -= OnPosted;
                _hooked = notes;
                if (_hooked != null) _hooked.Posted += OnPosted;
            }
            bool inGame = InGame();
            if (inGame && !_wasInGame) _nextStart = Time.unscaledTime + 4f; // let the scene settle first
            _wasInGame = inGame;
            if (!inGame) return;
            if (_pending.Count == 0)
                foreach (var t in Library.threads)
                    if (t != null && t.trigger == "start" && !Started(t.id) && !_pending.Contains(t.id)) _pending.Enqueue(t.id);
            // One new thread every few seconds so arrivals don't pile up on the toast stack.
            if (_pending.Count > 0 && Time.unscaledTime >= _nextStart)
            {
                _nextStart = Time.unscaledTime + 6f;
                Begin(_pending.Dequeue());
            }
        }

        void OnPosted(CaptainsLogEntry e)
        {
            if (e == null || e.kind == NoticeKind.Message || e.playerNote) return;
            foreach (var t in Library.threads)
            {
                if (t == null || string.IsNullOrEmpty(t.trigger) || Started(t.id) || _pending.Contains(t.id)) continue;
                bool hit = t.trigger.StartsWith("notice:", StringComparison.Ordinal) && string.Equals(t.trigger.Substring(7), e.kind.ToString(), StringComparison.OrdinalIgnoreCase)
                        || t.trigger.StartsWith("id:", StringComparison.Ordinal) && !string.IsNullOrEmpty(e.id) && e.id.StartsWith(t.trigger.Substring(3), StringComparison.Ordinal);
                if (hit) _pending.Enqueue(t.id);
            }
        }
    }
}
