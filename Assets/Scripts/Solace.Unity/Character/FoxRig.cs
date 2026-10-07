using UnityEngine;

namespace Solace.Unity.Character
{
    /// <summary>
    /// Procedural lantern-fox rig, built entirely from primitives at runtime.
    ///
    /// A slender, long-limbed fox-like creature (~0.9 m at the shoulder) with a
    /// luminous core set into its chest. Coat: deep russet-amber back fading to
    /// pale cream chest/cheeks, dark "socks" on the lower legs, tail tipped pale.
    /// Light is life: <see cref="SetGlow"/> drives the chest core + point light;
    /// the coat itself stays matte. At glow 0 the fox reads as dimmed.
    ///
    /// Hierarchy (fox faces +Z, Y up, metres, scale = 1):
    ///   Root (ground, 0,0,0)
    ///    └─ Body      pelvis pivot        (0, 0.72, -0.10)
    ///        ├─ Chest                 (0, 0.77,  0.34)
    ///        │   ├─ Neck              (0, 0.90,  0.48)
    ///        │   │   └─ Head          (0, 1.00,  0.59)  (+EarL/EarR, muzzle, eyes)
    ///        │   ├─ ChestCore         emissive sphere, half-sunk in the chest
    ///        │   └─ CoreLight         warm PointLight at the core
    ///        ├─ TailBase → TailMid → TailTip   (long, full, slight upward curve)
    ///        └─ 4 × (Upper → Lower → Paw)     shoulder/hip pivots at the joints
    ///
    /// Legs: shoulder pivot ~0.68 m, upper 0.30 m, lower 0.28 m, paw ~0.06 m —
    /// roughly 0.61 m of leg under a 0.9 m shoulder: long-limbed, stylised.
    /// </summary>
    public class FoxRig : MonoBehaviour
    {
        public Transform Root, Body, Chest, Neck, Head, EarL, EarR;
        public Transform TailBase, TailMid, TailTip;
        public Transform LegFL_Upper, LegFL_Lower, LegFL_Paw;
        public Transform LegFR_Upper, LegFR_Lower, LegFR_Paw;
        public Transform LegBL_Upper, LegBL_Lower, LegBL_Paw;
        public Transform LegBR_Upper, LegBR_Lower, LegBR_Paw;
        public Transform ChestCore;      // small emissive sphere set into the chest
        public Light CoreLight;          // warm PointLight at the core
        /// <summary>
        /// Inner motion node between Root and Body. The animator writes its
        /// root-motion bob here so it never fights the world-space transform
        /// that external views (AgentView, KindredView) drive on Root.
        /// </summary>
        public Transform MotionRoot;

        Material _coreMat;
        readonly Color _coreEmission = new Color(1f, 0.52f, 0.16f);
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        float _scale = 1f;

        // ---- visual smoothing (presentation only; the sim drives Root as a target)
        Vector3 _smoothPos;
        float _smoothYaw;
        bool _hasSmooth;
        Vector3 _prevSmoothPos;
        float _visualSpeed;          // smoothed world-units-per-second, real time
        float _speedFactor;          // 0..1 normalised for glow/run feel
        float _turnVel;              // smoothed signed yaw velocity, deg/s
        float _baseGlow = 1f;
        float _playbackRate = 1f;    // smoothed gait rate fed to the animator
        float _lastBank;             // smoothed bank roll, degrees
        FoxAnimator _anim;           // cached; lives on Root's GameObject

        // ---------------------------------------------------------------- build

        /// <summary>Builds a complete lantern-fox under <paramref name="parent"/>.</summary>
        public static FoxRig Build(Transform parent, float scale = 1f)
        {
            var go = new GameObject("LanternFox");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<FoxRig>();
            rig._scale = Mathf.Max(0.01f, scale);
            rig.BuildRig(rig._scale);
            rig.SetGlow(1f);
            return rig;
        }

        /// <summary>
        /// 0..1: drives core emissive intensity + light intensity/range.
        /// At 0 the core is dark and the light is off — the dimmed/sick read.
        /// The coat never changes: only the core and its light respond.
        /// The value is stored as the base glow; LateUpdate breathes on top of
        /// it (lively pulse when healthy, slow faint flicker when dim) and adds
        /// a running boost, so the signature reads alive.
        /// </summary>
        public void SetGlow(float glow)
        {
            _baseGlow = Mathf.Clamp01(glow);
            ApplyGlow(_baseGlow, 1f, 0f); // immediate, unmodulated
        }

