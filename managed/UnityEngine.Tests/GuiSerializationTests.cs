using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class GuiSerializationTests
    {
        [Serializable] public class Holder { public GUIStyle style; public int control = 11; }
        public class Asset : ScriptableObject { public GUIStyle style; }

        [Fact]
        public void NestedOverwriteReplacesStyleAndKeepsUnmentionedHolderFields()
        {
            var holder = new Holder { style = new GUIStyle { name = "before", fontSize = 47 }, control = 59 };
            holder.style.hover.textColor = Color.red;
            holder.style.padding.right = 8;
            JsonUtility.FromJsonOverwrite("{\"style\":{\"m_Name\":\"after\",\"m_Normal\":{\"m_TextColor\":{\"r\":19}},\"m_Padding\":{\"m_Left\":27}}}", holder);
            Assert.Equal("after", holder.style.name);
            Assert.Equal(0, holder.style.fontSize);
            Assert.Equal(Color.black, holder.style.hover.textColor);
            Assert.Equal(new Color(19, 0, 0, 1), holder.style.normal.textColor);
            Assert.Equal(27, holder.style.padding.left);
            Assert.Equal(0, holder.style.padding.right);
            Assert.Equal(59, holder.control);
            JsonUtility.FromJsonOverwrite("{\"m_Name\":\"ignored\"}", holder.style);
            Assert.Equal("after", holder.style.name);
            Assert.Equal("{}", JsonUtility.ToJson(holder.style));
        }

        [Fact]
        public void StylePayloadLoadsThroughYamlDespiteNonSerializedWrapperFields()
        {
            var asset = ScriptableObject.CreateInstance<Asset>();
            new YamlReader(null).ReadScriptObject(asset, TestYaml.DocumentBody(@"
MonoBehaviour:
  style:
    m_Name: yaml-style
    m_FontSize: 23
    m_Normal:
      m_TextColor: {r: 0.25, g: 0.5, b: 0.75, a: 1}
    m_Padding: {m_Left: 1, m_Right: 2, m_Top: 3, m_Bottom: 4}
    m_IsSDF: 1
"));
            Assert.Equal("yaml-style", asset.style.name);
            Assert.Equal(23, asset.style.fontSize);
            Assert.Equal(new Color(.25f, .5f, .75f, 1), asset.style.normal.textColor);
            Assert.Equal(4, asset.style.padding.bottom);
            Assert.Contains("\"m_IsSDF\":true", JsonUtility.ToJson(new Holder { style = asset.style }));
            var copy = Object.Instantiate(asset);
            copy.style.normal.textColor = Color.red;
            copy.style.padding.left = 97;
            Assert.Equal(.25f, asset.style.normal.textColor.r);
            Assert.Equal(1, asset.style.padding.left);
            Assert.Contains("\"m_IsSDF\":true", JsonUtility.ToJson(new Holder { style = copy.style }));
            Object.DestroyImmediate(copy);
            Object.DestroyImmediate(asset);
        }
    }
}
