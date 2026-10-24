namespace UnityEngine
{
    /// <summary>A sphere (CullingGroup bounds; Cinemachine's target groups).</summary>
    public partial struct BoundingSphere
    {
        public Vector3 position;
        public float radius;

        public BoundingSphere(Vector3 pos, float rad)
        {
            position = pos;
            radius = rad;
        }

        public BoundingSphere(Vector4 packedSphere)
        {
            position = new Vector3(packedSphere.x, packedSphere.y, packedSphere.z);
            radius = packedSphere.w;
        }
    }
}
