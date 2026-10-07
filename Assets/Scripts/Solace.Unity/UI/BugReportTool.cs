// Solace.Unity — one-tap bug reporting (F12 key + on-screen button).
//
// Captures a screenshot PNG plus a TXT bundle (seed, generation, name/age/
// life-stage, X/Z, goal+activity, all needs, glow, sickness, last 15 journal
// entries, timestamps) into BugReports/ — project root in the editor, the
// executable's directory in players. Files are named
// bugreport-YYYYMMDD-HHMMSS-seedNNNNNN.png/.txt.
//
// Never throws, never pauses the sim, works in editor and players.
using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Solace.Core;

namespace Solace.Unity
{
    public class BugReportTool : MonoBehaviour
    {
        private void Start()
        {
            try
            {
                var hud = GetComponent<HudController>();
                Transform parent = hud != null ? hud.CanvasRoot : transform;
                var btn = UiKit.Button(parent, "BugReportButton", "Report Bug", 13);
                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 0f);
                rt.anchorMax = new Vector2(1f, 0f);
                rt.offsetMin = new Vector2(-136f, 56f);
                rt.offsetMax = new Vector2(-16f, 92f);
                btn.onClick.AddListener(Capture);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Solace] Bug report button failed: " + ex.Message);
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F12))
                Capture();
            // Gamepad: Back+Start chord fires a bug report (F12 equivalent).
            if (GamepadInput.IsConnected && GamepadInput.ChordDown(PadButton.Back, PadButton.Start))
                Capture();
        }

        public void Capture()
        {
            try
            {
                var boot = GameBootstrap.Instance;
                if (boot == null || boot.Sim == null) return;
                GameState s = boot.Sim.State;
                AgentState a = s.Agent;
                if (a == null) return;

                string dir = ReportDir();
                Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string baseName = "bugreport-" + stamp + "-seed" + s.Seed.ToString("D6");
                string png = Path.Combine(dir, baseName + ".png");
                string txt = Path.Combine(dir, baseName + ".txt");

                ScreenCapture.CaptureScreenshot(png);
                File.WriteAllText(txt, BuildReport(s, a));

                var hud = boot.Hud;
                if (hud != null)
                    hud.ShowToast("Bug report saved! Please send BOTH files to Solace so he can fix it.", 4f);
                Debug.Log("[Solace] Bug report written: " + txt);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Solace] Bug report failed: " + ex.Message);
            }
        }

        private static string ReportDir()
        {
            // Editor: project root. Player: executable directory.
            string root = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(root, "BugReports");
        }

        private static string BuildReport(GameState s, AgentState a)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("SOLACE bug report");
            sb.AppendLine("wall clock (UTC): " + DateTime.UtcNow.ToString("o"));
            sb.AppendLine("game time: " + UiKit.FormatGameTime((float)s.ElapsedSeconds) +
                          " (" + s.ElapsedSeconds.ToString("F1") + "s elapsed)");
            sb.AppendLine("seed: " + s.Seed);
            sb.AppendLine("generation: " + s.Lineage.Generation + " (" + UiKit.Roman(s.Lineage.Generation) + ")");
            sb.AppendLine("name: " + a.Name + " · age " + a.Age.ToString("F2") +
                          "y / lifespan " + a.LifespanYears.ToString("F1") + "y · stage " + a.Stage);
            sb.AppendLine("position: X " + a.X.ToString("F1") + " Z " + a.Z.ToString("F1") +
                          " facing " + (a.Facing * Mathf.Rad2Deg).ToString("F0") + "°");
            sb.AppendLine("goal: " + a.CurrentGoal);
            sb.AppendLine("activity: " + a.CurrentActivity);
            sb.AppendLine("needs: energy(light) " + a.Energy.ToString("F1") +
                          " hunger " + a.Hunger.ToString("F1") +
                          " thirst " + a.Thirst.ToString("F1") +
                          " health " + a.Health.ToString("F1") +
                          " mood " + a.Mood.ToString("F1") +
                          " curiosity " + a.Curiosity.ToString("F1"));
            sb.AppendLine("glow: " + a.Glow.ToString("F3") + " (light-shade hue " + a.LightShade.ToString("F2") + ")");
            sb.AppendLine("sickness: " + a.Sickness + " severity " + a.SicknessSeverity.ToString("F2"));
            sb.AppendLine("bond: " + (a.Bond != null ? a.Bond.PartnerId + " strength " + a.Bond.Strength.ToString("F2") : "none"));
            sb.AppendLine("weather: " + s.Weather + " · time of day " + s.TimeOfDay.ToString("F2") +
                          (s.IsNight ? " (night)" : " (day)"));
            sb.AppendLine("entities: " + s.Entities.Count + " · kits: " + s.Kits.Count);
            sb.AppendLine("inventory: bread " + s.Inventory.Bread + " potions " + s.Inventory.Potions +
                          " keepsakes " + s.Inventory.Keepsakes.Count);
            sb.AppendLine("companion: " + s.Companion.Level);
            sb.AppendLine();
            sb.AppendLine("--- last 15 journal entries ---");
            var recent = s.Journal.Recent(15);
            for (int i = recent.Count - 1; i >= 0; i--)
            {
                var e = recent[i];
                sb.Append("[G").Append(e.Generation).Append(' ')
                  .Append(UiKit.FormatGameTime(e.Time)).Append(" | ")
                  .Append(e.Category).Append("] ")
                  .AppendLine(e.Text);
            }
            return sb.ToString();
        }
    }
}
