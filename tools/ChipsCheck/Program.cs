using System;
using System.Collections.Generic;
using IgtapLab;

var inputs = new List<InputTick>
{
    new InputTick { x = 1f },
    new InputTick { x = 1f, press = true },
    new InputTick { x = 1f, y = 1f },
    new InputTick { release = true },
    new InputTick { dash = true, turnX = 0.35f, turnY = 1f },
    new InputTick { pause = 3, dashJump = 2 },
    new InputTick { springDash = 1 },
};
string[] expected =
{
    "0 stick →", "1 jump jump", "2 stick ↗", "3 stick ·", "3 release release", "4 dash dash", "4 turn turn ↑", "5 dashjump jump in dash", "5 pause pause", "6 springdash dash in spring",
};
var got = new List<string>();
foreach (Chip c in LabChips.Chips(inputs)) got.Add(c.Tick + " " + c.Kind + " " + c.Label);
if (string.Join("|", got) != string.Join("|", expected))
{
    Console.WriteLine("FAIL\n got: " + string.Join(" | ", got) + "\n want: " + string.Join(" | ", expected));
    return 1;
}
Console.WriteLine("ok: " + got.Count + " chips: " + string.Join(" | ", got));
return 0;
