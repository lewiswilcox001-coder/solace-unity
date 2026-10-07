// Solace.Unity — code-built UGUI helpers (legacy Text, no TextMeshPro).
// All HUD and panels are constructed in code so Main.unity stays empty.
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Solace.Unity
{
    public static class UiKit
    {
        private static Font _font;
        private static Texture2D _dotTex;

        public static Font Font
        {
            get
            {
                if (_font == null)
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static readonly Color Ink = new Color(0.93f, 0.88f, 0.78f);
        public static readonly Color DimInk = new Color(0.70f, 0.65f, 0.55f);
        public static readonly Color Gold = new Color(0.95f, 0.75f, 0.35f);
        public static readonly Color PanelBg = new Color(0.05f, 0.04f, 0.07f, 0.72f);
        public static readonly Color HealthRed = new Color(0.78f, 0.22f, 0.20f);
        public static readonly Color LightGreen = new Color(0.35f, 0.85f, 0.45f);
        public static readonly Color HungerAmber = new Color(0.92f, 0.66f, 0.25f);
        // -- storybook / ambient additions -------------------------------------
        /// <summary>Warm dark parchment for the Chronicle and Pack panels.</summary>
        public static readonly Color Parchment = new Color(0.13f, 0.10f, 0.065f, 0.94f);
        /// <summary>Warm cream ink for parchment panels.</summary>
        public static readonly Color ParchmentInk = new Color(0.93f, 0.86f, 0.71f);
        public static readonly Color GoldDim = new Color(0.72f, 0.55f, 0.28f);
        /// <summary>Barely-there glass for HUD chrome.</summary>
        public static readonly Color WhisperPanel = new Color(0.05f, 0.04f, 0.07f, 0.28f);
        public static readonly Color GlassPanel = new Color(0.05f, 0.04f, 0.07f, 0.45f);

        /// <summary>Screen-space canvas + scaler + raycaster + event system.</summary>
        public static Canvas CreateCanvas(string name, int sortOrder)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
            return canvas;
        }

        /// <summary>Anchored rect helper. Anchors 0..1, offsets in pixels.</summary>
        public static RectTransform Rect(Transform parent, string name,
            float ax0, float ay0, float ax1, float ay1,
            float ox0, float oy0, float ox1, float oy1)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(ax0, ay0);
            rt.anchorMax = new Vector2(ax1, ay1);
            rt.offsetMin = new Vector2(ox0, oy0);
            rt.offsetMax = new Vector2(ox1, oy1);
            return rt;
        }

        public static RectTransform Panel(Transform parent, string name, Color bg,
            float ax0, float ay0, float ax1, float ay1,
            float ox0, float oy0, float ox1, float oy1)
        {
            var rt = Rect(parent, name, ax0, ay0, ax1, ay1, ox0, oy0, ox1, oy1);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = bg;
            img.raycastTarget = false;
            return rt;
        }

        public static Text Label(Transform parent, string name, string text, int size,
                                 Color color, TextAnchor anchor)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        public static Button Button(Transform parent, string name, string label, int fontSize)
        {
            var rt = Rect(parent, name, 0f, 0f, 0f, 0f, 0f, 0f, 120f, 36f);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.16f, 0.13f, 0.18f, 0.9f);
            var btn = rt.gameObject.AddComponent<Button>();
            var t = Label(rt, "Text", label, fontSize, Ink, TextAnchor.MiddleCenter);
            t.GetComponent<RectTransform>().offsetMin = new Vector2(6f, 2f);
            t.GetComponent<RectTransform>().offsetMax = new Vector2(-6f, -2f);
            return btn;
        }

        public static InputField Input(Transform parent, string name, int fontSize,
                                       float width, float height)
        {
            var rt = Rect(parent, name, 0f, 0f, 0f, 0f, 0f, 0f, width, height);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.10f, 0.09f, 0.12f, 0.92f);
            var input = rt.gameObject.AddComponent<InputField>();
            var t = Label(rt, "Text", "", fontSize, Ink, TextAnchor.MiddleLeft);
            t.GetComponent<RectTransform>().offsetMin = new Vector2(8f, 4f);
            t.GetComponent<RectTransform>().offsetMax = new Vector2(-8f, -4f);
            input.textComponent = t;
            var ph = Label(rt, "Placeholder", "…", fontSize, DimInk, TextAnchor.MiddleLeft);
            ph.GetComponent<RectTransform>().offsetMin = new Vector2(8f, 4f);
            ph.GetComponent<RectTransform>().offsetMax = new Vector2(-8f, -4f);
            input.placeholder = ph;
            return input;
        }

        /// <summary>A labeled horizontal slider. Returns the Slider.</summary>
        public static Slider Slider(Transform parent, string name, string label,
                                    float min, float max, float value, float width)
        {
            var row = Rect(parent, name, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 28f);
            var lab = Label(row, "Lab", label, 13, Ink, TextAnchor.MiddleLeft);
            lab.GetComponent<RectTransform>().offsetMax = new Vector2(-(width + 64f), 0f);
            var sliderRt = Rect(row, "Slider", 1f, 0f, 1f, 1f, -(width + 56f), 4f, -56f, -4f);
            var slider = sliderRt.gameObject.AddComponent<UnityEngine.UI.Slider>();
            slider.minValue = min; slider.maxValue = max; slider.value = value;
            // Track.
            var bg = sliderRt.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.6f);
            // Fill.
            var fillRt = Rect(sliderRt, "Fill", 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);
            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.color = new Color(0.72f, 0.55f, 0.28f, 0.9f); // gold
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            // Handle.
            var handleRt = Rect(sliderRt, "Handle", 0f, 0f, 0f, 1f, -7f, -3f, 7f, 3f);
            var handle = handleRt.gameObject.AddComponent<Image>();
            handle.color = new Color(0.95f, 0.88f, 0.70f, 1f);
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handle;
            // Value label.
            var valLab = Label(row, "Val", Mathf.RoundToInt(value * 100) + "%", 12, DimInk, TextAnchor.MiddleRight);
            valLab.GetComponent<RectTransform>().anchorMin = new Vector2(1f, 0f);
            valLab.GetComponent<RectTransform>().anchorMax = new Vector2(1f, 1f);
            valLab.GetComponent<RectTransform>().offsetMin = new Vector2(-52f, 0f);
            valLab.GetComponent<RectTransform>().offsetMax = new Vector2(-4f, 0f);
            slider.onValueChanged.AddListener(v =>
            {
                valLab.text = Mathf.RoundToInt(v * 100) + "%";
            });
            return slider;
        }

        /// <summary>A labeled toggle button showing On/Off. Returns the Button; caller wires onClick.</summary>
        public static Button Toggle(Transform parent, string name, string label, bool value)
        {
            var row = Rect(parent, name, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 28f);
            var lab = Label(row, "Lab", label, 13, Ink, TextAnchor.MiddleLeft);
            lab.GetComponent<RectTransform>().offsetMax = new Vector2(-140f, 0f);
            var btnRt = Rect(row, "Btn", 1f, 0f, 1f, 1f, -132f, 2f, -8f, -2f);
            var img = btnRt.gameObject.AddComponent<Image>();
            var btn = btnRt.gameObject.AddComponent<Button>();
            var t = Label(btnRt, "Text", value ? "On" : "Off", 13,
                value ? new Color(0.65f, 0.95f, 0.65f) : DimInk, TextAnchor.MiddleCenter);
            btn.onClick.AddListener(() =>
            {
                bool on = t.text != "On";
                t.text = on ? "On" : "Off";
                t.color = on ? new Color(0.65f, 0.95f, 0.65f) : DimInk;
            });
            return btn;
        }

        /// <summary>A labeled cycle button (click to advance through options). Returns Button + label setter.</summary>
        public static Button Cycle(Transform parent, string name, string label, string value)
        {
            var row = Rect(parent, name, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 28f);
            var lab = Label(row, "Lab", label, 13, Ink, TextAnchor.MiddleLeft);
            lab.GetComponent<RectTransform>().offsetMax = new Vector2(-180f, 0f);
            var btnRt = Rect(row, "Btn", 1f, 0f, 1f, 1f, -172f, 2f, -8f, -2f);
            var img = btnRt.gameObject.AddComponent<Image>();
            img.color = new Color(0.16f, 0.13f, 0.18f, 0.9f);
            var btn = btnRt.gameObject.AddComponent<Button>();
            var t = Label(btnRt, "Text", value, 13, Ink, TextAnchor.MiddleCenter);
            t.name = "Value";
            return btn;
        }

        /// <summary>A labeled horizontal bar. Returns the fill Image.</summary>
        public static Image Bar(Transform parent, string name, string label, Color fillColor,
                                float width)
        {
            var row = Rect(parent, name, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 22f);
            var lab = Label(row, "Lab", label, 13, Ink, TextAnchor.MiddleLeft);
            lab.GetComponent<RectTransform>().offsetMax = new Vector2(-(width + 8f), 0f);
            var bgRt = Rect(row, "Bg", 1f, 0f, 1f, 1f, -(width + 8f), 3f, -8f, -3f);
            var bg = bgRt.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.6f);
            bg.raycastTarget = false;
            var fillRt = Rect(bgRt, "Fill", 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);
            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.color = fillColor;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.raycastTarget = false;
            return fill;
        }

        /// <summary>Small circle texture for the glow dot.</summary>
        public static Texture2D DotTexture()
        {
            if (_dotTex != null) return _dotTex;
            _dotTex = new Texture2D(24, 24, TextureFormat.RGBA32, false);
            for (int y = 0; y < 24; y++)
                for (int x = 0; x < 24; x++)
                {
                    float d = Mathf.Sqrt((x - 11.5f) * (x - 11.5f) + (y - 11.5f) * (y - 11.5f)) / 11.5f;
                    _dotTex.SetPixel(x, y, new Color(1f, 1f, 1f, d <= 1f ? 1f : 0f));
                }
            _dotTex.Apply();
            return _dotTex;
        }

        private static Sprite _dotSprite;
        public static Sprite DotSprite()
        {
            if (_dotSprite == null)
                _dotSprite = Sprite.Create(DotTexture(), new Rect(0f, 0f, 24f, 24f), new Vector2(0.5f, 0.5f));
            return _dotSprite;
        }

        private static Texture2D _softTex;
        /// <summary>Radial-gradient glow texture (white core fading to transparent).</summary>
        public static Texture2D SoftTexture()
        {
            if (_softTex != null) return _softTex;
            int s = 64;
            _softTex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2f - 1f;
                    float dy = (y + 0.5f) / s * 2f - 1f;
                    float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    float a = 1f - d;
                    _softTex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            _softTex.Apply();
            return _softTex;
        }

        private static Sprite _softSprite;
        public static Sprite SoftSprite()
        {
            if (_softSprite == null)
                _softSprite = Sprite.Create(SoftTexture(), new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f));
            return _softSprite;
        }

        private static Texture2D _ringTex;
        /// <summary>Thin soft-edged ring texture (white). Use with Image.Type.Filled + Radial360 for gauges.</summary>
        public static Texture2D RingTexture()
        {
            if (_ringTex != null) return _ringTex;
            int s = 96;
            _ringTex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2f - 1f;
                    float dy = (y + 0.5f) / s * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float band = 1f - Mathf.Clamp01(Mathf.Abs(d - 0.84f) / 0.07f);
                    _ringTex.SetPixel(x, y, new Color(1f, 1f, 1f, band));
                }
            _ringTex.Apply();
            return _ringTex;
        }

        private static Sprite _ringSprite;
        public static Sprite RingSprite()
        {
            if (_ringSprite == null)
                _ringSprite = Sprite.Create(RingTexture(), new Rect(0f, 0f, 96f, 96f), new Vector2(0.5f, 0.5f));
            return _ringSprite;
        }

        /// <summary>Soft drop shadow for text readability over bright scenes.</summary>
        public static Text AddShadow(Text t, float alpha)
        {
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, alpha);
            sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        public static Text AddShadow(Text t) { return AddShadow(t, 0.55f); }

        /// <summary>Thin horizontal hairline (1px via offsets).</summary>
        public static RectTransform Divider(Transform parent, string name, Color color,
            float ax0, float ay0, float ax1, float ay1,
            float ox0, float oy0, float ox1, float oy1)
        {
            var rt = Rect(parent, name, ax0, ay0, ax1, ay1, ox0, oy0, ox1, oy1);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        /// <summary>Letterspaced caps, e.g. "CHAPTER" -> "C H A P T E R".</summary>
        public static string Spaced(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder(s.Length * 2);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public static string Roman(int n)
        {
            if (n <= 0) return n.ToString();
            string[] thousands = { "", "M", "MM", "MMM" };
            string[] hundreds = { "", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM" };
            string[] tens = { "", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC" };
            string[] ones = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
            return thousands[n / 1000] + hundreds[(n % 1000) / 100] + tens[(n % 100) / 10] + ones[n % 10];
        }

        /// <summary>"Day 4 · 14:20" from game seconds.</summary>
        public static string FormatGameTime(float seconds)
        {
            int day = (int)(seconds / 86400f) + 1;
            float hod = (seconds % 86400f) / 3600f;
            int hh = (int)hod;
            int mm = (int)((hod - hh) * 60f);
            return "Day " + day + " · " + hh.ToString("D2") + ":" + mm.ToString("D2");
        }

        private static readonly string[] Winds = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        public static string CompassLetter(float yawDegrees)
        {
            float y = ((yawDegrees % 360f) + 360f) % 360f;
            return Winds[Mathf.RoundToInt(y / 45f) % 8];
        }
    }
}