        /// <summary>Current smoothed visual speed, world units per real second.</summary>
        public float VisualSpeed => _visualSpeed;

        /// <summary>Snap the smoothed visual transform to the sim target (teleports, succession).</summary>
        public void SnapVisual()
        {
            _smoothPos = Root.position;
            _smoothYaw = Root.rotation.eulerAngles.y;
            _prevSmoothPos = _smoothPos;
            _hasSmooth = true;
        }

        void LateUpdate()
        {
            if (Root == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            // ---- read the sim target (AgentView/KindredView wrote it in Update)
            Vector3 targetPos = Root.position;
            float targetYaw = Root.rotation.eulerAngles.y;

            // Teleport / succession / first frame: snap instead of gliding.
            if (!_hasSmooth || (targetPos - _smoothPos).sqrMagnitude > 16f)
            {
                SnapVisual();
                targetPos = Root.position;
                targetYaw = Root.rotation.eulerAngles.y;
            }

            // ---- critically-damped-ish follow: position eases (accel/decel feel)
            float pk = 1f - Mathf.Exp(-10f * dt);
            _smoothPos = Vector3.Lerp(_smoothPos, targetPos, pk);

            // ---- turning: rate-limited yaw so direction changes carve, never pivot
            float maxTurn = 300f * dt; // deg per frame cap
            float want = Mathf.DeltaAngle(_smoothYaw, targetYaw);
            float applied = Mathf.Clamp(want, -maxTurn, maxTurn);
            float tk = 1f - Mathf.Exp(-7f * dt);
            float newYaw = _smoothYaw + applied * tk;
            float rawVel = Mathf.DeltaAngle(_smoothYaw, newYaw) / dt;
            _turnVel = Mathf.Lerp(_turnVel, rawVel, 1f - Mathf.Exp(-6f * dt));
            _smoothYaw = newYaw;

            Root.position = _smoothPos;
            Root.rotation = Quaternion.Euler(0f, _smoothYaw, 0f);

            // ---- visual speed (for gait sync + glow boost)
            float inst = (_smoothPos - _prevSmoothPos).magnitude / dt;
            _prevSmoothPos = _smoothPos;
            _visualSpeed = Mathf.Lerp(_visualSpeed, inst, 1f - Mathf.Exp(-5f * dt));
            _speedFactor = Mathf.Clamp01(_visualSpeed / 8f);

            // ---- bank into the turn: roll the body, proportional to turn rate × speed
            if (Body != null)
            {
                float bank = Mathf.Clamp(-_turnVel * (0.4f + _speedFactor) * 0.045f, -18f, 18f);
                bank = Mathf.Lerp(_lastBank, bank, 1f - Mathf.Exp(-8f * dt));
                _lastBank = bank;
                Body.localRotation = Body.localRotation * Quaternion.Euler(0f, 0f, bank);
            }

            // ---- gait playback rate: keep paws near the ground speed
            if (_anim == null && Root != null) _anim = Root.GetComponent<FoxAnimator>();
            if (_anim != null)
            {
                float contract = ContractSpeed(_anim.Current);
                float wantRate = contract > 0f
                    ? Mathf.Clamp(_visualSpeed / contract, 0.7f, 1.35f)
                    : 1f;
                _playbackRate = Mathf.Lerp(_playbackRate, wantRate, 1f - Mathf.Exp(-4f * dt));
                _anim.PlaybackRate = _playbackRate;
            }

            // ---- breathing core glow
            ApplyGlow(_baseGlow, 1f + 0.30f * _speedFactor, _speedFactor);
        }

        /// <summary>Locomotion contract speeds (m/s) from the animator's gait clips.</summary>
        static float ContractSpeed(FoxAnimator.Clip clip)
        {
            switch (clip)
            {
                case FoxAnimator.Clip.Walk: return FoxAnimator.WalkSpeed;
                case FoxAnimator.Clip.Trot: return FoxAnimator.TrotSpeed;
                case FoxAnimator.Clip.Run:  return FoxAnimator.GallopSpeed;
                default: return 0f;
            }
        }

        void ApplyGlow(float glow, float runBoost, float speedFactor)
        {
            if (_coreMat == null || CoreLight == null) return;
            float t = Time.time;
            // Healthy = lively pulse; dim/sick = slow, faint flicker.
            float rate = Mathf.Lerp(0.7f, 2.1f, glow);
            float breathe = 1f + 0.09f * Mathf.Sin(t * rate * Mathf.PI * 2f);
            float e = 5.0f * Mathf.Pow(glow, 1.4f) * breathe * runBoost;
            _coreMat.SetColor(EmissionColorId, _coreEmission * e);
            CoreLight.intensity = 14f * glow * breathe * (1f + 0.5f * speedFactor);
            CoreLight.range = (2f + 4f * glow) * _scale * (1f + 0.2f * speedFactor);
            CoreLight.enabled = glow > 0.003f;
        }

        // ------------------------------------------------------------ rig build

        Material _russet, _russetDark, _cream, _dark, _eye, _tailTip;

        void BuildRig(float s)
        {
            Shader lit = LitShader();
            _russet     = Mat(lit, new Color(0.63f, 0.30f, 0.12f));                       // deep russet-amber
            _russetDark = Mat(lit, new Color(0.40f, 0.19f, 0.08f));                       // muzzle / shading
            _cream      = Mat(lit, new Color(0.93f, 0.86f, 0.73f));                       // chest, cheeks
            _dark       = Mat(lit, new Color(0.15f, 0.10f, 0.08f));                       // socks, nose
            _eye        = Mat(lit, new Color(0.10f, 0.05f, 0.02f), new Color(1f, 0.55f, 0.15f), 0.7f);
            _tailTip    = Mat(lit, new Color(0.95f, 0.88f, 0.76f), new Color(1f, 0.70f, 0.40f), 0.25f);
            _coreMat    = Mat(lit, new Color(0.16f, 0.07f, 0.03f), _coreEmission, 5f);

            Root = Node(transform, "Root", Vector3.zero);
            MotionRoot = Node(Root, "MotionRoot", Vector3.zero);

            // ---- spine ----
            Body = Node(MotionRoot, "Body", V(0, 0.72f, -0.10f, s));
            Ball(Body, V(0, 0.02f, -0.03f, s), 0.150f * s, _russet, new Vector3(0.95f, 1.0f, 1.25f)); // pelvis
            // torso: tapered box, deep at the chest, lying along Z
            var torso = MeshNode(Body, "Torso",
                TaperedBoxMesh(0.24f * s, 0.30f * s, 0.46f * s, 0.30f * s, 0.34f * s), _russet);
            torso.transform.localPosition = V(0, 0.02f, -0.06f, s);
            // Euler(90,0,0) maps mesh +Y to +Z: the wide (wTop) end faces the chest.
            torso.transform.localRotation = Quaternion.Euler(90f, 0, 0);

            Chest = Node(Body, "Chest", V(0, 0.05f, 0.44f, s));
            Ball(Chest, V(0, 0, 0.02f, s), 0.155f * s, _russet, new Vector3(1.0f, 1.08f, 1.2f)); // ribcage
            Ball(Chest, V(0, -0.06f, 0.10f, s), 0.120f * s, _cream, new Vector3(0.8f, 1.0f, 0.7f)); // bib

            // ---- the luminous core ----
            ChestCore = Node(Chest, "ChestCore", V(0, -0.10f, 0.19f, s));
            Ball(ChestCore, Vector3.zero, 0.050f * s, _coreMat, Vector3.one);
            var lightGo = new GameObject("CoreLight");
            lightGo.transform.SetParent(ChestCore, false);
            CoreLight = lightGo.AddComponent<Light>();
            CoreLight.type = LightType.Point;
            CoreLight.color = new Color(1f, 0.62f, 0.30f);
            CoreLight.shadows = LightShadows.None;

            // ---- neck & head ----
            Neck = Node(Chest, "Neck", V(0, 0.13f, 0.14f, s));
            CapsuleBetween(Neck, "NeckMesh", V(0, 0, 0, s), V(0, 0.09f, 0.10f, s), 0.080f * s, _russet);

            Head = Node(Neck, "Head", V(0, 0.10f, 0.11f, s));
            Ball(Head, Vector3.zero, 0.105f * s, _russet, new Vector3(0.92f, 0.88f, 1.0f)); // skull
            Ball(Head, V(-0.055f, -0.045f, 0.045f, s), 0.055f * s, _cream, Vector3.one);    // cheek L
            Ball(Head, V( 0.055f, -0.045f, 0.045f, s), 0.055f * s, _cream, Vector3.one);    // cheek R
            // muzzle: dark wedge, wide at skull, narrow at nose
            var muzzle = MeshNode(Head, "Muzzle",
                TaperedBoxMesh(0.100f * s, 0.055f * s, 0.14f * s, 0.075f * s, 0.050f * s), _russetDark);
            muzzle.transform.localPosition = V(0, -0.045f, 0.06f, s);
            muzzle.transform.localRotation = Quaternion.Euler(90f, 0, 0); // narrow end forward
            Ball(Head, V(0, -0.005f, 0.205f, s), 0.022f * s, _dark, Vector3.one);           // nose
            Ball(Head, V(-0.052f, 0.025f, 0.088f, s), 0.020f * s, _eye, Vector3.one);       // eye L
            Ball(Head, V( 0.052f, 0.025f, 0.088f, s), 0.020f * s, _eye, Vector3.one);       // eye R

            // big upright triangular ears (L = +X)
            EarL = Node(Head, "EarL", V(-0.062f, 0.095f, -0.01f, s));
            BuildEar(EarL, s);
            EarR = Node(Head, "EarR", V(0.062f, 0.095f, -0.01f, s));
            BuildEar(EarR, s);

            // ---- tail: long, full, three segments, slight upward curve at rest ----
            TailBase = Node(Body, "TailBase", V(0, 0.09f, -0.16f, s));
            TailBase.localRotation = Quaternion.Euler(12f, 0, 0);
            CapsuleBetween(TailBase, "TailSeg1", V(0, 0, 0, s), V(0, 0.03f, -0.20f, s), 0.062f * s, _russet);
            TailMid = Node(TailBase, "TailMid", V(0, 0.03f, -0.20f, s));
            TailMid.localRotation = Quaternion.Euler(10f, 0, 0);
            CapsuleBetween(TailMid, "TailSeg2", V(0, 0, 0, s), V(0, 0.035f, -0.19f, s), 0.075f * s, _russet);
            TailTip = Node(TailMid, "TailTip", V(0, 0.035f, -0.19f, s));
            TailTip.localRotation = Quaternion.Euler(14f, 0, 0);
            CapsuleBetween(TailTip, "TailSeg3", V(0, 0, 0, s), V(0, 0.03f, -0.17f, s), 0.088f * s, _tailTip);

            // ---- legs ----
            BuildLeg("FL", +0.105f, 0.38f, false, s, out LegFL_Upper, out LegFL_Lower, out LegFL_Paw);
            BuildLeg("FR", -0.105f, 0.38f, false, s, out LegFR_Upper, out LegFR_Lower, out LegFR_Paw);
            BuildLeg("BL", +0.110f, -0.12f, true, s, out LegBL_Upper, out LegBL_Lower, out LegBL_Paw);
            BuildLeg("BR", -0.110f, -0.12f, true, s, out LegBR_Upper, out LegBR_Lower, out LegBR_Paw);
        }

        void BuildLeg(string tag, float x, float z, bool rear, float s,
                      out Transform upper, out Transform lower, out Transform paw)
        {
            float y = -0.04f;
            upper = Node(Body, "Leg" + tag + "_Upper", V(x, y, z, s));
            float upperLen = rear ? 0.28f : 0.30f;
            float lowerLen = rear ? 0.30f : 0.28f;
            float rU = rear ? 0.065f : 0.055f;

            if (rear) // haunch mass stays with the body, not the swinging thigh
                Ball(Body, V(x, y + 0.05f, z - 0.03f, s), 0.115f * s, _russet, new Vector3(0.75f, 1.05f, 1.25f));
            else
                Ball(Body, V(x, y + 0.02f, z, s), 0.075f * s, _russet, new Vector3(0.8f, 1.0f, 1.1f));

            CapsuleBetween(upper, "UpperMesh", Vector3.zero, V(0, -upperLen, 0, s), rU * s, _russet);

            lower = Node(upper, "Leg" + tag + "_Lower", V(0, -upperLen, 0, s));
            CapsuleBetween(lower, "LowerMesh", Vector3.zero, V(0, -lowerLen, 0, s), 0.040f * s, _russet);
            // dark "sock" over the lower half of the cannon
            CapsuleBetween(lower, "SockMesh", V(0, -lowerLen * 0.40f, 0, s), V(0, -lowerLen, 0, s), 0.036f * s, _dark);

            paw = Node(lower, "Leg" + tag + "_Paw", V(0, -lowerLen, 0, s));
            Ball(paw, V(0, -0.030f, 0.025f, s), 0.048f * s, _dark, new Vector3(1f, 0.65f, 1.4f));

            if (rear) // digitigrade rest: thigh slightly back, hock slightly forward
            {
                upper.localRotation = Quaternion.Euler(7f, 0, 0);
                lower.localRotation = Quaternion.Euler(-9f, 0, 0);
                paw.localRotation = Quaternion.Euler(2f, 0, 0);
            }
        }

        void BuildEar(Transform pivot, float s)
        {
            var outer = MeshNode(pivot, "EarOuter", PrismMesh(0.075f * s, 0.160f * s, 0.045f * s), _russet);
            var inner = MeshNode(pivot, "EarInner", PrismMesh(0.045f * s, 0.100f * s, 0.020f * s), _cream);
            inner.transform.localPosition = new Vector3(0, 0.012f * s, 0.014f * s);
        }

        // ---------------------------------------------------------------- helpers

        static Vector3 V(float x, float y, float z, float s) => new Vector3(x * s, y * s, z * s);

        static Transform Node(Transform parent, string name, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        static Shader LitShader()
        {
            if (UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline != null)
            {
                var urp = Shader.Find("Universal Render Pipeline/Lit");
                if (urp != null) return urp;
            }
            return Shader.Find("Standard");
        }

        static Material Mat(Shader lit, Color albedo)
        {
            var m = new Material(lit);
            m.color = albedo;
            m.SetColor("_EmissionColor", Color.black);
            return m;
        }

        static Material Mat(Shader lit, Color albedo, Color emission, float intensity)
        {
            var m = Mat(lit, albedo);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return m;
        }

        static GameObject Prim(Transform parent, string name, PrimitiveType type, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            return go;
        }

        static void Ball(Transform parent, Vector3 localPos, float r, Material mat, Vector3 squash)
        {
            var go = Prim(parent, "ball", PrimitiveType.Sphere, mat);
            go.transform.localPosition = localPos;
            go.transform.localScale = squash * (r * 2f);
        }

        static void CapsuleBetween(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material mat)
        {
            var go = Prim(parent, name, PrimitiveType.Capsule, mat);
            Vector3 d = b - a;
            float dist = d.magnitude;
            go.transform.localPosition = (a + b) * 0.5f;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d / Mathf.Max(dist, 1e-6f));
            // default capsule: diameter 1, total height 2
            go.transform.localScale = new Vector3(radius * 2f, (dist + radius * 2f) * 0.5f, radius * 2f);
        }

        static GameObject MeshNode(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return go;
        }

        /// <summary>Box tapered along local Y: wide (wBot,dBot) at y=0, narrow (wTop,dTop) at y=h.</summary>
        static Mesh TaperedBoxMesh(float wBot, float wTop, float h, float dBot, float dTop)
        {
            float xb0 = wBot / 2, xb1 = wTop / 2, zb0 = dBot / 2, zb1 = dTop / 2;
            var v = new Vector3[]
            {
                new Vector3(-xb0, 0, -zb0), new Vector3(xb0, 0, -zb0),
                new Vector3(xb0, 0, zb0),   new Vector3(-xb0, 0, zb0),
                new Vector3(-xb1, h, -zb1), new Vector3(xb1, h, -zb1),
                new Vector3(xb1, h, zb1),   new Vector3(-xb1, h, zb1),
            };
            var t = new int[]
            {
                3, 2, 6, 3, 6, 7,   // front  (+Z)
                1, 0, 4, 1, 4, 5,   // back   (-Z)
                2, 1, 5, 2, 5, 6,   // right  (+X)
                0, 3, 7, 0, 7, 4,   // left   (-X)
                7, 6, 5, 7, 5, 4,   // top    (+Y)
                0, 1, 2, 0, 2, 3,   // bottom (-Y)
            };
            return BuildMesh(v, t);
        }

        /// <summary>Triangular prism (ear): base triangle in XZ at y=0, apex ridge at y=h.</summary>
        static Mesh PrismMesh(float w, float h, float d)
        {
            float x = w / 2, z = d / 2;
            var v = new Vector3[]
            {
                new Vector3(-x, 0, z), new Vector3(x, 0, z), new Vector3(0, h, z),    // front 0,1,2
                new Vector3(-x, 0, -z), new Vector3(x, 0, -z), new Vector3(0, h, -z),  // back  3,4,5
            };
            var t = new int[]
            {
                0, 1, 2,       // front (+Z)
                3, 5, 4,       // back  (-Z)
                0, 5, 3, 0, 2, 5,  // left edge quad  (-X)
                1, 4, 5, 1, 5, 2,  // right edge quad (+X)
                0, 4, 1, 0, 3, 4,  // base (-Y)
            };
            return BuildMesh(v, t);
        }

        static Mesh BuildMesh(Vector3[] v, int[] t)
        {
            var m = new Mesh();
            m.vertices = v;
            m.triangles = t;
            var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++) uv[i] = new Vector2(v[i].x + v[i].z, v[i].y);
            m.uv = uv;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
