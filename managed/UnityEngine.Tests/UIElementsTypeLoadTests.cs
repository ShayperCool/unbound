using System;
using System.Linq;
using Xunit;

namespace UnityEngine.Tests
{
    public class UIElementsTypeLoadTests
    {
        [Fact]
        public void All_engine_types_load()
        {
            var asm = typeof(UnityEngine.UIElements.VisualElement).Assembly;
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException e)
            {
                var msgs = e.LoaderExceptions.Where(x => x != null).Select(x => x.Message).Distinct().Take(20);
                throw new Exception(string.Join("\n", msgs));
            }
            Assert.NotEmpty(types);
        }
    }
}
