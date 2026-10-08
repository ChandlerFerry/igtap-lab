using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace IgtapLab
{
    /// <summary>
    /// The TAS overlay's hurtbox and spike graze: the hurtbox filled purple, green while a
    /// spike's grace holds; over it "SPIKE n/limit" while the hurtbox touches spikes, then "SPIKE GRACE n/limit" (or
    /// "SPIKE ✗") fading for a moment after.
    /// </summary>
    public sealed class LabGraze
    {
        const float LingerSeconds = 1.5f;
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly FieldInfo NormalCollider = typeof(Movement).GetField("normalCollider", Any),
            Counter = typeof(spikeScript).GetField("counter", Any), GraceFrames = typeof(spikeScript).GetField("graceFrames", Any),
            OnDash = typeof(spikeScript).GetField("killPlayerOnDash", Any), Killed = typeof(spikeScript).GetField("playerKilled", Any);
        static readonly Color HurtboxColor = new Color(0.663f, 0.545f, 1f, 0.95f), GraceColor = new Color(0.35f, 1f, 0.45f, 0.95f),
            KilledColor = new Color(1f, 0.2f, 0.25f, 0.95f);

        readonly List<BoxCollider2D> boxes = new List<BoxCollider2D>();
        readonly List<Collider2D> overlaps = new List<Collider2D>();
        readonly HashSet<spikeScript> spikes = new HashSet<spikeScript>();
        int ticks, limit, lastTicks, lastLimit;
        bool killed, dashing, grounded, lastKilled;
        float lastTime = float.NegativeInfinity;
        GUIStyle style;
        int styleHeight;

        /// <summary>The player's enabled box collider other than its body (normalCollider): what spikes hit.</summary>
        BoxCollider2D Hurtbox(Movement p)
        {
            object body = NormalCollider?.GetValue(p);
            p.GetComponents(boxes);
            foreach (BoxCollider2D box in boxes)
                if (box.enabled && !ReferenceEquals(box, body)) return box;
            return null;
        }

        /// <summary>Every frame, after its physics steps: the spikes' counters as their OnTriggerStay2D left them.</summary>
        public void Sample(Movement p)
        {
            int t = 0, l = int.MaxValue;
            bool k = false;
            dashing = p.dashActive;
            grounded = p.onGround;
            spikes.Clear();
            BoxCollider2D hurtbox = Hurtbox(p);
            if (hurtbox != null)
            {
                var filter = new ContactFilter2D { useTriggers = true, useLayerMask = true };
                filter.layerMask = (Physics2D.GetLayerCollisionMask(hurtbox.gameObject.layer) | hurtbox.includeLayers) & ~hurtbox.excludeLayers;
                hurtbox.Overlap(filter, overlaps);
                foreach (Collider2D c in overlaps)
                {
                    spikeScript spike = c.GetComponent<spikeScript>();
                    if (spike == null || !spike.enabled || !spikes.Add(spike)) continue;
                    t = Mathf.Max(t, (int)Counter.GetValue(spike) + 1);
                    k |= (bool)Killed.GetValue(spike);
                    l = Mathf.Min(l, dashing && (bool)OnDash.GetValue(spike) ? 1 : (int)GraceFrames.GetValue(spike) + 1);
                }
            }
            if (t == 0 && ticks > 0)
            {
                lastTicks = ticks;
                lastLimit = limit;
                lastKilled = killed;
                lastTime = Time.unscaledTime;
            }
            ticks = t;
            limit = t > 0 ? l : 0;
            killed = k;
        }

        public void Draw(Movement p)
        {
            Camera cam = Camera.main;
            BoxCollider2D box = Hurtbox(p);
            if (cam == null || box == null) return;
            float scale = Screen.height / 1080f;
            if (style == null || styleHeight != Screen.height)
            {
                styleHeight = Screen.height;
                style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(18 * scale), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow, wordWrap = false };
            }
            Rect r = ScreenRect(cam, box.bounds);
            Color fill = ticks > 0 && !killed ? GraceColor : HurtboxColor;
            LabTracker.Fill(r, new Color(fill.r, fill.g, fill.b, 0.6f));

            string text;
            Color color;
            if (ticks > 0)
            {
                text = (killed ? "SPIKE ✗ " : "SPIKE ") + ticks + "/" + limit + (dashing ? " · DASH" : "") + (grounded ? " · GROUNDED" : "");
                color = killed ? KilledColor : GraceColor;
            }
            else if (Time.unscaledTime - lastTime < LingerSeconds)
            {
                text = lastKilled ? "SPIKE ✗" : "SPIKE GRACE " + lastTicks + "/" + lastLimit;
                color = lastKilled ? KilledColor : GraceColor;
                color.a = 1f - (Time.unscaledTime - lastTime) / LingerSeconds;
            }
            else return;
            float width = 360 * scale, height = 28 * scale;
            var label = new Rect(r.center.x - width / 2, r.yMin - height - 6 * scale, width, height);
            LabTracker.Fill(new Rect(r.center.x - 130 * scale, label.y, 260 * scale, height), new Color(0f, 0f, 0f, 0.55f * color.a));
            LabTracker.Label(label, text, style, color);
        }

        static Rect ScreenRect(Camera cam, Bounds bounds)
        {
            Vector3 a = cam.WorldToScreenPoint(bounds.min), b = cam.WorldToScreenPoint(bounds.max);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y), Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));
        }
    }
}
