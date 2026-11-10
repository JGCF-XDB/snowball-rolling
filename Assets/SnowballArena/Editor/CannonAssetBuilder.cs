using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SnowballArena.Editor
{
    public static class CannonAssetBuilder
    {
        private const string Root = "Assets/SnowballArena/Art/SelectedCannonB";
        private const string LibraryPath = "Assets/SnowballArena/Resources/CannonArt.asset";
        [Serializable] private sealed class MaterialSource
        {
            public string name;
            public float[] color;
            public float metallic, roughness;
            public bool signal;
        }
        [Serializable] private sealed class MeshSource
        {
            public string name;
            public Vector3[] vertices, normals;
            public int[] triangles, materialSlots;
        }
        [Serializable] private sealed class Source
        {
            public float projectileRadius, muzzleInnerDiameter;
            public int muzzleSides;
            public Vector3 muzzle;
            public MaterialSource[] materials;
            public MeshSource[] meshes;
        }
        private sealed class Part
        {
            public Mesh mesh;
            public Material[] materials;
        }

        [MenuItem("Snowball Arena/Rebuild Selected Cannon B")]
        public static void Import()
        {
            Source source;
            using (var stream = new GZipStream(File.OpenRead("ArtSource/SelectedCannonB.json.gz"), CompressionMode.Decompress))
            using (var reader = new StreamReader(stream)) source = JsonUtility.FromJson<Source>(reader.ReadToEnd());
            Directory.CreateDirectory(Root + "/Meshes");
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Prefabs");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var materials = new Material[source.materials.Length];
            var signalTemplate = Resources.Load<Material>("CannonSignal");
            if (signalTemplate == null) throw new InvalidOperationException("CannonSignal material is missing.");
            for (int i = 0; i < materials.Length; i++)
            {
                var spec = source.materials[i];
                var material = spec.signal ? new Material(signalTemplate) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.name = spec.name;
                // Blender node colours are scene-linear; Unity serializes material
                // colour properties in sRGB and converts them for its linear shader.
                material.SetColor("_BaseColor", new Color(spec.color[0], spec.color[1], spec.color[2], 1).gamma);
                if (!spec.signal)
                {
                    material.SetFloat("_Metallic", spec.metallic);
                    material.SetFloat("_Smoothness", 1 - spec.roughness);
                }
                materials[i] = Save(material, Root + "/Materials/" + i + ".mat");
            }
            var parts = new Dictionary<string, Part>();
            foreach (var spec in source.meshes)
            {
                var slots = new List<int>();
                foreach (int slot in spec.materialSlots) if (!slots.Contains(slot)) slots.Add(slot);
                var mesh = new Mesh { name = "Cannon B " + spec.name, vertices = spec.vertices, normals = spec.normals };
                mesh.subMeshCount = slots.Count;
                var ordered = new Material[slots.Count];
                for (int slot = 0; slot < slots.Count; slot++)
                {
                    var triangles = new List<int>();
                    for (int tri = 0; tri < spec.materialSlots.Length; tri++)
                        if (spec.materialSlots[tri] == slots[slot])
                            for (int corner = 0; corner < 3; corner++) triangles.Add(spec.triangles[tri * 3 + corner]);
                    mesh.SetTriangles(triangles, slot);
                    ordered[slot] = materials[slots[slot]];
                }
                mesh.RecalculateBounds();
                parts[spec.name] = new Part { mesh = Save(mesh, Root + "/Meshes/" + spec.name + ".asset"), materials = ordered };
            }
            var on = parts["Signals"].materials[0];
            var off = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Dormant amber rune" };
            off.SetColor("_BaseColor", new Color(0.12f, 0.035f, 0.008f));
            off = Save(off, Root + "/Materials/SignalOff.mat");
            var trail = new Material(signalTemplate) { name = "Cannon B hot trail" };
            trail.SetColor("_BaseColor", new Color(1, 0.21f, 0.015f).gamma);
            trail = Save(trail, Root + "/Materials/Trail.mat");

            var cannon = new GameObject("Dark ice-rune long cannon B");
            AddPart(cannon.transform, "Mount", parts["Mount"]);
            var recoil = new GameObject("Barrel recoil").transform;
            recoil.SetParent(cannon.transform, false);
            AddPart(recoil, "Barrel", parts["Barrel"]);
            var signals = AddPart(recoil, "Warning runes", parts["Signals"]);
            var muzzle = new GameObject("Muzzle - inner diameter 0.60 m").transform;
            muzzle.SetParent(cannon.transform, false); muzzle.localPosition = source.muzzle;
            var flash = AddPart(muzzle, "Muzzle flash", parts["MuzzleFlash"]);
            var flashShader = Shader.Find("SnowArena/CannonFlash");
            if (flashShader == null) throw new InvalidOperationException("Round muzzle flash shader is missing.");
            var flashMaterial = new Material(flashShader) { name = "Soft round muzzle glow" };
            flashMaterial.SetColor("_BaseColor", new Color(1, 0.25f, 0.015f).gamma);
            flash.GetComponent<Renderer>().sharedMaterial = Save(flashMaterial, Root + "/Materials/MuzzleFlash.mat");
            flash.transform.localPosition = Vector3.forward * 0.025f;
            flash.SetActive(false);
            var visuals = cannon.AddComponent<CannonVisuals>();
            visuals.barrel = recoil; visuals.muzzle = muzzle; visuals.muzzleFlash = flash;
            visuals.signals = new[] { signals.GetComponent<Renderer>() };
            visuals.signalOn = on; visuals.signalOff = off;
            visuals.muzzleInnerDiameter = source.muzzleInnerDiameter;
            visuals.muzzleSides = source.muzzleSides;

            var projectile = new GameObject("Ice-core cannonball B - diameter 0.60 m");
            AddPart(projectile.transform, "Faceted core, ice shell and hot seams", parts["Projectile"]);
            var impact = new GameObject("Cannon B ice and ember burst");
            var fragments = new Transform[12];
            for (int i = 0; i < fragments.Length; i++)
                fragments[i] = AddPart(impact.transform, "Fragment " + i, parts[i % 3 == 0 ? "EmberFragment" : "IceFragment"]).transform;
            impact.AddComponent<CannonBurst>().fragments = fragments;

            var library = ScriptableObject.CreateInstance<CannonArt>();
            try
            {
                library.cannonPrefab = PrefabUtility.SaveAsPrefabAsset(cannon, Root + "/Prefabs/CannonB.prefab");
                library.projectilePrefab = PrefabUtility.SaveAsPrefabAsset(projectile, Root + "/Prefabs/CannonballB.prefab");
                library.impactPrefab = PrefabUtility.SaveAsPrefabAsset(impact, Root + "/Prefabs/ImpactB.prefab");
                library.trailMaterial = trail; library.projectileRadius = source.projectileRadius;
                Save(library, LibraryPath);
            }
            finally { Object.DestroyImmediate(cannon); Object.DestroyImmediate(projectile); Object.DestroyImmediate(impact); }
            AssetDatabase.SaveAssets();
            Validate();
            Debug.Log("SELECTED CANNON B: dark Blender models baked into persistent Unity meshes and prefabs.");
        }

        public static void Validate()
        {
            var library = AssetDatabase.LoadAssetAtPath<CannonArt>(LibraryPath);
            if (library == null || library.cannonPrefab == null || library.projectilePrefab == null || library.impactPrefab == null)
                throw new InvalidOperationException("Selected cannon resources are incomplete.");
            var visuals = library.cannonPrefab.GetComponent<CannonVisuals>();
            foreach (var message in ShaderUtil.GetShaderMessages(visuals.muzzleFlash.GetComponent<Renderer>().sharedMaterial.shader))
                if (message.severity.ToString() == "Error") throw new InvalidOperationException("Muzzle glow shader: " + message.message);
            float diameter = library.projectileRadius * 2;
            if (Mathf.Abs(visuals.muzzleInnerDiameter - diameter) > 0.0001f)
                throw new InvalidOperationException("Projectile and muzzle diameters differ.");
            var barrel = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Meshes/Barrel.asset");
            float boreRadius = float.PositiveInfinity;
            foreach (var v in barrel.vertices)
                if (v.z >= visuals.muzzle.localPosition.z - 0.12f - 0.001f && v.z <= visuals.muzzle.localPosition.z + 0.001f)
                    boreRadius = Mathf.Min(boreRadius, new Vector2(v.x, v.y - visuals.muzzle.localPosition.y).magnitude);
            float boreDiameter = boreRadius * 2 * Mathf.Cos(Mathf.PI / visuals.muzzleSides);
            if (!float.IsFinite(boreDiameter) || Mathf.Abs(boreDiameter - diameter) > 0.001f)
                throw new InvalidOperationException("The modeled circular bore does not fit the projectile: " + boreDiameter);
            var shotMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Meshes/Projectile.asset");
            float radius = 0;
            foreach (var v in shotMesh.vertices) radius = Mathf.Max(radius, v.magnitude);
            if (Mathf.Abs(radius - library.projectileRadius) > 0.001f)
                throw new InvalidOperationException("The authored projectile exceeds its physical radius.");
            int triangles = 0;
            foreach (var prefab in new[] { library.cannonPrefab, library.projectilePrefab, library.impactPrefab })
            {
                if (prefab.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException("Cannon visual assets must not add gameplay obstacles.");
                foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!AssetDatabase.Contains(filter.sharedMesh)) throw new InvalidOperationException("Transient cannon mesh found.");
                    triangles += filter.sharedMesh.triangles.Length / 3;
                }
            }
            if (triangles > 30000) throw new InvalidOperationException("Cannon triangle budget exceeded: " + triangles);
            Directory.CreateDirectory("TestResults/SelectedCannonB");
            File.WriteAllLines("TestResults/SelectedCannonB/assets.txt", new[] {
                "PASS Persistent Blender-derived meshes and Resources-linked prefabs",
                "PASS Actual circular bore inner diameter equals projectile diameter: " + boreDiameter,
                "PASS Projectile fits physical radius: " + radius,
                "PASS No visual collider; triangles=" + triangles
            });
        }

        public static void ImportAndBuild() { Import(); MatchSceneBuilder.BuildWindowsGame(); }

        private static GameObject AddPart(Transform parent, string name, Part part)
        {
            var item = new GameObject(name) { layer = 0 };
            item.transform.SetParent(parent, false);
            item.AddComponent<MeshFilter>().sharedMesh = part.mesh;
            item.AddComponent<MeshRenderer>().sharedMaterials = part.materials;
            return item;
        }

        private static T Save<T>(T value, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(value, path); return value; }
            EditorUtility.CopySerialized(value, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(value);
            return existing;
        }
    }
}
