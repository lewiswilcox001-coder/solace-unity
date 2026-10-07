// Solace.Unity — milestones celebration and gallery.
// Self-bootstrapping via RuntimeInitializeOnLoadMethod; GameBootstrap untouched.
// Polls GameState.Milestones for new unlocks and shows a golden celebration
// overlay. Press K for the gallery: every milestone, unlocked or locked,
// grouped by category like a collector's album.
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Solace.Core;

namespace Solace.Unity
{
    public class MilestoneView : MonoBehaviour
    {
        // -- bootstrap ----------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoEnsure()
        {
            if (Object.FindObjectOfType<MilestoneView>() != null) return;
            var go = new GameObject("MilestoneView");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<MilestoneView>();
        }

        // -- state ---------------------------------------------------------------

        private Canvas _canvas;
        private bool _built;
        private int _builtSeed = -1;

        // Celebration overlay.
        private GameObject _celebrateRoot;
        private Text _celebEyebrow;
        private Text _celebName;
        private Text _celebDesc;
        private CanvasGroup _celebGroup;
        private readonly Queue<string> _celebrationQueue = new Queue<string>();
        private int _lastCelebratedCount;
        private bool _pollSynced;
        private float _celebT; // -1 = hidden, else seconds since shown
        private const float CelebDuration = 5.5f;

        // Gallery panel.
        private GameObject _galleryRoot;
        private Text _galleryBody;
        private ScrollRect _galleryScroll;
        private bool _galleryOpen;
        private int _galleryBuiltForCount = -1;

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Sim == null) return;
            if (!_built) { Build(); _built = true; }

            // New life (seed change): reset celebration tracking.
            if (boot.Seed != _builtSeed)
            {
                _builtSeed = boot.Seed;
                _lastCelebratedCount = 0;
                _pollSynced = false;
                _celebrationQueue.Clear();
                _galleryBuiltForCount = -1;
            }

