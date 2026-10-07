// Solace.Unity — global colorblind filter.
//
// When AccessibilitySettings.ColorblindMode is non-zero, applies the CVD
// simulation filter to the main camera every frame (outside photo mode).
// Uses the same manual render + blit pattern as PhotoMode: the camera
// renders to a RenderTexture, then Graphics.Blit applies the grade shader
// (with _CvdMode set) to the screen. ScreenSpaceOverlay UI draws after,
// so the HUD is unaffected — only the 3D world is filtered.
//
// Self-bootstrapping via RuntimeInitializeOnLoadMethod. Presentation only.
using System.Collections;
using UnityEngine;

namespace Solace.Unity
{
    public class CvdFilter : MonoBehaviour
    {
        private Material _gradeMat;
        private RenderTexture _rt;
        private Coroutine _loop;
        private Camera _cam;
        private bool _camWasEnabled = true;
        private static readonly int CvdId = Shader.PropertyToID("_CvdMode");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoEnsure()
        {
            if (Object.FindObjectOfType<CvdFilter>() != null) return;
            var go = new GameObject("CvdFilter");
            go.AddComponent<CvdFilter>();
        }

        private void Update()
        {
            int mode = AccessibilitySettings.ColorblindMode;
            var boot = GameBootstrap.Instance;
            Camera cam = boot != null ? boot.Camera.GetComponent<Camera>() : null;

            // PhotoMode owns the camera while active — stay out of its way.
            var photo = FindObjectOfType<PhotoMode>();
            bool photoActive = photo != null && IsPhotoModeActive(photo);
            if (photoActive) mode = 0;

            if (mode != 0 && cam != null)
            {
                if (_cam != cam) { StopLoop(); _cam = cam; }
                if (!EnsureMaterial()) return;
                _gradeMat.SetFloat(CvdId, (float)mode);
                // Neutral grade: CVD only, no tint/sat/con/bri/vig changes.
                _gradeMat.SetColor("_Tint", Color.white);
                _gradeMat.SetFloat("_Saturation", 1f);
                _gradeMat.SetFloat("_Contrast", 1f);
                _gradeMat.SetFloat("_Brightness", 0f);
                _gradeMat.SetFloat("_Vignette", 0f);
                StartLoop();
            }
            else
            {
                StopLoop();
            }
        }

        private static bool IsPhotoModeActive(PhotoMode photo)
        {
            // PhotoMode._active is private; infer from its camera takeover:
            // while active it disables the ObserverCamera.
            var boot = GameBootstrap.Instance;
            return boot != null && boot.Camera != null && !boot.Camera.enabled;
        }

        private bool EnsureMaterial()
        {
            if (_gradeMat != null) return true;
            Shader s = Shader.Find("Solace/PhotoGrade");
            if (s == null)
            {
                Debug.LogWarning("[Solace] PhotoGrade shader missing — colorblind filter unavailable.");
                return false;
            }
            _gradeMat = new Material(s);
            return true;
        }

        private void StartLoop()
        {
            if (_loop != null || _cam == null) return;
            _camWasEnabled = _cam.enabled;
            _cam.enabled = false; // manual render via loop
            _loop = StartCoroutine(FilterLoop());
        }

        private void StopLoop()
        {
            if (_loop != null)
            {
                StopCoroutine(_loop);
                _loop = null;
            }
            if (_cam != null)
            {
                _cam.enabled = _camWasEnabled;
                _cam = null;
            }
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
        }

        private IEnumerator FilterLoop()
        {
            while (_cam != null && AccessibilitySettings.ColorblindMode != 0)
            {
                yield return new WaitForEndOfFrame();
                if (_cam == null || _gradeMat == null) break;
                if (_rt == null || _rt.width != Screen.width || _rt.height != Screen.height)
                {
                    if (_rt != null) { _rt.Release(); Destroy(_rt); }
                    _rt = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
                    _rt.Create();
                }
                _cam.targetTexture = _rt;
                _cam.Render();
                _cam.targetTexture = null;
                Graphics.Blit(_rt, (RenderTexture)null, _gradeMat);
            }
            StopLoop();
        }

        private void OnDestroy()
        {
            StopLoop();
            if (_gradeMat != null) Destroy(_gradeMat);
        }
    }
}
