using UnityEngine;

public partial class NativeGfxVulkanProbe
{
    void ForeignStorageControls()
    {
        var previousLogs = logs.ToArray();
        logs.Clear();
        ExternalControls.ForeignStorageControls(Value, action =>
        {
            Application.logMessageReceived -= OnLog;
            try { action(); }
            finally { Application.logMessageReceived += OnLog; }
        });
        Value("foreignStorage.logs", L(logs));
        logs.Clear();
        logs.AddRange(previousLogs);
    }
}
