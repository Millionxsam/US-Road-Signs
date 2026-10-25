using System.Collections.Generic;
using USRoadSigns.Geometry;

namespace USRoadSigns.Route
{
    /// <summary>
    /// Route marker outlines traced from the standard Interstate (M1-1) and US route (M1-4) artwork. Each table is
    /// the right half (x, y pairs, unit box, y down) from the top center to the bottom point; the left half is mirrored.
    /// </summary>
    public static class ShieldOutlines
    {
        private static readonly float[] InterstateHalf = { 0.5000f, 0.0012f, 0.5868f, 0.0198f, 0.6786f, 0.0276f, 0.7571f, 0.0228f, 0.8139f, 0.0133f, 0.8589f, 0.0020f, 0.8684f, 0.0047f, 0.8962f, 0.0426f, 0.9303f, 0.1012f, 0.9613f, 0.1731f, 0.9789f, 0.2299f, 0.9882f, 0.2717f, 0.9975f, 0.3453f, 0.9992f, 0.3838f, 0.9958f, 0.4440f, 0.9882f, 0.5008f, 0.9773f, 0.5493f, 0.9597f, 0.6045f, 0.9370f, 0.6580f, 0.9148f, 0.6998f, 0.8907f, 0.7383f, 0.8580f, 0.7818f, 0.8306f, 0.8135f, 0.7771f, 0.8643f, 0.7371f, 0.8953f, 0.6970f, 0.9215f, 0.6369f, 0.9536f, 0.5952f, 0.9711f, 0.5467f, 0.9871f, 0.5000f, 0.9977f };
        private static readonly float[] InterstateInnerHalf = { 0.5000f, 0.0200f, 0.5835f, 0.0382f, 0.6636f, 0.0460f, 0.7137f, 0.0460f, 0.7521f, 0.0426f, 0.7972f, 0.0367f, 0.8556f, 0.0237f, 0.8628f, 0.0254f, 0.8910f, 0.0661f, 0.9161f, 0.1112f, 0.9446f, 0.1781f, 0.9524f, 0.2015f, 0.9546f, 0.2231f, 0.9619f, 0.2383f, 0.9699f, 0.2768f, 0.9791f, 0.3587f, 0.9791f, 0.4089f, 0.9728f, 0.4724f, 0.9619f, 0.5293f, 0.9491f, 0.5744f, 0.9120f, 0.6630f, 0.8894f, 0.7032f, 0.8587f, 0.7483f, 0.8155f, 0.8002f, 0.7871f, 0.8286f, 0.7354f, 0.8719f, 0.6953f, 0.8994f, 0.6536f, 0.9235f, 0.5968f, 0.9497f, 0.5351f, 0.9704f, 0.5000f, 0.9784f };
        private static readonly float[] InterstateWideHalf = { 0.5000f, 0.0016f, 0.6105f, 0.0298f, 0.6785f, 0.0394f, 0.7255f, 0.0417f, 0.7735f, 0.0394f, 0.8265f, 0.0323f, 0.9478f, 0.0033f, 0.9638f, 0.0481f, 0.9822f, 0.1231f, 0.9937f, 0.1981f, 0.9995f, 0.2819f, 0.9985f, 0.3369f, 0.9918f, 0.4106f, 0.9814f, 0.4719f, 0.9658f, 0.5344f, 0.9463f, 0.5931f, 0.9209f, 0.6531f, 0.8944f, 0.7044f, 0.8606f, 0.7581f, 0.8185f, 0.8131f, 0.7855f, 0.8487f, 0.7355f, 0.8935f, 0.6935f, 0.9241f, 0.6495f, 0.9499f, 0.6015f, 0.9719f, 0.5505f, 0.9887f, 0.5000f, 0.9987f };
        private static readonly float[] InterstateWideInnerHalf = { 0.5000f, 0.0267f, 0.6005f, 0.0526f, 0.6905f, 0.0644f, 0.7595f, 0.0644f, 0.8015f, 0.0606f, 0.8615f, 0.0501f, 0.9359f, 0.0315f, 0.9524f, 0.0819f, 0.9678f, 0.1556f, 0.9751f, 0.2056f, 0.9785f, 0.2756f, 0.9785f, 0.3144f, 0.9729f, 0.3881f, 0.9643f, 0.4469f, 0.9458f, 0.5269f, 0.9284f, 0.5806f, 0.9079f, 0.6306f, 0.8804f, 0.6856f, 0.8492f, 0.7344f, 0.8136f, 0.7822f, 0.7825f, 0.8174f, 0.7405f, 0.8577f, 0.6955f, 0.8927f, 0.6455f, 0.9235f, 0.6025f, 0.9442f, 0.5535f, 0.9615f, 0.5000f, 0.9730f };
        private static readonly float[] UsHalf = { 0.5000f, 0.0225f, 0.5089f, 0.0255f, 0.5403f, 0.0459f, 0.5671f, 0.0578f, 0.5984f, 0.0672f, 0.6432f, 0.0727f, 0.6834f, 0.0690f, 0.7081f, 0.0633f, 0.7438f, 0.0498f, 0.7841f, 0.0245f, 0.7953f, 0.0235f, 0.9740f, 0.2002f, 0.9775f, 0.2069f, 0.9773f, 0.2136f, 0.9507f, 0.2629f, 0.9343f, 0.3121f, 0.9251f, 0.3725f, 0.9251f, 0.3971f, 0.9345f, 0.4597f, 0.9700f, 0.5559f, 0.9810f, 0.6141f, 0.9810f, 0.6521f, 0.9755f, 0.6902f, 0.9661f, 0.7237f, 0.9536f, 0.7528f, 0.9213f, 0.8020f, 0.8781f, 0.8438f, 0.8557f, 0.8592f, 0.8244f, 0.8756f, 0.7774f, 0.8913f, 0.7327f, 0.8982f, 0.6678f, 0.8982f, 0.6365f, 0.9014f, 0.5940f, 0.9131f, 0.5470f, 0.9382f, 0.5089f, 0.9720f, 0.5000f, 0.9760f };
        private static readonly float[] UsWideHalf = { 0.5000f, 0.0210f, 0.5721f, 0.0587f, 0.6062f, 0.0696f, 0.6483f, 0.0762f, 0.6864f, 0.0762f, 0.7485f, 0.0635f, 0.7866f, 0.0474f, 0.8267f, 0.0226f, 0.8347f, 0.0221f, 0.8427f, 0.0288f, 0.9770f, 0.1963f, 0.9814f, 0.2037f, 0.9821f, 0.2137f, 0.9603f, 0.2662f, 0.9493f, 0.3063f, 0.9440f, 0.3362f, 0.9409f, 0.3962f, 0.9491f, 0.4637f, 0.9787f, 0.5637f, 0.9861f, 0.6288f, 0.9803f, 0.6937f, 0.9732f, 0.7238f, 0.9607f, 0.7588f, 0.9471f, 0.7863f, 0.9289f, 0.8137f, 0.9048f, 0.8424f, 0.8848f, 0.8599f, 0.8427f, 0.8846f, 0.8006f, 0.8971f, 0.6343f, 0.8988f, 0.6002f, 0.9043f, 0.5721f, 0.9154f, 0.5521f, 0.9274f, 0.5261f, 0.9496f, 0.5060f, 0.9732f, 0.5000f, 0.9757f };

        /// <summary>Interstate / Business shield (outer die-cut edge, or the inner colored field) in a width x height box.</summary>
        public static List<Vec2> Interstate(float width, float height, bool inner = false)
        {
            bool wide = width > height * 1.1f;
            float[] half = wide ? (inner ? InterstateWideInnerHalf : InterstateWideHalf) : (inner ? InterstateInnerHalf : InterstateHalf);
            return Build(half, width, height);
        }

        /// <summary>The white US shield on the black M1-4 plate; width x height is the whole plate.</summary>
        public static List<Vec2> Us(float width, float height) => Build(width > height * 1.1f ? UsWideHalf : UsHalf, width, height);

        private static List<Vec2> Build(float[] half, float width, float height)
        {
            int n = half.Length / 2;
            var pts = new List<Vec2>(n * 2);
            Vec2 P(int i, bool mirror) => new Vec2(((mirror ? 1f - half[i * 2] : half[i * 2]) - 0.5f) * width, (0.5f - half[i * 2 + 1]) * height);
            for (int i = 0; i < n; i++)
                pts.Add(P(i, false));
            for (int i = n - 2; i > 0; i--)
                pts.Add(P(i, true));
            return MeshPart.ToCounterClockwise(pts);
        }
    }
}
