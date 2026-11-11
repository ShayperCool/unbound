using System;
using System.Collections.Generic;
using Unbound.Runtime.Humanoid;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The keyframe reduction of a Humanoid model clip's curves (animation compression Keyframe
    /// Reduction and Optimal): every curve on its own, as a float curve with the importer's
    /// Position Error (6000.0.71f1, tests/projects/modelclips HumanReduced and HumanOptimal; the
    /// Manual's model Animation tab says the errors are how much the reduced curve may deviate
    /// and gives no rule).
    /// </summary>
    public sealed class HumanClipReductionTests
    {
        const float Frame = 1f / 30f;

        static float[] Times(int count)
        {
            var times = new float[count];
            for (int i = 0; i < count; i++) times[i] = (float)i * Frame;
            return times;
        }

        static float[] Slopes(float[] times, float[] values)
        {
            var slopes = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                int a = Math.Max(i - 1, 0), b = Math.Min(i + 1, values.Length - 1);
                slopes[i] = (values[b] - values[a]) / (times[b] - times[a]);
            }
            return slopes;
        }

        static (int[] Kept, bool Flat) Reduce(float[] values, float[] slopes, float error)
        {
            var kept = new List<int>();
            bool flat = HumanClipReduction.Reduce(Times(values.Length), values, slopes, Frame, error, kept);
            return (kept.ToArray(), flat);
        }

        // Every-frame curves of one clip as the Editor imports them with compression Off (values
        // and slopes of the keys, a capture outside the reference): the body's height, a chest
        // muscle that stays below 6e-4, a leg muscle and the body rotation's w.
        static readonly float[] s_BodyHeight = { 1.0063985586166382f, 1.0043553113937378f, 0.998578667640686f, 0.9896966814994812f, 0.9784151911735535f, 0.9654436111450195f, 0.9514386057853699f, 0.9369480609893799f, 0.9223496913909912f, 0.9078282117843628f, 0.8933776617050171f, 0.8788174390792847f, 0.8638508319854736f, 0.8481060862541199f, 0.8312289714813232f, 0.8130130171775818f, 0.7931724786758423f, 0.7723314166069031f, 0.7510355114936829f, 0.7299779057502747f, 0.709869384765625f, 0.6913447380065918f, 0.6749238967895508f, 0.6609513163566589f, 0.6495813727378845f, 0.6407750248908997f, 0.6343225836753845f, 0.629907488822937f, 0.6271575689315796f, 0.6257068514823914f, 0.6252657175064087f };
        static readonly float[] s_BodyHeightSlopes = { -0.06129741296172142f, -0.11729835718870163f, -0.2198794186115265f, -0.3024521470069885f, -0.36379608511924744f, -0.40464872121810913f, -0.42743319272994995f, -0.4363337457180023f, -0.43679776787757874f, -0.4345804750919342f, -0.43516162037849426f, -0.44290226697921753f, -0.4606701135635376f, -0.48932796716690063f, -0.5263963341712952f, -0.5708473920822144f, -0.6102235317230225f, -0.6320545673370361f, -0.6353026628494263f, -0.6174919605255127f, -0.5794975757598877f, -0.5241823196411133f, -0.4559013843536377f, -0.38013753294944763f, -0.3026443421840668f, -0.2288818657398224f, -0.1630130112171173f, -0.10747525095939636f, -0.06300954520702362f, -0.028377797454595566f, -0.013234032317996025f };
        static readonly float[] s_ChestFrontBack = { -1.2806603422177432e-07f, 1.4808815649303142e-05f, -5.905209263801225e-07f, -4.3400486902100965e-05f, -0.00011439806257840246f, -0.00021081154409330338f, -0.000256974104559049f, -0.00032467994606122375f, -0.0003701921086758375f, -0.0004049137933179736f, -0.00042961639701388776f, -0.00044517029891721904f, -0.0004525562690105289f, -0.00047685939352959394f, -0.0004544674011413008f, -0.0005449396558105946f, -0.0005691717378795147f, -0.0005341885844245553f, -0.0004525562690105289f, -0.00044517029891721904f, -0.00042961639701388776f, -0.0004049137933179736f, -0.0003701921086758375f, -0.00032467994606122375f, -0.000256974104559049f, -0.00021081154409330338f, -0.00012059368600603193f, -5.689849058398977e-05f, -2.028104427154176e-05f, -1.1703743439284153e-05f, -1.2806603422177432e-07f };
        static readonly float[] s_ChestFrontBackSlopes = { 0.0004481064388528466f, -6.936825229786336e-06f, -0.0008731394773349166f, -0.0017071131151169538f, -0.002511166036128998f, -0.002138640498742461f, -0.001708025811240077f, -0.0016982702072709799f, -0.0012035078834742308f, -0.0008913644123822451f, -0.0006038476130925119f, -0.000344097992638126f, -0.0004753363609779626f, -2.866698196157813e-05f, -0.0010212052147835493f, -0.0017205660697072744f, 0.00016126589616760612f, 0.001749232760630548f, 0.0013352753594517708f, 0.0003440981963649392f, 0.0006038474966771901f, 0.0008913645287975669f, 0.0012035076506435871f, 0.0016982685774564743f, 0.001708025811240077f, 0.0020457059144973755f, 0.002308695577085018f, 0.0015046901535242796f, 0.0006779208779335022f, 0.00030229496769607067f, 0.00034727065940387547f };
        static readonly float[] s_UpperLegFrontBack = { 0.3628062307834625f, 0.37809133529663086f, 0.4210868179798126f, 0.48747169971466064f, 0.5729118585586548f, 0.6730964183807373f, 0.7838418483734131f, 0.9007697105407715f, 1.0197287797927856f, 1.1366139650344849f, 1.2471354007720947f, 1.3471297025680542f, 1.4324530363082886f, 1.4986755847930908f, 1.541627049446106f, 1.5568733215332031f, 1.5416191816329956f, 1.4986720085144043f, 1.4324530363082886f, 1.347129464149475f, 1.2471354007720947f, 1.1366138458251953f, 1.0197288990020752f, 0.9007699489593506f, 0.7838417291641235f, 0.6730963587760925f, 0.5729125738143921f, 0.4874710142612457f, 0.4210888147354126f, 0.3780924081802368f, 0.3628062307834625f };
        static readonly float[] s_UpperLegFrontBackSlopes = { 0.45855310559272766f, 0.8742087483406067f, 1.6407053470611572f, 2.2773756980895996f, 2.7843708992004395f, 3.163949489593506f, 3.4150989055633545f, 3.538304328918457f, 3.5376639366149902f, 3.411099433898926f, 3.157736301422119f, 2.779763698577881f, 2.2731871604919434f, 1.6376103162765503f, 0.8729663491249084f, -0.0001175999641418457f, -0.8730189800262451f, -1.6374926567077637f, -2.2731380462646484f, -2.7797648906707764f, -3.1577343940734863f, -3.411097764968872f, -3.53765869140625f, -3.5383048057556152f, -3.4151039123535156f, -3.163937568664551f, -2.7843801975250244f, -2.2773568630218506f, -1.640678882598877f, -0.8742395639419556f, -0.4585857689380646f };
        static readonly float[] s_BodyRotationW = { 1.0f, 0.9999991655349731f, 0.9999892115592957f, 0.9999502897262573f, 0.9998598098754883f, 0.9996971487998962f, 0.9994482398033142f, 0.9991095066070557f, 0.9986880421638489f, 0.9982019066810608f, 0.9976797699928284f, 0.9971574544906616f, 0.9966764450073242f, 0.9962815642356873f, 0.9960150718688965f, 0.9959588646888733f, 0.9960159063339233f, 0.9962819218635559f, 0.9966764450073242f, 0.9971574544906616f, 0.9976798295974731f, 0.9982019066810608f, 0.9986879825592041f, 0.9991095662117004f, 0.9994481205940247f, 0.9996971487998962f, 0.9998599886894226f, 0.9999503493309021f, 0.9999892115592957f, 0.9999991655349731f, 1.0f };
        static readonly float[] s_BodyRotationWSlopes = { -2.503394898667466e-05f, -0.00016182659601327032f, -0.0007331370143219829f, -0.0019410252571105957f, -0.003797114361077547f, -0.006173550151288509f, -0.008814631961286068f, -0.011402965523302555f, -0.01361399982124567f, -0.015124084427952766f, -0.015666784718632698f, -0.015049869194626808f, -0.01313834823668003f, -0.00992059800773859f, -0.004840494599193335f, 1.2515462003648281e-05f, 0.004845853894948959f, 0.009908081963658333f, 0.013132989406585693f, 0.015050770714879036f, 0.01566678285598755f, 0.015122294425964355f, 0.013614894822239876f, 0.011402061209082603f, 0.008813737891614437f, 0.0061780228279531f, 0.003798007033765316f, 0.001938343863002956f, 0.0007322427118197083f, 0.00016182677063625306f, 2.503397445252631e-05f };

        static (float[] Values, float[] Slopes) Curve(string name) => name switch
        {
            "RootT.y" => (s_BodyHeight, s_BodyHeightSlopes),
            "Chest Front-Back" => (s_ChestFrontBack, s_ChestFrontBackSlopes),
            "Left Upper Leg Front-Back" => (s_UpperLegFrontBack, s_UpperLegFrontBackSlopes),
            "RootQ.w" => (s_BodyRotationW, s_BodyRotationWSlopes),
            _ => throw new ArgumentException(name),
        };

        /// <summary>The keys the Editor keeps of those curves with a Position Error of 0.5 and
        /// of 5 (the same clip imported with Keyframe Reduction): the rule gives the same keys.
        /// The body's height at 5 is the two end keys with their own slopes: its ends are far
        /// more than the error apart, so it is not a constant curve, although a segment with
        /// zero slopes between them would stay within the error. The body rotation's w varies
        /// within the error of its first key and becomes two keys with zero slopes.</summary>
        [Theory]
        [InlineData("RootT.y", 0.5f, false, new[] { 0, 12, 25, 30 })]
        [InlineData("RootT.y", 5f, false, new[] { 0, 30 })]
        [InlineData("Chest Front-Back", 0.5f, false, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 9, 11, 12, 13, 14, 15, 17, 18, 19, 22, 23, 24, 25, 26, 28, 29, 30 })]
        [InlineData("Chest Front-Back", 5f, false, new[] { 0, 1, 2, 4, 5, 6, 11, 13, 14, 16, 18, 21, 24, 25, 26, 28, 29, 30 })]
        [InlineData("Left Upper Leg Front-Back", 0.5f, false, new[] { 0, 1, 15, 29, 30 })]
        [InlineData("Left Upper Leg Front-Back", 5f, false, new[] { 0, 10, 22, 30 })]
        [InlineData("RootQ.w", 0.5f, true, new[] { 0, 30 })]
        [InlineData("RootQ.w", 5f, true, new[] { 0, 30 })]
        public void TheEditorsCurvesReduceToTheEditorsKeys(string curve, float percent, bool flat, int[] kept)
        {
            var (values, slopes) = Curve(curve);
            var reduced = Reduce(values, slopes, percent / 100f);
            Assert.Equal(kept, reduced.Kept);
            Assert.Equal(flat, reduced.Flat);
        }

        /// <summary>The Rotation and Scale Errors play no part: there is one error, and a larger
        /// one keeps fewer keys. Control: the leg muscle at the two errors.</summary>
        [Fact]
        public void ALargerErrorKeepsFewerKeys()
        {
            var (values, slopes) = Curve("Left Upper Leg Front-Back");
            Assert.True(Reduce(values, slopes, 0.05f).Kept.Length < Reduce(values, slopes, 0.005f).Kept.Length);
            Assert.Equal(values.Length, Reduce(values, slopes, 0f).Kept.Length);
        }

        /// <summary>A straight run keeps its ends with their slopes; a corner in it is kept.</summary>
        [Fact]
        public void AStraightRunKeepsItsEndsAndACorner()
        {
            var times = Times(21);
            var line = new float[21];
            for (int i = 0; i < line.Length; i++) line[i] = 1f + 0.5f * times[i];
            var straight = Reduce(line, Slopes(times, line), 0.005f);
            Assert.Equal(new[] { 0, 20 }, straight.Kept);
            Assert.False(straight.Flat);

            var bent = (float[])line.Clone();
            for (int i = 11; i < bent.Length; i++) bent[i] = bent[10] - 2f * (times[i] - times[10]);
            Assert.Contains(10, Reduce(bent, Slopes(times, bent), 0.005f).Kept);
        }

        /// <summary>A reduced segment spans fewer than fifty frames.</summary>
        [Fact]
        public void ASegmentSpansFewerThanFiftyFrames()
        {
            var times = Times(121);
            var line = new float[121];
            for (int i = 0; i < line.Length; i++) line[i] = 1f + times[i];
            var kept = Reduce(line, Slopes(times, line), 0.005f).Kept;
            Assert.True(kept.Length > 2);
            for (int i = 1; i < kept.Length; i++) Assert.True(kept[i] - kept[i - 1] < 50, $"{kept[i - 1]}..{kept[i]}");
        }

        /// <summary>Values around zero: a curve of exact zeros is constant; float noise above the
        /// zero tolerance is compared like any value, so its keys depend on its last bits.</summary>
        [Fact]
        public void ZerosAreConstantAndNoiseIsNot()
        {
            var zeros = new float[21];
            var constant = Reduce(zeros, Slopes(Times(21), zeros), 0.005f);
            Assert.Equal(new[] { 0, 20 }, constant.Kept);
            Assert.True(constant.Flat);

            var noise = new float[21];
            for (int i = 0; i < noise.Length; i++) noise[i] = i % 2 == 0 ? 7e-8f : -3e-8f;
            Assert.True(Reduce(noise, Slopes(Times(21), noise), 0.005f).Kept.Length > 2);
        }

        /// <summary>Curves of one or two keys stay as they are.</summary>
        [Fact]
        public void ShortCurvesKeepTheirKeys()
        {
            var two = new[] { 1f, 2f };
            Assert.Equal(new[] { 0, 1 }, Reduce(two, new[] { 30f, 30f }, 0.5f).Kept);
            Assert.Equal(new[] { 0 }, Reduce(new[] { 1f }, new[] { 0f }, 0.5f).Kept);
        }
    }
}
