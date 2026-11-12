"""Metadata controls outside the wide profile's admitted PCM grid."""
import json
import re

BUFFERS = (0, 1, 2, 3, 7, 16, 31, 33, 63, 65, 97, 384, 16384)


def prepare(project):
    # This component is added only to a private metadata capture project.
    source = r'''using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
public class OutputConfigurationCapture : MonoBehaviour {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Setup() {
        foreach (var old in UnityEngine.Object.FindObjectsOfType<EffectProbe>()) old.enabled = false;
        var go = new GameObject("Output configurations");
        go.AddComponent<AudioListener>(); go.AddComponent<OutputConfigurationCapture>();
    }
    IEnumerator Start() {
        int rate = int.Parse(Environment.GetEnvironmentVariable("EFFECT_PROBE_RATE"));
        var rows = new List<string>();
        foreach (int size in new[] { BUFFERS }) {
            var config = AudioSettings.GetConfiguration();
            config.sampleRate = rate; config.dspBufferSize = size;
            config.speakerMode = AudioSpeakerMode.Mode7point1;
            bool result = AudioSettings.Reset(config);
            yield return null;
            var actual = AudioSettings.GetConfiguration();
            AudioSettings.GetDSPBufferSize(out int frames, out int count);
            rows.Add("{\"rate\":"+rate+",\"buffer\":"+size+",\"ok\":"+(result?"true":"false")
                +",\"actual_rate\":"+actual.sampleRate+",\"actual_buffer\":"+actual.dspBufferSize
                +",\"frames\":"+frames+",\"count\":"+count+",\"mode\":"+(int)actual.speakerMode+"}");
        }
        string output = Environment.GetEnvironmentVariable("EFFECT_PROBE_OUT");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "settings.json"), "["+string.Join(",", rows)+"]\n");
        typeof(EffectProbe).GetMethod("Exit", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { 0 });
    }
}
'''.replace('BUFFERS', ','.join(map(str, BUFFERS)))
    (project/'Assets/Scripts/OutputConfigurationCapture.cs').write_text(source)


def verify(path, rate, release):
    log = (path.parents[1]/'editor.log').read_text()
    version = re.search(r'^Initialize engine version:\s+(\S+)', log, re.M)
    assert version and version[1] == release, 'configuration capture used another Editor release'
    rows = json.loads(path.read_text())
    assert [row['buffer'] for row in rows] == list(BUFFERS)
    for row in rows:
        assert row['ok'] and row['rate'] == row['actual_rate'] == rate, row
        assert row['mode'] == 6 and row['count'] == 4, row
        assert row['actual_buffer'] == row['frames'] > 0, row
    return rows
