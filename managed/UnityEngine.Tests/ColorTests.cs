using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    public class ColorTests
    {
        [Fact]
        public void NamedColorsHaveUnityValues()
        {
            Assert.Equal(new Color(1f, 0.92156863f, 0.015686275f, 1f), Color.yellow);
            Assert.Equal(new Color(0.5f, 0.5f, 0.5f, 1f), Color.grey);
            Assert.Equal(Color.grey, Color.gray);
            Assert.Equal(new Color(0f, 1f, 1f, 1f), Color.cyan);
            Assert.Equal(new Color(1f, 0f, 1f, 1f), Color.magenta);
            Assert.Equal(new Color(0f, 0f, 0f, 0f), Color.clear);
            Assert.Equal(1f, new Color(0.1f, 0.2f, 0.3f).a);
        }

        [Fact]
        public void ToStringFormats()
        {
            Assert.Equal("RGBA(1.000, 0.922, 0.016, 1.000)", Color.yellow.ToString());
            Assert.Equal("RGBA(1.0, 0.9, 0.0, 1.0)", Color.yellow.ToString("F1"));
            Assert.Equal("RGBA(255, 128, 0, 255)", new Color32(255, 128, 0, 255).ToString());
        }

        [Fact]
        public void HsvConversions()
        {
            Near.Equal(Color.red, Color.HSVToRGB(0f, 1f, 1f));
            Near.Equal(Color.green, Color.HSVToRGB(1f / 3f, 1f, 1f), 1e-5f);
            Near.Equal(Color.blue, Color.HSVToRGB(2f / 3f, 1f, 1f), 1e-5f);
            Near.Equal(Color.red, Color.HSVToRGB(1f, 1f, 1f));
            Near.Equal(new Color(2f, 0f, 0f, 1f), Color.HSVToRGB(0f, 1f, 2f));
            Near.Equal(Color.red, Color.HSVToRGB(0f, 1f, 2f, false));
            Near.Equal(new Color(0.3f, 0.3f, 0.3f, 1f), Color.HSVToRGB(0.7f, 0f, 0.3f));

            Color.RGBToHSV(new Color(0.2f, 0.4f, 0.6f), out float h, out float s, out float v);
            Near.Equal(7f / 12f, h, 1e-5f);
            Near.Equal(2f / 3f, s, 1e-5f);
            Near.Equal(0.6f, v, 1e-6f);
            Near.Equal(new Color(0.2f, 0.4f, 0.6f), Color.HSVToRGB(h, s, v), 1e-5f);

            Color.RGBToHSV(new Color(1f, 0f, 0.5f), out h, out s, out v);
            Near.Equal(11f / 12f, h, 1e-5f);
            Color.RGBToHSV(Color.black, out h, out s, out v);
            Assert.Equal(0f, h);
            Assert.Equal(0f, s);
            Assert.Equal(0f, v);
        }

        [Fact]
        public void Color32Conversions()
        {
            Color32 c = new Color(0.5f, 1.5f, -1f, 0.2f);
            Assert.Equal(128, c.r); // 127.5 rounds to even
            Assert.Equal(255, c.g);
            Assert.Equal(0, c.b);
            Assert.Equal(51, c.a);
            Color back = new Color32(255, 0, 51, 255);
            Near.Equal(new Color(1f, 0f, 0.2f, 1f), back, 1e-6f);
            Assert.Equal(new Color32(127, 127, 127, 127), Color32.Lerp(new Color32(0, 0, 0, 0), new Color32(255, 255, 255, 255), 0.5f));
            Assert.Equal(4, Marshal.SizeOf<Color32>());
            Assert.Equal(3, (int)Marshal.OffsetOf<Color32>("a"));
        }

        [Fact]
        public void DerivedProperties()
        {
            Near.Equal(0.299f, Color.red.grayscale);
            Near.Equal(0.8f, new Color(0.2f, 0.8f, 0.5f).maxColorComponent);
            Near.Equal(new Color(0.21404114f, 0.21404114f, 0.21404114f, 0.5f), new Color(0.5f, 0.5f, 0.5f, 0.5f).linear, 1e-6f);
            Near.Equal(new Color(0.5f, 0.5f, 0.5f, 0.5f), new Color(0.5f, 0.5f, 0.5f, 0.5f).linear.gamma, 1e-5f);
            Vector4 v = Color.red;
            Assert.Equal(new Vector4(1f, 0f, 0f, 1f), v);
            Color fromV = new Vector4(0.1f, 0.2f, 0.3f, 0.4f);
            Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 0.4f), fromV);
        }

        [Fact]
        public void EqualityAndIndexer()
        {
            Assert.True(new Color(0.5f, 0.5f, 0.5f) == new Color(0.5f, 0.5f, 0.500001f));
            Assert.False(new Color(0.5f, 0.5f, 0.5f).Equals(new Color(0.5f, 0.5f, 0.500001f)));
            Color c = Color.black;
            c[2] = 0.75f;
            Assert.Equal(0.75f, c.b);
            Assert.Equal("Invalid Color index(4)!", Assert.Throws<IndexOutOfRangeException>(() => c[4]).Message);
            Near.Equal(new Color(0.5f, 0.5f, 0.5f, 1f), Color.Lerp(Color.black, Color.white, 0.5f));
            Near.Equal(Color.white, Color.Lerp(Color.black, Color.white, 2f));
        }
    }
}
