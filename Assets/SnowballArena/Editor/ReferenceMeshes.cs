using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SnowballArena.Editor
{
    internal static class ReferenceMeshes
    {
        private const string SourceRoot = PrototypeBuilder.Root + "/Art/Concept/";

        public static Mesh RockOnly(Mesh mountain, string name, bool save)
        {
            var result = Object.Instantiate(mountain);
            var indices = mountain.GetTriangles(1);
            result.name = name;
            result.subMeshCount = 1;
            result.SetTriangles(indices,0);
            if (!save) return result;
            string path = PrototypeBuilder.Root + "/Art/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(result,path); return result; }
            EditorUtility.CopySerialized(result,existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(result);
            return existing;
        }

        // Bake the FBX transform into a Unity-native mesh while keeping its authored
        // face normals, corner colors and material order. The .blend remains editable.
        public static Mesh Import(string sourceName, string assetName, int materialCount)
        {
            string sourcePath = SourceRoot + sourceName + ".fbx";
            var importer = AssetImporter.GetAtPath(sourcePath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing Blender export: " + sourcePath);
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            var filters = model.GetComponentsInChildren<MeshFilter>();
            if (filters.Length != 1) throw new InvalidOperationException("Expected one joined mesh in " + sourcePath);
            var source = filters[0].sharedMesh;
            var sourceMaterials = filters[0].GetComponent<MeshRenderer>().sharedMaterials;
            if (source.subMeshCount != materialCount)
                throw new InvalidOperationException(sourceName + " material order changed: expected " + materialCount + ", got " + source.subMeshCount);
            var result = Object.Instantiate(source);
            result.name = assetName;
            var matrix = filters[0].transform.localToWorldMatrix;
            var normalMatrix = matrix.inverse.transpose;
            var vertices = result.vertices;
            var normals = result.normals;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            }
            result.vertices = vertices;
            result.normals = normals;
            string[] expected = sourceName.Contains("Cedar") ? new[] { "Snow", "Foliage", "Bark" }
                : sourceName.Contains("Mountain") ? new[] { "Snow", "Rock" }
                : sourceName.Contains("Snowball") ? new[] { "Snow", "PackedSnow" }
                : new[] { "Snow", "Ice", "Rock", "IceCore" };
            var orderedTriangles = new int[materialCount][];
            for (int slot = 0; slot < expected.Length; slot++)
            {
                int importedSlot = Array.FindIndex(sourceMaterials, mat => mat != null && mat.name == expected[slot]);
                if (importedSlot < 0) throw new InvalidOperationException("Missing authored material slot " + expected[slot] + " in " + sourceName);
                orderedTriangles[slot] = result.GetTriangles(importedSlot);
            }
            for (int slot = 0; slot < materialCount; slot++) result.SetTriangles(orderedTriangles[slot], slot);
            if (matrix.determinant < 0)
                for (int submesh = 0; submesh < result.subMeshCount; submesh++)
                {
                    var indices = result.GetTriangles(submesh);
                    for (int i = 0; i < indices.Length; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
                    result.SetTriangles(indices, submesh);
                }
            result.RecalculateBounds();
            if (result.colors32.Length != result.vertexCount) throw new InvalidOperationException("Authored vertex colors were lost: " + sourceName);
            if (sourceName == "ConceptPlatform" && (result.bounds.size.x < 21 || result.bounds.size.z < 21 || result.bounds.size.y > 8.5f))
                throw new InvalidOperationException("FBX coordinate conversion failed: " + result.bounds);
            string outputPath = PrototypeBuilder.Root + "/Art/" + assetName + ".asset";
            if (assetName.StartsWith("Study", StringComparison.Ordinal)) return result;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(outputPath);
            if (existing == null) { AssetDatabase.CreateAsset(result, outputPath); return result; }
            existing.Clear();
            existing.indexFormat = result.indexFormat;
            existing.vertices = result.vertices;
            existing.normals = result.normals;
            existing.colors = result.colors;
            existing.uv = result.uv;
            existing.subMeshCount = result.subMeshCount;
            for (int slot = 0; slot < result.subMeshCount; slot++) existing.SetTriangles(result.GetTriangles(slot), slot);
            existing.bounds = result.bounds;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(result);
            return existing;
        }
    }
}
