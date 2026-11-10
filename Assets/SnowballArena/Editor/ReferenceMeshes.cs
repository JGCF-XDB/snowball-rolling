using System.Collections.Generic;
using UnityEngine;

namespace SnowballArena.Editor
{
    // Deterministic, flat-shaded meshes. These contain visual detail only, never collision geometry.
    internal static class ReferenceMeshes
    {
        private sealed class Draft
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<int>[] triangles;

            public Draft(int materials)
            {
                triangles = new List<int>[materials];
                for (int i = 0; i < materials; i++) triangles[i] = new List<int>();
            }

            public void Tri(int material, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0) (b, c) = (c, b);
                int first = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                triangles[material].Add(first); triangles[material].Add(first + 1); triangles[material].Add(first + 2);
            }

            public void Quad(int material, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
            {
                Tri(material, a, b, c, outward); Tri(material, a, c, d, outward);
            }

            public Mesh Finish(string name)
            {
                var mesh = new Mesh { name = name, subMeshCount = triangles.Length };
                mesh.SetVertices(vertices);
                for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }
        }

        private static Vector3 Radial(float angle) => new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
        private static float Wave(float angle, float phase) => Mathf.Sin(angle * 5 + phase) * 0.6f + Mathf.Cos(angle * 9 - phase) * 0.4f;

        public static Mesh Platform(float radius, float depth, int segments)
        {
            var mesh = new Draft(3);
            float height = depth / 3.2f;
            Vector3 Point(int index, int ring)
            {
                float angle = (index % segments) * Mathf.PI * 2 / segments;
                float noise = Wave(angle, 0.4f);
                float r, y;
                switch (ring)
                {
                    case 0: r = radius * (1 + noise * (radius < 2 ? 0.065f : 0.003f)); y = 0; break;
                    case 1: r = radius * (1.003f + noise * (radius < 2 ? 0.06f : 0.008f)); y = (-0.34f - noise * 0.07f) * height; break;
                    case 2: r = radius * (0.986f + noise * 0.008f); y = (-1.02f - noise * 0.13f) * height; break;
                    case 3: r = radius * (0.97f + Wave(angle, 1.3f) * 0.018f); y = (-1.9f - noise * 0.18f) * height; break;
                    default: r = radius * (0.83f + Wave(angle, 2.1f) * 0.025f); y = (-3.1f - noise * 0.12f) * height; break;
                }
                return Radial(angle) * r + Vector3.up * y;
            }
            for (int i = 0; i < segments; i++)
            {
                mesh.Tri(0, Vector3.zero, Point(i, 0), Point(i + 1, 0), Vector3.up);
                Vector3 outward = Radial((i + 0.5f) * Mathf.PI * 2 / segments);
                for (int ring = 0; ring < 4; ring++)
                    mesh.Quad(ring == 0 ? 0 : ring == 1 ? 1 : 2,
                        Point(i, ring), Point(i + 1, ring), Point(i + 1, ring + 1), Point(i, ring + 1), outward);
                mesh.Tri(2, Vector3.down * depth, Point(i + 1, 4), Point(i, 4), Vector3.down);
                if (i % 2 == 0)
                {
                    float angle = i * Mathf.PI * 2 / segments;
                    var center = Radial(angle) * radius * 0.995f + Vector3.down * 0.75f * height;
                    Crystal(mesh, 1, center, Radial(angle), radius * (0.016f + 0.005f * (1 + Mathf.Sin(i * 1.7f))),
                        (0.38f + 0.55f * (0.5f + 0.5f * Mathf.Sin(i * 2.3f))) * height);
                }
            }
            return mesh.Finish("Layered snow, glacier ice and faceted rock");
        }

