using System;
using UnityEngine;

namespace IgtapLab
{
    public sealed class LabGhost : IDisposable
    {
        const int HoldTicks = 25;
        static readonly Color Tint = new Color(0.35f, 0.85f, 1f, 0.65f);

        readonly LabDef lab;
        readonly float[][] path;
        readonly GameObject root;
        readonly Animator animator;
        readonly Vector2 scale;
        int anim = -1;

        public LabGhost(LabDef lab, DemoDef demo, Movement player)
        {
            this.lab = lab;
            path = demo.path;
            root = new GameObject("lab ghost");
            // Cloned under an inactive root, so none of the copied scripts wakes up before it is stripped.
            root.SetActive(false);
            GameObject visual = UnityEngine.Object.Instantiate(player.animator.gameObject, root.transform, false);
            visual.name = "ghost sprite";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            foreach (MonoBehaviour script in visual.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(script);
            // Only the body: the ability indicators under it stay off.
            foreach (Transform child in visual.transform) child.gameObject.SetActive(false);
            var renderer = visual.GetComponent<SpriteRenderer>();
            if (renderer != null)
            {
                renderer.color = Tint;
                // A flat silhouette (the sprite's alpha filled with the colour); the sprite's own material if the shader isn't in the build.
                Shader shader = Shader.Find("GUI/Text Shader");
                if (shader != null) renderer.sharedMaterial = new Material(shader);
                renderer.sortingOrder -= 1;
            }
            animator = visual.GetComponent<Animator>();
            animator.fireEvents = false;
            animator.applyRootMotion = false;
            root.SetActive(true);
            Vector3 s = player.transform.lossyScale;
            scale = new Vector2(Mathf.Abs(s.x), s.y);
        }

        public void Show(float ticks)
        {
            float cycle = path.Length - 1 + HoldTicks;
            float t = Mathf.Min(ticks % cycle, path.Length - 1);
            int i = Mathf.Min((int)t, path.Length - 2);
            float[] a = path[i], b = path[i + 1];
            Vector2 at = Vector2.Lerp(LabSession.LabToScene(lab, a[0], a[1]), LabSession.LabToScene(lab, b[0], b[1]), t - i);
            root.transform.position = new Vector3(at.x, at.y, 0f);
            root.transform.localScale = new Vector3(scale.x * (b[2] < 0f ? -1f : 1f), scale.y, 1f);
            int state = (int)b[3];
            if (state != anim) { animator.SetInteger("Animation", state); anim = state; }
            animator.speed = b[4];
        }

        public void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
        }
    }
}
