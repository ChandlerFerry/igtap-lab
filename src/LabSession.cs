using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IgtapLab
{
    public static class LabSession
    {
        public static LabDef Current { get; private set; }
        public static DemoDef Demo => Current == null || Current.demos.Count == 0 ? null : Current.demos[_demo % Current.demos.Count];
        public static Movement Player { get; private set; }
        public static event Action AttemptStarted;

        // Anchors are in the world frame; scene positions add FloatingOrigin.currentOrigin.
        public static Vector2 LabToScene(LabDef lab, float x, float y) => lab.Anchor + new Vector2(x, y) + Origin();

        public static bool InFinish()
        {
            float[] f = Current?.finish;
            if (f == null || f.Length < 4 || Player == null) return false;
            Vector2 at = (Vector2)Player.transform.position - LabToScene(Current, 0f, 0f);
            return Mathf.Abs(at.x - f[0]) <= f[2] / 2f + 20f && Mathf.Abs(at.y - f[1]) <= f[3] / 2f + 27f;
        }

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly FieldInfo IsDead = typeof(Movement).GetField("isDead", Any);
        static readonly MethodInfo Respawn = typeof(Movement).GetMethod("respawn", Any);
        static readonly FieldInfo Tracking = typeof(courseScript).GetField("tracking", Any);
        static readonly FieldInfo IsSaving = typeof(Saveloader).GetField("IsSaving", Any);
        static readonly globalStats.globalUpgradeSet[] Upgrades =
            { globalStats.globalUpgradeSet.unlockJiggleDrops, globalStats.globalUpgradeSet.moreRefreshOrbs, globalStats.globalUpgradeSet.zipMoversUnlocked };

        static readonly string[] CategoryFields = { "wallJumpUnlocked", "dashUnlocked", "maxAirDashes", "airDashesLeft", "doubleJumpUnlocked",
            "maxAirJumps", "airJumpsLeft", "blockSwapUnlocked", "omniDashUnlocked" };
        static readonly string[] BodyFields = { "Velocity", "momentum", "movingPlatformVelocity", "facingRight", "onGround", "courseResetPoint", "respawnPoint" };
        // Absolute positions at the lab's old anchor: Place sets both from the start instead.
        static readonly string[] SkippedStartFields = { "respawnPoint", "courseResetPoint" };

        static int _demo;
        static bool _wasDead;
        static Scene _scene;

        static Dictionary<FieldInfo, object> _fields;
        static Vector3 _position, _scale;
        static Vector2 _bodyVelocity, _origin;
        static readonly Dictionary<globalStats.globalUpgradeSet, double?> _upgrades = new Dictionary<globalStats.globalUpgradeSet, double?>();
        static readonly Dictionary<globalStats.globalUpgradeSet, double> _written = new Dictionary<globalStats.globalUpgradeSet, double>();
        static bool? _blueActive;
        static int? _zone;
        static List<courseScript> _tracking;
        static bool _isSaving, _autosaving;

        public static void Enter(LabDef lab, int demo)
        {
            if (Current == null)
            {
                Movement player = FindPlayer();
                if (player == null) { LabMod.Host?.LogWarning("No player in this scene: start the game before entering a lab."); return; }
                LabWorld.Build(LabMod.World, player);
                Snapshot(player, LabMod.World);
                Player = player;
                _scene = player.gameObject.scene;
            }
            Current = lab;
            _demo = demo;
            Place();
        }

        public static void NextDemo()
        {
            if (Current == null) return;
            _demo = (_demo + 1) % Math.Max(1, Current.demos.Count);
            Place();
        }

        public static void Exit()
        {
            if (Current == null) return;
            try { Restore(); }
            catch (Exception e) { LabMod.Host?.LogError("Leaving the lab: " + e); }
            finally
            {
                Current = null;
                Player = null;
                _fields = null;
                _tracking = null;
                LabWorld.Destroy();
            }
        }

        public static void Update()
        {
            if (Current == null) return;
            Movement p = Player;
            if (p == null) { Exit(); return; }
            if (!p.gameObject.activeInHierarchy) return;
            LabWorld.Update();
            if ((bool)IsDead.GetValue(p)) { _wasDead = true; return; }
            // The game's quick restart (and a death respawn with checkpoint respawns off) lands on courseResetPoint and zeroes it.
            if (_wasDead || p.courseResetPoint == Vector2.zero || !InBox(p)) { Place(); return; }
            // courseResetPoint is a scene position: it follows a rebase of the floating origin.
            p.courseResetPoint = StartScene() + new Vector2(0f, 12f);
        }

        public static void SceneUnloaded(Scene scene)
        {
            if (Current != null && scene == _scene) Exit();
        }

        static StartDef Start => Demo?.start ?? Current.start;

        static Vector2 StartScene() => LabToScene(Current, Start.x ?? 0f, Start.y ?? 0f);

        static Vector2 Origin()
        {
            FloatingOrigin origin = Singleton<FloatingOrigin>.Instance;
            return origin == null ? Vector2.zero : (Vector2)origin.currentOrigin;
        }

        static bool InBox(Movement p)
        {
            float[] b = Current.box;
            if (b == null || b.Length < 4) return true;
            Vector2 at = (Vector2)p.transform.position - LabToScene(Current, 0f, 0f);
            return at.x >= b[0] && at.x <= b[0] + b[2] && at.y >= b[1] && at.y <= b[1] + b[3];
        }

        static Movement FindPlayer()
        {
            GameObject go = GameObject.FindGameObjectWithTag("Player");
            return go != null ? go.GetComponent<Movement>() : null;
        }

        static void Place()
        {
            Movement p = Player;
            StartDef start = Start;
            Vector2 scene = StartScene();
            _wasDead = false;

            bool blockSwap = ApplyCategory(p, Demo?.category ?? Current.category);
            // A pending death respawn would move the player again.
            p.CancelInvoke("deathRespawn");
            p.courseResetPoint = scene + new Vector2(0f, 12f);
            Respawn.Invoke(p, new object[] { true });
            Rigidbody2D body = p.GetComponent<Rigidbody2D>();
            if (body != null) body.position = scene;

            if (start.fields != null)
                foreach (JProperty field in start.fields.Properties())
                {
                    if (SkippedStartFields.Contains(field.Name)) continue;
                    FieldInfo f = typeof(Movement).GetField(field.Name, Any);
                    if (f == null) { LabMod.Host?.LogWarning("Lab " + Current.id + ": Movement has no field " + field.Name + "."); continue; }
                    f.SetValue(p, field.Value.ToObject(f.FieldType));
                }

            p.Velocity = new Vector2(start.vx ?? 0f, start.vy ?? 0f);
            p.momentum = new Vector2(start.mx ?? 0f, start.my ?? 0f);
            if (body != null) body.linearVelocity = p.Velocity + p.momentum;
            bool right = (start.facing ?? 1f) > 0f;
            p.facingRight = right;
            p.transform.localScale = new Vector3(right ? 1f : -1f, 1f, 1f);

            // Without block swap the blocks start unswapped (blue active), as a new game has them.
            bool? blue = start.blueActive ?? (blockSwap ? (bool?)null : true);
            colouredBlockSwapper swapper = Singleton<colouredBlockSwapper>.Instance;
            if (blue.HasValue && swapper != null) swapper.swapBlocks(blue.Value);

            // Movement.respawn: respawnPoint is in the world frame (it adds the origin), courseResetPoint a scene position; both 12 up.
            p.respawnPoint = Current.Anchor + new Vector2(start.x ?? 0f, (start.y ?? 0f) + 12f);
            p.courseResetPoint = scene + new Vector2(0f, 12f);

            Physics2D.SyncTransforms();
            if (p.cam != null) p.cam.setup(scene, p.cam.camSize);
            LabWorld.Update();
            AttemptStarted?.Invoke();
        }

        // Category tokens, in this order: WJ, DS/DSn (air dashes), DJ/DJn (air jumps), BS, ORB, ZIP, OM; NONE when empty.
        static bool ApplyCategory(Movement p, string category)
        {
            bool wj = false, bs = false, orb = false, zip = false, om = false;
            int ds = 0, dj = 0;
            foreach (string token in (category ?? "NONE").Split('_'))
            {
                if (token == "WJ") wj = true;
                else if (token.StartsWith("DS")) ds = token.Length > 2 ? int.Parse(token.Substring(2)) : 1;
                else if (token.StartsWith("DJ")) dj = token.Length > 2 ? int.Parse(token.Substring(2)) : 1;
                else if (token == "BS") bs = true;
                else if (token == "ORB") orb = true;
                else if (token == "ZIP") zip = true;
                else if (token == "OM") om = true;
                else if (token != "NONE") LabMod.Host?.LogWarning("Category " + category + ": unknown token " + token + ".");
            }
            p.wallJumpUnlocked = wj;
            p.dashUnlocked = ds > 0;
            p.maxAirDashes = p.airDashesLeft = ds;
            p.doubleJumpUnlocked = dj > 0;
            p.maxAirJumps = p.airJumpsLeft = dj;
            p.blockSwapUnlocked = bs;
            p.omniDashUnlocked = om;
            // ORB: every refill orb (the orbs' box 1, "more orbs" 2); ZIP: the zip movers.
            Write(globalStats.globalUpgradeSet.unlockJiggleDrops, orb ? 1.0 : 0.0);
            Write(globalStats.globalUpgradeSet.moreRefreshOrbs, orb ? 2.0 : 0.0);
            Write(globalStats.globalUpgradeSet.zipMoversUnlocked, zip ? 1.0 : 0.0);
            return bs;
        }

        static void Write(globalStats.globalUpgradeSet key, double value)
        {
            globalStats.globalUpgradeDict[key] = value;
            _written[key] = value;
        }

        static void Revive(Movement p)
        {
            if (!(bool)IsDead.GetValue(p)) return;
            p.CancelInvoke("deathRespawn");
            Respawn.Invoke(p, new object[] { true });
        }

        static void Snapshot(Movement p, WorldDef world)
        {
            Revive(p);
            // Saves are off while in a lab, so quitting from one would lose what came before it.
            Saveloader saver = Singleton<Saveloader>.Instance;
            if (saver != null) saver.manualSave();

            var names = new HashSet<string>(CategoryFields.Concat(BodyFields));
            foreach (LabDef lab in world.labs)
                foreach (StartDef start in lab.demos.Select(d => d.start).Append(lab.start).Append(lab.hand?.start))
                    if (start?.fields != null) names.UnionWith(start.fields.Properties().Select(f => f.Name));
            _fields = new Dictionary<FieldInfo, object>();
            foreach (string name in names)
            {
                FieldInfo f = typeof(Movement).GetField(name, Any);
                if (f != null) _fields[f] = f.GetValue(p);
            }
            _position = p.transform.position;
            _scale = p.transform.localScale;
            Rigidbody2D body = p.GetComponent<Rigidbody2D>();
            _bodyVelocity = body != null ? body.linearVelocity : Vector2.zero;
            _origin = Origin();

            _upgrades.Clear();
            foreach (globalStats.globalUpgradeSet u in Upgrades)
                _upgrades[u] = globalStats.globalUpgradeDict.TryGetValue(u, out double level) ? level : (double?)null;
            colouredBlockSwapper swapper = Singleton<colouredBlockSwapper>.Instance;
            _blueActive = swapper != null ? swapper.isBlueActive : (bool?)null;
            ZoneLoader zones = Singleton<ZoneLoader>.Instance;
            _zone = zones != null ? zones.activeZone : (int?)null;

            // A course that is tracking writes courseResetPoint every tick: it pauses while in the lab.
            _tracking = UnityEngine.Object.FindObjectsByType<courseScript>(FindObjectsSortMode.None).Where(c => (bool)Tracking.GetValue(c)).ToList();
            foreach (courseScript c in _tracking) Tracking.SetValue(c, false);

            // Saveloader skips autosave and manualSave while courseResetPoint is not zero, which the lab keeps; a quick restart zeroes
            // it until the next frame, so saving is also switched off (manualSave reads IsSaving) and the autosave timer stopped.
            if (saver != null)
            {
                _isSaving = (bool)IsSaving.GetValue(saver);
                _autosaving = saver.IsInvoking("autosave");
                IsSaving.SetValue(saver, false);
                saver.CancelInvoke("autosave");
            }
        }

        static void Restore()
        {
            // Gone with its scene: the next scene's player, upgrades, blocks, courses and Saveloader are loaded from the save, untouched.
            Movement p = Player;
            if (p == null) return;
            // A key the game changed since the lab wrote it (a deleted save zeroes them all) is the game's now.
            foreach (var u in _upgrades)
            {
                globalStats.globalUpgradeDict.TryGetValue(u.Key, out double now);
                if (now != _written[u.Key]) continue;
                if (u.Value.HasValue) globalStats.globalUpgradeDict[u.Key] = u.Value.Value;
                else globalStats.globalUpgradeDict.Remove(u.Key);
            }
            Revive(p);
            // Scene positions move with any rebase since the snapshot.
            Vector2 shift = Origin() - _origin;
            foreach (var f in _fields) f.Key.SetValue(p, f.Value);
            Vector2 reset = (Vector2)_fields.First(f => f.Key.Name == "courseResetPoint").Value;
            if (reset != Vector2.zero) p.courseResetPoint = reset + shift;
            Vector3 position = _position + (Vector3)shift;
            p.transform.position = position;
            p.transform.localScale = _scale;
            Rigidbody2D body = p.GetComponent<Rigidbody2D>();
            if (body != null) { body.position = position; body.linearVelocity = _bodyVelocity; }

            colouredBlockSwapper swapper = Singleton<colouredBlockSwapper>.Instance;
            if (_blueActive.HasValue && swapper != null) swapper.swapBlocks(_blueActive.Value);
            ZoneLoader zones = Singleton<ZoneLoader>.Instance;
            if (_zone.HasValue && zones != null && zones.activeZone != _zone.Value) zones.LoadZone(_zone.Value);
            foreach (courseScript c in _tracking)
                if (c != null) Tracking.SetValue(c, true);
            Physics2D.SyncTransforms();
            if (p.cam != null) p.cam.setup(position, p.cam.camSize);

            Saveloader saver = Singleton<Saveloader>.Instance;
            if (saver != null)
            {
                IsSaving.SetValue(saver, _isSaving);
                if (_autosaving) saver.InvokeRepeating("autosave", 30f, 30f);
            }
        }
    }
}