            PollUnlocks(boot.Sim.State);
            TickCelebration();
            TickGalleryInput();
        }

        // -- construction ---------------------------------------------------------

        private void Build()
        {
            var go = new GameObject("MilestoneCanvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 900; // above HUD chrome, below bug-report overlay
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();

            BuildCelebration();
            BuildGallery();
        }

        private static Text MakeText(Transform parent, string name, string text,
                                     int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            // Soft shadow for readability.
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.55f);
            sh.effectDistance = new Vector2(1.5f, -1.5f);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return t;
        }

        private static Image MakePanel(Transform parent, string name, Color bg)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bg;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return img;
        }

        private void BuildCelebration()
        {
            var root = new GameObject("Celebration");
            root.transform.SetParent(_canvas.transform, false);
            _celebrateRoot = root;
            _celebGroup = root.AddComponent<CanvasGroup>();

            // Centered golden card.
            var card = MakePanel(root.transform, "Card",
                new Color(0.07f, 0.05f, 0.10f, 0.88f));
            var crt = card.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(460f, 190f);
            crt.anchoredPosition = new Vector2(0f, 120f);

            // Thin gold border via four edge images.
            var gold = new Color(0.95f, 0.75f, 0.35f, 0.9f);
            AddEdge(root.transform, gold, 460f, 2f, 0f, 120f + 95f);
            AddEdge(root.transform, gold, 460f, 2f, 0f, 120f - 95f);
            AddEdge(root.transform, gold, 2f, 190f, -230f, 120f);
            AddEdge(root.transform, gold, 2f, 190f, 230f, 120f);

            _celebEyebrow = MakeText(root.transform, "Eyebrow",
                "M I L E S T O N E   U N L O C K E D", 13,
                new Color(0.95f, 0.75f, 0.35f), TextAnchor.MiddleCenter);
            var ert = _celebEyebrow.GetComponent<RectTransform>();
            ert.sizeDelta = new Vector2(440f, 24f);
            ert.anchoredPosition = new Vector2(0f, 120f + 58f);

            _celebName = MakeText(root.transform, "Name", "", 30,
                new Color(0.98f, 0.88f, 0.66f), TextAnchor.MiddleCenter);
            var nrt = _celebName.GetComponent<RectTransform>();
            nrt.sizeDelta = new Vector2(440f, 44f);
            nrt.anchoredPosition = new Vector2(0f, 120f + 12f);

            _celebDesc = MakeText(root.transform, "Desc", "", 15,
                new Color(0.82f, 0.76f, 0.64f), TextAnchor.MiddleCenter);
            var drt = _celebDesc.GetComponent<RectTransform>();
            drt.sizeDelta = new Vector2(420f, 60f);
            drt.anchoredPosition = new Vector2(0f, 120f - 44f);

            root.SetActive(false);
            _celebT = -1f;
        }

        private static void AddEdge(Transform parent, Color c, float w, float h, float x, float y)
        {
            var go = new GameObject("Edge");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private void BuildGallery()
        {
            var root = new GameObject("Gallery");
            root.transform.SetParent(_canvas.transform, false);
            _galleryRoot = root;

            // Dim backdrop.
            var bg = MakePanel(root.transform, "Backdrop", new Color(0f, 0f, 0f, 0.55f));
            var bgrt = bg.GetComponent<RectTransform>();
            bgrt.anchorMin = Vector2.zero;
            bgrt.anchorMax = Vector2.one;
            bgrt.pivot = new Vector2(0.5f, 0.5f);
            bgrt.sizeDelta = Vector2.zero;
            bgrt.anchoredPosition = Vector2.zero;

            // Centered panel.
            var panel = MakePanel(root.transform, "Panel",
                new Color(0.06f, 0.05f, 0.08f, 0.96f));
            var prt = panel.GetComponent<RectTransform>();
            prt.sizeDelta = new Vector2(560f, 520f);
            prt.anchoredPosition = Vector2.zero;

            var title = MakeText(root.transform, "Title", "MILESTONES", 22,
                new Color(0.95f, 0.75f, 0.35f), TextAnchor.MiddleCenter);
            var trt = title.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(520f, 30f);
            trt.anchoredPosition = new Vector2(0f, 222f);

            var hint = MakeText(root.transform, "Hint",
                "K or Esc to close — the world keeps turning", 12,
                new Color(0.55f, 0.52f, 0.45f), TextAnchor.MiddleCenter);
            var hrt = hint.GetComponent<RectTransform>();
            hrt.sizeDelta = new Vector2(520f, 20f);
            hrt.anchoredPosition = new Vector2(0f, -240f);

            // Scrollable list.
            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(root.transform, false);
            var srt = scrollGo.AddComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.5f, 0.5f);
            srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.sizeDelta = new Vector2(520f, 400f);
            srt.anchoredPosition = new Vector2(0f, -8f);
            scrollGo.AddComponent<RectMask2D>();
            _galleryScroll = scrollGo.AddComponent<ScrollRect>();
            _galleryScroll.horizontal = false;

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(scrollGo.transform, false);
            var crt = contentGo.AddComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = new Vector2(12f, 0f);
            crt.offsetMax = new Vector2(-12f, 0f);
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _galleryScroll.content = crt;

            _galleryBody = MakeText(crt, "Body", "", 14,
                new Color(0.88f, 0.83f, 0.72f), TextAnchor.UpperLeft);
            _galleryBody.lineSpacing = 1.35f;
            var brt = _galleryBody.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 1f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.offsetMin = new Vector2(0f, 0f);
            brt.offsetMax = new Vector2(0f, 0f);
            var lf = _galleryBody.gameObject.AddComponent<ContentSizeFitter>();
            lf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            root.SetActive(false);
        }

        // -- celebration ------------------------------------------------------------

        private void PollUnlocks(GameState s)
        {
            if (s.Milestones == null) return;
            int count = s.Milestones.UnlockedCount;
            if (!_pollSynced)
            {
                // First poll (e.g. after save-load): don't replay old unlocks.
                _lastCelebratedCount = count;
                _pollSynced = true;
                return;
            }
            if (count > _lastCelebratedCount)
            {
                for (int i = _lastCelebratedCount; i < count; i++)
                {
                    if (i < s.Milestones.UnlockedIds.Count)
                        _celebrationQueue.Enqueue(s.Milestones.UnlockedIds[i]);
                }
                _lastCelebratedCount = count;
            }
        }

        private void TickCelebration()
        {
            if (_celebT < 0f)
            {
                // Hidden: show next in queue.
                if (_celebrationQueue.Count > 0 && !_galleryOpen)
                {
                    string id = _celebrationQueue.Dequeue();
                    var def = MilestoneDefs.ById(id);
                    if (def != null)
                    {
                        _celebName.text = def.Name;
                        _celebDesc.text = def.Description;
                        _celebrateRoot.SetActive(true);
                        _celebT = 0f;
                    }
                }
                return;
            }

            _celebT += Time.deltaTime;
            float t = _celebT;
            // Fade in 0.5s, hold, fade out last 1s.
            float alpha;
            if (t < 0.5f) alpha = t / 0.5f;
            else if (t > CelebDuration - 1f) alpha = Mathf.Max(0f, (CelebDuration - t) / 1f);
            else alpha = 1f;
            _celebGroup.alpha = alpha;

            // Gentle rise.
            var card = _celebrateRoot.transform.Find("Card");
            if (card != null)
            {
                var rt = card.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(0f, 120f + Mathf.Min(t * 12f, 18f) * (t < 0.5f ? t / 0.5f : 1f));
            }

            if (t >= CelebDuration)
            {
                _celebrateRoot.SetActive(false);
                _celebT = -1f;
            }
        }

        // -- gallery -----------------------------------------------------------------

        private void TickGalleryInput()
        {
            // Don't steal keys while typing in chat.
            var hud = GameBootstrap.Instance.Hud;
            if (hud != null && hud.IsTyping) return;

            if (Input.GetKeyDown(KeyCode.K))
                SetGalleryOpen(!_galleryOpen);
            else if (_galleryOpen && Input.GetKeyDown(KeyCode.Escape))
                SetGalleryOpen(false);
            // Gamepad: B closes the gallery.
            if (GamepadInput.IsConnected && _galleryOpen && GamepadInput.GetButtonDown(PadButton.B))
                SetGalleryOpen(false);
        }

        private void SetGalleryOpen(bool open)
        {
            _galleryOpen = open;
            _galleryRoot.SetActive(open);
            if (open) RefreshGallery();
        }

        private void RefreshGallery()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Sim == null) return;
            var ms = boot.Sim.State.Milestones;
            if (ms == null) return;
            if (ms.UnlockedCount == _galleryBuiltForCount) return;
            _galleryBuiltForCount = ms.UnlockedCount;

            var sb = new StringBuilder(4096);
            var cats = new[] {
                MilestoneCategory.Survival, MilestoneCategory.Discovery,
                MilestoneCategory.Lineage, MilestoneCategory.Combat,
                MilestoneCategory.Social, MilestoneCategory.Seasons,
                MilestoneCategory.Dreams, MilestoneCategory.Daily
            };
            int total = MilestoneDefs.All.Count;
            sb.AppendLine("<color=#f2c159>" + ms.UnlockedCount + " / " + total +
                          " milestones</color>\n");

            foreach (var cat in cats)
            {
                sb.AppendLine("<color=#d8b46a><b>" + cat.ToString().ToUpper() + "</b></color>");
                foreach (var def in MilestoneDefs.All)
                {
                    if (def.Category != cat) continue;
                    if (ms.IsUnlocked(def.Id))
                        sb.AppendLine("  <color=#ffd97a>◆ " + def.Name + "</color> — " + def.Description);
                    else
                        sb.AppendLine("  <color=#5a5348>◇ " + def.Name + "</color> <color=#4a443c>— " +
                                      def.Description + "</color>");
                }
                sb.AppendLine();
            }

            _galleryBody.text = sb.ToString();
            // Scroll to top.
            _galleryScroll.verticalNormalizedPosition = 1f;
        }
    }
}
