using Unity.Collections;
using Unity.Mathematics;

namespace MiniECS.Physics
{
    /// <summary>
    /// Narrow-phase collision routines. Every shape is treated as convex and
    /// resolved through GJK (boolean overlap) + EPA (contact normal & penetration).
    /// Support functions cover sphere / box / capsule / convex mesh, so the same
    /// code path handles every primitive pair and mesh pairs alike.
    /// </summary>
    public static class CollisionMath
    {
        public struct CShape
        {
            public ColliderShapeType Type;
            public float3 Center;        // world-space center (offset already applied)
            public quaternion Rot;
            public float Radius;
            public float3 HalfExtents;
            public float CapsuleHeight;
            public int CapsuleAxis;      // 0 = X, 1 = Y, 2 = Z
            public int HullIndex;        // ConvexHullRegistry index (ConvexMesh only)
            public int IsTrigger;
        }

        public static CShape BuildShape(in ColliderComponent c, float3 pos, quaternion rot)
        {
            CShape s;
            s.Type = c.Shape;
            s.Rot = rot;
            s.Radius = c.Radius;
            s.HalfExtents = c.HalfExtents;
            s.CapsuleHeight = c.CapsuleHeight;
            s.CapsuleAxis = c.CapsuleAxis;
            s.HullIndex = c.MeshHullIndex;
            s.IsTrigger = c.IsTrigger;
            s.Center = pos + math.mul(rot, c.CenterOffset);
            return s;
        }

        public static float3 Support(in CShape s, float3 dir)
        {
            float l = math.length(dir);
            float3 nd = l > 1e-9f ? dir / l : new float3(1f, 0f, 0f);

            if (s.Type == ColliderShapeType.Sphere)
                return s.Center + s.Radius * nd;

            if (s.Type == ColliderShapeType.Box)
            {
                float3 local = math.mul(math.conjugate(s.Rot), nd);
                float3 sign = new float3(
                    local.x < 0f ? -s.HalfExtents.x : s.HalfExtents.x,
                    local.y < 0f ? -s.HalfExtents.y : s.HalfExtents.y,
                    local.z < 0f ? -s.HalfExtents.z : s.HalfExtents.z);
                return s.Center + math.mul(s.Rot, sign);
            }

            if (s.Type == ColliderShapeType.Capsule)
            {
                float h = math.max(0f, (s.CapsuleHeight - 2f * s.Radius) * 0.5f);
                float3 localAxis = s.CapsuleAxis == 0 ? new float3(1f, 0f, 0f)
                    : s.CapsuleAxis == 1 ? new float3(0f, 1f, 0f) : new float3(0f, 0f, 1f);
                float3 axis = math.mul(s.Rot, localAxis);
                float3 seg = axis * h;
                float3 end = math.dot(nd, seg) >= 0f ? s.Center + seg : s.Center - seg;
                return end + s.Radius * nd;
            }

            if (s.Type == ColliderShapeType.ConvexMesh)
            {
                ConvexHullRegistry.GetHull(s.HullIndex, out var pts, out int start, out int count);
                float3 local = math.mul(math.conjugate(s.Rot), nd);
                float best = -3.40282347E+38f;
                float3 bestP = s.Center;
                for (int i = 0; i < count; i++)
                {
                    float3 wp = s.Center + math.mul(s.Rot, pts[start + i]);
                    float d = math.dot(wp, nd);
                    if (d > best) { best = d; bestP = wp; }
                }
                return bestP;
            }

            return s.Center;
        }

        static float3 SupportAB(in CShape a, in CShape b, float3 dir)
            => Support(a, dir) - Support(b, -dir);

