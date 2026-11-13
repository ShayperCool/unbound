using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests;

public class AccessorStateTests
{
    [Fact]
    public void StartupFocusKeepsTheExistingEventPhase()
    {
        bool previous = Application.isFocused;
        int changes = 0;
        void Changed(bool value) => changes++;
        Application.Internal_SetFocusState(false);
        Application.focusChanged += Changed;
        try
        {
            PlayerLoopDriver.SendPlayModeStart();
            Assert.True(Application.isFocused);
            Assert.Equal(0, changes);
            Application.Internal_FocusChanged(false);
            Assert.False(Application.isFocused);
            Assert.Equal(1, changes);
        }
        finally
        {
            Application.focusChanged -= Changed;
            Application.Internal_SetFocusState(previous);
        }
    }

    [Fact]
    public void ExplicitTextureIncrementKeepsThePublicCounter()
    {
        var texture = new Texture2D(2, 2);
        try
        {
            uint before = texture.updateCount;
            texture.IncrementUpdateCount();
            Assert.Equal(unchecked(before + 1), texture.updateCount);
            texture.IncrementUpdateCount();
            Assert.Equal(unchecked(before + 2), texture.updateCount);
        }
        finally { Object.DestroyImmediate(texture); }
    }
}
