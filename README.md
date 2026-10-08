# IGTAP Lab

A [Recharge](https://github.com/SumDumIdiut/recharge) mod for IGTAP: every tech lab from the IGTAP TAS mod on one map.
Each lab sits in its own box with its name. The lab's demo plays as a ghost, and an input tracker shows the demo's
inputs tick by tick, marking yours green (on the tick), amber (1–3 ticks off) or red.

## Install

Install RechargeLoader with Recharge, download `igtap-lab.zip` from
[Releases](https://github.com/ChandlerFerry/igtap-lab/releases/latest) (or build it: `tools/package.ps1` writes
`igtap-lab.igtap`, the same zip), and extract it into `<game>/Recharge/Mods/chandlerferry.igtaplab/`.

## Play

Pause, **Labs**, pick a lab. Quick restart (or leaving the lab's box) starts the attempt again at the demo's start. In
the checkpoint labs (`checkpoint-midair-*`, `momentum-after-quick-restart`) the quick restart is the tech: an attempt's
first lands on the lab's checkpoint, the next starts the attempt again (as does a death).
Your hurtbox is drawn purple, green while a spike's grace holds, with `SPIKE n/limit` over it while you touch spikes
and `SPIKE GRACE n/limit` after you live through them.
**Next demo** switches between a lab's demos, and **Exit lab** puts you back where you were, with your unlocks.
The game doesn't save while you're in a lab.

## Build

```powershell
python tools/pack.py                         # lists free regions for the pack
$env:REGION = "-15848,16296"; python tools/pack.py   # labs/world.json from ../IGTAPTasMod/labmaps (IGTAP_TAS overrides)
tools/package.ps1                            # IgtapLab.dll + igtap-lab.igtap
dotnet run --project tools/ChipsCheck        # input chips check
python tools/pack.py --selftest; python tools/pack.py --check
```

`labs/world.json` is committed and built into `IgtapLab.dll` (Recharge's own mod build deploys only the DLL and
`mod.json`). Regenerating it needs an IGTAPTasMod checkout with its built `enginesim` and the game's
traces; `--selftest` and `--check` need neither.

`IgtapLab.csproj` builds against the Steam install's `Managed` folder (`-p:ManagedDir=...` for another install). Where
RechargeLoader isn't installed (no `Recharge.ModApi.dll` there), it builds the ModApi from a
[Recharge](https://github.com/SumDumIdiut/recharge) checkout at `../recharge` (`-p:RechargeDir=...` for another).
