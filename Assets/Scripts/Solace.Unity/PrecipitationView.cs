// Solace.Unity — precipitation: gentle rain and drifting snow.
//
// Pure presentation: no sim state, no AI. Reads weather + season from the
// sim each frame and renders instanced streaks (rain) or flakes (snow) in
// a volume that follows the camera. Calming by design: soft speeds, muted
// colors, smooth intensity fades so weather never pops.
//
// Self-bootstrapping via RuntimeInitializeOnLoadMethod (same pattern as
// AmbientLife): no changes to existing files required.
using UnityEngine;
using UnityEngine.Rendering;
using Solace.Core;

namespace Solace.Unity
{
    public class PrecipitationView : MonoBehaviour, ISimView
    {
        private const int RainCount = 420;
        private const int StormCount = 680;
        private const int SnowCount = 320;

        private const float VolumeW = 64f;   // XZ extent of the fall volume
        private const float VolumeH = 30f;   // height of the fall volume

        private const float RainFallSpeed = 17f;
        private const float StormFallSpeed = 24f;
        private const float SnowFallSpeed = 2.1f;

        // -- meshes / materials (shared, built once) --------------------------

        private static bool _meshesBuilt;
        private static Mesh _streakMesh;
        private static Mesh _flakeMesh;

        private Material _rainMat;
        private Material _snowMat;

        // -- particle state ----------------------------------------------------

        private struct Drop
        {
            public float x, y, z;   // position: x/z relative to camera, y above volume base
            public float speed;     // fall speed multiplier
            public float phase;     // sway phase (snow)
            public float swayAmp;   // sway amplitude (snow)
        }

        private readonly Drop[] _drops = new Drop[StormCount]; // max of all modes
        private readonly Matrix4x4[] _mats = new Matrix4x4[StormCount];

        private bool _raining;
        private bool _storming;
        private bool _snowing;
        private float _intensity;        // 0..1, eased toward target
        private float _targetIntensity;

        private float _camX, _camZ;
        private int _builtSeed = -1;
        private bool _built;
        private WorldData _world;

        private System.Random _rng = new System.Random();

