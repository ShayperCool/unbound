using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    // Expected observations come from two independent Unity 6000.0.71f1 recordings
    // in query2dparity/Reference. The broad comparator's 1e-3 rule is unchanged.
    public class Physics2DQueryParityTests : IDisposable
    {
        readonly List<GameObject> made = new List<GameObject>();
        GameObject Make(string name, Vector2 p, float angle = 0, Vector2? scale = null)
        {
            var go = new GameObject(name);
            go.transform.position = p;
            go.transform.rotation = Quaternion.Euler(0, 0, angle);
            if (scale.HasValue) go.transform.localScale = new Vector3(scale.Value.x, scale.Value.y, 1);
            made.Add(go);
            return go;
        }
        public Physics2DQueryParityTests()
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.autoSyncTransforms = true;
            Physics2D.defaultContactOffset = 0.01f;
            Physics2D.queriesStartInColliders = true;
            Physics2D.queriesHitTriggers = true;
            Make("box", Vector2.zero, 20).AddComponent<BoxCollider2D>().size = new Vector2(2, 1);
            var circle = Make("circle", new Vector2(4, 0)).AddComponent<CircleCollider2D>();
            circle.radius = 0.75f; circle.offset = new Vector2(0.2f, 0.1f);
            Make("capsuleV", new Vector2(-4, 0)).AddComponent<CapsuleCollider2D>().size = new Vector2(1, 3);
            var cap = Make("capsuleH", new Vector2(0, 4), -15).AddComponent<CapsuleCollider2D>();
            cap.size = new Vector2(3, 1); cap.direction = CapsuleDirection2D.Horizontal;
            Make("polygon", new Vector2(4, 4), 10, new Vector2(1.5f, 0.75f)).AddComponent<PolygonCollider2D>().points = new[]
            {
                new Vector2(0, 1), new Vector2(0.95f, 0.31f), new Vector2(0.59f, -0.81f), new Vector2(-0.59f, -0.81f), new Vector2(-0.95f, 0.31f)
            };
            Make("edge", new Vector2(0, -4)).AddComponent<EdgeCollider2D>().points = new[]
            {
                new Vector2(-5, 0), new Vector2(-1, 0.5f), new Vector2(2, -0.5f), new Vector2(5, 0)
            };
            var trigger = Make("trigger", new Vector2(-4, 4)).AddComponent<BoxCollider2D>();
            trigger.size = new Vector2(1.5f, 1.5f); trigger.isTrigger = true;
            Make("scaledBox", new Vector2(8, 0), 45, new Vector2(1.5f, 0.5f)).AddComponent<BoxCollider2D>().edgeRadius = 0.1f;
            var body = Make("bodyBox", new Vector2(8, 4));
            body.AddComponent<BoxCollider2D>(); body.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            Physics2D.SyncTransforms();
        }
        public void Dispose()
        {
            foreach (var go in made) Object.DestroyImmediate(go);
        }
        static void Near(float expected, float actual) =>
            Assert.True(Math.Abs(expected - actual) <= 1e-3f * Math.Max(1f, Math.Abs(expected)), $"Expected {expected:R}, got {actual:R}");
        static void Check(RaycastHit2D h, string name, float[] v)
        {
            Assert.NotNull(h.collider);
            Assert.Equal(name, h.collider.name);
            Near(v[0], h.distance); Near(v[1], h.fraction);
            Near(v[2], h.point.x); Near(v[3], h.point.y);
            Near(v[4], h.normal.x); Near(v[5], h.normal.y);
            Near(v[6], h.centroid.x); Near(v[7], h.centroid.y);
        }
        public static IEnumerable<object[]> NativeCasts()
        {
            yield return new object[] { "box", "d60", new string[] { "edge" }, new float[][] { new float[] { 1.09881139f, 0.0366270319f, -1.2741617f, -3.53678942f, 0.124034733f, -0.992277861f, -1.45059431f, -3.976605f } } };
            yield return new object[] { "box", "h-3.5", new string[] { "edge" }, new float[][] { new float[] { 5.63775158f, 0.187925041f, -4.538681f, -3.93981552f, -0.124034733f, 0.992277861f, -4.36224842f, -3.5f } } };
            yield return new object[] { "box", "h0", new string[] { "capsuleV" }, new float[][] { new float[] { 5.189338f, 0.172977954f, -4.49250031f, -0.350511074f, -1f, 0f, -4.810662f, 0f } } };
            yield return new object[] { "capsuleH", "d0", new string[] { "capsuleV" }, new float[][] { new float[] { 2.19867969f, 0.07328933f, -4.146989f, 1.47044945f, -0.298226357f, 0.9544952f, -3.80132031f, 2f } } };
            yield return new object[] { "capsuleH", "d240", new string[] { "polygon", "box", "edge" }, new float[][] { new float[] { 4.53565836f, 0.151188627f, 3.44522572f, 4.49679041f, -0.4993786f, 0.8663839f, 3.73217f, 5.000208f }, new float[] { 9.013023f, 0.300434142f, 0.785615f, 0.7726549f, 0.9396926f, 0.3420202f, 1.49348807f, 1.12269592f }, new float[] { 13.7643871f, 0.4588129f, -1.2789259f, -3.53234649f, -0.124034733f, 0.992277861f, -0.8821933f, -2.99210548f } } };
            yield return new object[] { "capsuleH", "d270", new string[] { "capsuleH", "polygon", "edge" }, new float[][] { new float[] { 5.503852f, 0.183461711f, 1.34302223f, 4.057989f, 0.765657365f, 0.6432486f, 1.99999988f, 4.496148f }, new float[] { 5.999011f, 0.199967042f, 2.56714678f, 3.99067616f, -0.5f, 0.8660254f, 2f, 4.000989f }, new float[] { 13.7947054f, 0.4598235f, 1.47449064f, -4.322195f, 0.316227764f, 0.948683262f, 2f, -3.79470539f } } };
            yield return new object[] { "capsuleH", "d30", new string[] { "capsuleV", "box", "polygon" }, new float[][] { new float[] { 0.129545078f, 0.00431817444f, -4.24029064f, -1.42990375f, -0.487899184f, -0.872900069f, -4.816014f, -1.93522739f }, new float[] { 3.58205247f, 0.119401745f, -1.11327422f, 0.127035558f, -0.95584923f, -0.2938578f, -1.82605481f, -0.208973765f }, new float[] { 9.60461f, 0.320153683f, 3.7718544f, 3.3403616f, 0.173648208f, -0.98480767f, 3.38963366f, 2.80230522f } } };
            yield return new object[] { "capsuleH", "d90", new string[] { "edge", "polygon", "capsuleH" }, new float[][] { new float[] { 1.0230999f, 0.0341033228f, 2.38492632f, -4.43838024f, 0.164398983f, -0.9863939f, 2.00000024f, -4.9769f }, new float[] { 9.430223f, 0.31434077f, 2.647853f, 3.878717f, -0.7344975f, -0.678611338f, 1.99999976f, 3.430223f }, new float[] { 9.484766f, 0.316158861f, 1.34293139f, 3.42354059f, 0.7647494f, -0.6443278f, 1.99999976f, 3.484766f } } };
            yield return new object[] { "capsuleH", "h0", new string[] { "capsuleV", "box", "circle", "scaledBox" }, new float[][] { new float[] { 4.78198767f, 0.159399584f, -4.4925f, 0.249999985f, -1f, 0f, -5.21801233f, 0f }, new float[] { 8.187605f, 0.2729202f, -1.11304784f, 0.128892511f, -0.910306454f, 0.413934827f, -1.81239462f, 0f }, new float[] { 12.7429132f, 0.424763769f, 3.46533823f, 0.207608655f, -0.98944217f, 0.144927382f, 2.74291325f, 0f }, new float[] { 16.90482f, 0.563494f, 7.54466152f, 0.04317131f, -0.707106769f, 0.7071069f, 6.90482044f, 0f } } };
            yield return new object[] { "capsuleH", "h2", new string[] { "capsuleV" }, new float[][] { new float[] { 6.19867945f, 0.20662266f, -4.146989f, 1.47044945f, -0.298226357f, 0.9544952f, -3.80132055f, 2f } } };
            yield return new object[] { "capsuleH", "h4", new string[] { "trigger", "capsuleH", "polygon", "bodyBox" }, new float[][] { new float[] { 4.521987f, 0.1507329f, -4.75250053f, 4.25f, -1f, 0f, -5.478013f, 4f }, new float[] { 7.81607f, 0.260535657f, -1.45841527f, 4.25328636f, -0.999936938f, -0.0112339407f, -2.18393f, 4f }, new float[] { 11.9982862f, 0.399942875f, 2.56629f, 3.990182f, -0.5f, 0.8660254f, 1.99828625f, 4f }, new float[] { 16.7719879f, 0.559066236f, 7.4975f, 4.25f, -1f, 0f, 6.77198744f, 4f } } };
            yield return new object[] { "capsuleH", "h4.5", new string[] { "trigger", "capsuleH", "polygon", "bodyBox" }, new float[][] { new float[] { 4.521987f, 0.1507329f, -4.75250053f, 4.75f, -1f, 0f, -5.478013f, 4.5f }, new float[] { 7.988709f, 0.2662903f, -1.35010815f, 4.56698f, -0.780061066f, 0.625703454f, -2.011291f, 4.5f }, new float[] { 12.8643112f, 0.428810358f, 2.57737112f, 3.9965744f, -0.4993786f, 0.8663839f, 2.86431074f, 4.5f }, new float[] { 16.9097633f, 0.563658834f, 7.49858141f, 4.50225544f, -0.532366633f, 0.846514f, 6.90976429f, 4.5f } } };
            yield return new object[] { "capsuleH", "v2", new string[] { "capsuleH", "polygon", "edge" }, new float[][] { new float[] { 5.503852f, 0.183461711f, 1.34302235f, 4.057989f, 0.7656575f, 0.643248558f, 2f, 4.496148f }, new float[] { 5.999011f, 0.199967042f, 2.56714678f, 3.99067616f, -0.5f, 0.8660254f, 2f, 4.000989f }, new float[] { 13.7947054f, 0.4598235f, 1.47449064f, -4.322195f, 0.316227764f, 0.948683262f, 2f, -3.79470539f } } };
            yield return new object[] { "capsuleH", "v3", new string[] { "polygon", "circle", "edge" }, new float[][] { new float[] { 5.42181063f, 0.180727035f, 2.71305561f, 4.074772f, -0.4993786f, 0.8663839f, 3f, 4.57818937f }, new float[] { 9.454113f, 0.3151371f, 3.64987135f, 0.599131346f, -0.740600049f, 0.671946049f, 3f, 0.545887f }, new float[] { 13.8564339f, 0.4618811f, 2.615074f, -4.39495325f, -0.164398983f, 0.9863939f, 3f, -3.85643339f } } };
            yield return new object[] { "capsuleH", "v4", new string[] { "polygon", "circle", "edge" }, new float[][] { new float[] { 4.845417f, 0.16151391f, 3.71305561f, 4.651166f, -0.4993786f, 0.8663839f, 4f, 5.154583f }, new float[] { 8.820354f, 0.294011831f, 3.82875f, 0.743023753f, -0.5f, 0.8660254f, 4f, 1.17964506f }, new float[] { 13.6897659f, 0.456325531f, 3.61507416f, -4.22828627f, -0.164398983f, 0.9863939f, 4f, -3.689766f } } };
            yield return new object[] { "capsuleH", "v6", new string[] { "polygon" }, new float[][] { new float[] { 5.060494f, 0.168683127f, 5.364708f, 4.478224f, 0.6915497f, 0.7223288f, 6f, 4.939506f } } };
            yield return new object[] { "capsuleV", "d120", new string[] { "edge", "circle", "capsuleH" }, new float[][] { new float[] { 1.51450264f, 0.05048342f, 5.00249434f, -3.999657f, 0.990669549f, 0.136285618f, 5.242749f, -3.61660528f }, new float[] { 4.34999943f, 0.145f, 3.91733742f, -0.586676359f, -0.380649775f, -0.924719334f, 3.82500029f, -1.1609931f }, new float[] { 9.014526f, 0.30048418f, 1.318922f, 3.39772344f, 0.7167244f, -0.6973566f, 1.49273729f, 2.87860513f } } };
            yield return new object[] { "capsuleV", "d180", new string[] { "capsuleV" }, new float[][] { new float[] { 13.6568775f, 0.455229223f, -3.77008462f, 1.43554306f, 0.4668306f, 0.8843467f, -3.656877f, 1.99999952f } } };
            yield return new object[] { "capsuleV", "d210", new string[] { "polygon", "box", "capsuleV" }, new float[][] { new float[] { 3.83883619f, 0.1279612f, 5.35372257f, 4.43058157f, 1f, 0f, 5.603674f, 4.08058167f }, new float[] { 9.266359f, 0.30887863f, 0.7701129f, 0.814044237f, 0.5489917f, 0.8358278f, 0.903301239f, 1.36682057f }, new float[] { 14.0794992f, 0.469316632f, -3.50750017f, -0.844874442f, 1f, 0f, -3.26500034f, -1.03974891f } } };
            yield return new object[] { "capsuleV", "d330", new string[] { "trigger", "capsuleH", "circle", "scaledBox" }, new float[][] { new float[] { 1.31000221f, 0.04366674f, -3.79370737f, 4.75250053f, 0f, 1f, -3.79370737f, 5.34500027f }, new float[] { 3.72653866f, 0.12421795f, -1.45842588f, 4.258819f, -1f, 0f, -1.70092583f, 4.13673162f }, new float[] { 9.725513f, 0.324183762f, 3.66807079f, 0.618045568f, -0.716394067f, 0.6976959f, 3.49433851f, 1.13724327f }, new float[] { 13.7744179f, 0.459147245f, 7.206103f, -0.408122271f, -0.8465714f, -0.5322752f, 7.0007925f, -0.887209654f } } };
            yield return new object[] { "capsuleV", "h-2", new string[] { "capsuleV" }, new float[][] { new float[] { 5.654471f, 0.188482359f, -4.23143768f, -1.43537426f, -0.46938467f, -0.882993758f, -4.345529f, -2f } } };
            yield return new object[] { "capsuleV", "h-4.5", new string[] { "edge" }, new float[][] { new float[] { 4.804802f, 0.16016005f, -5.002448f, -4.0018816f, -0.7929228f, -0.6093221f, -5.195198f, -4.5f } } };
            yield return new object[] { "capsuleV", "h2", new string[] { "capsuleV" }, new float[][] { new float[] { 5.654471f, 0.188482359f, -4.23143768f, 1.43537426f, -0.46938467f, 0.882993758f, -4.345529f, 2f } } };
            yield return new object[] { "capsuleV", "h3", new string[] { "trigger", "capsuleH", "polygon", "bodyBox" }, new float[][] { new float[] { 5.005f, 0.166833341f, -4.7525f, 3.3f, -1f, 0f, -4.995f, 3f }, new float[] { 9.586012f, 0.3195337f, -0.351224959f, 3.584237f, -0.2588191f, -0.9659258f, -0.4139886f, 3f }, new float[] { 12.8061829f, 0.4268728f, 2.98429871f, 3.514563f, -0.7344975f, -0.678611338f, 2.80618334f, 3f }, new float[] { 17.306284f, 0.5768761f, 7.498022f, 3.49846888f, -0.7906723f, -0.61223954f, 7.30628347f, 3f } } };
            yield return new object[] { "capsuleV", "h4.5", new string[] { "trigger", "capsuleH", "polygon", "bodyBox" }, new float[][] { new float[] { 5.005f, 0.166833341f, -4.7525f, 4.45f, -1f, 0f, -4.995f, 4.5f }, new float[] { 8.299073f, 0.2766358f, -1.45842636f, 4.258819f, -1f, 0f, -1.70092678f, 4.5f }, new float[] { 12.3783865f, 0.4126129f, 2.55445671f, 3.983242f, -0.7260462f, 0.687646031f, 2.3783865f, 4.5f }, new float[] { 17.2550011f, 0.5751667f, 7.49750042f, 4.325f, -1f, 0f, 7.255001f, 4.5f } } };
            yield return new object[] { "capsuleV", "v0", new string[] { "capsuleH", "box", "edge" }, new float[][] { new float[] { 4.88907146f, 0.162969068f, -0.0627637058f, 4.52669144f, 0.2588191f, 0.9659258f, 0f, 5.11092854f }, new float[] { 8.857187f, 0.295239568f, 0.08294002f, 0.5649374f, -0.3420202f, 0.9396926f, 0f, 1.14281321f }, new float[] { 13.2250805f, 0.440836f, -0.07668534f, -3.80513573f, 0.316227764f, 0.948683262f, 0f, -3.22508f } } };
            yield return new object[] { "capsuleV", "v1", new string[] { "capsuleH", "box", "edge" }, new float[][] { new float[] { 5.15702057f, 0.1719007f, 0.9372363f, 4.25874233f, 0.2588191f, 0.9659258f, 1f, 4.84297943f }, new float[] { 8.757287f, 0.291909546f, 0.7721159f, 0.809769869f, 0.9396926f, 0.3420202f, 1f, 1.242713f }, new float[] { 13.5584145f, 0.451947153f, 0.9233149f, -4.13847f, 0.316227764f, 0.948683262f, 1f, -3.558415f } } };
            yield return new object[] { "circle", "h0", new string[] { "capsuleV" }, new float[][] { new float[] { 5.21500063f, 0.17383334f, -4.49249935f, 0f, -1f, 0f, -4.78499937f, 0f } } };
            yield return new object[] { "circle", "h3", new string[] { "trigger" }, new float[][] { new float[] { 5.09286356f, 0.1697621f, -4.75140572f, 3.24776363f, -0.532155931f, -0.8466463f, -4.90713644f, 3f } } };
            yield return new object[] { "circle", "v-5", new string[] { "trigger" }, new float[][] { new float[] { 5.09286356f, 0.1697621f, -4.75223637f, 4.75140572f, -0.8466463f, 0.532155931f, -5f, 4.90713644f } } };
            yield return new object[] { "circle", "v4", new string[] { "polygon" }, new float[][] { new float[] { 4.98474836f, 0.166158274f, 3.94941473f, 4.72715855f, 0.172941536f, 0.984932065f, 4f, 5.01525164f } } };
        }
        [Theory]
        [MemberData(nameof(NativeCasts))]
        public void StaticCastsMatchNative(string kind, string ray, string[] names, float[][] values)
        {
            float coordinate = float.Parse(ray.Substring(1), System.Globalization.CultureInfo.InvariantCulture);
            Vector2 origin, direction;
            if (ray[0] == 'h') { origin = new Vector2(-10, coordinate); direction = Vector2.right; }
            else if (ray[0] == 'v') { origin = new Vector2(coordinate, 10); direction = Vector2.down; }
            else
            {
                float a = coordinate * Mathf.Deg2Rad;
                direction = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                origin = new Vector2(2, 2) - direction * 8;
            }
            var hits = new RaycastHit2D[16];
            int count;
            if (kind == "circle") { hits[0] = Physics2D.CircleCast(origin, 0.3f, direction, 30); count = 1; }
            else if (kind == "box") { hits[0] = Physics2D.BoxCast(origin, new Vector2(0.5f, 0.8f), 10, direction, 30); count = 1; }
            else if (kind == "capsuleV") count = Physics2D.CapsuleCastNonAlloc(origin, new Vector2(0.5f, 1.2f), CapsuleDirection2D.Vertical, 0, direction, hits, 30);
            else count = Physics2D.CapsuleCastNonAlloc(origin, new Vector2(1.6f, 0.6f), CapsuleDirection2D.Horizontal, 30, direction, hits, 30);
            Assert.Equal(names.Length, count);
            for (int i = 0; i < count; i++) Check(hits[i], names[i], values[i]);
        }
        // Two fresh Native processes agree on these threshold, direction and range controls.
        public static IEnumerable<object[]> NativeInitialCasts()
        {
            yield return new object[] { "box", "circle", -0.004f, false, "toward", null };
            yield return new object[] { "box", "circle", -0.004f, true, "toward", new float[] { 0f, 0f, -1.003f, 0f, -1f, 0f, -1.296f, 0f } };
            yield return new object[] { "box", "circle", -0.001f, false, "toward", new float[] { 0.0039999485f, 0.001333336f, -1.0025f, 0f, -1f, 0f, -1.29500008f, 0f } };
            yield return new object[] { "box", "circle", -0.001f, true, "short", new float[] { 0.004999995f, 0.5f, -1.002f, 0f, -1f, 0f, -1.294f, 0f } };
            yield return new object[] { "box", "circle", -0.001f, true, "zero", null };
            yield return new object[] { "box", "circle", -0.001f, true, "away", null };
            yield return new object[] { "circle", "circle", -0.016f, false, "toward", null };
            yield return new object[] { "circle", "circle", -0.016f, true, "toward", new float[] { 0f, 0f, -0.742f, 0f, -1f, 0f, -1.034f, 0f } };
            yield return new object[] { "circle", "circle", -0.012f, false, "toward", new float[] { 0.003000021f, 0.001000007f, -0.742499948f, 0f, -1f, 0f, -1.035f, 0f } };
            yield return new object[] { "circle", "circle", -0.012f, true, "short", new float[] { 0.003000021f, 0.3000024f, -0.742499948f, 0f, -1f, 0f, -1.035f, 0f } };
            yield return new object[] { "circle", "circle", -0.012f, true, "zero", null };
            yield return new object[] { "circle", "circle", -0.012f, true, "away", null };
            yield return new object[] { "box", "box", 0.001f, false, "toward", null };
            yield return new object[] { "box", "box", 0.001f, true, "toward", new float[] { 0f, 0f, -1.0005f, 0f, -1f, 0f, -1.301f, 0f } };
            yield return new object[] { "box", "box", 0.01f, false, "short", new float[] { 0.004999995f, 0.5f, -1.0025f, 0f, -1f, 0f, -1.30500007f, 0f } };
            yield return new object[] { "box", "box", 0.01f, true, "away", null };
            yield return new object[] { "capsuleV", "capsule", -0.012f, false, "toward", new float[] { 0.00299996138f, 0.0009999871f, -0.4925f, 0f, -1f, 0f, -0.785f, 0f } };
            yield return new object[] { "capsuleV", "capsule", -0.012f, true, "short", new float[] { 0.00299996138f, 0.299996436f, -0.4925f, 0f, -1f, 0f, -0.785f, 0f } };
            yield return new object[] { "capsuleH", "box", -0.001f, false, "toward", new float[] { 0.00400006771f, 0.0013333559f, -1.4925f, 0f, -1f, 0f, -1.795f, 0f } };
            yield return new object[] { "capsuleH", "box", -0.001f, true, "short", new float[] { 0.004999995f, 0.5f, -1.492f, 0f, -1f, 0f, -1.794f, 0f } };
        }
        [Theory]
        [MemberData(nameof(NativeInitialCasts))]
        public void InitialCastThresholdMatchesNative(string target, string query, float gap, bool include, string range, float[] expected)
        {
            foreach (var old in made) Object.DestroyImmediate(old);
            made.Clear();
            var go = Make("target", Vector2.zero);
            if (target == "box") go.AddComponent<BoxCollider2D>().size = new Vector2(2, 1);
            else if (target == "circle") go.AddComponent<CircleCollider2D>().radius = 0.75f;
            else
            {
                var capsule = go.AddComponent<CapsuleCollider2D>();
                capsule.direction = target == "capsuleH" ? CapsuleDirection2D.Horizontal : CapsuleDirection2D.Vertical;
                capsule.size = target == "capsuleH" ? new Vector2(3, 1) : new Vector2(1, 3);
            }
            Physics2D.SyncTransforms();
            Physics2D.queriesStartInColliders = include;
            var origin = new Vector2(go.GetComponent<Collider2D>().bounds.min.x - .3f - gap, 0);
            var direction = range == "away" ? Vector2.left : Vector2.right;
            float distance = range == "zero" ? 0 : range == "short" ? .01f : 3;
            var hit = query == "circle" ? Physics2D.CircleCast(origin, .3f, direction, distance)
                : query == "box" ? Physics2D.BoxCast(origin, new Vector2(.6f, .8f), 0, direction, distance)
                : Physics2D.CapsuleCast(origin, new Vector2(.6f, .8f), CapsuleDirection2D.Vertical, 0, direction, distance);
            if (expected == null) Assert.Null(hit.collider);
            else Check(hit, "target", expected);
        }

        [Theory]
        [InlineData(.5f, .262500018f)]
        [InlineData(1f, .07500002f)]
        [InlineData(2f, -.3f)]
        public void CoincidentCircleCastUsesNativeContactOrientation(float scale, float pointX)
        {
            foreach (var old in made) Object.DestroyImmediate(old);
            made.Clear();
            var origin = new Vector2(.3f, -.2f);
            Make("target", origin, 0, new Vector2(scale, scale * .8f))
                .AddComponent<CircleCollider2D>().radius = .75f;
            Physics2D.SyncTransforms();
            var hit = Physics2D.CircleCast(origin, .3f, Vector2.right, 10);
            Check(hit, "target", new[] { 0f, 0f, pointX, -.2f, -1f, 0f, .3f, -.2f });
        }

        // Independent fine-angle controls distinguish the contact averaging boundary
        // from an angular tolerance or defaultContactOffset-dependent threshold.
        [InlineData("box", .5f, .001f, 3.516674e-06f, 0.252504349f)]
        [InlineData("box", 1f, .0007f, 4.887581e-06f, 0.5025061f)]
        [InlineData("box", 1f, .0008f, -1.01999426f, 0.5024998f)]
        [InlineData("capsule", 1f, .0011f, 0f, 0.5025058f)]
        [InlineData("capsule", 1f, .0012f, -0.6f, 0.5025f)]
        [InlineData("capsule", 2f, .001f, -1.2f, 1.0025f)]
        [Theory]
        public void NearParallelContactSelectionMatchesNative(string kind, float scale, float angle, float x, float y)
        {
            foreach (var go in made) Object.DestroyImmediate(go);
            made.Clear();
            Make("box", Vector2.zero).AddComponent<BoxCollider2D>().size = new Vector2(4, 1) * scale;
            Physics2D.SyncTransforms();
            var origin = new Vector2(0, 4) * scale;
            var size = new Vector2(2, 0.8f) * scale;
            var hit = kind == "box" ? Physics2D.BoxCast(origin, size, angle, Vector2.down, 5 * scale)
                : Physics2D.CapsuleCast(origin, size, CapsuleDirection2D.Horizontal, angle, Vector2.down, 5 * scale);
            Assert.NotNull(hit.collider);
            Near(x, hit.point.x); Near(y, hit.point.y);
        }

        // Both fine-selection Native recordings create the target at .01 before
        // changing the query skin to .001. Its captured skin is still unrounded.
        [Theory]
        [InlineData("box", .0006f, 2.05636024e-06f, .257002652f)]
        [InlineData("box", .0014f, 4.827976e-06f, .257006079f)]
        [InlineData("capsule", .0006f, 0f, .2525016f)]
        [InlineData("capsule", .0014f, 0f, .252503663f)]
        public void NearParallelCastUsesCapturedTargetSkin(string kind, float angle, float x, float y)
        {
            foreach (var go in made) Object.DestroyImmediate(go);
            made.Clear();
            Make("box", Vector2.zero).AddComponent<BoxCollider2D>().size = new Vector2(2, .5f);
            Physics2D.defaultContactOffset = .001f;
            Physics2D.SyncTransforms();
            var origin = new Vector2(0, 2);
            var size = new Vector2(1, .4f);
            var hit = kind == "box" ? Physics2D.BoxCast(origin, size, angle, Vector2.down, 2.5f)
                : Physics2D.CapsuleCast(origin, size, CapsuleDirection2D.Horizontal, angle, Vector2.down, 2.5f);
            Assert.NotNull(hit.collider);
            Near(x, hit.point.x); Near(y, hit.point.y);
        }
    }
}
