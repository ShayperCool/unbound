using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>
/// Core system capabilities and low-level memory page controls on Linux OpenGL Core and Vulkan:
/// SystemInfo.maxGraphicsBufferSize and GraphicsBuffer size checks, GetTiledRenderTargetStorageSize,
/// ScalableBufferManager / dynamic resolution scaling, and Baselib page allocation / protection.
/// </summary>
public sealed class CoreCapsProbe : MonoBehaviour
{
    readonly StringBuilder records = new StringBuilder();
    readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();
    List<string> captureLogs;
    string output;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CORECAPS_OUT"))) return;
        new GameObject("CoreCapsProbe").AddComponent<CoreCapsProbe>();
    }

    void Start()
    {
        output = Environment.GetEnvironmentVariable("CORECAPS_OUT");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        Application.logMessageReceived += OnLog;
        StartCoroutine(Run());
    }

    void OnLog(string condition, string stack, LogType type)
    {
        if (captureLogs != null) captureLogs.Add(type + ": " + condition);
    }

    T Keep<T>(T o) where T : UnityEngine.Object { made.Add(o); return o; }

    void Raw(string key, string json)
    {
        records.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":").Append(json).Append("}\n");
    }

    void Str(string key, string value) => Raw(key, Q(value));
    void Num(string key, long value) => Raw(key, value.ToString(CultureInfo.InvariantCulture));
    void Bool(string key, bool value) => Raw(key, value ? "true" : "false");
    void List(string key, IEnumerable<string> items) => Raw(key, "[" + string.Join(",", items.Select(Q)) + "]");

    static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
    static string Q(string s) => "\"" + Escape(s) + "\"";
    static string Factors() => $"{ScalableBufferManager.widthScaleFactor:R} {ScalableBufferManager.heightScaleFactor:R}";

    IEnumerator Run()
    {
        yield return null;
        int code = 0;
        try
        {
            Str("device", SystemInfo.graphicsDeviceType.ToString());
            RecordBufferLimit();
            RecordTiledStorage();
            RecordDynamicResolution();
            RecordBaselibMemory();
        }
        catch (Exception e)
        {
            Str("error", e.ToString());
            code = 1;
        }
        finally
        {
            foreach (var o in made) if (o != null) DestroyImmediate(o);
            Bool("complete", code == 0);
            File.WriteAllText(output, records.ToString());
            Application.logMessageReceived -= OnLog;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    // ------------------------------------------------------------------ maxGraphicsBufferSize

    void RecordBufferLimit()
    {
        long max = SystemInfo.maxGraphicsBufferSize;
        Num("bufferLimit.max", max);
        using (var ok = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4))
        {
            Str("bufferLimit.valid", $"{ok.IsValid()} {ok.count} {ok.stride}");
        }
        try
        {
            int overCount = (int)Math.Min((max / 4L) + 1L, int.MaxValue);
            using var over = new GraphicsBuffer(GraphicsBuffer.Target.Structured, overCount, 4);
            Str("bufferLimit.over", "created");
        }
        catch (Exception e)
        {
            Str("bufferLimit.over", e.GetType().Name + ": " + e.Message);
        }
    }

    // ------------------------------------------------------------------ GetTiledRenderTargetStorageSize

    void RecordTiledStorage()
    {
        Num("tiled.max", SystemInfo.maxTiledPixelStorageSize);
        var formats = new[]
        {
            GraphicsFormat.R8G8B8A8_UNorm,
            GraphicsFormat.R16G16B16A16_SFloat,
            GraphicsFormat.D32_SFloat_S8_UInt,
            GraphicsFormat.R8_UNorm,
            GraphicsFormat.None,
            GraphicsFormat.RGBA_DXT1_UNorm,
        };
        var samples = new[] { 1, 4, 8, 0, -1 };
        foreach (var fmt in formats)
        {
            foreach (var s in samples)
            {
                try { Num($"tiled.{fmt}.{s}", SystemInfo.GetTiledRenderTargetStorageSize(fmt, s)); }
                catch (Exception e) { Str($"tiled.{fmt}.{s}", e.GetType().Name + ": " + e.Message); }
            }
        }
    }

    // ------------------------------------------------------------------ Dynamic Resolution

    static void Clear(RenderTexture target, Color color)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = target;
        GL.Clear(true, true, color);
        RenderTexture.active = prev;
    }

    static string Rows(Color32[] pixels, int width, int height)
    {
        var rows = new List<string>(height);
        for (int y = 0; y < height; y++)
        {
            var row = new StringBuilder();
            int x = 0;
            while (x < width)
            {
                var c = pixels[y * width + x];
                int n = 1;
                while (x + n < width && pixels[y * width + x + n].Equals(c)) n++;
                if (row.Length > 0) row.Append(' ');
                row.Append($"{c.r:x2}{c.g:x2}{c.b:x2}{c.a:x2}*{n}");
                x += n;
            }
            rows.Add(row.ToString());
        }
        return "[" + string.Join(",", rows.Select(Q)) + "]";
    }

    static string Read(RenderTexture source, int width, int height)
    {
        var prev = RenderTexture.active;
        var cpu = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        var fill = new Color32[width * height];
        for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(0xcd, 0xcd, 0xcd, 0xcd);
        cpu.SetPixels32(fill);
        RenderTexture.active = source;
        cpu.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        RenderTexture.active = prev;
        var pixels = cpu.GetPixels32();
        DestroyImmediate(cpu);
        return Rows(pixels, width, height);
    }

    static string Sampled(Texture source, int width, int height)
    {
        var prev = RenderTexture.active;
        var tmp = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(source, tmp);
        var result = Read(tmp, width, height);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(tmp);
        return result;
    }

    void RecordDynamicResolution()
    {
        Bool("dynres.supports", SystemInfo.supportsDynamicResolution);
        var camGo = Keep(new GameObject("DynResCam"));
        var cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.red;
        cam.cullingMask = 0;
        cam.allowHDR = false;
        cam.allowMSAA = false;

        Bool("dynres.camera.allow.default", cam.allowDynamicResolution);
        cam.allowDynamicResolution = true;
        Bool("dynres.camera.allow.setTrue", cam.allowDynamicResolution);
        cam.allowDynamicResolution = false;
        Bool("dynres.camera.allow.setFalse", cam.allowDynamicResolution);

        // Scale factor quantization and validation controls.
        captureLogs = new List<string>();
        Str("dynres.factors.before", Factors());
        ScalableBufferManager.ResizeBuffers(0.5f, 0.25f);
        Str("dynres.factors.half", Factors());
        ScalableBufferManager.ResizeBuffers(0.33f, 0.99f);
        Str("dynres.factors.step33", Factors());
        ScalableBufferManager.ResizeBuffers(0.12f, 0.06f);
        Str("dynres.factors.step12", Factors());
        ScalableBufferManager.ResizeBuffers(0.95f, 0.94f);
        Str("dynres.factors.step95", Factors());
        ScalableBufferManager.ResizeBuffers(0.3f, 0.7f);
        Str("dynres.factors.step30", Factors());
        ScalableBufferManager.ResizeBuffers(2f, 0f);
        Str("dynres.factors.outside", Factors());
        ScalableBufferManager.ResizeBuffers(-1f, 0.01f);
        Str("dynres.factors.neg", Factors());
        ScalableBufferManager.ResizeBuffers(float.NaN, 0.5f);
        Str("dynres.factors.nan", Factors());
        ScalableBufferManager.ResizeBuffers(1f, 1f);
        Str("dynres.factors.reset", Factors());
        List("dynres.factors.logs", captureLogs);
        captureLogs = null;

        // Additional exact-step, between-step, and infinity float32 quantization controls.
        captureLogs = new List<string>();
        ScalableBufferManager.ResizeBuffers(0.35f, 0.2f);
        Str("dynres.factors.step35_20", Factors());
        ScalableBufferManager.ResizeBuffers(0.15f, 0.45f);
        Str("dynres.factors.step15_45", Factors());
        ScalableBufferManager.ResizeBuffers(0.65f, 0.85f);
        Str("dynres.factors.step65_85", Factors());
        ScalableBufferManager.ResizeBuffers(0.73f, 0.04f);
        Str("dynres.factors.step73_04", Factors());
        ScalableBufferManager.ResizeBuffers(0.96f, 0.05f);
        Str("dynres.factors.step96_05", Factors());
        ScalableBufferManager.ResizeBuffers(0.34999996f, 0.35000002f);
        Str("dynres.factors.step35straddle", Factors());
        ScalableBufferManager.ResizeBuffers(float.PositiveInfinity, float.NegativeInfinity);
        Str("dynres.factors.inf", Factors());
        ScalableBufferManager.ResizeBuffers(1f, 1f);
        List("dynres.factors.steps.logs", captureLogs);
        captureLogs = null;

        // RenderTexture scaling and camera/shader observation at (0.5, 0.25).
        var dynamicRt = Keep(new RenderTexture(16, 16, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { name = "dynamic", useDynamicScale = true });
        dynamicRt.Create();
        var fixedRt = Keep(new RenderTexture(16, 16, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { name = "fixed" });
        fixedRt.Create();
        var oddBefore = Keep(new RenderTexture(15, 13, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { name = "oddbefore", useDynamicScale = true });
        oddBefore.Create();
        var explicitRt = Keep(new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { name = "explicit", useDynamicScale = true, useDynamicScaleExplicit = true });
        explicitRt.Create();

        ScalableBufferManager.ResizeBuffers(0.5f, 0.25f);
        var oddAfter = Keep(new RenderTexture(15, 13, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { name = "oddafter", useDynamicScale = true });
        oddAfter.Create();

        Str("dynres.rt.sizes", $"{dynamicRt.width}x{dynamicRt.height} {fixedRt.width}x{fixedRt.height} {oddBefore.width}x{oddBefore.height} {oddAfter.width}x{oddAfter.height}");

        captureLogs = new List<string>();
        foreach (var (name, rt) in new[] { ("dynamic", dynamicRt), ("fixed", fixedRt), ("oddbefore", oddBefore), ("oddafter", oddAfter), ("explicitBefore", explicitRt) })
        {
            foreach (var allow in new[] { true, false })
            {
                cam.targetTexture = rt;
                cam.allowDynamicResolution = allow;
                Str($"dynres.cam.{name}.{(allow ? "allow" : "plain")}",
                    $"pixel {cam.pixelWidth}x{cam.pixelHeight} scaled {cam.scaledPixelWidth}x{cam.scaledPixelHeight} rect {cam.pixelRect}");
            }
        }

        explicitRt.ApplyDynamicScale();
        cam.targetTexture = explicitRt;
        cam.allowDynamicResolution = true;
        Str("dynres.cam.explicitAfter.allow",
            $"pixel {cam.pixelWidth}x{cam.pixelHeight} scaled {cam.scaledPixelWidth}x{cam.scaledPixelHeight} rect {cam.pixelRect}");

        // Render and Blit controls (fixed first, then dynamic at (0.5, 0.25), then dynamic at (0.5, 0.5)).
        Clear(fixedRt, Color.blue);
        cam.targetTexture = fixedRt;
        cam.allowDynamicResolution = true;
        cam.Render();
        Raw("dynres.render.fixed.readFull", Read(fixedRt, 16, 16));

        Clear(dynamicRt, Color.blue);
        cam.targetTexture = dynamicRt;
        cam.allowDynamicResolution = true;
        cam.Render();
        Raw("dynres.render.dynamic.sampled", Sampled(dynamicRt, 16, 16));
        Raw("dynres.render.dynamic.readScaled", Read(dynamicRt, 8, 4));
        Raw("dynres.render.dynamic.readFull", Read(dynamicRt, 16, 16));

        var shader = Shader.Find("Probe/CoreCaps/DynamicParams");
        if (shader != null)
        {
            var prev = RenderTexture.active;
            var mat = Keep(new Material(shader));
            var seen = Keep(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
            seen.Create();
            Graphics.Blit(dynamicRt, seen, mat, 0);
            RenderTexture.active = prev;
            Raw("dynres.shader.texelsize", Read(seen, 1, 1));
            Graphics.Blit(null, dynamicRt, mat, 1);
            RenderTexture.active = prev;
            Raw("dynres.shader.blitparams", Sampled(dynamicRt, 1, 1));
        }

        // Symmetric (0.5, 0.5) control: widthScale == heightScale.
        ScalableBufferManager.ResizeBuffers(0.5f, 0.5f);
        Clear(dynamicRt, Color.blue);
        cam.targetTexture = dynamicRt;
        cam.allowDynamicResolution = true;
        cam.Render();
        Raw("dynres.render.sym.sampled", Sampled(dynamicRt, 16, 16));
        Raw("dynres.render.sym.readScaled", Read(dynamicRt, 8, 8));
        Raw("dynres.render.sym.readFull", Read(dynamicRt, 16, 16));

        List("dynres.rt.logs", captureLogs);
        captureLogs = null;

        ScalableBufferManager.ResizeBuffers(1f, 1f);
        Str("dynres.factors.end", Factors());
    }

    // ------------------------------------------------------------------ Baselib Memory Pages

    [StructLayout(LayoutKind.Sequential)]
    struct ErrorStateBuf
    {
        public IntPtr file;
        public IntPtr function;
        public uint lineNumber;
        public uint pad0;
        public ulong nativeErrorCode;
        public ulong extraInformation;
        public int code;
        public byte nativeErrorCodeType;
        public byte extraInformationType;
        public ushort pad1;
        public uint pad2;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PageSizeInfoBuf
    {
        public ulong defaultPageSize;
        public ulong p0, p1, p2, p3, p4, p5;
        public ulong len;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PageAllocBuf
    {
        public IntPtr ptr;
        public ulong pageSize;
        public ulong pageCount;
    }

    static TDelegate EmitStaticCall<TDelegate>(MethodInfo target, Type[] paramTypes, Type returnType) where TDelegate : Delegate
    {
        var dm = new DynamicMethod("call_" + target.Name, returnType, paramTypes, typeof(CoreCapsProbe).Module, true);
        var il = dm.GetILGenerator();
        for (int i = 0; i < paramTypes.Length; i++) il.Emit(OpCodes.Ldarg, i);
        il.Emit(OpCodes.Call, target);
        il.Emit(OpCodes.Ret);
        return (TDelegate)dm.CreateDelegate(typeof(TDelegate));
    }

    delegate void GetPageSizeInfoDel(IntPtr outInfo);
    delegate PageAllocBuf AllocPagesDel(ulong pageSize, ulong pageCount, ulong align, int state, IntPtr err);
    delegate PageAllocBuf AllocPagesExDel(ulong pageSize, ulong pageCount, ulong align, int state, uint ext, IntPtr err);
    delegate void ReleasePagesDel(PageAllocBuf alloc, IntPtr err);
    delegate void SetPageStateDel(IntPtr ptr, ulong pageSize, ulong pageCount, int state, IntPtr err);
    delegate void SetPageStateExDel(IntPtr ptr, ulong pageSize, ulong pageCount, int state, uint ext, IntPtr err);

    unsafe void RecordBaselibMemory()
    {
        var binding = Type.GetType("Unity.Baselib.LowLevel.Binding, UnityEngine.CoreModule");
        if (binding == null)
        {
            Str("baselib.binding", "missing");
            return;
        }
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var getPageSizeInfoMi = binding.GetMethod("Baselib_Memory_GetPageSizeInfo", flags);
        var allocPagesMi = binding.GetMethod("Baselib_Memory_AllocatePages", flags);
        var allocPagesExMi = binding.GetMethod("Baselib_Memory_AllocatePagesEx", flags);
        var releasePagesMi = binding.GetMethod("Baselib_Memory_ReleasePages", flags);
        var setPageStateMi = binding.GetMethod("Baselib_Memory_SetPageState", flags);
        var setPageStateExMi = binding.GetMethod("Baselib_Memory_SetPageStateEx", flags);
        var errorCodeType = binding.GetNestedType("Baselib_ErrorCode", BindingFlags.Public | BindingFlags.NonPublic);
        var allocType = binding.GetNestedType("Baselib_Memory_PageAllocation", BindingFlags.Public | BindingFlags.NonPublic);
        var ptrField = allocType.GetField("ptr");
        var pageSizeField = allocType.GetField("pageSize");
        var pageCountField = allocType.GetField("pageCount");

        var getPageSizeInfo = EmitStaticCall<GetPageSizeInfoDel>(getPageSizeInfoMi, new[] { typeof(IntPtr) }, typeof(void));
        var setPageState = EmitStaticCall<SetPageStateDel>(setPageStateMi, new[] { typeof(IntPtr), typeof(ulong), typeof(ulong), typeof(int), typeof(IntPtr) }, typeof(void));
        var setPageStateEx = EmitStaticCall<SetPageStateExDel>(setPageStateExMi, new[] { typeof(IntPtr), typeof(ulong), typeof(ulong), typeof(int), typeof(uint), typeof(IntPtr) }, typeof(void));

        // AllocatePages / ReleasePages pass/return Baselib_Memory_PageAllocation by value (same layout as PageAllocBuf).
        var allocDm = new DynamicMethod("call_Alloc", typeof(void), new[] { typeof(ulong), typeof(ulong), typeof(ulong), typeof(int), typeof(IntPtr), typeof(IntPtr) }, typeof(CoreCapsProbe).Module, true);
        {
            var il = allocDm.GetILGenerator();
            var loc = il.DeclareLocal(allocType);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Call, allocPagesMi);
            il.Emit(OpCodes.Stloc, loc);
            il.Emit(OpCodes.Ldarg, 5);
            il.Emit(OpCodes.Ldloca, loc);
            il.Emit(OpCodes.Ldfld, ptrField);
            il.Emit(OpCodes.Stfld, typeof(PageAllocBuf).GetField("ptr"));
            il.Emit(OpCodes.Ldarg, 5);
            il.Emit(OpCodes.Ldloca, loc);
            il.Emit(OpCodes.Ldfld, pageSizeField);
            il.Emit(OpCodes.Stfld, typeof(PageAllocBuf).GetField("pageSize"));
            il.Emit(OpCodes.Ldarg, 5);
            il.Emit(OpCodes.Ldloca, loc);
            il.Emit(OpCodes.Ldfld, pageCountField);
            il.Emit(OpCodes.Stfld, typeof(PageAllocBuf).GetField("pageCount"));
            il.Emit(OpCodes.Ret);
        }
        var allocPagesRaw = (Action<ulong, ulong, ulong, int, IntPtr, IntPtr>)allocDm.CreateDelegate(typeof(Action<ulong, ulong, ulong, int, IntPtr, IntPtr>));

        var allocExDm = new DynamicMethod("call_AllocEx", typeof(void), new[] { typeof(ulong), typeof(ulong), typeof(ulong), typeof(int), typeof(uint), typeof(IntPtr), typeof(IntPtr) }, typeof(CoreCapsProbe).Module, true);
        {
            var il = allocExDm.GetILGenerator();
            var loc = il.DeclareLocal(allocType);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Ldarg, 4); il.Emit(OpCodes.Ldarg, 5);
            il.Emit(OpCodes.Call, allocPagesExMi);
            il.Emit(OpCodes.Stloc, loc);
            il.Emit(OpCodes.Ldarg, 6);
            il.Emit(OpCodes.Ldloca, loc);
            il.Emit(OpCodes.Ldfld, ptrField);
            il.Emit(OpCodes.Stfld, typeof(PageAllocBuf).GetField("ptr"));
            il.Emit(OpCodes.Ldarg, 6);
            il.Emit(OpCodes.Ldloca, loc);
            il.Emit(OpCodes.Ldfld, pageSizeField);
            il.Emit(OpCodes.Stfld, typeof(PageAllocBuf).GetField("pageSize"));
            il.Emit(OpCodes.Ldarg, 6);
            il.Emit(OpCodes.Ldloca, loc);
            il.Emit(OpCodes.Ldfld, pageCountField);
            il.Emit(OpCodes.Stfld, typeof(PageAllocBuf).GetField("pageCount"));
            il.Emit(OpCodes.Ret);
        }
        var allocPagesExRaw = (Action<ulong, ulong, ulong, int, uint, IntPtr, IntPtr>)allocExDm.CreateDelegate(typeof(Action<ulong, ulong, ulong, int, uint, IntPtr, IntPtr>));

        var releaseDm = new DynamicMethod("call_Release", typeof(void), new[] { typeof(IntPtr), typeof(ulong), typeof(ulong), typeof(IntPtr) }, typeof(CoreCapsProbe).Module, true);
        {
            var il = releaseDm.GetILGenerator();
            var loc = il.DeclareLocal(allocType);
            il.Emit(OpCodes.Ldloca, loc); il.Emit(OpCodes.Initobj, allocType);
            il.Emit(OpCodes.Ldloca, loc); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Stfld, ptrField);
            il.Emit(OpCodes.Ldloca, loc); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, pageSizeField);
            il.Emit(OpCodes.Ldloca, loc); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stfld, pageCountField);
            il.Emit(OpCodes.Ldloc, loc);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, releasePagesMi);
            il.Emit(OpCodes.Ret);
        }
        var releasePagesRaw = (Action<IntPtr, ulong, ulong, IntPtr>)releaseDm.CreateDelegate(typeof(Action<IntPtr, ulong, ulong, IntPtr>));

        PageAllocBuf AllocPages(ulong ps, ulong pc, ulong al, int st, ErrorStateBuf* e)
        {
            PageAllocBuf r = default;
            allocPagesRaw(ps, pc, al, st, (IntPtr)e, (IntPtr)(&r));
            return r;
        }
        PageAllocBuf AllocPagesEx(ulong ps, ulong pc, ulong al, int st, uint ext, ErrorStateBuf* e)
        {
            PageAllocBuf r = default;
            allocPagesExRaw(ps, pc, al, st, ext, (IntPtr)e, (IntPtr)(&r));
            return r;
        }
        void ReleasePages(PageAllocBuf a, ErrorStateBuf* e) => releasePagesRaw(a.ptr, a.pageSize, a.pageCount, (IntPtr)e);

        string CodeName(int code) => Enum.IsDefined(errorCodeType, code) ? Enum.GetName(errorCodeType, code) : code.ToString(CultureInfo.InvariantCulture);

        ulong sysPage = (ulong)Environment.SystemPageSize;
        var info = new PageSizeInfoBuf();
        getPageSizeInfo((IntPtr)(&info));
        Str("baselib.pageSizeInfo", $"defaultEq={info.defaultPageSize == sysPage} len={info.len} p0Eq={info.p0 == sysPage}");

        // 1. Allocate ReadWrite, check zeroing, write/read, release.
        var err = new ErrorStateBuf();
        var aRw = AllocPages(sysPage, 2UL, 1UL, 4, &err);
        bool zeroed = aRw.ptr != IntPtr.Zero && *(uint*)aRw.ptr == 0u && *(uint*)((byte*)aRw.ptr + sysPage) == 0u;
        uint rwRead = 0;
        if (aRw.ptr != IntPtr.Zero)
        {
            *(uint*)aRw.ptr = 0x12345678u;
            rwRead = *(uint*)aRw.ptr;
        }
        var relErr = new ErrorStateBuf();
        ReleasePages(aRw, &relErr);
        Str("baselib.alloc.rw", $"err={CodeName(err.code)} nonNull={aRw.ptr != IntPtr.Zero} aligned={(ulong)aRw.ptr % sysPage == 0} psEq={aRw.pageSize == sysPage} pc={aRw.pageCount} zeroed={zeroed} rw=0x{rwRead:x8} rel={CodeName(relErr.code)}");

        // 2. Allocate with alignmentInPages = 4.
        err = new ErrorStateBuf();
        var aAl = AllocPages(sysPage, 2UL, 4UL, 4, &err);
        relErr = new ErrorStateBuf();
        ReleasePages(aAl, &relErr);
        Str("baselib.alloc.align4", $"err={CodeName(err.code)} nonNull={aAl.ptr != IntPtr.Zero} aligned4={(ulong)aAl.ptr % (4UL * sysPage) == 0} rel={CodeName(relErr.code)}");

        // 3. Allocate Reserved -> ReadWrite -> write -> ReadOnly -> read -> Reserved -> ReadWrite -> check zeroed -> Release.
        err = new ErrorStateBuf();
        var aRes = AllocPages(sysPage, 2UL, 1UL, 0, &err);
        var eRw1 = new ErrorStateBuf();
        setPageState(aRes.ptr, sysPage, 2UL, 4, (IntPtr)(&eRw1));
        *(uint*)aRes.ptr = 0xCAFEBABEu;
        var eRo = new ErrorStateBuf();
        setPageState(aRes.ptr, sysPage, 2UL, 2, (IntPtr)(&eRo));
        uint roRead = *(uint*)aRes.ptr;
        var eBackRes = new ErrorStateBuf();
        setPageState(aRes.ptr, sysPage, 2UL, 0, (IntPtr)(&eBackRes));
        var eRw2 = new ErrorStateBuf();
        setPageState(aRes.ptr, sysPage, 2UL, 4, (IntPtr)(&eRw2));
        uint afterResRead = *(uint*)aRes.ptr;
        relErr = new ErrorStateBuf();
        ReleasePages(aRes, &relErr);
        Str("baselib.state.roundtrip", $"alloc={CodeName(err.code)} rw1={CodeName(eRw1.code)} ro={CodeName(eRo.code)} roRead=0x{roRead:x8} res={CodeName(eBackRes.code)} rw2={CodeName(eRw2.code)} afterRes=0x{afterResRead:x8} rel={CodeName(relErr.code)}");

        // 4. Allocate NoAccess -> ReadWrite -> write/read -> Release.
        err = new ErrorStateBuf();
        var aNa = AllocPages(sysPage, 1UL, 1UL, 1, &err);
        var eNaRw = new ErrorStateBuf();
        setPageState(aNa.ptr, sysPage, 1UL, 4, (IntPtr)(&eNaRw));
        *(uint*)aNa.ptr = 0x55AA55AAu;
        uint naRead = *(uint*)aNa.ptr;
        relErr = new ErrorStateBuf();
        ReleasePages(aNa, &relErr);
        Str("baselib.state.noaccess", $"alloc={CodeName(err.code)} rw={CodeName(eNaRw.code)} read=0x{naRead:x8} rel={CodeName(relErr.code)}");

        // 5. Allocate ReadWrite_Executable -> ReadOnly_Executable -> Release.
        err = new ErrorStateBuf();
        var aEx = AllocPages(sysPage, 1UL, 1UL, 20, &err);
        var eRoEx = new ErrorStateBuf();
        setPageState(aEx.ptr, sysPage, 1UL, 18, (IntPtr)(&eRoEx));
        relErr = new ErrorStateBuf();
        ReleasePages(aEx, &relErr);
        Str("baselib.state.exec", $"alloc={CodeName(err.code)} rox={CodeName(eRoEx.code)} rel={CodeName(relErr.code)}");

        // 6. Ex variants (extPageState = 0).
        err = new ErrorStateBuf();
        var aExt = AllocPagesEx(sysPage, 1UL, 1UL, 4, 0u, &err);
        var eExt = new ErrorStateBuf();
        setPageStateEx(aExt.ptr, sysPage, 1UL, 2, 0u, (IntPtr)(&eExt));
        relErr = new ErrorStateBuf();
        ReleasePages(aExt, &relErr);
        Str("baselib.state.ex", $"alloc={CodeName(err.code)} setEx={CodeName(eExt.code)} rel={CodeName(relErr.code)}");

        // 7. Error controls on AllocatePages.
        void AllocErr(string name, ulong ps, ulong pc, ulong al, int state)
        {
            var e = new ErrorStateBuf();
            var a = AllocPages(ps, pc, al, state, &e);
            Str("baselib.err." + name, $"code={CodeName(e.code)} ptrNull={a.ptr == IntPtr.Zero}");
            if (a.ptr != IntPtr.Zero)
            {
                var re = new ErrorStateBuf();
                ReleasePages(a, &re);
            }
        }
        AllocErr("alloc.zeroCount", sysPage, 0UL, 1UL, 4);
        AllocErr("alloc.badPageSize", 123UL, 1UL, 1UL, 4);
        AllocErr("alloc.badAlign0", sysPage, 1UL, 0UL, 4);
        AllocErr("alloc.badAlign3", sysPage, 1UL, 3UL, 4);
        AllocErr("alloc.badState", sysPage, 1UL, 1UL, 99);
        AllocErr("alloc.overflow", sysPage, ulong.MaxValue, 1UL, 4);

        // 8. Error controls on SetPageState and ReleasePages.
        err = new ErrorStateBuf();
        var aTmp = AllocPages(sysPage, 1UL, 1UL, 4, &err);
        void SetErr(string name, IntPtr p, ulong ps, ulong pc, int state, int initialCode = 0)
        {
            var e = new ErrorStateBuf { code = initialCode };
            setPageState(p, ps, pc, state, (IntPtr)(&e));
            Str("baselib.err." + name, $"code={CodeName(e.code)}");
        }
        SetErr("set.nullPtr", IntPtr.Zero, sysPage, 1UL, 4);
        SetErr("set.zeroCount", aTmp.ptr, sysPage, 0UL, 4);
        SetErr("set.badPageSize", aTmp.ptr, 123UL, 1UL, 4);
        SetErr("set.badState", aTmp.ptr, sysPage, 1UL, 99);
        SetErr("set.overflow", aTmp.ptr, sysPage, ulong.MaxValue, 4);
        SetErr("set.sticky", aTmp.ptr, 123UL, 1UL, 4, 16777219); // InvalidArgument

        var eRelNull = new ErrorStateBuf();
        ReleasePages(new PageAllocBuf { ptr = IntPtr.Zero, pageSize = sysPage, pageCount = 1UL }, &eRelNull);
        Str("baselib.err.release.null", $"code={CodeName(eRelNull.code)}");

        var eRelZero = new ErrorStateBuf();
        ReleasePages(new PageAllocBuf { ptr = aTmp.ptr, pageSize = sysPage, pageCount = 0UL }, &eRelZero);
        Str("baselib.err.release.zeroCount", $"code={CodeName(eRelZero.code)}");

        var eRelBadPs = new ErrorStateBuf();
        ReleasePages(new PageAllocBuf { ptr = aTmp.ptr, pageSize = 123UL, pageCount = 1UL }, &eRelBadPs);
        Str("baselib.err.release.badPageSize", $"code={CodeName(eRelBadPs.code)}");

        relErr = new ErrorStateBuf();
        ReleasePages(aTmp, &relErr);
        Str("baselib.err.cleanup", $"code={CodeName(relErr.code)}");
    }
}
