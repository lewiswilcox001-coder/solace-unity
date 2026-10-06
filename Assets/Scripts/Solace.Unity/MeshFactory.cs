// Solace.Unity — code-generated meshes. No external assets: Unity primitive
// meshes are harvested from CreatePrimitive, and custom low-poly shapes
// (cones, discs, merged kits) are built by hand with correct winding.
using System.Collections.Generic;
using UnityEngine;

namespace Solace.Unity
{
    /// <summary>
    /// All geometry in the presentation layer comes from here. Primitive meshes
    /// are Unity's own shared assets (harvested once and cached); custom meshes
    /// are built with duplicated vertices so RecalculateNormals yields flat,
    /// faceted shading.
    /// </summary>
    public static class MeshFactory
    {
        private static readonly Dictionary<PrimitiveType, Mesh> PrimitiveCache =
            new Dictionary<PrimitiveType, Mesh>();
        private static readonly Dictionary<string, Mesh> CustomCache =
            new Dictionary<string, Mesh>();

        /// <summary>Unity's built-in primitive mesh (shared asset — never mutate).</summary>
        public static Mesh GetPrimitive(PrimitiveType type)
        {
            Mesh m;
            if (PrimitiveCache.TryGetValue(type, out m)) return m;
            var go = GameObject.CreatePrimitive(type);
            m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            PrimitiveCache[type] = m;
            return m;
        }

