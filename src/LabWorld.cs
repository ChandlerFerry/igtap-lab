using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IgtapLab
{
    public static class LabWorld
    {
        static readonly List<GameObject> Roots = new List<GameObject>();
        static readonly List<(Collider2D collider, bool blue, SpriteRenderer fill)> Colours = new List<(Collider2D, bool, SpriteRenderer)>();
        static bool? _blue;
        static Material _material;
        static Sprite _pixel;
        static int _sortingLayer, _playerOrder;

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly FieldInfo SpringUpForce = typeof(SpringScript).GetField("upForce", Any),
            SpringStrength = typeof(SpringScript).GetField("strength", Any),
            SpringMovementLock = typeof(SpringScript).GetField("movementLock", Any);

        public static void Build(WorldDef world, Movement player)
        {
            if (Roots.Count > 0) return;
            Transform spriteT = player.transform.Find("Sprite");
            SpriteRenderer playerSprite = spriteT != null ? spriteT.GetComponent<SpriteRenderer>() : player.GetComponentInChildren<SpriteRenderer>();
            _sortingLayer = playerSprite != null ? playerSprite.sortingLayerID : 0;
            _playerOrder = playerSprite != null ? playerSprite.sortingOrder : 0;
            TMP_FontAsset font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();

            try
            {
                foreach (LabDef lab in world.labs) BuildLab(lab, player, font);
            }
            catch
            {
                Destroy();
                throw;
            }
            _blue = null;
            Update();
        }

        static void BuildLab(LabDef lab, Movement player, TMP_FontAsset font)
        {
            // FloatingOrigin.MoveRootTransforms shifts every root object of the loaded scenes on a rebase: a root in the player's
            // scene moves with the terrain. Not under a DontDestroyOnLoad object, whose scene it skips.
            var root = new GameObject("IgtapLab " + lab.id);
            Roots.Add(root);
            SceneManager.MoveGameObjectToScene(root, player.gameObject.scene);
            root.transform.position = LabSession.LabToScene(lab, 0f, 0f);
            IEnumerable<ShapeDef> shapes = lab.hand?.shapes == null ? lab.shapes : lab.shapes.Concat(lab.hand.shapes);
            int i = 0;
            foreach (ShapeDef shape in shapes) BuildShape(root.transform, lab.id + " " + ++i, shape);
            Outline(root.transform, lab, font);
        }

        public static void Destroy()
        {
            foreach (GameObject root in Roots)
                if (root != null) UnityEngine.Object.Destroy(root);
            Roots.Clear();
            Colours.Clear();
            _blue = null;
        }

        // Polled: colouredBlockSwapper.swapBlocks raises no event, and the mod patches nothing.
        public static void Update()
        {
            colouredBlockSwapper swapper = Singleton<colouredBlockSwapper>.Instance;
            bool blue = swapper == null || swapper.isBlueActive;
            if (_blue == blue) return;
            _blue = blue;
            foreach (var (collider, isBlue, fill) in Colours)
            {
                if (collider == null) continue;
                collider.enabled = isBlue == blue;
                if (fill == null) continue;
                Color c = fill.color;
                c.a = isBlue == blue ? 1f : 0.3f;
                fill.color = c;
            }
        }

        static void BuildShape(Transform root, string name, ShapeDef shape)
        {
            var go = new GameObject(name + " " + shape.kind);
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(shape.at != null ? shape.at[0] : 0f, shape.at != null ? shape.at[1] : 0f, 0f);
            go.transform.localRotation = shape.Rotation();
            go.layer = LayerNumber(shape.layer);
            try { go.tag = string.IsNullOrEmpty(shape.tag) ? "Untagged" : shape.tag; }
            catch (UnityException) { go.tag = "Untagged"; }
            Collider2D collider;
            switch (shape.kind)
            {
                case "box":
                    var box = go.AddComponent<BoxCollider2D>();
                    box.size = new Vector2(shape.size[0], shape.size[1]);
                    collider = box;
                    break;
                case "polygon":
                    var poly = go.AddComponent<PolygonCollider2D>();
                    poly.points = shape.points.Select(p => new Vector2(p[0], p[1])).ToArray();
                    collider = poly;
                    break;
                case "edge":
                    var edge = go.AddComponent<EdgeCollider2D>();
                    edge.points = shape.points.Select(p => new Vector2(p[0], p[1])).ToArray();
                    collider = edge;
                    break;
                default: throw new InvalidOperationException("A lab shape's kind is box, polygon or edge, not " + shape.kind + ".");
            }
            collider.isTrigger = shape.trigger;
            if (shape.spike) go.AddComponent<spikeScript>();
            if (shape.spring != null)
            {
                // No Animator: SpringScript then skips its animation and sound.
                var spring = go.AddComponent<SpringScript>();
                SpringUpForce.SetValue(spring, shape.spring.upForce);
                SpringStrength.SetValue(spring, shape.spring.strength);
                SpringMovementLock.SetValue(spring, shape.spring.movementLock);
            }
            SpriteRenderer fill = ShapeVisual(go.transform, shape);
            if (shape.colour != null) Colours.Add((collider, shape.colour == "blue", fill));
        }

        static int LayerNumber(string layer)
        {
            if (int.TryParse(layer, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)) return number;
            int named = LayerMask.NameToLayer(layer);
            if (named < 0) throw new InvalidOperationException("No Unity layer named " + layer + ".");
            return named;
        }

        static SpriteRenderer ShapeVisual(Transform parent, ShapeDef shape)
        {
            Color color = shape.spike || shape.layer == "Spikes" ? new Color(0.75f, 0.22f, 0.17f) : shape.spring != null ? new Color(1f, 0.84f, 0.31f)
                : shape.colour == "blue" ? new Color(0.20f, 0.45f, 0.95f) : shape.colour == "orange" ? new Color(0.95f, 0.55f, 0.15f)
                : shape.trigger ? new Color(0.56f, 0.27f, 0.68f) : shape.tag == "Moss" ? new Color(0.15f, 0.68f, 0.38f) : new Color(0.10f, 0.14f, 0.20f);
            if (shape.kind != "box")
            {
                Polyline(parent, shape.points.Select(p => new Vector2(p[0], p[1])).ToArray(), shape.kind == "polygon", color, 6f, _playerOrder - 1);
                return null;
            }
            float w = shape.size[0] / 2f, h = shape.size[1] / 2f;
            SpriteRenderer fill = Quad(parent, Vector2.zero, new Vector2(shape.size[0], shape.size[1]), color, _playerOrder - 2);
            Polyline(parent, new[] { new Vector2(-w, -h), new Vector2(w, -h), new Vector2(w, h), new Vector2(-w, h) }, true, new Color(0.3f, 0.85f, 1f), 3f, _playerOrder - 1);
            if (shape.spring != null)
            {
                float tip = h + 40f, head = Mathf.Min(w, 16f);
                Polyline(parent, new[] { new Vector2(0f, h), new Vector2(0f, tip) }, false, color, 6f, _playerOrder - 1);
                Polyline(parent, new[] { new Vector2(-head, tip - head), new Vector2(0f, tip), new Vector2(head, tip - head) }, false, color, 6f, _playerOrder - 1);
            }
            return fill;
        }

        static void Outline(Transform root, LabDef lab, TMP_FontAsset font)
        {
            if (lab.box == null || lab.box.Length < 4) return;
            float x = lab.box[0], y = lab.box[1], w = lab.box[2], h = lab.box[3];
            // Scaled to the lab, so a box taller than the screen keeps a visible outline.
            float width = Mathf.Max(6f, Mathf.Max(w, h) / 300f);
            var tint = new Color(0.3f, 0.85f, 1f, 0.5f);
            Polyline(root, new[] { new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h) }, true, tint, width, _playerOrder - 3);
            if (font == null) return;

            var go = new GameObject("label", typeof(RectTransform));
            go.transform.SetParent(root, false);
            var text = go.AddComponent<TextMeshPro>();
            text.font = font;
            text.text = lab.name;
            text.color = new Color(0.8f, 0.95f, 1f);
            text.alignment = TextAlignmentOptions.BottomLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            // Autosized into a rect of the height wanted, whatever the font's units: 48 u tall, more on a big lab.
            text.enableAutoSizing = true;
            text.fontSizeMin = 1f;
            text.fontSizeMax = 5000f;
            float height = Mathf.Max(48f, Mathf.Max(w, h) / 40f);
            RectTransform rect = text.rectTransform;
            rect.pivot = Vector2.zero;
            rect.sizeDelta = new Vector2(Mathf.Max(w, height * lab.name.Length * 0.7f), height);
            rect.localPosition = new Vector3(x, y + h + width * 2f, 0f);
            text.sortingLayerID = _sortingLayer;
            text.sortingOrder = _playerOrder - 3;
        }

        static void Polyline(Transform parent, Vector2[] points, bool loop, Color color, float width, int order)
        {
            for (int i = 0; i + 1 < points.Length; i++) Segment(parent, points[i], points[i + 1], color, width, order);
            if (loop && points.Length > 2) Segment(parent, points[points.Length - 1], points[0], color, width, order);
        }

        // Quads, not LineRenderers: FloatingOrigin.MoveLineRenderers shifts every LineRenderer's points on a rebase, local-space ones
        // too, so a line under a moving root would move twice.
        static void Segment(Transform parent, Vector2 a, Vector2 b, Color color, float width, int order)
        {
            Vector2 d = b - a;
            SpriteRenderer quad = Quad(parent, (a + b) / 2f, new Vector2(d.magnitude + width, width), color, order);
            quad.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        static SpriteRenderer Quad(Transform parent, Vector2 at, Vector2 size, Color color, int order)
        {
            if (_pixel == null)
            {
                var texture = new Texture2D(1, 1) { filterMode = FilterMode.Point };
                texture.SetPixel(0, 0, Color.white);
                texture.Apply();
                _pixel = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            }
            var go = new GameObject("visual");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = _pixel;
            sprite.color = color;
            Material material = Material();
            if (material != null) sprite.sharedMaterial = material;
            sprite.sortingLayerID = _sortingLayer;
            sprite.sortingOrder = order;
            return sprite;
        }

        // Unlit, so the labs stay visible without the game's 2D lights.
        static Material Material()
        {
            if (_material != null) return _material;
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
            return shader != null ? _material = new Material(shader) : null;
        }
    }
}
