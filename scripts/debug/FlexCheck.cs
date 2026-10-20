using System.Text;
using DillControl.Foliage;
using Godot;

namespace DillControl.Debug;

/// <summary>
/// Checks a plant's <see cref="Rod"/> headless: it sways at the period it was
/// built for, settles after a hit, leans under a steady pull and back, and a
/// cut through it neither blows up nor leans past its limit.
/// <c>flexcheck.gd</c> runs it.
/// </summary>
[GlobalClass]
public partial class FlexCheck : Node
{
    public string Run()
    {
        var report = new StringBuilder();
        bool pass = true;

        foreach ((string name, int segments, float height, float period, float taper) in new[]
        {
            ("vine", 12, 117.0f, 6.0f, 1.0f),
            ("tree", 8, 30.0f, 3.0f, 1.0f),
            ("whip", 16, 60.0f, 4.0f, 2.0f),
        })
        {
            // Tapering from foot to tip, like a stalk.
            var mass = new float[segments];
            var section = new float[segments];
            for (int s = 0; s < segments; s++)
            {
                float t = (float)s / segments;
                section[s] = Mathf.Lerp(400.0f, 20.0f, t);
                mass[s] = 3000.0f * section[s] / (210.0f * segments);
            }
            var rod = new Rod(segments, height / segments);
            rod.Build(mass, section, period, 0.08f, taper, 2.0f, Mathf.DegToRad(30.0f), 9.8f);

            // Free sway after a kick: count the tip's crossings.
            rod.AddImpulse(height * 0.6f, new Vector2(2000.0f, 0.0f));
            float dt = 1.0f / 60.0f, previous = 0.0f, first = -1.0f, last = -1.0f, peak = 0.0f;
            int crossings = 0;
            for (int i = 0; i < 60 * 30; i++)
            {
                rod.Step(dt);
                float x = rod.Offset(segments).X;
                peak = Mathf.Max(peak, Mathf.Abs(x));
                if (i > 0 && previous <= 0.0f && x > 0.0f)
                {
                    if (first < 0.0f)
                    {
                        first = i * dt;
                    }
                    last = i * dt;
                    crossings++;
                }
                previous = x;
            }
            float measured = crossings > 1 ? (last - first) / (crossings - 1) : -1.0f;
            float after = Mathf.Abs(rod.Offset(segments).X);
            bool swayOk = measured > period * 0.7f && measured < period * 1.3f;
            bool settled = after < peak * 0.05f;
            pass &= swayOk && settled && float.IsFinite(after);
            report.AppendLine($"{name}: period asked {period:F1}s measured {measured:F2}s, tip peak {peak:F2} m, after 30 s {after:F3} m, substeps {rod.Substeps}"
                + (swayOk ? "" : "  <- period") + (settled ? "" : "  <- not settled"));

            // A steady pull at 70 % up (a player of 90 kg at 2 g), then let go.
            float lean = 0.0f;
            for (int i = 0; i < 60 * 20; i++)
            {
                rod.AddForce(height * 0.7f, new Vector2(0.0f, 90.0f * 9.8f * 2.0f));
                rod.Step(dt);
                lean = rod.Offset(segments).Y;
            }
            for (int i = 0; i < 60 * 30; i++)
            {
                rod.Step(dt);
            }
            float back = Mathf.Abs(rod.Offset(segments).Y);
            pass &= lean > 0.0f && back < lean * 0.05f;
            report.AppendLine($"  pulled at 70 %: tip leans {lean:F2} m, after letting go {back:F3} m");

            // Cut 90 % through at a third of the height: it goes soft, leans,
            // and never past its limit.
            int cut = segments / 3;
            section[cut] *= 0.1f;
            rod.SetProfile(mass, section, 0.08f);
            float worst = 0.0f;
            for (int i = 0; i < 60 * 20; i++)
            {
                rod.SetSegmentForce(segments - 1, new Vector2(400.0f, 0.0f));
                rod.Step(dt);
                worst = Mathf.Max(worst, rod.Offset(segments).Length());
            }
            float limit = Mathf.Sin(Mathf.DegToRad(30.0f)) * height * 1.05f;
            bool cutOk = float.IsFinite(worst) && worst <= limit;
            pass &= cutOk;
            report.AppendLine($"  cut 90 % at joint {cut}: tip leans up to {worst:F2} m (limit {limit:F1}), substeps {rod.Substeps}" + (cutOk ? "" : "  <- past limit"));

            // What it costs: a whole nest's worth.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 230; i++)
            {
                rod.Step(dt);
            }
            report.AppendLine($"  230 steps (a nest's vines, one tick): {watch.Elapsed.TotalMilliseconds:F3} ms");
        }

        return (pass ? "PASS\n" : "FAIL\n") + report;
    }
}
