using System;
using BattleTech.UI;
using BattleTech.UI.TMProWrapper;
using HBS.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace GalaxyatWar
{
    public class DeploymentIndicator
    {
        private static double counter;
        private static GameObject root = UIManager.Instance.gameObject;
        private static GameObject PlayPauseButton = root.gameObject.FindFirstChildNamed("timeBttn_playPause");
        private static GameObject CmdCenterButton = root.gameObject.FindFirstChildNamed("uixPrfBttn_SIM_navButton-prime");
        private static GameObject CmdCenterBgFill = CmdCenterButton.FindFirstChildNamed("bgFill");
        private static Image Image = CmdCenterBgFill.GetComponent<Image>();
        private static Color originalColor = Image.color;
        private static GameObject TimeLineText = root.gameObject.FindFirstChildNamed("time_labelText");
        private static LocalizableText Text = TimeLineText.GetComponent<LocalizableText>();

        internal static void SetDeploymentIndicator(bool isDeploymentRequired)
        {
            // if (counter + Time.deltaTime > 1)
            {
                if (Time.frameCount % 60 == 0)
                {
                    //Logger.LogDebug($"Stuff {PlayPauseButton} {CmdCenterButton} {CmdCenterBgFill} {Image}");
                    // SetActive is pretty expensive, so avoid doing it
                    if (PlayPauseButton.activeSelf && isDeploymentRequired)
                    {
                        Image.color = Color.red * 0.8f;
                        Text.text = "Deployment Required";

                        PlayPauseButton.SetActive(false);
                    }
                    else if (!PlayPauseButton.activeSelf && !isDeploymentRequired)
                    {
                        Image.color = originalColor;
                        PlayPauseButton.SetActive(true);
                        Text.text = "Timeline Paused";
                    }
                }
            }
        }
    }
}
