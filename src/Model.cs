using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace IgtapLab
{
    public sealed class WorldDef
    {
        public List<LabDef> labs = new List<LabDef>();
    }

    /// <summary>One lab. Everything but <c>anchor</c> is relative to the anchor, in world units (scene position minus the floating origin).</summary>
    public sealed class LabDef
    {
        public string id, name, category = "NONE";
        public float[] anchor;
        /// <summary>The bounding box [x, y, w, h], x/y its lower-left corner.</summary>
        public float[] box;
        public List<ShapeDef> shapes = new List<ShapeDef>();
        public StartDef start = new StartDef();
        public HandDef hand;
        /// <summary>The finish box [cx, cy, w, h].</summary>
        public float[] finish;
        public List<DemoDef> demos = new List<DemoDef>();

        public Vector2 Anchor => new Vector2(anchor[0], anchor[1]);
    }

    public sealed class ShapeDef
    {
        public string kind = "box";
        public float[] at, size;
        public float[][] points;
        public string layer = "Ground", tag = "Ground";
        public bool trigger, spike;
        /// <summary>Degrees about z, counter-clockwise.</summary>
        public float rotation;
        public SpringDef spring;
        /// <summary>"blue" or "orange": on only while that colour is active.</summary>
        public string colour;

        public Quaternion Rotation()
        {
            if (rotation == 0f) return Quaternion.identity;
            double half = rotation * Math.PI / 360.0;
            return new Quaternion(0f, 0f, (float)Math.Sin(half), (float)Math.Cos(half));
        }
    }

    public sealed class SpringDef
    {
        public float upForce = 70f, strength, movementLock = 0.25f;
    }

    public sealed class StartDef
    {
        public float? x, y, vx, vy, mx, my, facing;
        public bool? blueActive;
        public JObject fields;
    }

    public sealed class HandDef
    {
        public List<ShapeDef> shapes = new List<ShapeDef>();
        public StartDef start;
    }

    public sealed class DemoDef
    {
        /// <summary>"" for the lab's main demo, else the variant's name.</summary>
        public string name = "";
        /// <summary>Where the demo starts (its own start, else the lab's): every attempt is placed here.</summary>
        public StartDef start;
        /// <summary>The unlocks the demo plays with (its <c>requires</c>, else the lab's category).</summary>
        public string category;
        public List<InputTick> inputs = new List<InputTick>();
        /// <summary>One entry per physics tick from the lab start: [x, y, facing, animation, animator speed], anchor-relative. Null: no ghost.</summary>
        public float[][] path;
    }

    public sealed class InputTick
    {
        public float x, y;
        public bool press, release, dash;
        public int dashJump, springDash;
        public float? turnX, turnY;
        public int pause;
    }
}
