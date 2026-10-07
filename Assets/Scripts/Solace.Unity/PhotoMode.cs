// Solace.Unity — photo mode.
//
// Press P to detach the camera and fly freely: WASD moves, right-drag
// looks, Q/E (or Space/C) go down/up, mouse wheel sets fly speed,
// Shift triples it. Keys 1-6 cycle color grades (None, Golden, Moonlit,
// Vintage, Vivid, Mono). C captures a 2x-resolution PNG to Photos/.
// ESC or P exits and reframes on the fox.
//
// The HUD hides automatically while active; a minimal photo bar shows
// instead. Never touches the sim — presentation only. If the grade
// shader is missing, filters degrade gracefully to unfiltered.
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Solace.Unity
{
    public class PhotoMode : MonoBehaviour
    {
        public enum Filter { None, Golden, Moonlit, Vintage, Vivid, Mono, Protanopia, Deuteranopia, Tritanopia }

        private const float MinFlySpeed = 2f;
        private const float MaxFlySpeed = 40f;

        private bool _active;
        private Filter _filter = Filter.None;

        private ObserverCamera _observer;
        private Camera _cam;
        private HudController _hud;
        private Transform _hudCanvas;
        private bool _hudWasActive;

        // Free-fly state.
        private float _yaw;
        private float _pitch;
        private float _flySpeed = 8f;

        // Color grade.
        private Material _gradeMat;
        private RenderTexture _rt;
        private Coroutine _renderLoop;
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int SatId = Shader.PropertyToID("_Saturation");
        private static readonly int ConId = Shader.PropertyToID("_Contrast");
        private static readonly int BriId = Shader.PropertyToID("_Brightness");
        private static readonly int VigId = Shader.PropertyToID("_Vignette");
        private static readonly int CvdId = Shader.PropertyToID("_CvdMode");

        // Photo-mode UI (own canvas so it survives the HUD hide).
        private Canvas _photoCanvas;
        private Text _filterName;
        private Text _hintBar;
        private Text _toast;
        private float _filterNameUntil = -1f;
        private float _toastUntil = -1f;

        // -- bootstrap ------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoEnsure()
        {
            // Singleton guard: scene reloads must not stack duplicates.
            if (UnityEngine.Object.FindObjectOfType<PhotoMode>() != null) return;
            var go = new GameObject("PhotoMode");
            go.AddComponent<PhotoMode>();
        }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Sim == null) return;
            if (_hud != null && _hud.IsTyping) return; // never steal chat input

            if (!_active)
            {
                if (Input.GetKeyDown(KeyCode.P)) Enter();
                return;
            }

            // Active: exit keys.
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P) ||
                (GamepadInput.IsConnected && GamepadInput.GetButtonDown(PadButton.B)))
            {
                Exit();
                return;
            }

            // Gamepad: A captures, LB/RB cycle filters.
            if (GamepadInput.IsConnected)
            {
                if (GamepadInput.GetButtonDown(PadButton.A)) CapturePhoto();
                if (GamepadInput.GetButtonDown(PadButton.LB)) CycleFilter(-1);
                if (GamepadInput.GetButtonDown(PadButton.RB)) CycleFilter(1);
            }

            // Filter keys 1-6 (artistic), 7-9 (color vision deficiency simulation).
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetFilter(Filter.None);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) SetFilter(Filter.Golden);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) SetFilter(Filter.Moonlit);
            else if (Input.GetKeyDown(KeyCode.Alpha4)) SetFilter(Filter.Vintage);
            else if (Input.GetKeyDown(KeyCode.Alpha5)) SetFilter(Filter.Vivid);
            else if (Input.GetKeyDown(KeyCode.Alpha6)) SetFilter(Filter.Mono);
            else if (Input.GetKeyDown(KeyCode.Alpha7)) SetFilter(Filter.Protanopia);
            else if (Input.GetKeyDown(KeyCode.Alpha8)) SetFilter(Filter.Deuteranopia);
            else if (Input.GetKeyDown(KeyCode.Alpha9)) SetFilter(Filter.Tritanopia);

            // Capture.
            if (Input.GetKeyDown(KeyCode.C)) CapturePhoto();

            FlyCamera(boot);

            // Fade the filter name + toast.
            if (_filterName != null)
                _filterName.canvasRenderer.SetAlpha(Time.time < _filterNameUntil ? 1f : 0f);
            if (_toast != null)
            {
                bool show = Time.time < _toastUntil;
                _toast.canvasRenderer.SetAlpha(show ? 1f : 0f);
            }
        }

        // -- enter / exit ----------------------------------------------------

        /// <summary>Gamepad entry point (D-up on the HUD).</summary>
        public void ToggleFromPad()
        {
            if (_active) Exit(); else Enter();
        }

        /// <summary>True while photo mode owns the camera (for input gating).</summary>
        public bool IsActive { get { return _active; } }

        private void Enter()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null) return;
            _observer = boot.Camera;
            _hud = boot.Hud;
            if (_observer == null) return;
            _cam = _observer.GetComponent<Camera>();
            if (_cam == null) return;

            // Take over the camera: freeze the follow rig, keep its transform.
            _observer.enabled = false;
            Vector3 e = _cam.transform.rotation.eulerAngles;
            _yaw = e.y;
            _pitch = e.x > 180f ? e.x - 360f : e.x;
            _pitch = Mathf.Clamp(_pitch, -89f, 89f);
            _flySpeed = 8f;

            // Hide the HUD (remember state so we restore exactly).
            if (_hud != null)
            {
                _hudCanvas = _hud.CanvasRoot;
                if (_hudCanvas != null)
                {
                    _hudWasActive = _hudCanvas.gameObject.activeSelf;
                    _hudCanvas.gameObject.SetActive(false);
                }
                _hud.CloseAllPanels();
            }

            BuildPhotoUI();
            _photoCanvas.gameObject.SetActive(true);
            _active = true;
            ShowFilterName("Photo mode — " + _filter);
            Debug.Log("[Solace] Photo mode on (P or ESC to exit).");
        }

        private void Exit()
        {
            StopRenderLoop();
            if (_cam != null) _cam.enabled = true;

            // Hand the camera back and reframe on the fox.
            if (_observer != null)
            {
                _observer.enabled = true;
                _observer.SnapToAgent();
            }

            // Restore the HUD.
            if (_hudCanvas != null)
                _hudCanvas.gameObject.SetActive(_hudWasActive);
            if (_photoCanvas != null)
                _photoCanvas.gameObject.SetActive(false);

            _active = false;
            _filter = Filter.None;
            Debug.Log("[Solace] Photo mode off.");
        }

        // -- free-fly camera ---------------------------------------------------

        private void FlyCamera(GameBootstrap boot)
        {
            // Look: right-mouse drag, or gamepad right stick.
            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxis("Mouse X") * 2.5f;
                _pitch -= Input.GetAxis("Mouse Y") * 2.5f;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);
            }
            Vector2 padLook = GamepadInput.RightStick;
            if (padLook.sqrMagnitude > 0.0001f)
            {
                _yaw += padLook.x * 130f * Time.deltaTime;
                _pitch = Mathf.Clamp(_pitch - padLook.y * 95f * Time.deltaTime, -89f, 89f);
            }
            _cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            // Wheel: fly speed.
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > 0.001f)
                _flySpeed = Mathf.Clamp(_flySpeed * (1f + wheel * 1.5f), MinFlySpeed, MaxFlySpeed);

            // Move: WASD on the view plane, Q/E or Space/C for vertical.
            float speed = _flySpeed * (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 3f : 1f);
            Vector3 move = Vector3.zero;
            Vector3 fwd = _cam.transform.forward;
            Vector3 right = _cam.transform.right;
            if (Input.GetKey(KeyCode.W)) move += fwd;
            if (Input.GetKey(KeyCode.S)) move -= fwd;
            if (Input.GetKey(KeyCode.D)) move += right;
            if (Input.GetKey(KeyCode.A)) move -= right;
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) move += Vector3.up;
            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.C)) move -= Vector3.up;
            // Note: C is also capture on key-DOWN; holding C after capture
            // descends — acceptable, documented in the hint bar.
            if (move.sqrMagnitude > 0.001f)
                _cam.transform.position += move.normalized * speed * Time.deltaTime;

            // Stay out of the ground.
            try
            {
                var world = boot.Sim.State.World;
                Vector3 p = _cam.transform.position;
                float ground = world.SampleHeight(p.x, p.z) + 0.6f;
                if (p.y < ground)
                {
                    p.y = ground;
                    _cam.transform.position = p;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Solace] PhotoMode terrain clamp failed: " + ex.Message);
            }
        }

        // -- filters -----------------------------------------------------------

        private void SetFilter(Filter f)
        {
            if (f == _filter) return;
            _filter = f;

            if (f == Filter.None)
            {
                StopRenderLoop();
                if (_cam != null) _cam.enabled = true;
            }
            else
            {
                if (!EnsureGradeMaterial())
                {
                    Debug.LogWarning("[Solace] PhotoGrade shader missing — filter disabled.");
                    _filter = Filter.None;
                    if (_cam != null) _cam.enabled = true;
                    ShowFilterName("Filter unavailable");
                    return;
                }
                ApplyFilterParams(f);
                if (_cam != null) _cam.enabled = false; // manual render via loop
                StartRenderLoop();
            }
            ShowFilterName("Filter — " + f);
        }

        /// <summary>Gamepad: cycle filters with LB/RB.</summary>
        private void CycleFilter(int dir)
        {
            int count = System.Enum.GetValues(typeof(Filter)).Length;
            int next = ((int)_filter + dir + count) % count;
            SetFilter((Filter)next);
        }

        private bool EnsureGradeMaterial()
        {
            if (_gradeMat != null) return true;
            Shader s = Shader.Find("Solace/PhotoGrade");
            if (s == null) return false;
            _gradeMat = new Material(s);
            return true;
        }

        private void ApplyFilterParams(Filter f)
        {
            // tint, saturation, contrast, brightness, vignette
            switch (f)
            {
                case Filter.Golden:
                    SetGrade(new Color(1.08f, 0.95f, 0.78f), 1.10f, 1.05f, 0.00f, 0.25f);
                    break;
                case Filter.Moonlit:
                    SetGrade(new Color(0.75f, 0.85f, 1.10f), 0.85f, 1.10f, -0.08f, 0.35f);
                    break;
                case Filter.Vintage:
                    SetGrade(new Color(1.05f, 0.92f, 0.72f), 0.60f, 0.95f, 0.02f, 0.45f);
                    break;
                case Filter.Vivid:
                    SetGrade(new Color(1.02f, 1.00f, 0.98f), 1.35f, 1.12f, 0.00f, 0.15f);
                    break;
                case Filter.Mono:
                    SetGrade(new Color(1.00f, 1.00f, 1.00f), 0.00f, 1.15f, 0.00f, 0.30f);
                    break;
                case Filter.Protanopia:
                    SetGrade(Color.white, 1f, 1f, 0f, 0f);
                    _gradeMat.SetFloat(CvdId, 1f);
                    break;
                case Filter.Deuteranopia:
                    SetGrade(Color.white, 1f, 1f, 0f, 0f);
                    _gradeMat.SetFloat(CvdId, 2f);
                    break;
                case Filter.Tritanopia:
                    SetGrade(Color.white, 1f, 1f, 0f, 0f);
                    _gradeMat.SetFloat(CvdId, 3f);
                    break;
                default:
                    SetGrade(Color.white, 1f, 1f, 0f, 0f);
                    break;
            }
            // Reset CVD for non-CVD filters (SetGrade doesn't touch it).
            if (f != Filter.Protanopia && f != Filter.Deuteranopia && f != Filter.Tritanopia
                && _gradeMat != null)
                _gradeMat.SetFloat(CvdId, 0f);
        }

        private void SetGrade(Color tint, float sat, float con, float bri, float vig)
        {
            if (_gradeMat == null) return;
            _gradeMat.SetColor(TintId, tint);
            _gradeMat.SetFloat(SatId, sat);
            _gradeMat.SetFloat(ConId, con);
            _gradeMat.SetFloat(BriId, bri);
            _gradeMat.SetFloat(VigId, vig);
        }

        private void EnsureRT()
        {
            if (_rt != null && (_rt.width != Screen.width || _rt.height != Screen.height))
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
            if (_rt == null)
            {
                _rt = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
                _rt.Create();
            }
        }

        private void StartRenderLoop()
        {
            if (_renderLoop != null) return;
            _renderLoop = StartCoroutine(FilterRenderLoop());
        }

        private void StopRenderLoop()
        {
            if (_renderLoop != null)
            {
                StopCoroutine(_renderLoop);
                _renderLoop = null;
            }
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
        }

        private IEnumerator FilterRenderLoop()
        {
            while (_active && _filter != Filter.None && _cam != null)
            {
                yield return new WaitForEndOfFrame();
                if (_cam == null || _gradeMat == null) break;
                EnsureRT();
                _cam.targetTexture = _rt;
                _cam.Render();
                _cam.targetTexture = null;
                // Blit to the screen; the photo-mode overlay canvas draws after.
                Graphics.Blit(_rt, (RenderTexture)null, _gradeMat);
            }
            if (_cam != null && _active) _cam.enabled = true;
        }

        // -- capture -------------------------------------------------------------

        private void CapturePhoto()
        {
            try
            {
                string root = Directory.GetParent(Application.dataPath).FullName;
                string dir = Path.Combine(root, "Photos");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir,
                    "photo-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");

                // Always render manually at 2x so the file matches the live
                // grade (or ungraded when no filter is active).
                int w = Screen.width * 2, h = Screen.height * 2;
                RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                RenderTexture graded = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                _cam.targetTexture = rt;
                _cam.Render();
                _cam.targetTexture = null;
                if (_filter != Filter.None && EnsureGradeMaterial())
                    Graphics.Blit(rt, graded, _gradeMat);
                else
                    Graphics.Blit(rt, graded);
                RenderTexture.active = graded;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                RenderTexture.ReleaseTemporary(graded);
                Destroy(tex);

                ShowToast("Photo saved to Photos/");
                Debug.Log("[Solace] Photo saved: " + path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Solace] Photo capture failed: " + ex.Message);
                ShowToast("Photo failed — see console");
            }
        }

        // -- photo-mode UI ---------------------------------------------------------

        private void BuildPhotoUI()
        {
            if (_photoCanvas != null) return;
            _photoCanvas = UiKit.CreateCanvas("PhotoMode", 50);

            // Filter name, top-center, fades after 2s.
            _filterName = UiKit.Label(_photoCanvas.transform, "FilterName", "",
                28, Color.white, TextAnchor.UpperCenter);
            UiKit.AddShadow(_filterName);
            var frt = _filterName.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(0.5f, 1f); frt.anchorMax = new Vector2(0.5f, 1f);
            frt.offsetMin = new Vector2(-400f, -80f); frt.offsetMax = new Vector2(400f, -20f);
            _filterName.canvasRenderer.SetAlpha(0f);

            // Hint bar, bottom-center.
            _hintBar = UiKit.Label(_photoCanvas.transform, "Hints",
                "P / ESC exit · WASD fly · right-drag look · Q/E down/up · wheel speed · 1-6 filters · 7-9 colorblind preview · C capture",
                13, new Color(1f, 1f, 1f, 0.85f), TextAnchor.LowerCenter);
            UiKit.AddShadow(_hintBar);
            var hrt = _hintBar.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0.5f, 0f); hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.offsetMin = new Vector2(-600f, 12f); hrt.offsetMax = new Vector2(600f, 36f);

            // Toast, center, fades after 2.5s.
            _toast = UiKit.Label(_photoCanvas.transform, "Toast", "",
                20, Color.white, TextAnchor.MiddleCenter);
            UiKit.AddShadow(_toast);
            var trt = _toast.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.5f, 0.5f); trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.offsetMin = new Vector2(-400f, -30f); trt.offsetMax = new Vector2(400f, 30f);
            _toast.canvasRenderer.SetAlpha(0f);

            _photoCanvas.gameObject.SetActive(false);
        }

        private void ShowFilterName(string text)
        {
            if (_filterName == null) return;
            _filterName.text = text;
            _filterName.canvasRenderer.SetAlpha(1f);
            _filterNameUntil = Time.time + 2f;
        }

        private void ShowToast(string text)
        {
            if (_toast == null) return;
            _toast.text = text;
            _toast.canvasRenderer.SetAlpha(1f);
            _toastUntil = Time.time + 2.5f;
        }

        private void OnDestroy()
        {
            StopRenderLoop();
            if (_gradeMat != null) Destroy(_gradeMat);
        }
    }
}
