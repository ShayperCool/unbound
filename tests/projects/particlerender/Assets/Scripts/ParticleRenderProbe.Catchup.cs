using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Sub-emitter catch-up in long simulation calls (<c>catchup/*</c>): one restarting Simulate call of
/// 4.5, 5.5, 7, 10.5 or 13 s on a parent whose one particle (30 s) carries a Birth child emitting
/// 2.5 particles a second (30 s, moving at 1 unit/s); every child's age, position and seed, as
/// <see cref="RecordPrewarm"/>. A child emitted a time before the call's end is caught up over that
/// time.
/// </summary>
public partial class ParticleRenderProbe
{
    IEnumerator CatchupControls()
    {
        yield return null;
        var origin = new Vector3(0f, 0f, -80f);
        int slot = 0;
        foreach (float span in new[] { 4.5f, 5.5f, 7f, 10.5f, 13f })
        {
            string key = "catchup/" + span.ToString(CultureInfo.InvariantCulture);
            var parent = Make(key + "/parent", s =>
            {
                var m = s.main; m.startLifetime = 30f; m.startSpeed = 0f; m.duration = 1f; m.loop = false;
                m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                var e = s.emission; e.enabled = true; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            });
            parent.transform.position = origin + new Vector3(10f * slot++, 0f, 0f);
            var child = LinkedChild(key + "/child", parent, s =>
            {
                var m = s.main; m.startLifetime = 30f; m.startSpeed = 1f; m.loop = true; m.duration = 40f;
                var e = s.emission; e.rateOverTime = 2.5f;
            });
            parent.Simulate(span, true, true, true);
            RecordPrewarm(key + "/parent", parent);
            RecordPrewarm(key + "/child", child);
            Done(parent);
        }
    }
}