        /// <summary>Returns true on overlap; out normal points from A toward B with penetration depth.</summary>
        public static bool Collide(in CShape a, in CShape b, out float3 normalAB, out float depth)
        {
            normalAB = float3.zero;
            depth = 0f;

            if (!Gjk(a, b, out var simplex))
                return false;

            if (Epa(a, b, simplex, out var n, out depth))
            {
                if (math.dot(n, b.Center - a.Center) < 0f)
                    n = -n;
                normalAB = n;
                return true;
            }

            float3 d = b.Center - a.Center;
            float len = math.length(d);
            normalAB = len > 1e-6f ? d / len : new float3(1f, 0f, 0f);
            depth = 0.01f;
            return true;
        }

        static bool Gjk(in CShape a, in CShape b, out FixedList64Bytes<float3> simplexOut)
        {
            simplexOut = default;
            float3 dir = a.Center - b.Center;
            if (math.lengthsq(dir) < 1e-9f) dir = new float3(1f, 0f, 0f);

            var s = new FixedList64Bytes<float3>();
            s.Add(SupportAB(a, b, dir));
            dir = -s[0];

            for (int iter = 0; iter < 64; iter++)
            {
                if (math.lengthsq(dir) < 1e-12f) { simplexOut = s; return s.Length >= 4; }
                float3 nsup = SupportAB(a, b, dir);
                if (math.dot(nsup, dir) < 0f) { simplexOut = default; return false; }
                s.Add(nsup);
                if (ReduceSimplex(ref s, ref dir)) { simplexOut = s; return true; }
            }
            simplexOut = s;
            return false;
        }

        static bool ReduceSimplex(ref FixedList64Bytes<float3> s, ref float3 dir)
        {
            int n = s.Length;
            if (n == 1) { dir = -s[0]; return false; }

            if (n == 2)
            {
                float3 a = s[1], b = s[0];
                float3 ab = b - a;
                float3 ao = -a;
                if (math.dot(ab, ao) > 0f)
                {
                    float3 tmp = math.cross(ab, ao);
                    dir = math.cross(tmp, ab);
                }
                else
                {
                    s.Clear(); s.Add(a);
                    dir = ao;
                }
                return false;
            }

            if (n == 3)
            {
                float3 a = s[2], b = s[1], c = s[0];
                float3 ab = b - a, ac = c - a, ao = -a;
                float3 abc = math.cross(ab, ac);

                if (math.dot(math.cross(abc, ac), ao) > 0f)
                {
                    if (math.dot(ac, ao) > 0f)
                    {
                        s.Clear(); s.Add(a); s.Add(c);
                        float3 tmp = math.cross(ac, ao);
                        dir = math.cross(tmp, ac);
                    }
                    else
                    {
                        s.Clear(); s.Add(a); s.Add(b);
                        float3 ab2 = b - a, ao2 = -a;
                        float3 tmp = math.cross(ab2, ao2);
                        dir = math.cross(tmp, ab2);
                    }
                }
                else
                {
                    if (math.dot(math.cross(ab, abc), ao) > 0f)
                    {
                        s.Clear(); s.Add(a); s.Add(b);
                        float3 ab2 = b - a, ao2 = -a;
                        float3 tmp = math.cross(ab2, ao2);
                        dir = math.cross(tmp, ab2);
                    }
                    else
                    {
                        if (math.dot(abc, ao) > 0f)
                        {
                            s.Clear(); s.Add(a); s.Add(b); s.Add(c);
                            dir = abc;
                        }
                        else
                        {
                            s.Clear(); s.Add(a); s.Add(c); s.Add(b);
                            dir = -abc;
                        }
                    }
                }
                return false;
            }

            // tetrahedron
            float3 pa = s[3], pb = s[2], pc = s[1], pd = s[0];
            float3 pab = pb - pa, pac = pc - pa, pad = pd - pa, pao = -pa;
            float3 pabc = math.cross(pab, pac);
            float3 pacd = math.cross(pac, pad);
            float3 padb = math.cross(pad, pab);
            if (math.dot(pabc, pd - pa) > 0f) pabc = -pabc;
            if (math.dot(pacd, pb - pa) > 0f) pacd = -pacd;
            if (math.dot(padb, pc - pa) > 0f) padb = -padb;

            if (math.dot(pabc, pao) > 0f) { s.Clear(); s.Add(pa); s.Add(pb); s.Add(pc); dir = pabc; return false; }
            if (math.dot(pacd, pao) > 0f) { s.Clear(); s.Add(pa); s.Add(pc); s.Add(pd); dir = pacd; return false; }
            if (math.dot(padb, pao) > 0f) { s.Clear(); s.Add(pa); s.Add(pd); s.Add(pb); dir = padb; return false; }
            return true;
        }

