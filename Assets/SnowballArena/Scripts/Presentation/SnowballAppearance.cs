using UnityEngine;
using UnityEngine.Rendering;

namespace SnowballArena
{
    public sealed class SnowballAppearance : MonoBehaviour
    {
        public MeshRenderer snowRenderer;
        public MeshRenderer outlineRenderer;

        [Header("Player identity")]
        [Tooltip("Outline colour reserved for the player, so the ball it controls reads without any text.")]
        public Color playerOutline = new Color(1.00f, 0.62f, 0.12f);
        [Tooltip("Halo diameter under the player, as a multiple of the ball radius.")]
        [Min(1.4f)] public float playerHaloScale = 3.4f;
        [Tooltip("Seconds the halo takes to settle after a round starts.")]
        [Min(0.05f)] public float playerHaloPulseSeconds = 1.5f;

        private Material snowMaterial, packedMaterial, outlineMaterial;
        private Material haloMaterial;
        private MeshRenderer haloRenderer;
        private Transform halo;
        private float haloBornAt = -1f;
        private float ballRadius = 0.85f;
        private bool isPlayer;

        // Cold family only: warm hues are reserved for the player, so a bot can never be
        // mistaken for the ball the player is driving.
        private static readonly Color[] Palette =
        {
            new Color(0.24f,0.62f,1.00f), new Color(0.30f,0.90f,0.86f),
            new Color(0.52f,0.42f,1.00f), new Color(0.22f,0.80f,0.55f),
            new Color(0.36f,0.52f,0.98f), new Color(0.60f,0.36f,1.00f),
            new Color(0.20f,0.72f,0.92f), new Color(0.44f,0.88f,0.68f),
            new Color(0.28f,0.46f,0.88f), new Color(0.66f,0.52f,0.98f),
            new Color(0.16f,0.86f,0.74f), new Color(0.40f,0.60f,1.00f),
            new Color(0.50f,0.30f,0.92f), new Color(0.26f,0.78f,0.62f),
            new Color(0.34f,0.70f,0.96f), new Color(0.58f,0.62f,1.00f)
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
            isPlayer = index == 0;
            Color identity = isPlayer ? playerOutline : Palette[Mathf.Abs(index) % Palette.Length];
            // A hint of warmth in the ball itself, so even at a glance the player's snow is not
            // the same neutral white as every bot.
            snowMaterial.SetColor("_BaseColor", isPlayer ? Color.Lerp(Color.white, playerOutline, 0.12f) : Color.white);
            // The player keeps a cleaner snow surface so its outline and halo do the talking.
            packedMaterial.SetColor("_BaseColor", Color.Lerp(Color.white, identity, isPlayer ? 0.16f : 0.55f));
            outlineMaterial.SetColor("_OutlineColor", identity);
            if (isPlayer) BuildHalo();
        }

        // A flat additive glow under the ball. It is the cue that survives a crowd, because it is
        // on the ground and the camera looks down at the arena.
        private void BuildHalo()
        {
            if (halo != null) return;
            var template = Resources.Load<Material>("PlayerHalo");
            if (template == null) return;
            var body = GetComponent<SnowballBody>();
            if (body != null) ballRadius = body.Radius;
            haloMaterial = new Material(template);
            haloMaterial.SetColor("_BaseColor", playerOutline);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Player halo";
            var collider = quad.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            quad.transform.SetParent(transform, false);
            haloRenderer = quad.GetComponent<MeshRenderer>();
            haloRenderer.sharedMaterial = haloMaterial;
            haloRenderer.shadowCastingMode = ShadowCastingMode.Off;
            haloRenderer.receiveShadows = false;
            halo = quad.transform;
            haloBornAt = Time.time;
        }

        private void LateUpdate()
        {
            if (halo == null) return;
            // The halo stays flat on the ground and does not roll with the ball.
            Vector3 position = transform.position;
            halo.position = new Vector3(position.x, position.y - ballRadius + 0.03f, position.z);
            halo.rotation = Quaternion.Euler(90f, 0f, 0f);
            // Bloom in from slightly wider and brighter, then settle: a one-off pulse that pulls
            // the eye to the player without any text prompt.
            float t = Mathf.Clamp01((Time.time - haloBornAt) / playerHaloPulseSeconds);
            float radius = ballRadius * playerHaloScale * Mathf.Lerp(1.7f, 1f, t);
            halo.localScale = Vector3.one * (radius * 2f);
            float breathe = 0.9f + 0.1f * Mathf.Sin(Time.time * 2.4f);
            var tint = playerOutline;
            // Alpha blend, not additive: the halo tints the snow instead of brightening it.
            tint.a = Mathf.Lerp(0.95f, 0.62f, t) * breathe;
            haloMaterial.SetColor("_BaseColor", tint);
        }

        private void OnDestroy()
        {
            Release(snowMaterial); Release(packedMaterial); Release(outlineMaterial); Release(haloMaterial);
        }

        private static void Release(Material material)
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
    }
}
