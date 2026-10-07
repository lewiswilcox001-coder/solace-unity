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
    /// are built by hand with correct winding. Two shading families: FLAT
    /// (duplicated vertices → faceted/crystalline, e.g. FacetedRock, Cone) and
    /// SMOOTH (shared vertices → soft rounded shading, e.g. SmoothRock,
    /// SmoothCanopy/SoftCrown). Prefer the smooth family for organic shapes
    /// (terrain, trees, rocks, clouds); keep flat for crystals and hard props.
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

        /// <summary>
        /// Merges transformed meshes into one mesh with multiple submeshes (one
        /// per material slot). Unlike Merge(), this preserves the source normals
        /// exactly (via inverse-transpose transform) so flat-shaded facets look
        /// identical — no RecalculateNormals. Use for baking static creature
        /// body parts into a single draw call per material.
        /// </summary>
        public static Mesh MergeWithSubmeshes(List<Mesh> meshes, List<Matrix4x4> transforms,
                                              List<int> materialSlots, int submeshCount)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var subTris = new List<int>[submeshCount];
            for (int s = 0; s < submeshCount; s++) subTris[s] = new List<int>();

            for (int i = 0; i < meshes.Count; i++)
            {
                Mesh m = meshes[i];
                Matrix4x4 t = transforms[i];
                // Inverse-transpose for correct normals under non-uniform scale.
                Matrix4x4 nt = Matrix4x4.Transpose(Matrix4x4.Inverse(t));
                Vector3[] mv = m.vertices;
                Vector3[] mn = m.normals;
                Vector2[] muv = m.uv;
                bool hasNormals = mn != null && mn.Length == mv.Length;
                bool hasUvs = muv != null && muv.Length == mv.Length;
                int base_ = verts.Count;
                for (int v = 0; v < mv.Length; v++)
                {
                    verts.Add(t.MultiplyPoint3x4(mv[v]));
                    normals.Add(hasNormals
                        ? Vector3.Normalize(nt.MultiplyVector(mn[v]))
                        : Vector3.up);
                    uvs.Add(hasUvs ? muv[v] : Vector2.zero);
                }
                int slot = Mathf.Clamp(materialSlots[i], 0, submeshCount - 1);
                int[] mt = m.triangles;
                for (int ti = 0; ti < mt.Length; ti++) subTris[slot].Add(base_ + mt[ti]);
            }

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshCount;
            for (int s = 0; s < submeshCount; s++) mesh.SetTriangles(subTris[s], s);
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

        /// <summary>
        /// A soft rounded rock: the same jittered octahedron silhouette as
        /// FacetedRock, but with SHARED vertices so RecalculateNormals averages
        /// them into smooth, "A Short Hike"-style shading. Drop-in replacement
        /// for FacetedRock wherever rocks should read soft instead of crystalline.
        /// Origin at vertical center; roughly 1.4 wide, 1.4 tall.
        /// </summary>
        public static Mesh SmoothRock()
        {
            const string key = "smoothrock";
            Mesh cached;
            if (CustomCache.TryGetValue(key, out cached)) return cached;

            // Same hand-jittered points as FacetedRock — identical silhouette.
            var top = new Vector3(0.06f, 0.85f, -0.04f);
            var bottom = new Vector3(-0.05f, -0.55f, 0.06f);
            var ring = new Vector3[]
            {
                new Vector3(0.78f, 0.10f, 0.05f),
                new Vector3(0.02f, -0.14f, 0.66f),
                new Vector3(-0.85f, 0.16f, -0.08f),
                new Vector3(-0.06f, -0.06f, -0.72f),
            };
            // Shared vertices: 0 = top, 1 = bottom, 2..5 = ring.
            var verts = new List<Vector3> { top, bottom, ring[0], ring[1], ring[2], ring[3] };
            var tris = new List<int>(24);
            for (int s = 0; s < 4; s++)
            {
                int r0 = 2 + s, r1 = 2 + (s + 1) % 4;
                // Top fan — same winding as FacetedRock's top faces.
                tris.Add(r0); tris.Add(0); tris.Add(r1);
                // Bottom fan — same winding as FacetedRock's bottom faces.
                tris.Add(r0); tris.Add(r1); tris.Add(1);
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); // shared verts → smooth averaged normals
            mesh.RecalculateBounds();
            CustomCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// A soft tree-canopy blob: a once-subdivided icosahedron (42 verts,
        /// 80 tris) with gentle deterministic jitter and shared vertices, so
        /// RecalculateNormals yields smooth rounded shading. Roughly 2.4 wide,
        /// 1.9 tall, origin at center. The soft counterpart to Cone-based crowns.
        /// </summary>
        public static Mesh SmoothCanopy()
        {
            const string key = "smoothcanopy";
            Mesh cached;
            if (CustomCache.TryGetValue(key, out cached)) return cached;

            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var baseVerts = new Vector3[]
            {
                new Vector3(-1f,  t, 0f), new Vector3( 1f,  t, 0f),
                new Vector3(-1f, -t, 0f), new Vector3( 1f, -t, 0f),
                new Vector3(0f, -1f,  t), new Vector3(0f,  1f,  t),
                new Vector3(0f, -1f, -t), new Vector3(0f,  1f, -t),
                new Vector3( t, 0f, -1f), new Vector3( t, 0f,  1f),
                new Vector3(-t, 0f, -1f), new Vector3(-t, 0f,  1f),
            };
            int[] baseTris = new int[]
            {
                0,11,5,  0,5,1,  0,1,7,  0,7,10,  0,10,11,
                1,5,9,  5,11,4,  11,10,2,  10,7,6,  7,1,8,
                3,9,4,  3,4,2,  3,2,6,  3,6,8,  3,8,9,
                4,9,5,  2,4,11,  6,2,10,  8,6,7,  9,8,1,
            };

            // Deterministic gentle jitter so the blob is organic, not a ball.
            var rng = new System.Random(1234567);
            var jittered = new Vector3[12];
            for (int i = 0; i < 12; i++)
            {
                Vector3 v = baseVerts[i].normalized;
                float j = 1f + ((float)rng.NextDouble() - 0.5f) * 0.16f;
                jittered[i] = v * j;
            }

            // One subdivision with edge-midpoint welding (shared verts).
            var verts = new List<Vector3>(jittered);
            var midCache = new Dictionary<long, int>();
            var tris = new List<int>(baseTris.Length * 4);
            for (int f = 0; f < baseTris.Length; f += 3)
            {
                int a = baseTris[f], b = baseTris[f + 1], c = baseTris[f + 2];
                int ab = SubdivMid(verts, midCache, a, b);
                int bc = SubdivMid(verts, midCache, b, c);
                int ca = SubdivMid(verts, midCache, c, a);
                tris.Add(a); tris.Add(ab); tris.Add(ca);
                tris.Add(b); tris.Add(bc); tris.Add(ab);
                tris.Add(c); tris.Add(ca); tris.Add(bc);
                tris.Add(ab); tris.Add(bc); tris.Add(ca);
            }

            // Squash to a canopy-ish blob, roughly 2.4 wide × 1.9 tall.
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 v = verts[i];
                verts[i] = new Vector3(v.x * 1.2f, v.y * 0.95f, v.z * 1.2f);
            }

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); // shared verts → smooth rounded shading
            mesh.RecalculateBounds();
            CustomCache[key] = mesh;
            return mesh;
        }

        private static int SubdivMid(List<Vector3> verts, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            int idx;
            if (cache.TryGetValue(key, out idx)) return idx;
            Vector3 mid = (verts[a] + verts[b]) * 0.5f;
            idx = verts.Count;
            verts.Add(mid); // unprojected: keeps the jittered organic feel
            cache[key] = idx;
            return idx;
        }

        /// <summary>
        /// A soft tree crown: three SmoothCanopy blobs stacked like PineCrown's
        /// cones, merged. Drop-in replacement for PineCrown with the same
        /// overall footprint but rounded, soft-shaded foliage.
        /// </summary>
        public static Mesh SoftCrown()
        {
            const string key = "softcrown";
            Mesh cached;
            if (CustomCache.TryGetValue(key, out cached)) return cached;
            var blob = SmoothCanopy();
            var meshes = new List<Mesh> { blob, blob, blob };
            var xforms = new List<Matrix4x4>
            {
                Matrix4x4.TRS(new Vector3(0f, 2.4f, 0f), Quaternion.identity, new Vector3(1.9f, 1.5f, 1.9f)),
                Matrix4x4.TRS(new Vector3(0f, 4.1f, 0f), Quaternion.identity, new Vector3(1.45f, 1.2f, 1.45f)),
                Matrix4x4.TRS(new Vector3(0f, 5.7f, 0f), Quaternion.identity, new Vector3(1.0f, 0.9f, 1.0f)),
            };
            Mesh merged = Merge(meshes, xforms);
            CustomCache[key] = merged;
            return merged;
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
