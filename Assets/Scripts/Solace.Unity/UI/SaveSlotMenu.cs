// Solace.Unity.UI — the main menu: three save slots.
//
// First thing Lewis sees on boot: a quiet title over three world-cards.
// Each card shows its procedural thumbnail, the fox's name, generation,
// seed, time in the vale, and when it was last wandered. Empty cards
// invite a new life. Styled to match the HUD: glass panels, gold hairlines,
// parchment ink.
using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Solace.Core;

namespace Solace.Unity.UI
{
    public static class SaveSlotMenu
    {
        private const int CardW = 340;
        private const int CardH = 500;
        private const int CardGap = 44;

        /// <summary>Shows the slot menu. The menu drives boot: it calls
        /// LoadSlot / NewLifeInSlot on the bootstrap, then destroys itself.</summary>
        public static void Show(GameBootstrap boot)
        {
            if (boot == null) return;
            Canvas canvas = UiKit.CreateCanvas("SaveSlotMenu", 100);
            var root = canvas.GetComponent<RectTransform>();

            // Full-screen backdrop: deep night, slightly transparent so the
            // (empty) scene behind doesn't matter.
            UiKit.Panel(root, "Backdrop", new Color(0.03f, 0.03f, 0.06f, 0.96f),
                0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);

            // Title block.
            var titleRt = UiKit.Rect(root, "Title", 0.5f, 1f, 0.5f, 1f, -400f, -190f, 400f, -60f);
            var title = UiKit.Label(titleRt, "TitleText", "S O L A C E", 64, UiKit.Ink, TextAnchor.MiddleCenter);
            UiKit.AddShadow(title);
            var tagRt = UiKit.Rect(root, "Tagline", 0.5f, 1f, 0.5f, 1f, -400f, -235f, 400f, -195f);
            var tag = UiKit.Label(tagRt, "TagText", "three vales · three lives · one light", 20, UiKit.DimInk, TextAnchor.MiddleCenter);
            UiKit.AddShadow(tag);

            // Cards row, centered: the 3 save slots plus today's shared vale.
            int cardCount = SaveSlots.MaxSlots + 1;
            float totalW = cardCount * CardW + (cardCount - 1) * CardGap;
            float startX = -totalW / 2f;
            SaveSlots.SlotInfo[] infos;
            try { infos = SaveSlots.ListSlots(GameBootstrap.SaveDir); }
            catch { infos = new SaveSlots.SlotInfo[0]; }

            for (int i = 0; i < cardCount; i++)
            {
                float x0 = startX + i * (CardW + CardGap);
                if (i < SaveSlots.MaxSlots)
                {
                    SaveSlots.SlotInfo info = i < infos.Length ? infos[i] : new SaveSlots.SlotInfo { Slot = i };
                    BuildCard(root, boot, info, x0);
                }
                else
                {
                    BuildDailyCard(root, boot, x0);
                }
            }

            // Footer hint.
            var footRt = UiKit.Rect(root, "Footer", 0.5f, 0f, 0.5f, 0f, -400f, 40f, 400f, 80f);
            var foot = UiKit.Label(footRt, "FootText", "choose a vale to wander", 16, UiKit.DimInk, TextAnchor.MiddleCenter);
            UiKit.AddShadow(foot);
        }

        private static void BuildCard(RectTransform root, GameBootstrap boot, SaveSlots.SlotInfo info, float x0)
        {
            int slot = info.Slot;
            var card = UiKit.Panel(root, "Slot" + slot, UiKit.GlassPanel,
                0.5f, 0.5f, 0.5f, 0.5f, x0, -CardH / 2f, x0 + CardW, CardH / 2f);
            // Gold hairline frame.
            var frame = card.gameObject.AddComponent<Outline>();
            frame.effectColor = new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0.35f);
            frame.effectDistance = new Vector2(1f, 1f);

            // Whole card is the button. NOTE: UiKit.Panel sets raycastTarget=false,
            // which would make the Button deaf to clicks — re-enable it here so
            // the card actually receives pointer events.
            var cardImg = card.GetComponent<Image>();
            if (cardImg != null) cardImg.raycastTarget = true;
            var btn = card.gameObject.AddComponent<Button>();
            if (cardImg != null) btn.targetGraphic = cardImg;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.12f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.2f);
            btn.colors = colors;
            btn.onClick.AddListener(() => Choose(boot, info, card));

