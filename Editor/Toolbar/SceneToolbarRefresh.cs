using System;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace SceneToolbar.Editor
{
    internal static class SceneToolbarRefresh
    {
        internal const string SettingsPath = "Scene Pin/Settings";
        internal const string ScenesPath = "Scene Pin/Scenes";

        private static bool refreshScheduled;

        internal static void Request()
        {
            SceneToolbarPresence.Request();

            if (refreshScheduled)
            {
                return;
            }

            refreshScheduled = true;
            EditorApplication.delayCall -= RefreshNow;
            EditorApplication.delayCall += RefreshNow;
        }

        private static void RefreshNow()
        {
            refreshScheduled = false;
            EditorApplication.delayCall -= RefreshNow;

            RefreshPath(SettingsPath);
            RefreshPath(ScenesPath);
        }

        private static void RefreshPath(string toolbarPath)
        {
            try
            {
                MainToolbar.Refresh(toolbarPath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[Scene Pin] Toolbar refresh failed for '{toolbarPath}': {exception.Message}");
            }
        }
    }
}
