"""Packs every IGTAPTasMod lab map into one world (labs/world.json).

  python tools/pack.py             build labs/world.json (runs the offline engine: lab space, lab replay)
                                   REGION=x,y (lower-left of the free region, from `lab space`) is required
  python tools/pack.py --selftest  bounds() on two hand-made labs
  python tools/pack.py --check     no two boxes overlap, every shape/start/finish inside its box
"""
import glob, json, math, os, shutil, subprocess, sys, tempfile
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
WORLD = os.path.join(HERE, "..", "labs", "world.json")
TAS = os.environ.get("IGTAP_TAS") or os.path.join(HERE, "..", "..", "IGTAPTasMod")
MAPS = os.path.join(TAS, "labmaps")
EXE = os.path.join(TAS, "enginesim", "Cli", "bin", "Release", "net10.0", "IGTAP.EngineSim.exe")


def traces():
    sys.path.insert(0, os.path.join(TAS, "tools"))
    from gamepaths import TRACES  # the game's config folder (IGTAP_GAME overrides the install)
    return TRACES

PLAYER = (20.5, 27)
MARGIN, GAP = 150, 300
ROW_WIDTH = 20000


def shape_points(s):
    ax, ay = s.get("at", [0, 0])
    if s.get("kind", "box") == "box":
        w, h = s["size"]
        a = math.radians(s.get("rotation", 0))
        c, n = math.cos(a), math.sin(a)
        return [(ax + dx * c - dy * n, ay + dx * n + dy * c)
                for dx, dy in ((-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2))]
    return [(ax + px, ay + py) for px, py in s["points"]]


def content(lab):
    pts = []
    for s in lab["shapes"] + (lab.get("hand") or {}).get("shapes", []):
        pts += shape_points(s)
    starts = [lab["start"]] + [d["start"] for d in lab.get("demos", []) if d.get("start")]
    hs = (lab.get("hand") or {}).get("start")
    if hs:
        starts.append(hs)
    for st in starts:
        if st.get("x") is not None and st.get("y") is not None:
            for sx in (-1, 1):
                for sy in (-1, 1):
                    pts.append((st["x"] + sx * PLAYER[0], st["y"] + sy * PLAYER[1]))
    cx, cy, w, h = lab["finish"]
    pts += [(cx - w / 2, cy - h / 2), (cx + w / 2, cy + h / 2)]
    return pts


def extent(pts):
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    return min(xs), min(ys), max(xs), max(ys)


def bounds(lab):
    x0, y0, x1, y1 = extent(content(lab))
    return [x0 - MARGIN, y0 - MARGIN, x1 - x0 + 2 * MARGIN, y1 - y0 + 2 * MARGIN]


def selftest():
    box = {"shapes": [{"kind": "box", "at": [0, 0], "size": [100, 100], "rotation": 45}],
           "start": {}, "finish": [0, 0, 10, 10]}
    x, y, w, h = bounds(box)
    r = 50 * math.sqrt(2)
    assert abs(x - (-r - MARGIN)) < 1e-6 and abs(w - (2 * r + 2 * MARGIN)) < 1e-6, (x, w)
    assert abs(y - x) < 1e-6 and abs(h - w) < 1e-6
    poly = {"shapes": [{"kind": "polygon", "at": [100, 0], "points": [[0, 0], [300, 0], [0, 200]]}],
            "start": {"x": 500, "y": 50}, "finish": [100, 100, 20, 20],
            "demos": [{"start": {"x": -200, "y": 10}}]}
    x, y, w, h = bounds(poly)
    assert x == -200 - PLAYER[0] - MARGIN, x
    assert y == 10 - PLAYER[1] - MARGIN, y
    assert x + w == 500 + PLAYER[0] + MARGIN, x + w
    assert y + h == 200 + MARGIN, y + h
    print("selftest ok")


