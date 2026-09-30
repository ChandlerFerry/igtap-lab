using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IgtapLab
{
    public sealed class LabTracker
    {
        const int Window = 3, Grace = 4, MaxShown = 8;
        static readonly FieldInfo JumpField = typeof(Movement).GetField("jumpAction", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo DashField = typeof(Movement).GetField("dashAction", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo JumpField2 = typeof(Movement).GetField("jumpAction2", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo DashField2 = typeof(Movement).GetField("dashAction2", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo FramesField = typeof(Movement).GetField("dashFramesRemaining", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo PauseField = typeof(pauseMenuScript).GetField("pauseAction", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly Color Green = new Color(0.15f, 0.6f, 0.25f, 0.95f), Amber = new Color(0.85f, 0.6f, 0.1f, 0.95f),
            Red = new Color(0.7f, 0.18f, 0.15f, 0.95f), Grey = new Color(1f, 1f, 1f, 0.12f);

        DemoDef demo;
        List<Chip> want = new List<Chip>();
        readonly List<Chip> live = new List<Chip>();
        public int Ticks, PhysTicks, Attempts, Successes;
        bool finished, jumpPressed, jumpReleased, dashPressed, pauseOpened, menuWasOpen;
        string stick = LabChips.Neutral;
        string lastTurn = "";
        GUIStyle title, right, text;
        int styleHeight;

        public void Begin(DemoDef d)
        {
            if (d != demo) { demo = d; want = LabChips.Chips(d.inputs); Attempts = Successes = 0; }
            Attempts++;
            Ticks = 0;
            jumpPressed = jumpReleased = dashPressed = pauseOpened = false;
            finished = false;
            stick = LabChips.Neutral;
            lastTurn = "";
            PhysTicks = 0;
            live.Clear();
        }

        public void Frame(Movement p)
        {
            // Movement reads both bindings of each button.
            var jump = JumpField?.GetValue(p) as InputAction;
            var jump2 = JumpField2?.GetValue(p) as InputAction;
            var dash = DashField?.GetValue(p) as InputAction;
            var dash2 = DashField2?.GetValue(p) as InputAction;
            jumpPressed |= (jump != null && jump.WasPressedThisFrame()) || (jump2 != null && jump2.WasPressedThisFrame());
            jumpReleased |= (jump != null && jump.WasReleasedThisFrame()) || (jump2 != null && jump2.WasReleasedThisFrame());
            dashPressed |= (dash != null && dash.WasPressedThisFrame()) || (dash2 != null && dash2.WasPressedThisFrame());
            bool open = p.pauseMenu != null && p.pauseMenu.menuOpen;
            pauseOpened |= open && !menuWasOpen;
            menuWasOpen = open;
        }

        /// <summary>
        /// Every physics tick of the attempt, before Movement.FixedUpdate. A tick takes an input tick as the game's
        /// physics tick has it: only with no cutscene, or on a dash's last tick. On the other (locked) ticks a jump in the dash, a dash in a
        /// spring and the freeze's turn go to the input tick that started them, the rest wait for the next one.
        /// </summary>
        public void Tick(Movement p, bool inFinish)
        {
            if (demo == null) return;
            PhysTicks++;
            if (finished) return;
            bool dashEnds = p.cutsceneMode == Movement.cutsceneModes.dash && p.dashActive && FramesField != null && (float)FramesField.GetValue(p) <= 0f;
            string direction = LabChips.Direction(p.MoveAxis.x, p.MoveAxis.y);
            if (p.cutsceneMode != Movement.cutsceneModes.none && !dashEnds)
            {
                int owner = Ticks - 1;
                if (p.cutsceneMode == Movement.cutsceneModes.dash)
                {
                    if (jumpPressed) live.Add(new Chip { Tick = owner, Kind = "dashjump", Label = "jump in dash" });
                    jumpPressed = false;
                    if (direction != LabChips.Neutral && direction != lastTurn)
                        live.Add(new Chip { Tick = owner, Kind = "turn", Label = "turn " + direction });
                    lastTurn = direction;
                }
                else if (p.cutsceneMode == Movement.cutsceneModes.spring)
                {
                    if (dashPressed) live.Add(new Chip { Tick = owner, Kind = "springdash", Label = "dash in spring" });
                    dashPressed = false;
                }
                return;
            }
            lastTurn = "";
            if (direction != stick)
            {
                live.Add(new Chip { Tick = Ticks, Kind = "stick", Label = direction });
                stick = direction;
            }
            if (jumpPressed) live.Add(new Chip { Tick = Ticks, Kind = "jump", Label = "jump" });
            if (jumpReleased) live.Add(new Chip { Tick = Ticks, Kind = "release", Label = "release" });
            if (dashPressed) live.Add(new Chip { Tick = Ticks, Kind = "dash", Label = "dash" });
            if (pauseOpened) live.Add(new Chip { Tick = Ticks, Kind = "pause", Label = "pause" });
            jumpPressed = jumpReleased = dashPressed = pauseOpened = false;
            if (inFinish) { finished = true; Successes++; return; }
            Ticks++;
        }

        int?[] Match()
        {
            var result = new int?[want.Count];
            var used = new bool[live.Count];
            for (int i = 0; i < want.Count; i++)
            {
                int best = -1;
                for (int j = 0; j < live.Count; j++)
                {
                    if (used[j] || live[j].Kind != want[i].Kind || live[j].Label != want[i].Label) continue;
                    int off = Mathf.Abs(live[j].Tick - want[i].Tick);
                    if (off <= Window && (best < 0 || off < Mathf.Abs(live[best].Tick - want[i].Tick))) best = j;
                }
                if (best < 0) continue;
                used[best] = true;
                result[i] = live[best].Tick - want[i].Tick;
            }
            return result;
        }

        static string Keys(InputAction a, InputAction b, string fallback)
        {
            string x = a == null ? null : a.GetBindingDisplayString(), y = b == null ? null : b.GetBindingDisplayString();
            string keys = string.IsNullOrEmpty(x) ? y : string.IsNullOrEmpty(y) || y == x ? x : x + " / " + y;
            return string.IsNullOrEmpty(keys) ? fallback : keys;
        }

        static string Shown(Chip c, string jump, string dash, string pause)
        {
            return c.Kind == "jump" ? jump : c.Kind == "release" ? "let go " + jump : c.Kind == "dash" ? dash : c.Kind == "pause" ? pause : c.Label;
        }

        public void Draw(LabDef lab, Movement p)
        {
            if (demo == null) return;
            float scale = Mathf.Max(0.6f, Screen.height / 1080f);
            if (title == null || styleHeight != Screen.height)
            {
                styleHeight = Screen.height;
                title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(17 * scale), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
                right = new GUIStyle(title) { alignment = TextAnchor.MiddleRight };
                text = new GUIStyle(title) { fontSize = Mathf.RoundToInt(18 * scale), alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            }
            string jumpKeys = Keys(JumpField?.GetValue(p) as InputAction, JumpField2?.GetValue(p) as InputAction, "Jump");
            string dashKeys = Keys(DashField?.GetValue(p) as InputAction, DashField2?.GetValue(p) as InputAction, "Dash");
            string pauseKeys = Keys(p.pauseMenu != null ? PauseField?.GetValue(p.pauseMenu) as InputAction : null, null, "Pause");

            int?[] match = Match();
            // 1 on time, 2 off by a few ticks, -1 missed, 0 still to do.
            var state = new int[want.Count];
            string message = "";
            int first = want.Count;
            for (int i = 0; i < want.Count; i++)
            {
                state[i] = match[i] == null ? (Ticks >= want[i].Tick + Grace ? -1 : 0) : match[i] == 0 ? 1 : 2;
                if (state[i] < 1 && first == want.Count) first = i;
                if (state[i] < 0 && message == "") message = "missed " + Shown(want[i], jumpKeys, dashKeys, pauseKeys) + " at tick " + want[i].Tick;
            }
            if (finished) message = "finish!";
            Color messageColor = finished ? new Color(0.15f, 0.6f, 0.25f, 1f) : new Color(0.7f, 0.18f, 0.15f, 1f);

            int from = Mathf.Max(0, Mathf.Min(first, want.Count - MaxShown) - 1), to = Mathf.Min(want.Count, from + MaxShown);
            var labels = new List<string>();
            var fills = new List<Color>();
            float pad = 12f * scale, chipsW = -10f * scale;
            for (int i = from; i < to; i++)
            {
                string label = Shown(want[i], jumpKeys, dashKeys, pauseKeys);
                if (state[i] == 2) label += " " + (match[i] > 0 ? "+" : "−") + Mathf.Abs(match[i].Value);
                labels.Add(label);
                fills.Add(state[i] == 1 ? Green : state[i] == 2 ? Amber : state[i] < 0 ? Red : Grey);
                chipsW += text.CalcSize(new GUIContent(label)).x + 28f * scale + 10f * scale;
            }
            float width = Mathf.Max(chipsW, 560f * scale) + 2f * pad, height = pad * 2f + 22f * scale + 34f * scale + 26f * scale + 40f * scale;
            var box = new Rect((Screen.width - width) / 2f, 12f * scale, width, height);
            Fill(box, new Color(0.02f, 0.04f, 0.08f, 0.8f));
            float x = box.x + pad, y = box.y + pad, w = width - 2f * pad;
            string name = lab.name + (demo.name.Length > 0 ? " · " + demo.name : "");
            string count = Successes + " / " + Attempts;
            float countW = right.CalcSize(new GUIContent(count)).x + 10f * scale;
            Label(new Rect(x, y, w - countW, 22f * scale), name, title, new Color(1f, 1f, 1f, 0.7f));
            Label(new Rect(x, y, w, 22f * scale), count, right, new Color(1f, 1f, 1f, 0.7f));
            y += 22f * scale + 4f * scale;
            float cx = box.x + (width - chipsW) / 2f;
            for (int i = 0; i < labels.Count; i++)
            {
                float cw = text.CalcSize(new GUIContent(labels[i])).x + 28f * scale;
                var chip = new Rect(cx, y, cw, 34f * scale);
                Fill(chip, fills[i]);
                Label(chip, labels[i], text, Color.white);
                cx += cw + 10f * scale;
            }
            y += 34f * scale;
            Label(new Rect(x, y, w, 26f * scale), message, text, messageColor);
            y += 26f * scale;
            DrawTimeline(new Rect(x, y, w, 40f * scale), state);
        }

        void DrawTimeline(Rect r, int[] state)
        {
            float span = Mathf.Max(demo.inputs.Count, 1) + Grace;
            float barY = r.y + r.height * 0.1f, barH = r.height * 0.45f;
            Fill(new Rect(r.x, barY, r.width, barH), new Color(1f, 1f, 1f, 0.08f));
            float X(float tick) { return r.x + Mathf.Clamp01(tick / span) * r.width; }
            for (int i = 0; i < want.Count; i++)
                Fill(new Rect(X(want[i].Tick) - 1f, barY, 3f, barH / 2f), state[i] == 1 ? Green : state[i] == 2 ? Amber : state[i] < 0 ? Red : Color.white);
            foreach (Chip c in live) Fill(new Rect(X(c.Tick) - 1f, barY + barH / 2f, 3f, barH / 2f), new Color(0.3f, 1f, 1f, 0.9f));
            if (!finished) Fill(new Rect(X(Ticks), barY - 3f, 2f, barH + 6f), Color.white);
        }

        static void Fill(Rect r, Color color)
        {
            Color saved = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = saved;
        }

        static void Label(Rect r, string s, GUIStyle style, Color color)
        {
            Color saved = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(r, s, style);
            GUI.contentColor = saved;
        }
    }
}
