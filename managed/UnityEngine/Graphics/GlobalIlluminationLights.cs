using System;
using Unity.Collections;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.GlobalIllumination
{
    /// <summary>
    /// The lights global illumination is given (Unity 6000.0 Scripting API,
    /// UnityEngine.Experimental.GlobalIllumination): a render pipeline registers a delegate with
    /// <see cref="Lightmapping.SetDelegate"/> that turns each <see cref="Light"/> into a
    /// <see cref="LightDataGI"/>, with the helpers of <see cref="LightmapperUtils"/>. Unbound's
    /// precomputed realtime GI asks the delegate for its lights when a pipeline has set one
    /// (<c>UnityEngine.RealtimeGI</c>); baking is the Editor's.
    /// </summary>
    public enum LightType : byte
    {
        Directional = 0,
        Point = 1,
        Spot = 2,
        Rectangle = 3,
        Disc = 4,
        SpotPyramidShape = 5,
        SpotBoxShape = 6,
    }

    public enum LightMode : byte
    {
        Realtime = 0,
        Mixed = 1,
        Baked = 2,
        Unknown = 3,
    }

    public enum FalloffType : byte
    {
        InverseSquared = 0,
        InverseSquaredNoRangeAttenuation = 1,
        Linear = 2,
        Legacy = 3,
        Undefined = 4,
    }

    public enum AngularFalloffType : byte
    {
        LUT = 0,
        AnalyticAndInnerAngle = 1,
    }

    /// <summary>A color in linear space: components of at most 1 and an intensity that scales
    /// them.</summary>
    public partial struct LinearColor
    {
        float m_red, m_green, m_blue, m_intensity;

        public float red { get => m_red; set => m_red = Component(value); }
        public float green { get => m_green; set => m_green = Component(value); }
        public float blue { get => m_blue; set => m_blue = Component(value); }
        public float intensity { get => m_intensity; set => m_intensity = value; }

        static float Component(float value)
        {
            if (value < 0f || value > 1f) throw new ArgumentOutOfRangeException(nameof(value), "Color component must be in the range 0 to 1.");
            return value;
        }

        /// <summary>A light's color and intensity as a linear color: the intensity scales the
        /// linear color with GraphicsSettings.lightsUseLinearIntensity, else the gamma color before
        /// its conversion; the largest component becomes the intensity.</summary>
        public static LinearColor Convert(Color color, float intensity)
        {
            var linear = GraphicsSettings.lightsUseLinearIntensity ? color.linear.RGBMultiplied(intensity) : color.RGBMultiplied(intensity).linear;
            if (linear.r < 0f || linear.g < 0f || linear.b < 0f)
                throw new ArgumentOutOfRangeException("The input color to be converted must not contain negative values (red: " + linear.r + ", green: " + linear.g + ", blue: " + linear.b + ").");
            float largest = linear.maxColorComponent;
            if (largest <= 1e-20f) return Black();
            float scale = 1f / largest;
            return new LinearColor { m_red = linear.r * scale, m_green = linear.g * scale, m_blue = linear.b * scale, m_intensity = largest };
        }

        public static LinearColor Black() => default;
    }

    public partial struct Cookie
    {
        public int instanceID;
        public float scale;
        public Vector2 sizes;

        public static Cookie Defaults() => new Cookie { instanceID = 0, scale = 1f, sizes = new Vector2(1f, 1f) };
    }

    public partial struct DirectionalLight
    {
        public int instanceID;
        public bool shadow;
        public LightMode mode;
        public Vector3 position;
        public Quaternion orientation;
        public LinearColor color;
        public LinearColor indirectColor;
        public float penumbraWidthRadian;
        [Obsolete("Directional lights support cookies now. In order to position the cookie projection in the world, a position and full orientation are necessary. Use the position and orientation members instead of the direction parameter.", true)]
        public Vector3 direction;
    }

    public partial struct PointLight
    {
        public int instanceID;
        public bool shadow;
        public LightMode mode;
        public Vector3 position;
        public Quaternion orientation;
        public LinearColor color;
        public LinearColor indirectColor;
        public float range;
        public float sphereRadius;
        public FalloffType falloff;
    }

    public partial struct SpotLight
    {
        public int instanceID;
        public bool shadow;
        public LightMode mode;
        public Vector3 position;
        public Quaternion orientation;
        public LinearColor color;
        public LinearColor indirectColor;
        public float range;
        public float sphereRadius;
        public float coneAngle;
        public float innerConeAngle;
        public FalloffType falloff;
        public AngularFalloffType angularFalloff;
    }

    public partial struct RectangleLight
    {
        public int instanceID;
        public bool shadow;
        public LightMode mode;
        public Vector3 position;
        public Quaternion orientation;
        public LinearColor color;
        public LinearColor indirectColor;
        public float range;
        public float width;
        public float height;
        public FalloffType falloff;
    }

    public partial struct DiscLight
    {
        public int instanceID;
        public bool shadow;
        public LightMode mode;
        public Vector3 position;
        public Quaternion orientation;
        public LinearColor color;
        public LinearColor indirectColor;
        public float range;
        public float radius;
        public FalloffType falloff;
    }

    public partial struct SpotLightBoxShape
    {
        public int instanceID;
        public bool shadow;
        public LightMode mode;
        public Vector3 position;
        public Quaternion orientation;
        public LinearColor color;
        public LinearColor indirectColor;
        public float range;
        public float width;
        public float height;
    }

    public partial struct SpotLightPyramidShape
    {
        public int instanceID;
        public bool shadow;
        public LightMode mode;
        public Vector3 position;
        public Quaternion orientation;
        public LinearColor color;
        public LinearColor indirectColor;
        public float range;
        public float angle;
        public float aspectRatio;
        public FalloffType falloff;
    }

    /// <summary>A light as global illumination takes it, whatever its type: the two shape
    /// values and the cone angles hold what the type needs.</summary>
    public partial struct LightDataGI
    {
        public int instanceID;
        public int cookieID;
        public float cookieScale;
        public LinearColor color;
        public LinearColor indirectColor;
        public Quaternion orientation;
        public Vector3 position;
        public float range;
        public float coneAngle;
        public float innerConeAngle;
        public float shape0;
        public float shape1;
        public LightType type;
        public LightMode mode;
        public byte shadow;
        public FalloffType falloff;

        void Common(int id, bool shadows, LightMode lightMode, Vector3 at, Quaternion rotation, LinearColor direct, LinearColor indirect, ref Cookie cookie)
        {
            instanceID = id;
            cookieID = cookie.instanceID;
            cookieScale = cookie.scale;
            color = direct;
            indirectColor = indirect;
            orientation = rotation;
            position = at;
            mode = lightMode;
            shadow = (byte)(shadows ? 1 : 0);
        }

        public void Init(ref DirectionalLight light, ref Cookie cookie)
        {
            Common(light.instanceID, light.shadow, light.mode, light.position, light.orientation, light.color, light.indirectColor, ref cookie);
            range = 0f;
            coneAngle = cookie.sizes.x;
            innerConeAngle = cookie.sizes.y;
            shape0 = light.penumbraWidthRadian;
            shape1 = 0f;
            type = LightType.Directional;
            falloff = FalloffType.Undefined;
        }

        public void Init(ref PointLight light, ref Cookie cookie)
        {
            Common(light.instanceID, light.shadow, light.mode, light.position, light.orientation, light.color, light.indirectColor, ref cookie);
            range = light.range;
            coneAngle = 0f;
            innerConeAngle = 0f;
            shape0 = light.sphereRadius;
            shape1 = 0f;
            type = LightType.Point;
            falloff = light.falloff;
        }

        public void Init(ref SpotLight light, ref Cookie cookie)
        {
            Common(light.instanceID, light.shadow, light.mode, light.position, light.orientation, light.color, light.indirectColor, ref cookie);
            range = light.range;
            coneAngle = light.coneAngle;
            innerConeAngle = light.innerConeAngle;
            shape0 = light.sphereRadius;
            shape1 = (float)light.angularFalloff;
            type = LightType.Spot;
            falloff = light.falloff;
        }

        public void Init(ref RectangleLight light, ref Cookie cookie)
        {
            Common(light.instanceID, light.shadow, light.mode, light.position, light.orientation, light.color, light.indirectColor, ref cookie);
            range = light.range;
            coneAngle = 0f;
            innerConeAngle = 0f;
            shape0 = light.width;
            shape1 = light.height;
            type = LightType.Rectangle;
            falloff = light.falloff;
        }

        public void Init(ref DiscLight light, ref Cookie cookie)
        {
            Common(light.instanceID, light.shadow, light.mode, light.position, light.orientation, light.color, light.indirectColor, ref cookie);
            range = light.range;
            coneAngle = 0f;
            innerConeAngle = 0f;
            shape0 = light.radius;
            shape1 = 0f;
            type = LightType.Disc;
            falloff = light.falloff;
        }

        public void Init(ref SpotLightBoxShape light, ref Cookie cookie)
        {
            Common(light.instanceID, light.shadow, light.mode, light.position, light.orientation, light.color, light.indirectColor, ref cookie);
            range = light.range;
            coneAngle = 0f;
            innerConeAngle = 0f;
            shape0 = light.width;
            shape1 = light.height;
            type = LightType.SpotBoxShape;
            falloff = FalloffType.Undefined;
        }

        public void Init(ref SpotLightPyramidShape light, ref Cookie cookie)
        {
            Common(light.instanceID, light.shadow, light.mode, light.position, light.orientation, light.color, light.indirectColor, ref cookie);
            range = light.range;
            coneAngle = light.angle;
            innerConeAngle = 0f;
            shape0 = light.aspectRatio;
            shape1 = 0f;
            type = LightType.SpotPyramidShape;
            falloff = light.falloff;
        }

        public void Init(ref DirectionalLight light) { var cookie = Cookie.Defaults(); Init(ref light, ref cookie); }
        public void Init(ref PointLight light) { var cookie = Cookie.Defaults(); Init(ref light, ref cookie); }
        public void Init(ref SpotLight light) { var cookie = Cookie.Defaults(); Init(ref light, ref cookie); }
        public void Init(ref RectangleLight light) { var cookie = Cookie.Defaults(); Init(ref light, ref cookie); }
        public void Init(ref DiscLight light) { var cookie = Cookie.Defaults(); Init(ref light, ref cookie); }
        public void Init(ref SpotLightBoxShape light) { var cookie = Cookie.Defaults(); Init(ref light, ref cookie); }
        public void Init(ref SpotLightPyramidShape light) { var cookie = Cookie.Defaults(); Init(ref light, ref cookie); }

        /// <summary>A light global illumination does not take.</summary>
        public void InitNoBake(int lightInstanceID)
        {
            instanceID = lightInstanceID;
            mode = LightMode.Unknown;
        }
    }

    /// <summary>What a <see cref="Light"/> gives global illumination by default.</summary>
    public static partial class LightmapperUtils
    {
        /// <summary>The tangent of half a spot light's default inner cone over that of half its
        /// angle.</summary>
        const float InnerConeTangent = 46f / 64f;

        public static LightMode Extract(LightmapBakeType baketype) =>
            baketype == LightmapBakeType.Realtime ? LightMode.Realtime : baketype == LightmapBakeType.Mixed ? LightMode.Mixed : LightMode.Baked;

        /// <summary>The light's color (with its color temperature when lights use one).</summary>
        static Color TintedColor(Light l) =>
            l.useColorTemperature && GraphicsSettings.lightsUseColorTemperature ? l.color * Mathf.CorrelatedColorTemperatureToRGB(l.colorTemperature).gamma : l.color;

        static LinearColor Direct(Light l) => LinearColor.Convert(TintedColor(l), l.intensity);

        public static LinearColor ExtractIndirect(Light l) => LinearColor.Convert(TintedColor(l), l.intensity * l.bounceIntensity);

        public static float ExtractInnerCone(Light l) => 2f * Mathf.Atan(Mathf.Tan(l.spotAngle * 0.5f * Mathf.Deg2Rad) * InnerConeTangent);

        public static void Extract(Light l, ref DirectionalLight dir)
        {
            dir.instanceID = l.GetInstanceID();
            dir.mode = Extract(l.bakingOutput.lightmapBakeType);
            dir.shadow = l.shadows != LightShadows.None;
            dir.position = l.transform.position;
            dir.orientation = l.transform.rotation;
            dir.color = Direct(l);
            dir.indirectColor = ExtractIndirect(l);
            dir.penumbraWidthRadian = l.shadows == LightShadows.Soft ? Mathf.Deg2Rad * l.shadowAngle : 0f;
        }

        public static void Extract(Light l, ref PointLight point)
        {
            point.instanceID = l.GetInstanceID();
            point.mode = Extract(l.bakingOutput.lightmapBakeType);
            point.shadow = l.shadows != LightShadows.None;
            point.position = l.transform.position;
            point.orientation = l.transform.rotation;
            point.color = Direct(l);
            point.indirectColor = ExtractIndirect(l);
            point.range = l.range;
            point.sphereRadius = l.shadows == LightShadows.Soft ? l.shadowRadius : 0f;
            point.falloff = FalloffType.Legacy;
        }

        public static void Extract(Light l, ref SpotLight spot)
        {
            spot.instanceID = l.GetInstanceID();
            spot.mode = Extract(l.bakingOutput.lightmapBakeType);
            spot.shadow = l.shadows != LightShadows.None;
            spot.position = l.transform.position;
            spot.orientation = l.transform.rotation;
            spot.color = Direct(l);
            spot.indirectColor = ExtractIndirect(l);
            spot.range = l.range;
            spot.sphereRadius = l.shadows == LightShadows.Soft ? l.shadowRadius : 0f;
            spot.coneAngle = l.spotAngle * Mathf.Deg2Rad;
            spot.innerConeAngle = ExtractInnerCone(l);
            spot.falloff = FalloffType.Legacy;
            spot.angularFalloff = AngularFalloffType.LUT;
        }

        public static void Extract(Light l, ref RectangleLight rect)
        {
            rect.instanceID = l.GetInstanceID();
            rect.mode = Extract(l.bakingOutput.lightmapBakeType);
            rect.shadow = l.shadows != LightShadows.None;
            rect.position = l.transform.position;
            rect.orientation = l.transform.rotation;
            rect.color = Direct(l);
            rect.indirectColor = ExtractIndirect(l);
            rect.range = l.range;
            rect.width = l.areaSize.x;
            rect.height = l.areaSize.y;
            rect.falloff = FalloffType.Legacy;
        }

        public static void Extract(Light l, ref DiscLight disc)
        {
            disc.instanceID = l.GetInstanceID();
            disc.mode = Extract(l.bakingOutput.lightmapBakeType);
            disc.shadow = l.shadows != LightShadows.None;
            disc.position = l.transform.position;
            disc.orientation = l.transform.rotation;
            disc.color = Direct(l);
            disc.indirectColor = ExtractIndirect(l);
            disc.range = l.range;
            disc.radius = l.areaSize.x;
            disc.falloff = FalloffType.Legacy;
        }

        public static void Extract(Light l, out Cookie cookie)
        {
            cookie.instanceID = l.cookie != null ? l.cookie.GetInstanceID() : 0;
            cookie.scale = 1f;
            cookie.sizes = l.type == UnityEngine.LightType.Directional && l.cookie != null ? new Vector2(l.cookieSize, l.cookieSize) : new Vector2(1f, 1f);
        }
    }

    public static partial class Lightmapping
    {
        public delegate void RequestLightsDelegate(Light[] requests, NativeArray<LightDataGI> lightsOutput);

        /// <summary>What lights give global illumination when no pipeline says otherwise:
        /// <see cref="LightmapperUtils"/>'s values.</summary>
        static readonly RequestLightsDelegate s_DefaultDelegate = (requests, lightsOutput) =>
        {
            var data = new LightDataGI();
            for (int i = 0; i < requests.Length; i++)
            {
                var light = requests[i];
                switch (light.type)
                {
                    case UnityEngine.LightType.Directional:
                        var directional = new DirectionalLight();
                        LightmapperUtils.Extract(light, ref directional);
                        data.Init(ref directional);
                        break;
                    case UnityEngine.LightType.Point:
                        var point = new PointLight();
                        LightmapperUtils.Extract(light, ref point);
                        data.Init(ref point);
                        break;
                    case UnityEngine.LightType.Spot:
                        var spot = new SpotLight();
                        LightmapperUtils.Extract(light, ref spot);
                        data.Init(ref spot);
                        break;
                    case UnityEngine.LightType.Rectangle:
                        var rectangle = new RectangleLight();
                        LightmapperUtils.Extract(light, ref rectangle);
                        data.Init(ref rectangle);
                        break;
                    case UnityEngine.LightType.Disc:
                        var disc = new DiscLight();
                        LightmapperUtils.Extract(light, ref disc);
                        data.Init(ref disc);
                        break;
                    default:
                        data.InitNoBake(light.GetInstanceID());
                        break;
                }
                lightsOutput[i] = data;
            }
        };

        static RequestLightsDelegate s_RequestLightsDelegate = s_DefaultDelegate;

        /// <summary>Whether a delegate of a pipeline's (or a script's) converts the lights.</summary>
        internal static bool HasCustomDelegate => !ReferenceEquals(s_RequestLightsDelegate, s_DefaultDelegate);

        public static void SetDelegate(RequestLightsDelegate del) => s_RequestLightsDelegate = del ?? s_DefaultDelegate;

        public static RequestLightsDelegate GetDelegate() => s_RequestLightsDelegate;

        public static void ResetDelegate() => s_RequestLightsDelegate = s_DefaultDelegate;
    }
}
