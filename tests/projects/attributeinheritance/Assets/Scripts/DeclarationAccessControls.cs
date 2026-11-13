using System;
using System.Reflection;

public static class DeclarationAccessControls
{
    static readonly string[][] Declarations = {
        new string[] { "UnityEngine.PlayerLoop.PreUpdate+PhysicsClothUpdate", "UnityEngine.CoreModule", "type", "declaration" },
        new string[] { "UnityEngine.RectTransform", "UnityEngine.CoreModule", "P:drivenProperties():UnityEngine.DrivenTransformProperties", "declaration" },
        new string[] { "UnityEngine.RectTransform", "UnityEngine.CoreModule", "P:drivenProperties():UnityEngine.DrivenTransformProperties", "getter" },
        new string[] { "UnityEngine.SceneManagement.Scene", "UnityEngine.CoreModule", "P:dirtyID():System.Int32", "declaration" },
        new string[] { "UnityEngine.SceneManagement.Scene", "UnityEngine.CoreModule", "P:dirtyID():System.Int32", "getter" },
        new string[] { "UnityEngine.SceneManagement.Scene", "UnityEngine.CoreModule", "P:loadingState():UnityEngine.SceneManagement.Scene+LoadingState", "declaration" },
        new string[] { "UnityEngine.SceneManagement.Scene", "UnityEngine.CoreModule", "P:loadingState():UnityEngine.SceneManagement.Scene+LoadingState", "getter" },
        new string[] { "UnityEngine.SceneManagement.Scene+LoadingState", "UnityEngine.CoreModule", "type", "declaration" },
        new string[] { "UnityEditor.TextScriptImporter", "UnityEditor.CoreModule", "type", "declaration" },
        new string[] { "UnityEditor.Compilation.CompilationPipeline", "UnityEditor.CoreModule", "M:static IsScriptDebugInfoEnabled`0():System.Boolean", "declaration" },
        new string[] { "UnityEngine.Texture2D", "UnityEngine.CoreModule", "P:isPreProcessed():System.Boolean", "declaration" },
        new string[] { "UnityEngine.Texture2D", "UnityEngine.CoreModule", "P:isPreProcessed():System.Boolean", "getter" },
        new string[] { "UnityEngine.RenderTexture", "UnityEngine.CoreModule", "M:static GetCompatibleFormat`0(UnityEngine.RenderTextureFormat,UnityEngine.RenderTextureReadWrite):UnityEngine.Experimental.Rendering.GraphicsFormat", "declaration" },
        new string[] { "UnityEngine.GUI", "UnityEngine.IMGUIModule", "P:static mouseTooltip():System.String", "declaration" },
        new string[] { "UnityEngine.GUI", "UnityEngine.IMGUIModule", "P:static mouseTooltip():System.String", "getter" },
        new string[] { "UnityEngine.GUIStyle", "UnityEngine.IMGUIModule", "M:DrawPrefixLabel`0(UnityEngine.Rect,UnityEngine.GUIContent,System.Int32):System.Void", "declaration" },
        new string[] { "UnityEngine.Physics", "UnityEngine.PhysicsModule", "M:static GetColliderByInstanceID`0(System.Int32):UnityEngine.Collider", "declaration" },
        new string[] { "UnityEngine.Physics", "UnityEngine.PhysicsModule", "M:static GetBodyByInstanceID`0(System.Int32):UnityEngine.Component", "declaration" },
        new string[] { "UnityEngine.SystemInfo", "UnityEngine.CoreModule", "P:static maxRenderTextureSize():System.Int32", "declaration" },
        new string[] { "UnityEngine.SystemInfo", "UnityEngine.CoreModule", "P:static maxRenderTextureSize():System.Int32", "getter" },
        new string[] { "UnityEngine.TerrainCollider", "UnityEngine.TerrainPhysicsModule", "M:Raycast`0(UnityEngine.Ray,out UnityEngine.RaycastHit,System.Single,System.Boolean):System.Boolean", "declaration" },
        new string[] { "UnityEngine.TextCore.FaceInfo", "UnityEngine.TextCoreFontEngineModule", "P:faceIndex():System.Int32", "declaration" },
        new string[] { "UnityEngine.TextCore.FaceInfo", "UnityEngine.TextCoreFontEngineModule", "P:faceIndex():System.Int32", "getter" },
        new string[] { "UnityEngine.TextCore.FaceInfo", "UnityEngine.TextCoreFontEngineModule", "P:faceIndex():System.Int32", "setter" },
        new string[] { "UnityEngine.TextCore.FaceInfo", "UnityEngine.TextCoreFontEngineModule", "P:unitsPerEM():System.Int32", "declaration" },
        new string[] { "UnityEngine.TextCore.FaceInfo", "UnityEngine.TextCoreFontEngineModule", "P:unitsPerEM():System.Int32", "getter" },
        new string[] { "UnityEngine.TextCore.FaceInfo", "UnityEngine.TextCoreFontEngineModule", "P:unitsPerEM():System.Int32", "setter" },
        new string[] { "UnityEngine.TextCore.LowLevel.FontEngine", "UnityEngine.TextCoreFontEngineModule", "M:.ctor`0():System.Void", "declaration" },
    };
    static string Access(MemberInfo member)
    {
        var type = member as Type;
        if (type != null) return type.IsPublic || type.IsNestedPublic ? "public" : type.IsNestedFamily ? "protected" : type.IsNestedPrivate ? "private" : "internal";
        var property = member as PropertyInfo;
        var method = property == null ? (MethodBase)member : property.GetGetMethod(true);
        return method.IsPublic ? "public" : method.IsFamily ? "protected" : method.IsPrivate ? "private" : method.IsFamilyOrAssembly ? "protected internal" : method.IsFamilyAndAssembly ? "private protected" : "internal";
    }
    public static void Record(Action<string, object> row)
    {
        foreach (var d in Declarations)
        {
            var member = DeclarationSignatures.Find(d[0], d[1], d[2]);
            if (d[3] != "declaration") member = d[3] == "setter" ? ((PropertyInfo)member).GetSetMethod(true) : ((PropertyInfo)member).GetGetMethod(true);
            row("access." + d[0] + "." + d[2] + "." + d[3], member == null ? "absent" : Access(member));
        }
    }
}
