using System;
using System.Collections.Generic;

namespace IgtapLab
{
    public sealed class Chip
    {
        public int Tick;
        public string Kind, Label;
    }

    /// <summary>Pure (System types only, so tools/ChipsCheck runs it without the game).</summary>
    public static class LabChips
    {
        public const string Neutral = "·";
        static readonly string[] Arrows = { "→", "↗", "↑", "↖", "←", "↙", "↓", "↘" };

        public static string Direction(float x, float y)
        {
            if (Math.Max(Math.Abs(x), Math.Abs(y)) < 0.25f) return Neutral;
            double degrees = Math.Atan2(y, x) * 180.0 / Math.PI;
            return Arrows[((int)Math.Round(degrees / 45.0) % 8 + 8) % 8];
        }

        public static List<Chip> Chips(List<InputTick> inputs)
        {
            var chips = new List<Chip>();
            string previous = Neutral;
            for (int i = 0; i < inputs.Count; i++)
            {
                InputTick t = inputs[i];
                string direction = Direction(t.x, t.y);
                if (direction != previous) chips.Add(new Chip { Tick = i, Kind = "stick", Label = direction });
                previous = direction;
                if (t.press) chips.Add(new Chip { Tick = i, Kind = "jump", Label = "jump" });
                if (t.release) chips.Add(new Chip { Tick = i, Kind = "release", Label = "release" });
                if (t.dash)
                {
                    chips.Add(new Chip { Tick = i, Kind = "dash", Label = "dash" });
                    if (t.turnX.HasValue || t.turnY.HasValue)
                        chips.Add(new Chip { Tick = i, Kind = "turn", Label = "turn " + Direction(t.turnX ?? 0f, t.turnY ?? 0f) });
                }
                if (t.dashJump > 0) chips.Add(new Chip { Tick = i, Kind = "dashjump", Label = "jump in dash" });
                if (t.springDash > 0) chips.Add(new Chip { Tick = i, Kind = "springdash", Label = "dash in spring" });
                if (t.reset) chips.Add(new Chip { Tick = i, Kind = "restart", Label = "restart" });
                if (t.pause > 0) chips.Add(new Chip { Tick = i, Kind = "pause", Label = "pause" });
            }
            return chips;
        }
    }
}
