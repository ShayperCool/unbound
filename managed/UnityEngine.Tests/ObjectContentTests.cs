using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    public class ObjectContentTests
    {
        // The Editor's ObjectContent leaves its content's tooltip empty (tests/projects/editorui
        // EditorGUIUtility.ObjectContent: "tooltip":"").
        [Fact]
        public void ObjectContentsTooltipIsEmpty()
        {
            var data = ScriptableObject.CreateInstance<ScriptableObject>();
            try
            {
                Assert.Equal(string.Empty, EditorGUIUtility.ObjectContent(null, typeof(Material)).tooltip);
                Assert.Equal(string.Empty, EditorGUIUtility.ObjectContent(data, typeof(ScriptableObject)).tooltip);
                Assert.Equal(string.Empty, EditorGUIUtility.ObjectContent(null, null).tooltip);
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }
    }
}
