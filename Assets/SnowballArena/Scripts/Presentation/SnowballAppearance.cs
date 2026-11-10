using UnityEngine;

namespace SnowballArena
{
    public sealed class SnowballAppearance : MonoBehaviour
    {
        public MeshRenderer snowRenderer;
        public MeshRenderer outlineRenderer;
        private Material snowMaterial, packedMaterial, outlineMaterial;
        private static readonly Color[] Palette =
        {
            new Color(1.00f,0.12f,0.19f), new Color(1.00f,0.78f,0.08f),
            new Color(0.45f,0.94f,0.10f), new Color(0.06f,0.66f,1.00f),
            new Color(0.64f,0.24f,1.00f), new Color(1.00f,0.39f,0.06f),
            new Color(1.00f,0.12f,0.65f), new Color(0.03f,0.83f,0.57f),
            new Color(0.24f,0.36f,1.00f), new Color(0.98f,0.56f,0.81f),
            new Color(0.72f,0.94f,0.08f), new Color(0.12f,0.95f,0.93f),
            new Color(0.85f,0.39f,0.98f), new Color(0.95f,0.63f,0.25f),
            new Color(0.38f,0.70f,0.98f), new Color(0.85f,0.95f,1.00f)
        };

        public void SetIdentity(int index)
        {
            if (snowRenderer == null || outlineRenderer == null) return;
            if (snowMaterial == null)
            {
                snowMaterial = new Material(snowRenderer.sharedMaterials[0]);
                packedMaterial = new Material(snowRenderer.sharedMaterials[1]);
                outlineMaterial = new Material(outlineRenderer.sharedMaterial);
                snowRenderer.sharedMaterials = new[] { snowMaterial, packedMaterial };
                outlineRenderer.sharedMaterial = outlineMaterial;
            }
            Color identity = Palette[Mathf.Abs(index) % Palette.Length];
            snowMaterial.SetColor("_BaseColor", index == 0 ? Color.white : Color.Lerp(Color.white, identity, 0.27f));
            packedMaterial.SetColor("_BaseColor", Color.Lerp(Color.white, identity, index == 0 ? 0.13f : 0.55f));
            outlineMaterial.SetColor("_OutlineColor", identity);
        }

        private void OnDestroy()
        {
            Release(snowMaterial); Release(packedMaterial); Release(outlineMaterial);
        }

        private static void Release(Material material)
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
    }
}