        private static void Crystal(Draft mesh, int material, Vector3 center, Vector3 normal, float width, float length)
        {
            Vector3 tangent = Vector3.Cross(Vector3.up, normal).normalized;
            var ring = new[] { center + tangent * width, center + normal * width * 0.7f,
                center - tangent * width, center - normal * width * 0.7f };
            Vector3 tip = center + Vector3.down * length + normal * width * 0.4f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 outward = ((ring[i] + ring[(i + 1) % 4]) * 0.5f - center).normalized;
                mesh.Tri(material, ring[i], ring[(i + 1) % 4], tip, outward);
                mesh.Tri(material, ring[i], center + Vector3.up * width, ring[(i + 1) % 4], outward);
            }
        }

        private static Vector3 Sphere(float phi, float theta) => new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));

        public static Mesh Snowball()
        {
            var mesh = new Draft(2);
            const int latitude = 18, longitude = 30;
            Vector3 Point(int row, int column)
            {
                float phi = Mathf.PI * row / latitude, theta = Mathf.PI * 2 * (column % longitude) / longitude;
                float radius = 0.485f + Mathf.Sin(phi) * (Mathf.Sin(theta * 7 + phi * 3) * 0.003f + Mathf.Cos(theta * 3 - phi * 8) * 0.002f);
                return Sphere(phi, theta) * radius;
            }
            for (int row = 0; row < latitude; row++)
                for (int col = 0; col < longitude; col++)
                {
                    Vector3 a = Point(row, col), b = Point(row, col + 1), c = Point(row + 1, col + 1), d = Point(row + 1, col);
                    if (row > 0) mesh.Tri(0, a, b, d, (a + b + d).normalized);
                    if (row < latitude - 1) mesh.Tri(0, b, c, d, (b + c + d).normalized);
                }

            // A continuous raised spiral with a pale compressed edge reads clearly as the ball rotates.
            const int steps = 160;
            var tilt = Quaternion.Euler(18, 0, 27);
            var offsets = new[] { -0.115f, -0.077f, 0.077f, 0.115f };
            var radii = new[] { 0.486f, 0.501f, 0.497f, 0.485f };
            Vector3 Band(int step, int edge)
            {
                float t = step / (float)steps;
                float phi = 0.22f + t * (Mathf.PI - 0.44f) + offsets[edge];
                float theta = t * Mathf.PI * 7.6f + 0.14f * Mathf.Sin(t * Mathf.PI * 12);
                return tilt * Sphere(phi, theta) * radii[edge];
            }
            for (int i = 0; i < steps; i++)
                for (int edge = 0; edge < 3; edge++)
                {
                    Vector3 a = Band(i, edge), b = Band(i + 1, edge), c = Band(i + 1, edge + 1), d = Band(i, edge + 1);
                    mesh.Quad(edge == 0 ? 1 : 0, a, b, c, d, (a + b + c + d).normalized);
                }
            for (int i = 0; i < 11; i++)
            {
                Vector3 direction = Sphere(0.48f + (i * 0.731f % 2.15f), i * 2.39996f);
                Lump(mesh, direction * 0.48f, direction, 0.045f + 0.012f * (i % 3), 0.025f);
            }
            for (int i = 0; i < 16; i++)
            {
                Vector3 direction = Sphere(0.35f + (i * 0.631f % 2.4f), i * 2.161f);
                Grain(mesh, direction * 0.488f, direction, 0.009f + (i % 3) * 0.002f);
            }
            return mesh.Finish("Rounded faceted snowball with rolled snow bands");
        }

        private static void Lump(Draft mesh, Vector3 center, Vector3 normal, float width, float height)
        {
            var rotation = Quaternion.FromToRotation(Vector3.up, normal);
            Vector3 Point(int row, int col)
            {
                var p = Sphere(Mathf.PI * row / 4, Mathf.PI * 2 * (col % 7) / 7);
                return center + rotation * Vector3.Scale(p, new Vector3(width, height, width * 0.85f));
            }
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 7; col++)
                {
                    Vector3 a = Point(row, col), b = Point(row, col + 1), c = Point(row + 1, col + 1), d = Point(row + 1, col);
                    if (row > 0) mesh.Tri(0, a, b, d, (a + b + d) / 3 - center);
                    if (row < 3) mesh.Tri(0, b, c, d, (b + c + d) / 3 - center);
                }
        }

        private static void Grain(Draft mesh, Vector3 center, Vector3 normal, float size)
        {
            Vector3 tangent = Vector3.Cross(normal, Vector3.up).normalized;
            if (tangent.sqrMagnitude < 0.1f) tangent = Vector3.right;
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            var points = new[] { center + tangent * size, center + bitangent * size, center - tangent * size, center - bitangent * size };
            for (int i = 0; i < 4; i++) mesh.Tri(1, points[i], points[(i + 1) % 4], center + normal * size * 0.6f, normal);
        }

        public static Mesh Cedar()
        {
            var mesh = new Draft(3);
            const int sides = 7;
            for (int i = 0; i < sides; i++)
            {
                Vector3 a = Radial(i * Mathf.PI * 2 / sides) * 0.105f, b = Radial((i + 1) * Mathf.PI * 2 / sides) * 0.105f;
                mesh.Quad(2, a, b, b + Vector3.up * 0.85f, a + Vector3.up * 0.85f, a + b);
            }
            for (int tier = 0; tier < 4; tier++)
            {
                float y = 0.42f + tier * 0.59f, radius = 0.93f - tier * 0.195f, height = 1.25f - tier * 0.11f;
                Cone(mesh, 0, y, radius, height, sides, tier * 0.24f);
                Cone(mesh, 1, y + 0.16f, radius * 0.94f, height - 0.09f, sides, tier * 0.24f);
            }
            return mesh.Finish("Four tier snow cedar");
        }

        private static void Cone(Draft mesh, int material, float y, float radius, float height, int sides, float phase)
        {
            Vector3 top = new Vector3(0.03f, y + height, -0.02f);
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides + phase, b = (i + 1) * Mathf.PI * 2 / sides + phase;
                Vector3 p = Radial(a) * radius + Vector3.up * (y + 0.035f * Mathf.Sin(i * 2.4f));
                Vector3 q = Radial(b) * radius + Vector3.up * (y + 0.035f * Mathf.Sin(((i + 1) % sides) * 2.4f));
                mesh.Tri(material, p, top, q, Radial((a + b) * 0.5f) + Vector3.up);
                mesh.Tri(material, p, q, Vector3.up * y, Vector3.down);
            }
        }

        public static Mesh Mountain(int variant)
        {
            var mesh = new Draft(2);
            const int segments = 9;
            Vector3 Point(int index, int ring)
            {
                float angle = (index % segments) * Mathf.PI * 2 / segments;
                float noise = Wave(angle, variant * 1.7f + 0.7f);
                float radius = ring == 0 ? 1.1f : ring == 1 ? 0.79f : ring == 2 ? 0.43f : 0.12f;
                float y = ring == 0 ? 0 : ring == 1 ? 0.25f : ring == 2 ? 0.57f : 0.9f;
                return Radial(angle) * radius * (1 + noise * 0.15f) + new Vector3(ring * 0.045f, y + noise * (ring == 0 ? 0 : 0.085f), -ring * 0.02f);
            }
            for (int i = 0; i < segments; i++)
            {
                Vector3 outward = Radial((i + 0.5f) * Mathf.PI * 2 / segments);
                for (int ring = 0; ring < 3; ring++)
                    mesh.Quad(ring < 2 ? 0 : 1, Point(i, ring), Point(i + 1, ring), Point(i + 1, ring + 1), Point(i, ring + 1), outward);
                mesh.Tri(1, Point(i, 3), Point(i + 1, 3), new Vector3(0.18f, 1.1f + variant * 0.025f, -0.06f), outward + Vector3.up);
            }
            return mesh.Finish("Faceted alpine peak " + variant);
        }

        public static Mesh RollingTracks()
        {
            var mesh = new Draft(1);
            for (int arc = 0; arc < 9; arc++)
            {
                float radius = 2.5f + (arc % 4) * 1.55f, start = arc * 1.71f, sweep = 2.1f + (arc % 3) * 0.8f;
                Vector3 center = new Vector3(Mathf.Sin(arc * 2.4f) * 0.8f, 0.008f, Mathf.Cos(arc * 1.3f) * 0.8f);
                for (int line = 0; line < 2; line++)
                    for (int step = 0; step < 60; step++)
                    {
                        float a = start + sweep * step / 60, b = start + sweep * (step + 1) / 60;
                        float r = radius + line * 0.105f, width = 0.025f * Mathf.Sin(Mathf.PI * (step + 0.5f) / 60);
                        float ra = r + 0.055f * Mathf.Sin(a * 3 + arc), rb = r + 0.055f * Mathf.Sin(b * 3 + arc);
                        mesh.Quad(0, center + Radial(a) * (ra - width), center + Radial(b) * (rb - width),
                            center + Radial(b) * (rb + width), center + Radial(a) * (ra + width), Vector3.up);
                    }
            }
            return mesh.Finish("Subtle paired rolling arcs");
        }
    }
}