            if (info.Exists) BuildOccupiedCard(card, info);
            else BuildEmptyCard(card, slot);
        }

        private static void BuildOccupiedCard(RectTransform card, SaveSlots.SlotInfo info)
        {
            // Thumbnail.
            Texture2D thumb = null;
            try { thumb = SlotThumbnail.LoadSlotThumbnail(GameBootstrap.SaveDir, info.Slot); } catch { }
            if (thumb == null) { try { thumb = SlotThumbnail.Render(info.Seed); } catch { } }
            var thumbRt = UiKit.Rect(card, "Thumb", 0f, 1f, 1f, 1f, 14f, -234f, -14f, -14f);
            if (thumb != null)
            {
                var raw = thumbRt.gameObject.AddComponent<RawImage>();
                raw.texture = thumb;
                raw.raycastTarget = false;
            }
            else
            {
                UiKit.Panel(thumbRt, "ThumbFallback", new Color(0.10f, 0.10f, 0.16f, 1f),
                    0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);
            }

            float y = -250f;
            string name = string.IsNullOrEmpty(info.FoxName) ? "Nameless" : info.FoxName;
            var nameRt = UiKit.Rect(card, "Name", 0f, 1f, 1f, 1f, 14f, y - 40f, -14f, y);
            var nameT = UiKit.Label(nameRt, "NameText", name, 30, UiKit.Ink, TextAnchor.MiddleCenter);
            UiKit.AddShadow(nameT);
            y -= 44f;

            var genRt = UiKit.Rect(card, "Gen", 0f, 1f, 1f, 1f, 14f, y - 26f, -14f, y);
            var genT = UiKit.Label(genRt, "GenText",
                "Generation " + UiKit.Roman(Math.Max(1, info.Generation)), 17, UiKit.Gold, TextAnchor.MiddleCenter);
            UiKit.AddShadow(genT);
            y -= 30f;

            var seedRt = UiKit.Rect(card, "Seed", 0f, 1f, 1f, 1f, 14f, y - 24f, -14f, y);
            var seedT = UiKit.Label(seedRt, "SeedText", "seed " + info.Seed, 15, UiKit.DimInk, TextAnchor.MiddleCenter);
            y -= 28f;

            var timeRt = UiKit.Rect(card, "Time", 0f, 1f, 1f, 1f, 14f, y - 24f, -14f, y);
            var timeT = UiKit.Label(timeRt, "TimeText", FormatPlaytime(info.PlaytimeSeconds) + " in the vale", 15, UiKit.DimInk, TextAnchor.MiddleCenter);
            y -= 28f;

            var lastRt = UiKit.Rect(card, "Last", 0f, 1f, 1f, 1f, 14f, y - 24f, -14f, y);
            var lastT = UiKit.Label(lastRt, "LastText", "last wandered " + FormatAgo(info.LastSavedUtcTicks), 15, UiKit.DimInk, TextAnchor.MiddleCenter);

            var goRt = UiKit.Rect(card, "Go", 0f, 0f, 1f, 0f, 14f, 18f, -14f, 58f);
            var goT = UiKit.Label(goRt, "GoText", "W A N D E R", 18, UiKit.Gold, TextAnchor.MiddleCenter);
            UiKit.AddShadow(goT);
        }

        private static void BuildEmptyCard(RectTransform card, int slot)
        {
            var plusRt = UiKit.Rect(card, "Plus", 0f, 1f, 1f, 1f, 14f, -220f, -14f, -60f);
            var plusT = UiKit.Label(plusRt, "PlusText", "＋", 72, new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0.6f), TextAnchor.MiddleCenter);
            UiKit.AddShadow(plusT);

            var t1Rt = UiKit.Rect(card, "Empty1", 0f, 1f, 1f, 1f, 14f, -280f, -14f, -240f);
            var t1 = UiKit.Label(t1Rt, "Empty1Text", "Vale " + (slot + 1), 26, UiKit.Ink, TextAnchor.MiddleCenter);
            UiKit.AddShadow(t1);

            var t2Rt = UiKit.Rect(card, "Empty2", 0f, 1f, 1f, 1f, 14f, -320f, -14f, -284f);
            var t2 = UiKit.Label(t2Rt, "Empty2Text", "no life has begun here", 16, UiKit.DimInk, TextAnchor.MiddleCenter);

            var goRt = UiKit.Rect(card, "Go", 0f, 0f, 1f, 0f, 14f, 18f, -14f, 58f);
            var goT = UiKit.Label(goRt, "GoText", "B E G I N", 18, UiKit.Gold, TextAnchor.MiddleCenter);
            UiKit.AddShadow(goT);
        }

        private static void Choose(GameBootstrap boot, SaveSlots.SlotInfo info, RectTransform card)
        {
            try
            {
                bool ok;
                if (info.Exists)
                {
                    ok = boot.LoadSlot(info.Slot);
                    if (!ok && boot.Hud != null)
                        boot.Hud.ShowToast("That save couldn't be read. It was left untouched.", 6f);
                }
                else
                {
                    boot.NewLifeInSlot(info.Slot, ChallengeMode.Standard);
                    ok = true;
                }
                if (ok)
                {
                    var canvas = card.GetComponentInParent<Canvas>();
                    if (canvas != null) UnityEngine.Object.Destroy(canvas.gameObject);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                if (boot != null && boot.Hud != null)
                    boot.Hud.ShowToast("Couldn't start that vale — see the console.", 6f);
            }
        }

        // -- today's shared vale -----------------------------------------------------

        /// <summary>
        /// The daily card: same seed for every player today, with the visit
        /// streak. Gold frame marks it as special vs. the slot cards.
        /// </summary>
        private static void BuildDailyCard(RectTransform root, GameBootstrap boot, float x0)
        {
            DateTime today = DateTime.UtcNow.Date;
            string ds = DailyVale.DateString(today);
            int seed = DailyVale.SeedFor(today);
            int streak;
            try { streak = DailyVale.CurrentStreak(GameBootstrap.SaveDir, today); }
            catch { streak = 0; }
            bool visited = false;
            try { visited = DailyVale.DailyExists(GameBootstrap.SaveDir, today); }
            catch { }

            var card = UiKit.Panel(root, "DailyCard", UiKit.GlassPanel,
                0.5f, 0.5f, 0.5f, 0.5f, x0, -CardH / 2f, x0 + CardW, CardH / 2f);
            // Brighter gold frame: this one is special.
            var frame = card.gameObject.AddComponent<Outline>();
            frame.effectColor = new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0.85f);
            frame.effectDistance = new Vector2(2f, 2f);

            var btn = card.gameObject.AddComponent<Button>();
            var dCardImg = card.GetComponent<Image>();
            if (dCardImg != null)
            {
                dCardImg.raycastTarget = true; // Panel() disables it; the card must be clickable.
                btn.targetGraphic = dCardImg;
            }
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.12f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.2f);
            btn.colors = colors;
            btn.onClick.AddListener(() =>
            {
                try
                {
                    boot.LoadDailyWorld();
                    var canvas = card.GetComponentInParent<Canvas>();
                    if (canvas != null) UnityEngine.Object.Destroy(canvas.gameObject);
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                    if (boot.Hud != null)
                        boot.Hud.ShowToast("Couldn't start today's vale — see the console.", 6f);
                }
            });

            // Deterministic thumbnail from the daily seed.
            Texture2D thumb = null;
            try { thumb = SlotThumbnail.Render(seed); } catch { }
            var thumbRt = UiKit.Rect(card, "Thumb", 0f, 1f, 1f, 1f, 14f, -234f, -14f, -14f);
            if (thumb != null)
            {
                var raw = thumbRt.gameObject.AddComponent<RawImage>();
                raw.texture = thumb;
                raw.raycastTarget = false;
            }

            float y = -250f;
            var hRt = UiKit.Rect(card, "DailyHead", 0f, 1f, 1f, 1f, 14f, y - 40f, -14f, y);
            var hT = UiKit.Label(hRt, "DailyHeadText", "◆ TODAY'S VALE", 24, UiKit.Gold, TextAnchor.MiddleCenter);
            UiKit.AddShadow(hT);
            y -= 44f;

            var dRt = UiKit.Rect(card, "DailyDate", 0f, 1f, 1f, 1f, 14f, y - 26f, -14f, y);
            var dT = UiKit.Label(dRt, "DailyDateText",
                DailyVale.PrettyDate(ds) + " · one world, everyone", 16, UiKit.Ink, TextAnchor.MiddleCenter);
            UiKit.AddShadow(dT);
            y -= 30f;

            var sRt = UiKit.Rect(card, "DailyStreak", 0f, 1f, 1f, 1f, 14f, y - 26f, -14f, y);
            string streakLine = streak >= 2 ? streak + "-day streak — keep it burning"
                              : streak == 1 ? "streak: 1 day — come back tomorrow"
                              : "no streak yet — today begins it";
            var sT = UiKit.Label(sRt, "DailyStreakText", streakLine, 15, UiKit.DimInk, TextAnchor.MiddleCenter);
            UiKit.AddShadow(sT);

            var goRt = UiKit.Rect(card, "Go", 0f, 0f, 1f, 0f, 14f, 18f, -14f, 58f);
            var goT = UiKit.Label(goRt, "GoText", visited ? "W A N D E R" : "B E G I N", 18, UiKit.Gold,
                                  TextAnchor.MiddleCenter);
            UiKit.AddShadow(goT);
        }

        private static string FormatPlaytime(double seconds)
        {
            if (seconds < 3600) return Math.Max(1, (int)(seconds / 60)) + "m";
            return (seconds / 3600).ToString("0.0") + "h";
        }

        private static string FormatAgo(long ticks)
        {
            if (ticks <= 0) return "long ago";
            TimeSpan d = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            if (d.TotalSeconds < 90) return "just now";
            if (d.TotalMinutes < 90) return (int)d.TotalMinutes + "m ago";
            if (d.TotalHours < 48) return (int)d.TotalHours + "h ago";
            return (int)d.TotalDays + "d ago";
        }
    }
}