        struct Face
        {
            public int A;
            public int B;
            public int C;
            public float3 Normal;
            public float Dist;
        }

        static void AddFace(ref FixedList4096Bytes<Face> faces, ref FixedList512Bytes<float3> verts, int ia, int ib, int ic)
        {
            float3 a = verts[ia], b = verts[ib], c = verts[ic];
            float3 n = math.normalize(math.cross(b - a, c - a));
            int tb = ib, tc = ic;
            if (math.dot(n, a) < 0f) { n = -n; tb = ic; tc = ib; }
            Face f;
            f.A = ia; f.B = tb; f.C = tc;
            f.Normal = n;
            f.Dist = math.dot(n, verts[ia]);
            faces.Add(f);
        }

        static void AddEdge(ref FixedList512Bytes<int2> edges, int x, int y)
        {
            edges.Add(new int2(x, y));
        }

        static bool SameEdge(int2 a, int2 b)
        {
            return (a.x == b.x && a.y == b.y) || (a.x == b.y && a.y == b.x);
        }

        static bool Epa(in CShape a, in CShape b, FixedList64Bytes<float3> simplex, out float3 normal, out float depth)
        {
            normal = float3.zero;
            depth = 0f;
            if (simplex.Length < 4) return false;

            var verts = new FixedList512Bytes<float3>();
            for (int i = 0; i < 4; i++) verts.Add(simplex[i]);

            var faces = new FixedList4096Bytes<Face>();
            AddFace(ref faces, ref verts, 0, 1, 2);
            AddFace(ref faces, ref verts, 0, 2, 3);
            AddFace(ref faces, ref verts, 0, 3, 1);
            AddFace(ref faces, ref verts, 1, 3, 2);

            for (int iter = 0; iter < 16; iter++)
            {
                int best = -1;
                float bestDist = float.MaxValue;
                for (int f = 0; f < faces.Length; f++)
                {
                    if (faces[f].Dist < bestDist) { bestDist = faces[f].Dist; best = f; }
                }
                if (best < 0) break;

                float3 n = faces[best].Normal;
                float3 sup = SupportAB(a, b, n);
                float supDist = math.dot(sup, n);
                if (supDist - bestDist < 1e-4f)
                {
                    normal = n;
                    depth = bestDist;
                    return true;
                }

                int supIdx = verts.Length;
                verts.Add(sup);

                var edges = new FixedList512Bytes<int2>();
                for (int f = 0; f < faces.Length;)
                {
                    var fc = faces[f];
                    if (math.dot(fc.Normal, sup - verts[fc.A]) > 0f)
                    {
                        AddEdge(ref edges, fc.A, fc.B);
                        AddEdge(ref edges, fc.B, fc.C);
                        AddEdge(ref edges, fc.C, fc.A);
                        faces.RemoveAtSwapBack(f);
                    }
                    else f++;
                }

                for (int e = 0; e < edges.Length; e++)
                {
                    int cnt = 0;
                    for (int k = 0; k < edges.Length; k++)
                        if (SameEdge(edges[k], edges[e])) cnt++;
                    if (cnt == 1)
                        AddFace(ref faces, ref verts, edges[e].x, edges[e].y, supIdx);
                }
            }
            return false;
        }
    }
}