def pack(labs):
    off, x, y, row_h, W = {}, 0, 0, 0, 0
    for l in sorted(labs, key=lambda l: -l["box"][3]):
        w, h = l["box"][2], l["box"][3]
        if x > 0 and x + w > ROW_WIDTH:
            x, y, row_h = 0, y + row_h + GAP, 0
        off[l["id"]] = (x, y)
        x += w + GAP
        row_h = max(row_h, h)
        W = max(W, x - GAP)
    return off, W, y + row_h


def load():
    labs = {}
    for f in sorted(glob.glob(os.path.join(MAPS, "*.json"))):
        if not f.endswith(".demo.json"):
            lab = json.load(open(f, encoding="utf-8"))
            lab["demos"] = []
            labs[lab["id"]] = lab
    for f in sorted(glob.glob(os.path.join(MAPS, "*.demo.json"))):
        d = json.load(open(f, encoding="utf-8"))
        lab = labs[d["lab"]]
        base = os.path.basename(f)[:-len(".demo.json")]
        assert base == lab["id"] or base.startswith(lab["id"] + "."), base
        lab["demos"].append((base[len(lab["id"]) + 1:], d))
    for lab in labs.values():
        lab["demos"].sort(key=lambda t: t[0] != "")
        assert lab["demos"] and lab["demos"][0][0] == "", lab["id"]
    return labs


def engine(*args):
    r = subprocess.run([EXE, *map(str, args)], capture_output=True, text=True)
    return r.returncode, r.stdout + r.stderr


def replay_path(lab, name, d, anchor, tmp):
    """Sources: a packed anchor, b lab's own anchor (far out the engine misses the ground; paths are anchor-relative),
    c the demo file's stored path (negative variants never finish), else null."""
    path, out = replay_at(lab, name, d, anchor, tmp)
    if path is not None:
        return path, "a"
    path, out = replay_at(lab, name, d, lab["anchor"], tmp + "-home")
    if path is not None:
        return path, "b"
    if d.get("path"):
        return [[round(v, 2) if isinstance(v, float) else v for v in row] for row in d["path"]], "c"
    return None, "null"


def replay_at(lab, name, d, anchor, tmp):
    lp = os.path.join(tmp, lab["id"] + ".json")
    dp = os.path.join(tmp, lab["id"] + (("." + name) if name else "") + ".demo.json")
    os.makedirs(tmp, exist_ok=True)
    json.dump({**{k: v for k, v in lab.items() if k != "demos"}, "anchor": anchor}, open(lp, "w", encoding="utf-8"))
    json.dump(d, open(dp, "w", encoding="utf-8"))
    code, out = engine("lab", "replay", os.path.join(traces(), d["solvedIn"]), lp, dp, "--write-path")
    path = json.load(open(dp, encoding="utf-8")).get("path")
    if code != 0 or not path or "finish at tick" not in out:
        return None, out
    return [[round(v, 2) if isinstance(v, float) else v for v in row] for row in path], out


INPUT_KEYS = ("x", "y", "press", "release", "dash", "dashJump", "springDash", "turnX", "turnY", "pause", "reset")
SHAPE_KEYS = ("kind", "at", "size", "points", "layer", "tag", "trigger", "spike", "rotation", "spring", "colour")


def shapes_out(ss):
    return [{k: s[k] for k in SHAPE_KEYS if k in s} for s in ss]


def start_out(st, lab):
    """A checkpoint lab's respawnPoint (world frame, at the map's own anchor) as `respawn`, anchor-relative; the lab
    sets courseResetPoint itself (0,0 in a checkpoint lab: a quick restart goes to respawnPoint)."""
    fields = dict(st.get("fields") or {})
    rp, crp = fields.pop("respawnPoint", None), fields.pop("courseResetPoint", None)
    out = {**st, "fields": fields}
    if rp is not None and crp == "0,0":
        x, y = (float(v) for v in rp.split(","))
        out["respawn"] = [round(x - lab["anchor"][0], 2), round(y - lab["anchor"][1], 2)]
    return out


