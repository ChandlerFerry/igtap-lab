using UnityEngine;

namespace IgtapLab
{
    // Before Movement (order 0): Tick reads the state Movement.FixedUpdate is about to run on.
    [DefaultExecutionOrder(-1000)]
    public sealed class LabOverlay : MonoBehaviour
    {
        readonly LabTracker tracker = new LabTracker();
        LabGhost ghost;
        DemoDef ghostDemo;

        void OnEnable() { LabSession.AttemptStarted += OnAttempt; }

        void OnDisable()
        {
            LabSession.AttemptStarted -= OnAttempt;
            DropGhost();
        }

        void OnAttempt()
        {
            if (LabSession.Demo != null) tracker.Begin(LabSession.Demo);
        }

        void DropGhost()
        {
            ghost?.Dispose();
            ghost = null;
            ghostDemo = null;
        }

        void Update()
        {
            LabDef lab = LabSession.Current;
            Movement player = LabSession.Player;
            DemoDef demo = LabSession.Demo;
            if (lab == null || demo == null || player == null) { DropGhost(); return; }
            tracker.Frame(player);
            if (demo != ghostDemo)
            {
                DropGhost();
                ghostDemo = demo;
                if (demo.path != null && demo.path.Length >= 2 && player.animator != null) ghost = new LabGhost(lab, demo, player);
            }
            if (ghost != null)
                ghost.Show(tracker.PhysTicks + Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime));
        }

        void FixedUpdate()
        {
            if (LabSession.Current != null && LabSession.Player != null) tracker.Tick(LabSession.Player, LabSession.InFinish());
        }

        void OnGUI()
        {
            if (LabSession.Current != null && LabSession.Player != null) tracker.Draw(LabSession.Current, LabSession.Player);
        }
    }
}