        // -- bootstrap ----------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoEnsure()
        {
            if (Object.FindFirstObjectByType<PrecipitationView>() != null) return;
            var go = new GameObject("PrecipitationView");
            go.AddComponent<PrecipitationView>();
        }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Sim == null) return;
            if (!_built || boot.Seed != _builtSeed)
            {
                _builtSeed = boot.Seed;
                Build(boot.Sim.State.World);
                _built = true;
            }
            SyncFromState(boot.Sim.State);
            if (boot.Camera != null)
            {
                _camX = boot.Camera.transform.position.x;
                _camZ = boot.Camera.transform.position.z;
            }
            Animate(Time.deltaTime);
        }

        private void OnDestroy()
        {
            var boot = GameBootstrap.Instance;
            if (boot != null) boot.UnregisterView(this);
        }

        public void SyncFromState(GameState state)
        {
            Weather w = state.Weather;
            _raining = w == Weather.Rain;
            _storming = w == Weather.Storm;
            // In winter, precipitation falls as snow instead of rain.
            bool winter = SeasonSystem.Current(state) == Season.Winter;
            _snowing = winter && (_raining || _storming);
            if (_snowing) { _raining = false; _storming = false; }

            float target = 0f;
            if (_raining) target = 0.65f;
            else if (_storming) target = 1f;
            else if (_snowing) target = 0.8f;
            _targetIntensity = target;
        }

        // -- build --------------------------------------------------------------

        private void Build(WorldData world)
        {
            _world = world;
            EnsureMeshes();
            // Soft, desaturated: rain reads as pale silver-blue, snow as warm white.
            _rainMat = MaterialFactory.Unlit(new Color(0.62f, 0.72f, 0.82f));
            _snowMat = MaterialFactory.Unlit(new Color(0.92f, 0.94f, 0.97f));
            _rng = new System.Random(world.Seed ^ unchecked((int)0x9E3779B9));
            for (int i = 0; i < _drops.Length; i++) Respawn(i, true);
            _intensity = 0f;
            GameBootstrap.Instance.RegisterView(this);
        }

        private static void EnsureMeshes()
        {
            if (_meshesBuilt) return;
            _meshesBuilt = true;
            _streakMesh = MeshFactory.Streak();
            // Snowflake: a tiny cube, visible from every angle, cheap.
            _flakeMesh = MeshFactory.GetPrimitive(PrimitiveType.Cube);
        }

        private void Respawn(int i, bool anywhere)
        {
            Drop d;
            d.x = (float)(_rng.NextDouble() * 2.0 - 1.0) * VolumeW * 0.5f;
            d.z = (float)(_rng.NextDouble() * 2.0 - 1.0) * VolumeW * 0.5f;
            d.y = anywhere
                ? (float)_rng.NextDouble() * VolumeH
                : VolumeH + (float)_rng.NextDouble() * 4f;
            d.speed = 0.85f + (float)_rng.NextDouble() * 0.3f;
            d.phase = (float)_rng.NextDouble() * Mathf.PI * 2f;
            d.swayAmp = 0.4f + (float)_rng.NextDouble() * 0.8f;
            _drops[i] = d;
        }

        // -- animate ------------------------------------------------------------

        private void Animate(float dt)
        {
            // Ease intensity so precipitation fades in/out with the weather.
            float rate = _targetIntensity > _intensity ? 0.5f : 0.35f;
            _intensity = Mathf.MoveTowards(_intensity, _targetIntensity, rate * dt);
            if (_intensity <= 0.001f) return;

            bool snow = _snowing;
            int max = snow ? SnowCount : (_storming ? StormCount : RainCount);
            float fallSpeed = snow ? SnowFallSpeed : (_storming ? StormFallSpeed : RainFallSpeed);
            float t = Time.time;

            int n = Mathf.Min(Mathf.CeilToInt(max * _intensity), _drops.Length);

            // Volume base follows the terrain under the camera.
            float half = _world != null ? _world.HalfSize - 1f : 1000f;
            float ccx = Mathf.Clamp(_camX, -half, half);
            float ccz = Mathf.Clamp(_camZ, -half, half);
            float baseY = _world != null ? _world.SampleHeight(ccx, ccz) : 0f;

            // Wind slant for rain: lean the streaks slightly.
            Quaternion slant = snow ? Quaternion.identity : Quaternion.Euler(0f, 0f, -6f);
            Vector3 streakScale = new Vector3(1f, _storming ? 1.35f : 1f, 1f);
            Vector3 flakeScale = new Vector3(0.13f, 0.13f, 0.13f);

            for (int i = 0; i < n; i++)
            {
                Drop d = _drops[i];
                d.y -= fallSpeed * d.speed * dt;
                if (snow)
                {
                    d.x += Mathf.Sin(t * 0.9f + d.phase) * d.swayAmp * dt;
                    d.z += Mathf.Cos(t * 0.7f + d.phase * 1.3f) * d.swayAmp * 0.6f * dt;
                }
                else
                {
                    d.x += 1.6f * dt; // gentle wind drift
                }

                // Keep the drop inside the camera-following box.
                if (d.x > VolumeW * 0.5f) d.x -= VolumeW;
                else if (d.x < -VolumeW * 0.5f) d.x += VolumeW;
                if (d.z > VolumeW * 0.5f) d.z -= VolumeW;
                else if (d.z < -VolumeW * 0.5f) d.z += VolumeW;

                float wx = Mathf.Clamp(_camX + d.x, -half, half);
                float wz = Mathf.Clamp(_camZ + d.z, -half, half);
                float wy = baseY + d.y;
                float groundY = _world != null ? _world.SampleHeight(wx, wz) : 0f;
                float aboveGround = wy - groundY;

                // Recycle once the drop reaches the ground.
                if (aboveGround < 0.4f)
                {
                    Respawn(i, false);
                    d = _drops[i];
                    wx = Mathf.Clamp(_camX + d.x, -half, half);
                    wz = Mathf.Clamp(_camZ + d.z, -half, half);
                    wy = baseY + d.y;
                    aboveGround = wy - (_world != null ? _world.SampleHeight(wx, wz) : 0f);
                }

                // Melt into the ground over the last ~2 m: soft landing, no pop.
                float fade = Mathf.Clamp01((aboveGround - 0.4f) / 2f);
                Vector3 scale = snow ? flakeScale : streakScale;
                scale *= 0.35f + 0.65f * fade;

                _mats[i] = Matrix4x4.TRS(new Vector3(wx, wy, wz), slant, scale);
                _drops[i] = d;
            }

            MaterialFactory.DrawInstanced(
                snow ? _flakeMesh : _streakMesh,
                snow ? _snowMat : _rainMat,
                _mats, n, ShadowCastingMode.Off, false);
        }
    }
}
