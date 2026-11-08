using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SnowballArena.Editor
{
    public static class ArenaExtensionValidation
    {
        public static void Run()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var mesh = new Mesh();
            var definition = ScriptableObject.CreateInstance<ArenaDefinition>();
            var results = new List<string>();
            try
            {
                var root = new GameObject("Non-circular test arena") { layer = 8 };
                SceneManager.MoveGameObjectToScene(root, scene);
                var surface = root.AddComponent<ArenaSurface>(); surface.definition = definition;
                var collider = root.AddComponent<MeshCollider>();
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                void Quad(float x0, float x1, float z0, float z1)
                {
                    int start = vertices.Count;
                    vertices.Add(new Vector3(x0, 0, z0)); vertices.Add(new Vector3(x0, 0, z1));
                    vertices.Add(new Vector3(x1, 0, z1)); vertices.Add(new Vector3(x1, 0, z0));
                    triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                }
                Quad(-8, -2, -8, 8); Quad(2, 8, -8, 8); Quad(-2, 2, -8, -2); Quad(-2, 2, 2, 8);
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                collider.sharedMesh = mesh; Physics.SyncTransforms();
                if (ArenaQuery.TrySupport(scene.GetPhysicsScene(), new Vector3(0, 0.65f, 0), 0.65f, 1 << 8, 0.06f, 0.6f, out _))
                    throw new Exception("Central hole incorrectly reports support");
                if (!ArenaQuery.TrySupport(scene.GetPhysicsScene(), new Vector3(4, 0.65f, 0), 0.65f, 1 << 8, 0.06f, 0.6f, out _))
                    throw new Exception("Ring surface incorrectly reports missing support");
                results.Add("PASS Real mesh support distinguishes rectangular ring from its central hole");
                if (!SpawnService.TryGetPositions(surface, 8, 0.65f, out var spawns)) throw new Exception("Failed to spawn on ring arena");
                foreach (var spawn in spawns)
                    if (Mathf.Abs(spawn.x) < 2.65f && Mathf.Abs(spawn.z) < 2.65f) throw new Exception("Spawn overlaps hole clearance");
                results.Add("PASS Eight safe spawns avoid the hole without circular-radius assumptions");
                definition.localAimPoint = new Vector3(4, 0, 0);
                root.transform.SetPositionAndRotation(new Vector3(10, 0, -7), Quaternion.Euler(0, 23, 0));
                if (Vector3.Distance(surface.AimPoint, root.transform.TransformPoint(definition.localAimPoint)) > 0.001f)
                    throw new Exception("Custom aim point was not transformed with platform");
                results.Add("PASS Non-circular platform exposes a separately configured world-space cannon aim point");
                Directory.CreateDirectory("TestResults");
                File.WriteAllText("TestResults/arena-extension-validation.txt", string.Join("\n", results));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(definition);
            }
        }
    }
}
