using UnityEngine;
using UnityEngine.Rendering;

namespace SnowballArena
{
    public static class CannonWarningVisual
    {
        // The shader's dark core covers 19% of the ribbon; pale shoulders fade outwards.
        // CoreFraction must stay in sync with the 0.19 constant in CannonWarning.shader.
        public const float CoreFraction = .19f;
        public const float Width = 0.36f;
        public const float BoundaryInset = 0.18f;
        public const float GroundOffset = 0.035f;

        public static LineRenderer Create(Transform parent, ArenaSurface surface, Vector3 start, Vector3 end, Material material)
        {
            Vector3 direction = (end - start).normalized;
            Vector3 from = start + direction * BoundaryInset;
            Vector3 to = end - direction * BoundaryInset;
            from.y = to.y = surface.GetComponent<Collider>().bounds.max.y + GroundOffset;

            var line = new GameObject("Three-flash ground infrared warning").AddComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            // TransformZ fixes the ribbon to the snow plane instead of facing the
            // camera, which would lift its edges into the cannon or past the rim.
            line.transform.rotation = Quaternion.LookRotation(Vector3.up);
            line.alignment = LineAlignment.TransformZ;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = Width;
            line.textureMode = LineTextureMode.Stretch;
            line.sharedMaterial = material;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            return line;
        }
    }
}