def lab_out(lab, anchor, paths):
    demos = [{"name": n, "start": start_out(d.get("start") or lab["start"], lab),
              "category": d.get("requires") or lab["category"],
              "inputs": [{k: t[k] for k in INPUT_KEYS if k in t} for t in d["inputs"]],
              "path": paths[(lab["id"], n)]} for n, d in lab["demos"]]
    out = {"id": lab["id"], "name": lab["name"], "category": lab["category"],
           "anchor": anchor, "box": lab["box"], "shapes": shapes_out(lab["shapes"]), "start": start_out(lab["start"], lab),
           "finish": lab["finish"], "demos": demos}
    if lab.get("hand"):
        out["hand"] = {"shapes": shapes_out(lab["hand"]["shapes"]), "start": lab["hand"].get("start")}
    return out


def pauselift_world():
    with open(os.path.join(MAPS, "pauselift-wall.demo.json"), encoding="utf-8") as f:
        return json.load(f)["world"]


def build():
    labs = load()
    for lab in labs.values():
        lab["box"] = [round(v, 2) for v in bounds({**lab, "demos": [{"start": d.get("start")} for _, d in lab["demos"]]})]
    off, W, H = pack(list(labs.values()))
    print(f"pack {W:.0f} x {H:.0f}")
    if "REGION" not in os.environ:
        print(engine("lab", "space", os.path.join(traces(), pauselift_world()), math.ceil(W), math.ceil(H), "--list", 5)[1])
        sys.exit("set REGION=x,y to the region's lower-left corner")
    rx, ry = (float(v) for v in os.environ["REGION"].split(","))
    anchors = {i: [round(rx + off[i][0] - l["box"][0], 2), round(ry + off[i][1] - l["box"][1], 2)] for i, l in labs.items()}
    tmp = tempfile.mkdtemp(prefix="pack-")
    jobs = [(l, n, d) for l in labs.values() for n, d in l["demos"]]
    paths, src = {}, {k: [] for k in ("a", "b", "c", "null")}

    def run(j):
        l, n, d = j
        return replay_path(l, n, d, anchors[l["id"]], os.path.join(tmp, l["id"] + "." + n))

    with ThreadPoolExecutor(4) as ex:
        for (l, n, d), (p, out) in zip(jobs, ex.map(run, jobs)):
            paths[(l["id"], n)] = p
            src[out].append((l["id"], n))
    shutil.rmtree(tmp, ignore_errors=True)
    world = {"labs": [lab_out(l, anchors[i], paths) for i, l in labs.items()]}
    os.makedirs(os.path.dirname(WORLD), exist_ok=True)
    json.dump(world, open(WORLD, "w", encoding="utf-8"), separators=(",", ":"))
    print(f"{len(world['labs'])} labs, {len(jobs)} demos, " + ", ".join(f"{k}:{len(v)}" for k, v in src.items()))
    for k in ("c", "null"):
        for n in src[k]:
            print(f"source {k}:", n)


def check():
    labs = json.load(open(WORLD, encoding="utf-8"))["labs"]
    rects = []
    for l in labs:
        ax, ay = l["anchor"]
        x, y, w, h = l["box"]
        x0, y0, x1, y1 = extent(content(l))
        assert x <= x0 + 1e-3 and y <= y0 + 1e-3 and x + w >= x1 - 1e-3 and y + h >= y1 - 1e-3, l["id"]
        rects.append((l["id"], ax + x, ay + y, ax + x + w, ay + y + h))
    for i, a in enumerate(rects):
        for b in rects[i + 1:]:
            assert a[3] <= b[1] or b[3] <= a[1] or a[4] <= b[2] or b[4] <= a[2], (a[0], b[0])
    print(f"check ok: {len(labs)} labs, {sum(len(l['demos']) for l in labs)} demos")


if __name__ == "__main__":
    if "--selftest" in sys.argv:
        selftest()
    elif "--check" in sys.argv:
        check()
    else:
        build()
