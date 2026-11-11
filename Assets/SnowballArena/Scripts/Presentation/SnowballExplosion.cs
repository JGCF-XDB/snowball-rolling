using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SnowballArena
{
    public enum SnowballExplosionStyle { SnowPowder, IceShatter, AvalancheBlast }

    /// <summary>
    /// Visual-only cannonball kill effect. A snowball that is hit is replaced by a flash,
    /// an expanding snow shell, powder puffs, lit snow/ice chunks, a ground shockwave ring
    /// and short-lived glints, all scaled to the victim's current radius.
    ///
    /// Everything is driven by an explicit clock; Update advances it during gameplay, so the
    /// Unity Editor preview renderer can step it deterministically in batch mode and render
    /// exactly the effect that plays in the game. No colliders, no damage, no gameplay rules.
    /// </summary>
    public sealed class SnowballExplosion : MonoBehaviour
    {
        public const string DustMaterialName = "SnowExplosionDust";
        public const string GlowMaterialName = "SnowExplosionGlow";
        public const string SnowMaterialName = "SnowExplosionSnow";
        public const string IceMaterialName = "SnowExplosionIce";

        /// <summary>Live effects, so a round reset can remove every visual at once.</summary>
        public static readonly List<SnowballExplosion> Active = new List<SnowballExplosion>();
        /// <summary>Preview override for the billboard camera; gameplay falls back to Camera.main.</summary>
        public static Camera BillboardCamera;

        public SnowballExplosionStyle Style { get; private set; }
        public float Radius { get; private set; }
        public float Age => age;
        public float Lifetime => profile.lifetime;
        public float InfluenceRadius => profile.ringOuter * Radius;
        public int PuffCount => puffs == null ? 0 : puffs.Length;
        public int ChunkCount => chunks == null ? 0 : chunks.Length;
        public int GlintCount => glints == null ? 0 : glints.Length;
        /// <summary>Deterministic-state fingerprints used by the editor validation.</summary>
        public float PuffChecksum
        {
            get
            {
                if (puffs == null) return 0f;
                float sum = 0f;
                for (int i = 0; i < puffs.Length; i++)
                    sum += puffs[i].position.x * 1.7f + puffs[i].position.y * 2.3f + puffs[i].position.z * 3.1f + puffs[i].size * 5.1f;
                return sum;
            }
        }
        public float ChunkChecksum
        {
            get
            {
                if (chunks == null) return 0f;
                float sum = 0f;
                for (int i = 0; i < chunks.Length; i++)
                    if (chunks[i].transform != null)
                        sum += chunks[i].transform.localPosition.x * 1.3f + chunks[i].transform.localPosition.y * 2.9f
                            + chunks[i].transform.localPosition.z * 4.7f;
                return sum;
            }
        }
        /// <summary>Set by the preview tool: Advance() is called by hand and the effect never self-updates.</summary>
        public bool Manual;

        private struct Profile
        {
            public float lifetime;
            public int puffs, chunks, glints, iceChunks;
            public float puffSpeedMin, puffSpeedMax, puffGravity, puffDrag;
            public float puffSizeMin, puffSizeMax, puffGrowthMin, puffGrowthMax;
            public float puffLifeMin, puffLifeMax, puffAlpha, puffUpBias;
            public float directionShare, directionSpread;
            public float ringOuter, ringLife, ringAlpha, ringStart;
            public bool doubleRing;
            public float ringStretch, ringStretchAngle;
            public float flashLife, flashAlpha;
            public float shellScale, shellLife, shellAlpha;
            public float chunkSpeedMin, chunkSpeedMax, chunkGravity, chunkLifeMin, chunkLifeMax;
            public float chunkSizeMin, chunkSizeMax;
            public float glintSpeedMin, glintSpeedMax, glintLifeMin, glintLifeMax;
            public float lightIntensity;
        }

        private struct Puff
        {
            public Vector3 position, velocity;
            public float size, growth, life, age, drag, gravity, spin, spinSpeed, delay;
            public Color tint;
        }

        private struct Glint
        {
            public Vector3 position, velocity;
            public float size, life, age, spin, spinSpeed, phase;
            public Color tint;
        }

        private sealed class Chunk
        {
            public Transform transform;
            public Vector3 velocity, angularVelocity, baseScale;
            public float life, age, gravity, restY;
            public bool settled, spray;
        }

        private sealed class ShockRing
        {
            public Transform transform;
            public Renderer renderer;
            public Material material;
            public float startRadius, endRadius, life, delay, alpha, age;
            public Vector3 stretch;
            public Color tint;

            public void Advance(float dt)
            {
                age += dt;
                float local = age - delay;
                if (local < 0f) { transform.gameObject.SetActive(false); return; }
                transform.gameObject.SetActive(true);
                float t = Mathf.Clamp01(local / life);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                float radius = Mathf.Lerp(startRadius, endRadius, eased);
                transform.localScale = new Vector3(radius * stretch.x, radius, radius * stretch.z);
                float fade = Mathf.Pow(1f - t, 0.90f) * alpha;
                material.SetColor("_Color", new Color(tint.r, tint.g, tint.b, fade));
                if (t >= 1f) transform.gameObject.SetActive(false);
            }

            public void Dispose()
            {
                if (transform != null) DestroyNow(transform.gameObject);
                Release(material);
            }
        }

        private sealed class Flash
        {
            public Transform transform;
            public Renderer renderer;
            public Material material;
            public float startSize, endSize, life, age, alpha;
            public Vector3 offset;
            public Color tint;

            public void Advance(float dt)
            {
                age += dt;
                if (age >= life) { transform.gameObject.SetActive(false); return; }
                transform.gameObject.SetActive(true);
                var camera = BillboardCamera != null ? BillboardCamera : Camera.main;
                if (camera != null) transform.rotation = camera.transform.rotation;
                float t = Mathf.Clamp01(age / life);
                float eased = 1f - Mathf.Pow(1f - Mathf.Min(1f, t * 1.6f), 3f);
                float size = Mathf.Lerp(startSize, endSize, eased);
                transform.localScale = new Vector3(size, size, size);
                material.SetColor("_Color", new Color(tint.r, tint.g, tint.b, Mathf.Pow(1f - t, 1.4f) * alpha));
            }

            public void Dispose()
            {
                if (transform != null) DestroyNow(transform.gameObject);
                Release(material);
            }
        }

        private sealed class BillboardBatch
        {
            public readonly Mesh mesh;
            private readonly MeshFilter filter;
            private readonly MeshRenderer renderer;
            private readonly int count;
            private readonly Vector3[] centers;
            private readonly float[] sizes;
            private readonly float[] spins;
            private readonly Color[] colors;
            private readonly Vector3[] vertices;
            private readonly Color[] vertexColors;
            private readonly int[] triangles;

            public BillboardBatch(Transform parent, string name, Material material, int count)
            {
                this.count = count;
                centers = new Vector3[count];
                sizes = new float[count];
                spins = new float[count];
                colors = new Color[count];
                vertices = new Vector3[count * 4];
                vertexColors = new Color[count * 4];
                triangles = new int[count * 6];
                for (int i = 0; i < count; i++)
                {
                    int v = i * 4;
                    triangles[i * 6 + 0] = v;
                    triangles[i * 6 + 1] = v + 1;
                    triangles[i * 6 + 2] = v + 2;
                    triangles[i * 6 + 3] = v;
                    triangles[i * 6 + 4] = v + 2;
                    triangles[i * 6 + 5] = v + 3;
                }
                var host = new GameObject(name);
                host.transform.SetParent(parent, false);
                host.layer = 0;
                mesh = new Mesh { name = name + " quads", hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
                var uv = new Vector2[count * 4];
                for (int i = 0; i < count; i++)
                {
                    uv[i * 4 + 0] = new Vector2(0f, 0f);
                    uv[i * 4 + 1] = new Vector2(1f, 0f);
                    uv[i * 4 + 2] = new Vector2(1f, 1f);
                    uv[i * 4 + 3] = new Vector2(0f, 1f);
                }
                mesh.vertices = vertices;
                mesh.uv = uv;
                mesh.colors = vertexColors;
                mesh.triangles = triangles;
                filter = host.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                renderer = host.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            public void Set(int index, Vector3 center, float size, Color color, float spin)
            {
                centers[index] = center; sizes[index] = size; colors[index] = color; spins[index] = spin;
            }

            public void Apply()
            {
                var camera = BillboardCamera != null ? BillboardCamera : Camera.main;
                Vector3 right = camera != null ? camera.transform.right : Vector3.right;
                Vector3 up = camera != null ? camera.transform.up : Vector3.up;
                for (int i = 0; i < count; i++)
                {
                    float spin = spins[i];
                    float c = Mathf.Cos(spin), s = Mathf.Sin(spin);
                    Vector3 r = (right * c + up * s) * sizes[i];
                    Vector3 u = (up * c - right * s) * sizes[i];
                    Vector3 p = centers[i];
                    int v = i * 4;
                    vertices[v + 0] = p - r - u;
                    vertices[v + 1] = p + r - u;
                    vertices[v + 2] = p + r + u;
                    vertices[v + 3] = p - r + u;
                    Color color = colors[i];
                    vertexColors[v + 0] = color;
                    vertexColors[v + 1] = color;
                    vertexColors[v + 2] = color;
                    vertexColors[v + 3] = color;
                }
                mesh.vertices = vertices;
                mesh.colors = vertexColors;
                mesh.RecalculateBounds();
            }

            public void Dispose()
            {
                if (filter != null) DestroyNow(filter.gameObject);
                if (mesh != null) DestroyNow(mesh);
            }
        }

        [Header("Preview bookkeeping")]
        [Tooltip("Local Y of the snow platform surface under the impact; used by the ground shockwave.")]
        public float GroundOffset;

        private Profile profile;
        private float age;
        private bool built;
        private System.Random rng;
        private Vector3 shot;
        private float groundLocalY;
        private Color identity = Color.white;

        private Material dustMaterial, glowMaterial;
        private Material snowChunkMaterial, iceChunkMaterial;
        private BillboardBatch puffBatch, glintBatch;
        private Puff[] puffs;
        private Glint[] glints;
        private Chunk[] chunks;
        private ShockRing primaryRing, secondaryRing;
        private Flash flashCore, flashHalo;
        private Transform shell;
        private Renderer shellRenderer;
        private Material shellMaterial;
        private float shellScaleFrom, shellScaleTo, shellLife, shellAlpha;
        private Light flashLight;
        private float lightPeak;

        // ------------------------------------------------------------------ creation

        public static SnowballExplosion Play(SnowballBody victim, Vector3 shotDirection, SnowballExplosionStyle style, Transform parent)
        {
            if (victim == null) return null;
            Color identity = Color.white;
            var appearance = victim.GetComponent<SnowballAppearance>();
            if (appearance != null && appearance.outlineRenderer != null && appearance.outlineRenderer.sharedMaterial != null)
            {
                Material outline = appearance.outlineRenderer.sharedMaterial;
                if (outline.HasProperty("_OutlineColor")) identity = outline.GetColor("_OutlineColor");
            }
            var body = victim.RigidBody;
            Vector3 center = body != null ? body.position : victim.transform.position;
            return Play(center, victim.Radius, shotDirection, style, identity, parent, false, 7411);
        }

        public static SnowballExplosion Play(Vector3 center, float radius, Vector3 shotDirection,
            SnowballExplosionStyle style, Color identity, Transform parent, bool manual = false, int seed = 7411)
        {
            if (radius <= 0.01f) return null;
            var dust = Resources.Load<Material>(DustMaterialName);
            var glow = Resources.Load<Material>(GlowMaterialName);
            // A stripped or not-yet-built project must never break the match: skip the visual.
            if (dust == null || glow == null) return null;
            var host = new GameObject("Snowball explosion - " + style);
            if (parent != null) host.transform.SetParent(parent, false);
            host.transform.position = center;
            host.transform.rotation = Quaternion.identity;
            var effect = host.AddComponent<SnowballExplosion>();
            effect.Manual = manual;
            effect.Build(center, radius, shotDirection, style, identity, dust, glow, seed);
            Active.Add(effect);
            return effect;
        }

        private void Build(Vector3 center, float radius, Vector3 shotDirection,
            SnowballExplosionStyle style, Color identity, Material dustTemplate, Material glowTemplate, int seed)
        {
            built = false;
            Style = style; Radius = radius;
            shot = shotDirection.sqrMagnitude > 0.0001f ? shotDirection.normalized : Vector3.right;
            this.identity = identity;
            rng = new System.Random(seed + (int)style * 7919);
            profile = Profiles[(int)style];

            dustMaterial = new Material(dustTemplate) { name = "Snow explosion dust (runtime)" };
            dustMaterial.renderQueue = 3000;
            glowMaterial = new Material(glowTemplate) { name = "Snow explosion glow (runtime)" };
            glowMaterial.renderQueue = 3050;
            snowChunkMaterial = ChunkMaterial(SnowMaterialName, "Universal Render Pipeline/Lit", new Color(0.90f, 0.95f, 1f), 0.18f);
            iceChunkMaterial = ChunkMaterial(IceMaterialName, "Universal Render Pipeline/Lit", new Color(0.42f, 0.78f, 0.92f), 0.45f);
            if (snowChunkMaterial != null && snowChunkMaterial.HasProperty("_BaseColor"))
                snowChunkMaterial.SetColor("_BaseColor", new Color(0.90f, 0.95f, 1f));
            if (iceChunkMaterial != null && iceChunkMaterial.HasProperty("_BaseColor"))
                iceChunkMaterial.SetColor("_BaseColor", Color.Lerp(new Color(0.34f, 0.72f, 0.92f), identity, 0.35f));

            groundLocalY = FindGroundLocalY(center, radius);
            GroundOffset = groundLocalY;

            BuildFlash();
            BuildShell();
            BuildRings();
            BuildPuffs();
            BuildGlints();
            BuildChunks();
            BuildLight();

            built = true;
            Advance(0f);
        }

        private static readonly Profile[] Profiles =
        {
            new Profile
            {
                lifetime = 0.90f,
                puffs = 88, chunks = 34, glints = 14, iceChunks = 0,
                puffSpeedMin = 2.5f, puffSpeedMax = 6.0f, puffGravity = 2.4f, puffDrag = 1.4f,
                puffSizeMin = 0.70f, puffSizeMax = 1.60f, puffGrowthMin = 2.8f, puffGrowthMax = 6.0f,
                puffLifeMin = 0.35f, puffLifeMax = 0.70f, puffAlpha = 0.95f, puffUpBias = 0.42f,
                directionShare = 0.14f, directionSpread = 1.1f,
                ringOuter = 5.4f, ringLife = 0.58f, ringAlpha = 1.0f, ringStart = 0.5f,
                doubleRing = true, ringStretch = 1f, ringStretchAngle = 0f,
                flashLife = 0.14f, flashAlpha = 0.72f,
                shellScale = 1.60f, shellLife = 0.18f, shellAlpha = 0.85f,
                chunkSpeedMin = 3.8f, chunkSpeedMax = 8.6f, chunkGravity = 7f,
                chunkLifeMin = 0.55f, chunkLifeMax = 0.80f, chunkSizeMin = 0.08f, chunkSizeMax = 0.24f,
                glintSpeedMin = 4.5f, glintSpeedMax = 8.5f, glintLifeMin = 0.25f, glintLifeMax = 0.45f,
                lightIntensity = 3.6f
            },
            new Profile
            {
                lifetime = 1.00f,
                puffs = 84, chunks = 56, glints = 40, iceChunks = 14,
                puffSpeedMin = 3.7f, puffSpeedMax = 8.2f, puffGravity = 4.8f, puffDrag = 1.25f,
                puffSizeMin = 0.55f, puffSizeMax = 1.35f, puffGrowthMin = 2.6f, puffGrowthMax = 5.8f,
                puffLifeMin = 0.32f, puffLifeMax = 0.62f, puffAlpha = 0.92f, puffUpBias = 0.52f,
                directionShare = 0.30f, directionSpread = 0.9f,
                ringOuter = 4.8f, ringLife = 0.50f, ringAlpha = 1.0f, ringStart = 0.5f,
                doubleRing = false, ringStretch = 1f, ringStretchAngle = 0f,
                flashLife = 0.13f, flashAlpha = 0.66f,
                shellScale = 1.50f, shellLife = 0.16f, shellAlpha = 0.90f,
                chunkSpeedMin = 4.5f, chunkSpeedMax = 11.5f, chunkGravity = 9.5f,
                chunkLifeMin = 0.60f, chunkLifeMax = 0.90f, chunkSizeMin = 0.08f, chunkSizeMax = 0.25f,
                glintSpeedMin = 6f, glintSpeedMax = 12f, glintLifeMin = 0.24f, glintLifeMax = 0.48f,
                lightIntensity = 4.8f
            },
            new Profile
            {
                lifetime = 1.05f,
                puffs = 100, chunks = 44, glints = 20, iceChunks = 8,
                puffSpeedMin = 4.3f, puffSpeedMax = 9.6f, puffGravity = 4.2f, puffDrag = 1.15f,
                puffSizeMin = 0.85f, puffSizeMax = 1.90f, puffGrowthMin = 3.0f, puffGrowthMax = 6.4f,
                puffLifeMin = 0.40f, puffLifeMax = 0.80f, puffAlpha = 0.95f, puffUpBias = 0.34f,
                directionShare = 0.62f, directionSpread = 0.55f,
                ringOuter = 6.5f, ringLife = 0.62f, ringAlpha = 1.0f, ringStart = 0.58f,
                doubleRing = true, ringStretch = 1.35f, ringStretchAngle = 1f,
                flashLife = 0.16f, flashAlpha = 0.78f,
                shellScale = 1.65f, shellLife = 0.18f, shellAlpha = 0.92f,
                chunkSpeedMin = 5.0f, chunkSpeedMax = 12.0f, chunkGravity = 8.5f,
                chunkLifeMin = 0.65f, chunkLifeMax = 0.95f, chunkSizeMin = 0.09f, chunkSizeMax = 0.29f,
                glintSpeedMin = 5.5f, glintSpeedMax = 10f, glintLifeMin = 0.26f, glintLifeMax = 0.5f,
                lightIntensity = 5.6f
            }
        };

        private void BuildFlash()
        {
            flashCore = new Flash
            {
                startSize = Radius * 0.55f,
                endSize = Radius * 1.35f,
                life = profile.flashLife * 0.55f,
                alpha = 0.95f,
                offset = Vector3.up * Radius * 0.1f,
                // A short identity-tinted flash answers "which ball went down?" before the
                // neutral snow cloud takes over.
                tint = Color.Lerp(Color.white, identity, 0.62f)
            };
            CreateQuadElement(flashCore, "Explosion identity flash", dustMaterial);
            // A small powder veil covers the moment the snowball silhouette disappears; the
            // many puff quads build the actual cloud shape so this stays without a hard rim.
            flashHalo = new Flash
            {
                startSize = Radius * 0.9f,
                endSize = Radius * 1.45f,
                life = profile.flashLife * 0.9f,
                alpha = profile.flashAlpha * 0.8f,
                offset = Vector3.up * Radius * 0.08f,
                tint = new Color(0.42f, 0.60f, 0.92f)
            };
            CreateQuadElement(flashHalo, "Explosion initial dust veil", dustMaterial);
        }

        private void BuildShell()
        {
            var host = new GameObject("Explosion snow shell");
            host.transform.SetParent(transform, false);
            host.layer = 0;
            var mesh = IcoMesh(2, 0f, 1);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            shellRenderer = host.AddComponent<MeshRenderer>();
            shellMaterial = new Material(glowMaterial) { name = "Explosion shell (runtime)" };
            shellRenderer.sharedMaterial = shellMaterial;
            shellRenderer.shadowCastingMode = ShadowCastingMode.Off;
            shellRenderer.receiveShadows = false;
            shell = host.transform;
            shellScaleFrom = Radius;
            shellScaleTo = Radius * profile.shellScale;
            shellLife = profile.shellLife;
            shellAlpha = profile.shellAlpha;
        }

        private void BuildRings()
        {
            primaryRing = CreateRing("Explosion ground shockwave", profile.ringStart * Radius, profile.ringOuter * Radius,
                profile.ringLife, 0f, profile.ringAlpha, Color.Lerp(new Color(0.20f, 0.42f, 0.90f), identity, 0.32f));
            if (profile.doubleRing)
                secondaryRing = CreateRing("Explosion ground haze", profile.ringStart * Radius * 0.8f,
                    profile.ringOuter * Radius * 1.32f, profile.ringLife * 1.35f, profile.ringLife * 0.18f,
                    profile.ringAlpha * 0.46f, new Color(0.70f, 0.85f, 0.99f));
            if (profile.ringStretchAngle > 0f)
            {
                Vector3 flat = new Vector3(shot.x, 0f, shot.z);
                if (flat.sqrMagnitude > 0.0001f)
                {
                    float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                    primaryRing.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                    if (secondaryRing != null) secondaryRing.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                }
                primaryRing.stretch = new Vector3(profile.ringStretch, 1f, 1f);
                if (secondaryRing != null) secondaryRing.stretch = new Vector3(1f, 1f, Mathf.Lerp(1f, profile.ringStretch, 0.6f));
            }
            else
            {
                primaryRing.stretch = Vector3.one;
                if (secondaryRing != null) secondaryRing.stretch = Vector3.one;
            }
        }

        private void BuildPuffs()
        {
            int count = profile.puffs;
            puffs = new Puff[count];
            puffBatch = new BillboardBatch(transform, "Explosion powder puffs", dustMaterial, count);
            for (int i = 0; i < count; i++)
            {
                Vector3 direction = PuffDirection(i);
                float spread = Mathf.Lerp(0.55f, 0.95f, (float)rng.NextDouble());
                puffs[i] = new Puff
                {
                    position = direction * Radius * spread + new Vector3(0f, groundLocalY * 0.15f, 0f),
                    velocity = direction * (Radius * Mathf.Lerp(profile.puffSpeedMin, profile.puffSpeedMax, (float)rng.NextDouble()))
                        + Vector3.up * (Radius * profile.puffUpBias * (float)rng.NextDouble()),
                    size = Radius * Mathf.Lerp(profile.puffSizeMin, profile.puffSizeMax, (float)rng.NextDouble()),
                    growth = Radius * Mathf.Lerp(profile.puffGrowthMin, profile.puffGrowthMax, (float)rng.NextDouble()),
                    life = Mathf.Lerp(profile.puffLifeMin, profile.puffLifeMax, (float)rng.NextDouble()),
                    age = 0f,
                    drag = profile.puffDrag,
                    gravity = profile.puffGravity,
                    spin = (float)rng.NextDouble() * Mathf.PI * 2f,
                    spinSpeed = ((float)rng.NextDouble() - 0.5f) * 1.6f,
                    delay = (float)rng.NextDouble() * 0.055f,
                    tint = Color.Lerp(new Color(0.52f, 0.68f, 0.92f), identity, i % 7 == 0 ? 0.16f : 0f)
                };
            }
        }

        private Vector3 PuffDirection(int index)
        {
            // Golden-angle sphere so the cloud is even rather than clumpy.
            float t = (index + 0.5f) / profile.puffs;
            float y = 1f - 2f * t;
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float phase = index * 2.399963f;
            var direction = new Vector3(Mathf.Cos(phase) * r, y, Mathf.Sin(phase) * r);
            if (direction.y < -0.25f) direction.y = -0.25f;
            if ((float)rng.NextDouble() < profile.directionShare)
            {
                Vector3 cone = (shot + (float)rng.NextDouble() * profile.directionSpread * new Vector3(
                    (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f)).normalized;
                direction = Vector3.Lerp(direction, cone, 0.72f).normalized;
            }
            return direction.normalized;
        }

        private void BuildGlints()
        {
            int count = profile.glints;
            if (count <= 0) { glints = new Glint[0]; return; }
            glints = new Glint[count];
            glintBatch = new BillboardBatch(transform, "Explosion glints", glowMaterial, count);
            for (int i = 0; i < count; i++)
            {
                Vector3 direction = PuffDirection((i * 3 + 1) % Mathf.Max(1, profile.puffs));
                glints[i] = new Glint
                {
                    position = direction * Radius * 0.7f,
                    velocity = direction * (Radius * Mathf.Lerp(profile.glintSpeedMin, profile.glintSpeedMax, (float)rng.NextDouble()))
                        + Vector3.up * Radius * 1.2f,
                    size = Radius * Mathf.Lerp(0.05f, 0.12f, (float)rng.NextDouble()),
                    life = Mathf.Lerp(profile.glintLifeMin, profile.glintLifeMax, (float)rng.NextDouble()),
                    age = 0f,
                    spin = (float)rng.NextDouble() * Mathf.PI * 2f,
                    spinSpeed = ((float)rng.NextDouble() - 0.5f) * 8f,
                    phase = (float)rng.NextDouble() * Mathf.PI * 2f,
                    tint = i % 3 == 0 ? Color.Lerp(new Color(0.88f, 0.95f, 1f), identity, 0.55f) : new Color(0.88f, 0.95f, 1f)
                };
            }
        }

        private void BuildChunks()
        {
            int count = profile.chunks;
            chunks = new Chunk[count];
            for (int i = 0; i < count; i++)
            {
                bool ice = i < profile.iceChunks;
                var host = new GameObject((ice ? "Ice shard " : "Snow clod ") + i);
                host.transform.SetParent(transform, false);
                host.layer = 0;
                var filter = host.AddComponent<MeshFilter>();
                filter.sharedMesh = ice ? ShardMesh(i) : ClodMesh(i);
                var renderer = host.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = ice ? iceChunkMaterial : snowChunkMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                float size = Radius * Mathf.Lerp(profile.chunkSizeMin, profile.chunkSizeMax, (float)rng.NextDouble());
                if (ice) size = Mathf.Min(size * 1.55f, Radius * 0.26f);
                var baseScale = new Vector3(size * Mathf.Lerp(0.8f, 1.3f, (float)rng.NextDouble()), size,
                    size * Mathf.Lerp(0.8f, 1.3f, (float)rng.NextDouble()));
                // Every third snow clod is stretched along its velocity, so the burst shows a
                // readable radial spray even at the wide gameplay camera distance.
                bool spray = !ice && i % 3 == 0;
                if (spray) baseScale = new Vector3(size * 0.42f, size * 0.42f, size * 2.3f);
                host.transform.localScale = baseScale;
                Vector3 direction = PuffDirection(i + 5);
                if (ice) direction = Vector3.Lerp(direction, shot, 0.45f).normalized;
                host.transform.localRotation = spray ? Quaternion.LookRotation(direction)
                    : new Quaternion((float)rng.NextDouble(), (float)rng.NextDouble(),
                        (float)rng.NextDouble(), (float)rng.NextDouble()).normalized;
                chunks[i] = new Chunk
                {
                    transform = host.transform,
                    baseScale = baseScale,
                    spray = spray,
                    life = Mathf.Lerp(profile.chunkLifeMin, profile.chunkLifeMax, (float)rng.NextDouble()),
                    gravity = profile.chunkGravity,
                    velocity = direction * (Radius * Mathf.Lerp(profile.chunkSpeedMin, profile.chunkSpeedMax, (float)rng.NextDouble()))
                        + shot * Radius * 1.2f + Vector3.up * Radius * 1.4f,
                    angularVelocity = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f,
                        (float)rng.NextDouble() - 0.5f) * 480f,
                    restY = groundLocalY + size * 0.42f
                };
                host.transform.localPosition = direction * Radius * 0.72f;
            }
        }

        private void BuildLight()
        {
            var host = new GameObject("Explosion flash light");
            host.transform.SetParent(transform, false);
            host.transform.localPosition = Vector3.up * Radius * 0.2f;
            flashLight = host.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(0.72f, 0.88f, 1f);
            flashLight.range = Radius * 14f;
            flashLight.intensity = 0f;
            flashLight.shadows = LightShadows.None;
            lightPeak = profile.lightIntensity;
        }

        // ------------------------------------------------------------------ simulation

        private void Update()
        {
            if (Manual) return;
            if (!Advance(Time.deltaTime)) Dispose(this);
        }

        /// <summary>Advances the whole effect by hand; returns false once it has fully faded.</summary>
        public bool Advance(float deltaTime)
        {
            if (!built) return true;
            float dt = Mathf.Clamp(deltaTime, 0f, 0.05f);
            age += dt;
            AdvancePuffs(dt);
            AdvanceGlints(dt);
            AdvanceChunks(dt);
            primaryRing?.Advance(dt);
            secondaryRing?.Advance(dt);
            flashCore?.Advance(dt);
            flashHalo?.Advance(dt);
            AdvanceShell();
            if (flashLight != null)
            {
                float t = Mathf.Clamp01(age / (profile.flashLife * 1.5f));
                flashLight.intensity = lightPeak * Mathf.Pow(1f - t, 3f);
                if (t >= 1f) flashLight.enabled = false;
            }
            return age < profile.lifetime;
        }

        private void AdvancePuffs(float dt)
        {
            if (puffs == null) return;
            for (int i = 0; i < puffs.Length; i++)
            {
                ref Puff puff = ref puffs[i];
                puff.age += dt;
                float local = puff.age - puff.delay;
                if (local <= 0f)
                {
                    puffBatch.Set(i, puff.position, 0f, Color.clear, 0f);
                    continue;
                }
                float t = Mathf.Clamp01(local / puff.life);
                Vector3 velocity = puff.velocity;
                velocity.y -= puff.gravity * dt;
                velocity *= Mathf.Max(0f, 1f - puff.drag * dt);
                puff.velocity = velocity;
                puff.position += velocity * dt;
                puff.size = Mathf.Min(puff.size + puff.growth * dt, Radius * 2.6f);
                puff.spin += puff.spinSpeed * dt;
                float fadeIn = Mathf.Clamp01(local / 0.045f);
                float fadeOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 1f, t));
                float alpha = profile.puffAlpha * fadeIn * fadeOut;
                Color color = puff.tint;
                color.a *= alpha;
                puffBatch.Set(i, puff.position, puff.size, color, puff.spin);
            }
            puffBatch.Apply();
        }

        private void AdvanceGlints(float dt)
        {
            if (glints == null || glints.Length == 0) return;
            for (int i = 0; i < glints.Length; i++)
            {
                ref Glint glint = ref glints[i];
                glint.age += dt;
                float t = Mathf.Clamp01(glint.age / glint.life);
                Vector3 velocity = glint.velocity;
                velocity.y -= 11f * dt;
                glint.velocity = velocity;
                glint.position += velocity * dt;
                glint.spin += glint.spinSpeed * dt;
                float twinkle = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(glint.phase + glint.age * 42f));
                float alpha = Mathf.Pow(1f - t, 1.6f) * twinkle;
                Color color = glint.tint;
                color.a = alpha;
                glintBatch.Set(i, glint.position, glint.size * (1f - t * 0.55f), color, glint.spin);
            }
            glintBatch.Apply();
        }

        private void AdvanceChunks(float dt)
        {
            if (chunks == null) return;
            for (int i = 0; i < chunks.Length; i++)
            {
                var chunk = chunks[i];
                if (chunk == null || chunk.transform == null) continue;
                chunk.age += dt;
                if (chunk.age >= chunk.life) { chunk.transform.gameObject.SetActive(false); continue; }
                Vector3 position = chunk.transform.localPosition;
                if (!chunk.settled)
                {
                    chunk.velocity.y -= chunk.gravity * dt;
                    position += chunk.velocity * dt;
                    if (position.y <= chunk.restY)
                    {
                        // Visual-only tumble: a damped bounce reads like packed snow hitting the platform.
                        position.y = chunk.restY;
                        if (chunk.velocity.y < 0f) chunk.velocity.y = -chunk.velocity.y * 0.28f;
                        chunk.velocity.x *= 0.55f; chunk.velocity.z *= 0.55f;
                        chunk.angularVelocity *= 0.45f;
                        if (Mathf.Abs(chunk.velocity.y) < 0.35f) { chunk.velocity.y = 0f; chunk.settled = true; }
                    }
                    chunk.transform.localPosition = position;
                    chunk.transform.Rotate(chunk.angularVelocity * dt, Space.World);
                    if (chunk.spray && chunk.velocity.sqrMagnitude > 0.05f)
                        chunk.transform.localRotation = Quaternion.LookRotation(chunk.velocity);
                }
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 1f, chunk.age / chunk.life));
                chunk.transform.localScale = chunk.baseScale * Mathf.Max(0.001f, fade);
            }
        }

        private void AdvanceShell()
        {
            if (shell == null) return;
            float t = Mathf.Clamp01(age / shellLife);
            if (t >= 1f) { shell.gameObject.SetActive(false); return; }
            float scale = Mathf.Lerp(shellScaleFrom, shellScaleTo, 1f - Mathf.Pow(1f - t, 2f));
            shell.localScale = Vector3.one * scale;
            if (shellMaterial != null)
                shellMaterial.SetColor("_Color", new Color(0.72f, 0.86f, 1f, shellAlpha * Mathf.Pow(1f - t, 1.5f)));
        }

        // ------------------------------------------------------------------ helpers

        private static Material ChunkMaterial(string resourceName, string fallbackShader, Color color, float smoothness)
        {
            var source = Resources.Load<Material>(resourceName);
            if (source != null) return new Material(source) { name = resourceName + " (runtime)" };
            var shader = Shader.Find(fallbackShader);
            if (shader == null) return null;
            var material = new Material(shader) { name = resourceName + " (fallback)" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private void CreateQuadElement(Flash flash, string name, Material source)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            host.layer = 0;
            host.transform.localPosition = flash.offset;
            host.AddComponent<MeshFilter>().sharedMesh = QuadMesh;
            flash.material = new Material(source) { name = name + " (runtime)" };
            flash.renderer = host.AddComponent<MeshRenderer>();
            flash.renderer.sharedMaterial = flash.material;
            flash.renderer.shadowCastingMode = ShadowCastingMode.Off;
            flash.renderer.receiveShadows = false;
            flash.transform = host.transform;
        }

        private ShockRing CreateRing(string name, float start, float end, float life, float delay, float alpha, Color tint)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            host.layer = 0;
            host.transform.localPosition = new Vector3(0f, groundLocalY + 0.045f, 0f);
            host.AddComponent<MeshFilter>().sharedMesh = RingMesh;
            var renderer = host.AddComponent<MeshRenderer>();
            // Alpha blended: additive light disappears against the white snow surface.
            renderer.sharedMaterial = dustMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var ring = new ShockRing
            {
                transform = host.transform,
                renderer = renderer,
                material = new Material(dustMaterial) { name = name + " (runtime)" },
                startRadius = start,
                endRadius = end,
                life = Mathf.Max(0.05f, life),
                delay = delay,
                alpha = alpha,
                tint = tint
            };
            renderer.sharedMaterial = ring.material;
            return ring;
        }

        private float FindGroundLocalY(Vector3 center, float radius)
        {
            Vector3 from = center + Vector3.up * (radius * 2f + 2f);
            if (Physics.Raycast(from, Vector3.down, out var hit, radius * 8f + 8f, 1 << 8))
                return hit.point.y - center.y;
            // Match scene has a flat snow top; the fallback keeps the ring at the ball's base.
            return -radius + 0.05f;
        }

        private void Dispose(SnowballExplosion effect)
        {
            if (effect == null) return;
            if (Application.isPlaying) Destroy(effect.gameObject); else DestroyImmediate(effect.gameObject);
        }

        private void OnDestroy()
        {
            Active.Remove(this);
            Release(dustMaterial); Release(glowMaterial); Release(snowChunkMaterial); Release(iceChunkMaterial);
            Release(shellMaterial);
            puffBatch?.Dispose(); glintBatch?.Dispose();
            primaryRing?.Dispose(); secondaryRing?.Dispose();
            flashCore?.Dispose(); flashHalo?.Dispose();
        }

        /// <summary>Removes every live explosion, used on round reset, menu return and scene teardown.</summary>
        public static void ClearAll()
        {
            if (Active.Count == 0) return;
            var copy = new List<SnowballExplosion>(Active);
            Active.Clear();
            foreach (var effect in copy)
            {
                if (effect == null) continue;
                // Destroy is deferred until the end of the frame in a player build.
                // Hide old-round visuals immediately, including synchronous smoke captures.
                effect.gameObject.SetActive(false);
                effect.Manual = false;
                if (Application.isPlaying) Destroy(effect.gameObject); else DestroyImmediate(effect.gameObject);
            }
        }

        private static void DestroyNow(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        private static void Release(Material material)
        {
            if (material != null) DestroyNow(material);
        }

        // ------------------------------------------------------------------ procedural meshes

        private static Mesh quadMesh, ringMesh;
        private static Mesh[] clodMeshes, shardMeshes;

        private static Mesh QuadMesh
        {
            get
            {
                if (quadMesh == null)
                {
                    quadMesh = new Mesh { name = "Explosion quad", hideFlags = HideFlags.DontSave };
                    quadMesh.vertices = new[] {
                        new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                        new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
                    quadMesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                    quadMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                    quadMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                    quadMesh.RecalculateBounds();
                }
                return quadMesh;
            }
        }

        private static Mesh RingMesh
        {
            get
            {
                if (ringMesh == null) ringMesh = BuildRingMesh();
                return ringMesh;
            }
        }

        private static Mesh BuildRingMesh()
        {
            const int segments = 64;
            float[] fractions = { 0.60f, 0.78f, 0.92f, 1.0f };
            float[] alphas = { 0f, 0.55f, 0.90f, 0f };
            var vertices = new Vector3[segments * fractions.Length];
            var uvs = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            var triangles = new int[segments * (fractions.Length - 1) * 6];
            for (int ring = 0; ring < fractions.Length; ring++)
            {
                for (int segment = 0; segment < segments; segment++)
                {
                    float angle = segment * Mathf.PI * 2f / segments;
                    int index = ring * segments + segment;
                    vertices[index] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * fractions[ring];
                    uvs[index] = new Vector2(0.5f, 0.5f);
                    colors[index] = new Color(1f, 1f, 1f, alphas[ring]);
                }
            }
            int cursor = 0;
            for (int ring = 0; ring < fractions.Length - 1; ring++)
            {
                for (int segment = 0; segment < segments; segment++)
                {
                    int a = ring * segments + segment;
                    int b = ring * segments + (segment + 1) % segments;
                    int c = (ring + 1) * segments + segment;
                    int d = (ring + 1) * segments + (segment + 1) % segments;
                    triangles[cursor++] = a; triangles[cursor++] = c; triangles[cursor++] = b;
                    triangles[cursor++] = b; triangles[cursor++] = c; triangles[cursor++] = d;
                }
            }
            var mesh = new Mesh { name = "Explosion shockwave ring", hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh ClodMesh(int variant)
        {
            if (clodMeshes == null) clodMeshes = new Mesh[3];
            int index = Mathf.Abs(variant) % clodMeshes.Length;
            if (clodMeshes[index] == null)
                clodMeshes[index] = IcoMesh(1, 0.30f, 101 + index * 17);
            return clodMeshes[index];
        }

        private static Mesh ShardMesh(int variant)
        {
            if (shardMeshes == null) shardMeshes = new Mesh[2];
            int index = Mathf.Abs(variant) % shardMeshes.Length;
            if (shardMeshes[index] == null) shardMeshes[index] = BuildShardMesh(index);
            return shardMeshes[index];
        }

        private static Mesh BuildShardMesh(int variant)
        {
            float lean = variant == 0 ? 0.18f : -0.22f;
            var ring = new[] {
                new Vector3(0.30f, 0f, 0f), new Vector3(0.09f, 0f, 0.29f),
                new Vector3(-0.26f, 0f, 0.16f), new Vector3(-0.29f, 0f, -0.17f), new Vector3(0.07f, 0f, -0.30f)
            };
            Vector3 top = new Vector3(lean * 0.5f, 1.05f, lean * 0.16f);
            Vector3 bottom = new Vector3(-lean * 0.25f, -0.5f, 0f);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < ring.Length; i++)
            {
                Vector3 a = ring[i], b = ring[(i + 1) % ring.Length];
                vertices.Add(top); vertices.Add(a); vertices.Add(b);
                vertices.Add(bottom); vertices.Add(b); vertices.Add(a);
            }
            for (int i = 0; i < vertices.Count; i++) triangles.Add(i);
            var mesh = new Mesh { name = "Explosion ice shard " + variant, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            var uvs = new List<Vector2>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++) uvs.Add(new Vector2(0.5f, 0.5f));
            mesh.SetUVs(0, uvs);
            var colors = new List<Color>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++) colors.Add(Color.white);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh IcoMesh(int subdivisions, float jitter, int seed)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var vertices = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < vertices.Count; i++) vertices[i] = vertices[i].normalized;
            var faces = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };
            for (int level = 0; level < subdivisions; level++)
            {
                var next = new List<int>(faces.Count * 4);
                var cache = new Dictionary<long, int>();
                for (int i = 0; i < faces.Count; i += 3)
                {
                    int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                    int ab = Midpoint(vertices, cache, a, b);
                    int bc = Midpoint(vertices, cache, b, c);
                    int ca = Midpoint(vertices, cache, c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                faces = next;
            }
            var random = new System.Random(seed);
            var outVertices = new List<Vector3>(faces.Count);
            var outTriangles = new List<int>(faces.Count);
            var outUvs = new List<Vector2>(faces.Count);
            var outColors = new List<Color>(faces.Count);
            for (int i = 0; i < faces.Count; i += 3)
            {
                float radial = jitter > 0f ? 1f + ((float)random.NextDouble() * 2f - 1f) * jitter : 1f;
                for (int corner = 0; corner < 3; corner++)
                {
                    outVertices.Add(vertices[faces[i + corner]] * radial);
                    outTriangles.Add(outTriangles.Count);
                    outUvs.Add(new Vector2(0.5f, 0.5f));
                    outColors.Add(Color.white);
                }
            }
            var mesh = new Mesh { name = "Explosion icosphere", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(outVertices);
            mesh.SetTriangles(outTriangles, 0);
            mesh.SetUVs(0, outUvs);
            mesh.SetColors(outColors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int Midpoint(List<Vector3> vertices, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int existing)) return existing;
            vertices.Add(((vertices[a] + vertices[b]) * 0.5f).normalized);
            cache[key] = vertices.Count - 1;
            return vertices.Count - 1;
        }

        // Kept for future validation hooks and to mirror the project's diagnostic style.
        public string DescribeLayers() =>
            $"style={Style} radius={Radius:F2} ground={GroundOffset:F2} puffs={PuffCount} chunks={ChunkCount} glints={GlintCount} influence={InfluenceRadius:F2}";
    }
}
