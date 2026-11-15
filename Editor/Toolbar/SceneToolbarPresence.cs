using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace SceneToolbar.Editor
{
    [InitializeOnLoad]
    internal static class SceneToolbarPresence
    {
        private const string PlayModeControlsPath = "Play Mode Controls";
        private const double CheckIntervalSeconds = 1d;

        private static readonly MethodInfo DockBeforeMethod = typeof(Overlay).GetMethod(
            "DockBefore",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static double nextCheckTime;
        private static bool applying;
        private static bool dockWarningLogged;

        static SceneToolbarPresence()
        {
            EditorApplication.update -= Maintain;
            EditorApplication.update += Maintain;
            Request();
        }

        internal static void Request()
        {
            nextCheckTime = 0d;
            EditorApplication.delayCall -= EnsureVisibleAndPositioned;
            EditorApplication.delayCall += EnsureVisibleAndPositioned;
        }

        private static void Maintain()
        {
            if (EditorApplication.timeSinceStartup < nextCheckTime)
            {
                return;
            }

            nextCheckTime = EditorApplication.timeSinceStartup + CheckIntervalSeconds;
            EnsureVisibleAndPositioned();
        }

        private static void EnsureVisibleAndPositioned()
        {
            EditorApplication.delayCall -= EnsureVisibleAndPositioned;
            if (applying)
            {
                return;
            }

            var toolbarCanvas = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Select(window => window.overlayCanvas)
                .FirstOrDefault(canvas =>
                    canvas != null &&
                    canvas.overlays.Any(overlay => overlay.id == PlayModeControlsPath));
            if (toolbarCanvas == null)
            {
                return;
            }

            var playModeControls = FindOverlay(toolbarCanvas, PlayModeControlsPath);
            var settings = FindOverlay(toolbarCanvas, SceneToolbarRefresh.SettingsPath);
            var scenes = FindOverlay(toolbarCanvas, SceneToolbarRefresh.ScenesPath);
            if (playModeControls == null || settings == null || scenes == null)
            {
                return;
            }

            applying = true;
            try
            {
                KeepDisplayed(settings);
                KeepDisplayed(scenes);
                DockImmediatelyBefore(settings, playModeControls);
                DockImmediatelyBefore(scenes, settings);
            }
            finally
            {
                applying = false;
            }
        }

        private static Overlay FindOverlay(OverlayCanvas canvas, string id)
        {
            return canvas.overlays.FirstOrDefault(overlay => overlay.id == id);
        }

        private static void KeepDisplayed(Overlay overlay)
        {
            overlay.displayedChanged -= OnDisplayedChanged;
            overlay.displayedChanged += OnDisplayedChanged;
            if (!overlay.displayed)
            {
                overlay.displayed = true;
            }
        }

        private static void OnDisplayedChanged(bool displayed)
        {
            if (!displayed && !applying)
            {
                Request();
            }
        }

        private static void DockImmediatelyBefore(Overlay overlay, Overlay target)
        {
            if (IsImmediatelyBefore(overlay.rootVisualElement, target.rootVisualElement))
            {
                return;
            }

            if (DockBeforeMethod == null)
            {
                LogDockWarning("Unity's internal Overlay.DockBefore method was not found.");
                return;
            }

            try
            {
                DockBeforeMethod.Invoke(overlay, new object[] { target });
            }
            catch (Exception exception)
            {
                LogDockWarning(exception.GetBaseException().Message);
            }
        }

        private static bool IsImmediatelyBefore(VisualElement element, VisualElement target)
        {
            var parent = element.parent;
            return parent != null &&
                   parent == target.parent &&
                   parent.IndexOf(element) + 1 == parent.IndexOf(target);
        }

        private static void LogDockWarning(string reason)
        {
            if (dockWarningLogged)
            {
                return;
            }

            dockWarningLogged = true;
            Debug.LogWarning(
                $"[Scene Pin] Could not keep the toolbar immediately left of Play Mode controls: {reason}");
        }
    }
}
