using System.Diagnostics;
using BattleTech;
using BattleTech.UI;
using BattleTech.UI.TMProWrapper;
using Harmony;
using HBS.Extensions;
using UnityEngine;
using UnityEngine.UI;

// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global

// ReSharper disable StringLiteralTypo 
// ReSharper disable ClassNeverInstantiated.Global

namespace GalaxyatWar
{
    public class DeploymentIndicator
    {
        private long counter;
        internal readonly GameObject playPauseButton;
        private readonly GameObject hitBox;
        private readonly Image image;
        private readonly LocalizableText text;

        public DeploymentIndicator()
        {
            var root = UIManager.Instance.gameObject;
            var cmdCenterButton = root.gameObject.FindFirstChildNamed("uixPrfBttn_SIM_navButton-prime");
            var cmdCenterBgFill = cmdCenterButton.FindFirstChildNamed("bgFill");
            var parent = root.gameObject.FindFirstChildNamed("uixPrfBttn_SIM_timeButton-Element-MANAGED");
            var timeLineText = root.gameObject.FindFirstChildNamed("time_labelText");
            playPauseButton = root.gameObject.FindFirstChildNamed("timeBttn_playPause");
            hitBox = parent.gameObject.FindFirstChildNamed("-HitboxOverlay-");
            image = cmdCenterBgFill.GetComponent<Image>();
            text = timeLineText.GetComponent<LocalizableText>();
            FileLog.Log("Deployment Indicator constructed.");
        }

        internal void ShowDeploymentIndicator(bool isDeploymentRequired)
        {
            if (Time.time > counter + 1)
            {
                counter++;
                // SetActive is pretty expensive, so avoid doing it
                if (isDeploymentRequired && playPauseButton.activeSelf)
                {
                    image.color = new Color(200, 0, 0);
                    text.text = "Deployment Required";
                    playPauseButton.SetActive(false);
                    hitBox.SetActive(false);
                }
                else if (!isDeploymentRequired && !playPauseButton.activeSelf)
                {
                    image.color = new Color(0, 0, 0, 0.863f);
                    playPauseButton.SetActive(true);
                    hitBox.SetActive(true);
                }
            }
        }

        [HarmonyPatch(typeof(SimGameState), "SimGameUXCreatorLoaded")]
        public class SimGameStateSimGameUXCreatorLoadedPatch
        {
            private static void Postfix()
            {
                FileLog.Log("SimGameStateSimGameUXCreatorLoadedPatch");
                Mod.DeploymentIndicator = new DeploymentIndicator();
            }
        }
    }
}