        /// <summary>Low-poly cone, origin at base center, +Y up. Flat shaded.</summary>
        public static Mesh Cone(float radius, float height, int segments)
        {
            string key = "cone|" + radius.ToString("F3") + "|" + height.ToString("F3") + "|" + segments;
            Mesh cached;
            if (CustomCache.TryGetValue(key, out cached)) return cached;

            var verts = new List<Vector3>(segments * 6);
            var tris = new List<int>(segments * 6);
            var apex = new Vector3(0f, height, 0f);
            var center = new Vector3(0f, 0f, 0f);
            for (int s = 0; s < segments; s++)
            {
                float a0 = (s / (float)segments) * Mathf.PI * 2f;
                float a1 = ((s + 1) / (float)segments) * Mathf.PI * 2f;
                var p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                var p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                // Side (outward-facing): (p0, apex, p1).
                int b = verts.Count;
                verts.Add(p0); verts.Add(apex); verts.Add(p1);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                // Base cap (faces down): (center, p0, p1).
                b = verts.Count;
                verts.Add(center); verts.Add(p0); verts.Add(p1);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            CustomCache[key] = mesh;
            return mesh;
        }

        /// <summary>Flat disc in the XZ plane, normal +Y. Flat shaded.</summary>
        public static Mesh Disc(float radius, int segments)
        {
            string key = "disc|" + radius.ToString("F3") + "|" + segments;
            Mesh cached;
            if (CustomCache.TryGetValue(key, out cached)) return cached;

            var verts = new List<Vector3>(segments * 3);
            var tris = new List<int>(segments * 3);
            var center = new Vector3(0f, 0f, 0f);
            for (int s = 0; s < segments; s++)
            {
                float a0 = (s / (float)segments) * Mathf.PI * 2f;
                float a1 = ((s + 1) / (float)segments) * Mathf.PI * 2f;
                var p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                var p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                int b = verts.Count;
                verts.Add(center); verts.Add(p1); verts.Add(p0); // +Y winding
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            CustomCache[key] = mesh;
            return mesh;
        }

        /// <summary>Merges transformed meshes into one (for multi-part instanced kits).</summary>
        public static Mesh Merge(List<Mesh> meshes, List<Matrix4x4> transforms)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < meshes.Count; i++)
            {
                Mesh m = meshes[i];
                Matrix4x4 t = transforms[i];
                Vector3[] mv = m.vertices;
                int base_ = verts.Count;
                for (int v = 0; v < mv.Length; v++) verts.Add(t.MultiplyPoint3x4(mv[v]));
                int[] mt = m.triangles;
                for (int ti = 0; ti < mt.Length; ti++) tris.Add(base_ + mt[ti]);
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A tall slender foxpine crown: three stacked cones, merged.</summary>
        public static Mesh PineCrown()
        {
            const string key = "pinecrown";
            Mesh cached;
            if (CustomCache.TryGetValue(key, out cached)) return cached;
            var meshes = new List<Mesh>
            {
                Cone(2.3f, 3.2f, 7),
                Cone(1.75f, 2.8f, 7),
                Cone(1.15f, 2.4f, 7),
            };
            var xforms = new List<Matrix4x4>
            {
                Matrix4x4.TRS(new Vector3(0f, 2.4f, 0f), Quaternion.identity, Vector3.one),
                Matrix4x4.TRS(new Vector3(0f, 4.1f, 0f), Quaternion.identity, Vector3.one),
                Matrix4x4.TRS(new Vector3(0f, 5.7f, 0f), Quaternion.identity, Vector3.one),
            };
            Mesh merged = Merge(meshes, xforms);
            CustomCache[key] = merged;
            return merged;
        }

        /// <summary>A grass tuft: three small tilted cones, merged.</summary>
        public static Mesh GrassTuft()
        {
            const string key = "grasstuft";
            Mesh cached;
            if (CustomCache.TryGetValue(key, out cached)) return cached;
            var cone = Cone(0.13f, 0.75f, 5);
            var meshes = new List<Mesh> { cone, cone, cone };
            var xforms = new List<Matrix4x4>
            {
                Matrix4x4.TRS(new Vector3(0f, 0f, 0f), Quaternion.Euler(0f, 0f, 8f), Vector3.one),
                Matrix4x4.TRS(new Vector3(0.16f, 0f, 0.05f), Quaternion.Euler(-6f, 0f, -10f), Vector3.one),
                Matrix4x4.TRS(new Vector3(-0.12f, 0f, 0.1f), Quaternion.Euler(10f, 0f, 4f), new Vector3(1f, 0.8f, 1f)),
            };
            Mesh merged = Merge(meshes, xforms);
            CustomCache[key] = merged;
            return merged;
        }

        /// <summary>
        /// A faceted poly-art rock: a jittered octahedron with duplicated
        /// vertices so every face shades flat. Deliberately asymmetric —
        /// one shared mesh, per-instance rotation/scale keeps it varied.
        /// Origin at vertical center; roughly 1.4 wide, 1.4 tall.
        /// </summary>
        public static Mesh FacetedRock()
        {
            const string key = "facetedrock";
            Mesh cached;
            if (CustomCache.TryGetValue(key, out cached)) return cached;

            var top = new Vector3(0.06f, 0.85f, -0.04f);
            var bottom = new Vector3(-0.05f, -0.55f, 0.06f);
            // Ring: hand-jittered so the silhouette is never a perfect diamond.
            var ring = new Vector3[]
            {
                new Vector3(0.78f, 0.10f, 0.05f),
                new Vector3(0.02f, -0.14f, 0.66f),
                new Vector3(-0.85f, 0.16f, -0.08f),
                new Vector3(-0.06f, -0.06f, -0.72f),
            };
            var verts = new List<Vector3>(24);
            var tris = new List<int>(24);
            for (int s = 0; s < 4; s++)
            {
                Vector3 r0 = ring[s], r1 = ring[(s + 1) % 4];
                // Top pyramid face: (ring, apex, next) — matches Cone()'s
                // proven outward winding.
                int b = verts.Count;
                verts.Add(r0); verts.Add(top); verts.Add(r1);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                // Bottom pyramid face: mirrored winding for -Y outward.
                b = verts.Count;
                verts.Add(r0); verts.Add(r1); verts.Add(bottom);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            CustomCache[key] = mesh;
            return mesh;
        }

        /// <summary>A thin vertical streak (rain), unit-ish height.</summary>
        public static Mesh Streak()
        {
            const string key = "streak";
            Mesh cached;
            if (CustomCache.TryGetValue(key, out cached)) return cached;
            var cube = GetPrimitive(PrimitiveType.Cube);
            Mesh merged = Merge(new List<Mesh> { cube },
                new List<Matrix4x4> { Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(0.035f, 1.6f, 0.035f)) });
            CustomCache[key] = merged;
            return merged;
        }

        /// <summary>
        /// Attaches a mesh to a new child GameObject and returns its renderer.
        /// Shadows off by default (perf); callers opt in.
        /// </summary>
        public static MeshRenderer AddMesh(GameObject parent, string name, Mesh mesh,
                                           Material material, Vector3 localPos,
                                           Vector3 localScale, Quaternion localRot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.transform.localRotation = localRot;
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        public static MeshRenderer AddMesh(GameObject parent, string name, Mesh mesh,
                                           Material material)
        {
            return AddMesh(parent, name, mesh, material, Vector3.zero, Vector3.one, Quaternion.identity);
        }
    }
}
