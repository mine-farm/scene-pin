using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SceneToolbar.Editor
{
    internal sealed class SceneToolbarSettingsWindow : EditorWindow
    {
        private enum SettingsTab
        {
            ProjectScenes,
            PinnedToolbar
        }

        private const string StyleSheetPath =
            "Packages/com.minefarm.scene-pin/Editor/Settings/SceneToolbarSettingsWindow.uss";

        private readonly HashSet<string> expandedEntries = new HashSet<string>(StringComparer.Ordinal);
        private IReadOnlyList<ResolvedSceneAsset> projectScenes = Array.Empty<ResolvedSceneAsset>();
        private SceneToolbarSettings currentSettings;
        private VisualElement projectSceneResults;
        private Label projectSceneCount;
        private string searchQuery = string.Empty;
        private SettingsTab activeTab;
        private bool rebuildScheduled;

        internal static void Open()
        {
            var window = GetWindow<SceneToolbarSettingsWindow>();
            window.titleContent = new GUIContent("Scene Pin");
            window.minSize = new Vector2(760f, 460f);
            window.Show();
        }

        private void OnEnable()
        {
            SceneToolbarSettingsRepository.SettingsChanged -= ScheduleRebuild;
            SceneToolbarSettingsRepository.SettingsChanged += ScheduleRebuild;
            EditorApplication.projectChanged -= ScheduleRebuild;
            EditorApplication.projectChanged += ScheduleRebuild;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            EditorBuildSettings.sceneListChanged -= ScheduleRebuild;
            EditorBuildSettings.sceneListChanged += ScheduleRebuild;
        }

        private void OnDisable()
        {
            SceneToolbarSettingsRepository.SettingsChanged -= ScheduleRebuild;
            EditorApplication.projectChanged -= ScheduleRebuild;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            EditorBuildSettings.sceneListChanged -= ScheduleRebuild;
        }

        public void CreateGUI()
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }

            rootVisualElement.name = "sceneToolbarRoot";
            rootVisualElement.AddToClassList("scene-toolbar-root");
            Rebuild();
        }

        private void Rebuild()
        {
            rootVisualElement.Clear();
            currentSettings = SceneToolbarSettingsRepository.GetOrCreate();
            if (currentSettings == null)
            {
                rootVisualElement.Add(new Label("Scene Pin settings could not be loaded.")
                    .WithClass("scene-toolbar-empty"));
                return;
            }

            projectScenes = SceneAssetUtility.FindProjectScenes();
            rootVisualElement.Add(CreateHero(currentSettings));
            rootVisualElement.Add(CreateSearchBar());

            rootVisualElement.Add(CreateTabView(currentSettings));

            rootVisualElement.Add(new Label(
                "Scene assets under Assets are discovered automatically. Opening a scene uses Single mode and keeps Unity's save prompt.")
                .WithClass("scene-toolbar-footer"));
        }

        private VisualElement CreateHero(SceneToolbarSettings settings)
        {
            var hero = new VisualElement();
            hero.AddToClassList("scene-toolbar-hero");

            var copy = new VisualElement();
            copy.AddToClassList("scene-toolbar-hero-copy");
            copy.Add(new Label("Scene Navigator").WithClass("scene-toolbar-title"));
            copy.Add(new Label("Find every project scene, open it, or pin it beside Play Mode.")
                .WithClass("scene-toolbar-subtitle"));
            hero.Add(copy);

            var stats = new VisualElement();
            stats.AddToClassList("scene-toolbar-stats");
            stats.Add(CreateStat(projectScenes.Count.ToString(), "PROJECT SCENES"));
            stats.Add(CreateStat(
                $"{settings.Entries.Count}/{SceneToolbarSettings.MaxEntries}",
                "PINNED"));
            hero.Add(stats);
            return hero;
        }

        private static VisualElement CreateStat(string value, string caption)
        {
            var stat = new VisualElement();
            stat.AddToClassList("scene-toolbar-stat");
            stat.Add(new Label(value).WithClass("scene-toolbar-stat-value"));
            stat.Add(new Label(caption).WithClass("scene-toolbar-stat-caption"));
            return stat;
        }

        private VisualElement CreateSearchBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("scene-toolbar-search-bar");

            var search = new ToolbarSearchField
            {
                name = "sceneSearchField",
                tooltip = "Search scene names and project paths"
            };
            search.SetValueWithoutNotify(searchQuery);
            search.AddToClassList("scene-toolbar-search");
            search.RegisterValueChangedCallback(changeEvent =>
            {
                searchQuery = changeEvent.newValue ?? string.Empty;
                RefreshProjectSceneResults();
            });
            bar.Add(search);

            var rescanButton = new Button(Rebuild)
            {
                text = "Rescan",
                tooltip = "Rescan all SceneAsset files under Assets"
            };
            rescanButton.AddToClassList("scene-toolbar-secondary-button");
            bar.Add(rescanButton);
            return bar;
        }

        private VisualElement CreateTabView(SceneToolbarSettings settings)
        {
            var tabView = new VisualElement
            {
                name = "sceneToolbarTabView"
            };
            tabView.AddToClassList("scene-toolbar-tab-view");

            var tabBar = new VisualElement
            {
                name = "sceneToolbarTabBar"
            };
            tabBar.AddToClassList("scene-toolbar-tab-bar");

            var projectScenesTab = new Button
            {
                name = "projectScenesTab",
                text = "Project Scenes"
            };
            projectScenesTab.AddToClassList("scene-toolbar-tab-button");
            tabBar.Add(projectScenesTab);

            var pinnedToolbarTab = new Button
            {
                name = "pinnedToolbarTab",
                text = "Pinned Toolbar"
            };
            pinnedToolbarTab.AddToClassList("scene-toolbar-tab-button");
            tabBar.Add(pinnedToolbarTab);
            tabView.Add(tabBar);

            var tabContent = new VisualElement
            {
                name = "sceneToolbarTabContent"
            };
            tabContent.AddToClassList("scene-toolbar-tab-content");

            var projectScenesPanel = CreateProjectScenePanel();
            projectScenesPanel.AddToClassList("scene-toolbar-tab-panel");
            var pinnedToolbarPanel = CreatePinnedScenePanel(settings);
            pinnedToolbarPanel.AddToClassList("scene-toolbar-tab-panel");
            tabContent.Add(projectScenesPanel);
            tabContent.Add(pinnedToolbarPanel);
            tabView.Add(tabContent);

            projectScenesTab.clicked += () => SelectTab(
                SettingsTab.ProjectScenes,
                projectScenesTab,
                projectScenesPanel,
                pinnedToolbarTab,
                pinnedToolbarPanel);
            pinnedToolbarTab.clicked += () => SelectTab(
                SettingsTab.PinnedToolbar,
                projectScenesTab,
                projectScenesPanel,
                pinnedToolbarTab,
                pinnedToolbarPanel);
            SelectTab(
                activeTab,
                projectScenesTab,
                projectScenesPanel,
                pinnedToolbarTab,
                pinnedToolbarPanel);
            return tabView;
        }

        private void SelectTab(
            SettingsTab tab,
            Button projectScenesTab,
            VisualElement projectScenesPanel,
            Button pinnedToolbarTab,
            VisualElement pinnedToolbarPanel)
        {
            activeTab = tab;
            var projectScenesActive = tab == SettingsTab.ProjectScenes;
            projectScenesTab.EnableInClassList(
                "scene-toolbar-tab-button-active",
                projectScenesActive);
            projectScenesPanel.EnableInClassList(
                "scene-toolbar-tab-panel-active",
                projectScenesActive);
            pinnedToolbarTab.EnableInClassList(
                "scene-toolbar-tab-button-active",
                !projectScenesActive);
            pinnedToolbarPanel.EnableInClassList(
                "scene-toolbar-tab-panel-active",
                !projectScenesActive);
        }

        private VisualElement CreateProjectScenePanel()
        {
            var panel = new VisualElement
            {
                name = "projectScenePanel"
            };
            panel.AddToClassList("scene-toolbar-panel");

            var header = new VisualElement();
            header.AddToClassList("scene-toolbar-panel-header");
            var heading = new VisualElement();
            heading.AddToClassList("scene-toolbar-panel-heading");
            heading.Add(new Label("Project Scenes").WithClass("scene-toolbar-panel-title"));
            heading.Add(new Label("Search by scene name or any part of its path.")
                .WithClass("scene-toolbar-panel-description"));
            header.Add(heading);
            projectSceneCount = new Label();
            projectSceneCount.AddToClassList("scene-toolbar-count-badge");
            header.Add(projectSceneCount);
            panel.Add(header);

            var scrollView = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "projectSceneList"
            };
            scrollView.AddToClassList("scene-toolbar-scroll");
            projectSceneResults = new VisualElement();
            projectSceneResults.AddToClassList("scene-toolbar-results");
            scrollView.Add(projectSceneResults);
            panel.Add(scrollView);

            RefreshProjectSceneResults();
            return panel;
        }

        private void RefreshProjectSceneResults()
        {
            if (projectSceneResults == null || projectSceneCount == null || currentSettings == null)
            {
                return;
            }

            projectSceneResults.Clear();
            var matches = projectScenes
                .Where(scene => SceneAssetUtility.MatchesSearch(scene, searchQuery))
                .ToList();
            projectSceneCount.text = string.IsNullOrWhiteSpace(searchQuery)
                ? matches.Count.ToString()
                : $"{matches.Count}/{projectScenes.Count}";

            if (matches.Count == 0)
            {
                projectSceneResults.Add(new Label(
                    string.IsNullOrWhiteSpace(searchQuery)
                        ? "No SceneAsset files were found under Assets."
                        : $"No scenes match ‘{searchQuery}’."
                ).WithClass("scene-toolbar-empty-card"));
                return;
            }

            var buildScenePaths = new HashSet<string>(
                EditorBuildSettings.scenes
                    .Where(scene => scene.enabled)
                    .Select(scene => scene.path),
                StringComparer.OrdinalIgnoreCase);
            foreach (var scene in matches)
            {
                projectSceneResults.Add(CreateProjectSceneRow(
                    scene,
                    buildScenePaths.Contains(scene.Path)));
            }
        }

        private VisualElement CreateProjectSceneRow(ResolvedSceneAsset scene, bool isBuildScene)
        {
            var isPinned = currentSettings.Contains(scene.Guid);
            var isActive = SceneAssetUtility.IsActiveScene(scene.Path);
            var isOnlyOpenScene = SceneAssetUtility.IsOnlyOpenScene(scene.Path);
            var row = new VisualElement();
            row.AddToClassList("scene-toolbar-scene-row");
            if (isActive)
            {
                row.AddToClassList("scene-toolbar-scene-row-active");
            }

            row.Add(CreateIconPreview(
                SceneAssetUtility.GetDefaultSceneIcon(),
                "scene-toolbar-scene-icon"));

            var information = new VisualElement();
            information.AddToClassList("scene-toolbar-scene-information");
            var nameLine = new VisualElement();
            nameLine.AddToClassList("scene-toolbar-name-line");
            nameLine.Add(new Label(scene.AssetName).WithClass("scene-toolbar-scene-name"));
            if (isActive)
            {
                nameLine.Add(CreateBadge("ACTIVE", "scene-toolbar-badge-active"));
            }

            if (isPinned)
            {
                nameLine.Add(CreateBadge("PINNED", "scene-toolbar-badge-pinned"));
            }

            if (isBuildScene)
            {
                nameLine.Add(CreateBadge("BUILD", "scene-toolbar-badge-build"));
            }

            information.Add(nameLine);
            information.Add(new Label(scene.Path)
            {
                tooltip = scene.Path
            }.WithClass("scene-toolbar-scene-path"));
            row.Add(information);

            var actions = new VisualElement();
            actions.AddToClassList("scene-toolbar-row-actions");
            var locateButton = new Button(() =>
            {
                Selection.activeObject = scene.SceneAsset;
                EditorGUIUtility.PingObject(scene.SceneAsset);
            })
            {
                text = "Locate",
                tooltip = "Select this scene in the Project window"
            };
            locateButton.AddToClassList("scene-toolbar-small-button");
            actions.Add(locateButton);

            var openButton = new Button(() => SceneOpenAction.Open(scene.Guid))
            {
                text = "Open",
                tooltip = isOnlyOpenScene
                    ? "This is already the only open scene"
                    : "Open this scene in Single mode"
            };
            openButton.SetEnabled(SceneAssetUtility.CanOpenScenes && !isOnlyOpenScene);
            openButton.AddToClassList("scene-toolbar-small-button");
            actions.Add(openButton);

            var pinButton = new Button(() =>
            {
                if (isPinned)
                {
                    SceneToolbarSettingsRepository.Remove(scene.Guid);
                }
                else
                {
                    SceneToolbarSettingsRepository.AddScenes(new[] { scene.SceneAsset });
                }
            })
            {
                text = isPinned ? "Unpin" : "Pin",
                tooltip = isPinned
                    ? "Remove this scene from the main toolbar"
                    : "Add this scene beside the Play Mode controls"
            };
            pinButton.SetEnabled(
                isPinned || currentSettings.Entries.Count < SceneToolbarSettings.MaxEntries);
            pinButton.AddToClassList("scene-toolbar-small-button");
            pinButton.AddToClassList(
                isPinned ? "scene-toolbar-unpin-button" : "scene-toolbar-pin-button");
            actions.Add(pinButton);
            row.Add(actions);
            return row;
        }

        private VisualElement CreatePinnedScenePanel(SceneToolbarSettings settings)
        {
            var panel = new VisualElement
            {
                name = "pinnedScenePanel"
            };
            panel.AddToClassList("scene-toolbar-panel");

            var header = new VisualElement();
            header.AddToClassList("scene-toolbar-panel-header");
            var heading = new VisualElement();
            heading.AddToClassList("scene-toolbar-panel-heading");
            heading.Add(new Label("Pinned Toolbar").WithClass("scene-toolbar-panel-title"));
            heading.Add(new Label("Ordered left to right beside Play Mode.")
                .WithClass("scene-toolbar-panel-description"));
            header.Add(heading);
            header.Add(new Label($"{settings.Entries.Count}/{SceneToolbarSettings.MaxEntries}")
                .WithClass("scene-toolbar-count-badge"));
            panel.Add(header);

            var scrollView = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "pinnedSceneList"
            };
            scrollView.AddToClassList("scene-toolbar-scroll");
            if (settings.Entries.Count == 0)
            {
                var empty = new VisualElement();
                empty.AddToClassList("scene-toolbar-empty-card");
                empty.Add(new Label("Nothing pinned yet.").WithClass("scene-toolbar-empty-title"));
                empty.Add(new Label("Use Pin on any project scene to add a one-click toolbar button.")
                    .WithClass("scene-toolbar-empty-copy"));
                scrollView.Add(empty);
            }
            else
            {
                for (var index = 0; index < settings.Entries.Count; index++)
                {
                    var entry = settings.Entries[index];
                    if (entry != null)
                    {
                        scrollView.Add(CreatePinnedSceneCard(
                            entry,
                            index,
                            settings.Entries.Count));
                    }
                }
            }

            panel.Add(scrollView);
            return panel;
        }

        private VisualElement CreatePinnedSceneCard(
            SceneToolbarEntry entry,
            int index,
            int entryCount)
        {
            var sceneGuid = entry.SceneGuid;
            var scene = SceneAssetUtility.ResolveScene(sceneGuid);
            var card = new VisualElement();
            card.AddToClassList("scene-toolbar-pinned-card");

            var header = new VisualElement();
            header.AddToClassList("scene-toolbar-pinned-header");
            header.Add(new Label((index + 1).ToString()).WithClass("scene-toolbar-position"));
            header.Add(CreateIconPreview(
                SceneAssetUtility.ResolveIcon(entry) ?? SceneAssetUtility.GetDefaultSceneIcon(),
                "scene-toolbar-pinned-icon"));

            var information = new VisualElement();
            information.AddToClassList("scene-toolbar-pinned-information");
            var displayName = !string.IsNullOrWhiteSpace(entry.DisplayName)
                ? entry.DisplayName
                : !string.IsNullOrWhiteSpace(scene.AssetName)
                    ? scene.AssetName
                    : "Missing Scene";
            information.Add(new Label(displayName).WithClass("scene-toolbar-pinned-name"));
            information.Add(new Label(scene.Path)
            {
                tooltip = scene.Path
            }.WithClass("scene-toolbar-scene-path"));
            header.Add(information);

            var actions = new VisualElement();
            actions.AddToClassList("scene-toolbar-row-actions");
            var openButton = new Button(() => SceneOpenAction.Open(sceneGuid))
            {
                text = "Open",
                tooltip = "Open this scene"
            };
            openButton.SetEnabled(
                scene.IsValid &&
                SceneAssetUtility.CanOpenScenes &&
                !SceneAssetUtility.IsOnlyOpenScene(scene.Path));
            openButton.AddToClassList("scene-toolbar-small-button");
            actions.Add(openButton);
            actions.Add(CreateMoveButton(sceneGuid, index, -1, "↑", "Move left"));
            actions.Add(CreateMoveButton(sceneGuid, index, 1, "↓", "Move right", entryCount));

            var removeButton = new Button(() => SceneToolbarSettingsRepository.Remove(sceneGuid))
            {
                text = "Unpin",
                tooltip = "Remove this scene from the main toolbar"
            };
            removeButton.AddToClassList("scene-toolbar-small-button");
            removeButton.AddToClassList("scene-toolbar-unpin-button");
            actions.Add(removeButton);
            header.Add(actions);
            card.Add(header);

            if (!scene.IsValid)
            {
                card.Add(new Label($"Missing SceneAsset (GUID: {sceneGuid})")
                    .WithClass("scene-toolbar-warning"));
            }

            var appearance = new Foldout
            {
                text = "Toolbar appearance",
                value = expandedEntries.Contains(sceneGuid)
            };
            appearance.AddToClassList("scene-toolbar-appearance");
            appearance.RegisterValueChangedCallback(changeEvent =>
            {
                if (changeEvent.newValue)
                {
                    expandedEntries.Add(sceneGuid);
                }
                else
                {
                    expandedEntries.Remove(sceneGuid);
                }
            });
            appearance.Add(CreateDisplayNameField(entry, sceneGuid));
            appearance.Add(CreateDisplayModeField(entry, sceneGuid));
            appearance.Add(CreateIconSourceField(entry, sceneGuid));
            switch (entry.IconSource)
            {
                case SceneToolbarIconSource.BuiltIn:
                    appearance.Add(CreateBuiltInIconControls(entry, sceneGuid));
                    break;
                case SceneToolbarIconSource.Custom:
                    appearance.Add(CreateCustomIconControls(entry, sceneGuid));
                    break;
            }

            card.Add(appearance);
            return card;
        }

        private static Button CreateMoveButton(
            string sceneGuid,
            int index,
            int offset,
            string text,
            string tooltip,
            int entryCount = int.MaxValue)
        {
            var button = new Button(() => SceneToolbarSettingsRepository.Move(sceneGuid, offset))
            {
                text = text,
                tooltip = tooltip
            };
            button.SetEnabled(offset < 0 ? index > 0 : index < entryCount - 1);
            button.AddToClassList("scene-toolbar-icon-button");
            return button;
        }

        private static TextField CreateDisplayNameField(SceneToolbarEntry entry, string sceneGuid)
        {
            var field = new TextField("Toolbar Label")
            {
                value = entry.DisplayName,
                isDelayed = true,
                tooltip = "Leave empty to use the SceneAsset name."
            };
            field.AddToClassList("scene-toolbar-field");
            field.RegisterValueChangedCallback(changeEvent =>
                SceneToolbarSettingsRepository.Update(
                    sceneGuid,
                    "Change Scene Pin Label",
                    currentEntry => currentEntry.DisplayName = changeEvent.newValue));
            return field;
        }

        private static EnumField CreateDisplayModeField(SceneToolbarEntry entry, string sceneGuid)
        {
            var field = new EnumField("Display Mode", entry.DisplayMode);
            field.AddToClassList("scene-toolbar-field");
            field.RegisterValueChangedCallback(changeEvent =>
                SceneToolbarSettingsRepository.Update(
                    sceneGuid,
                    "Change Scene Pin Display Mode",
                    currentEntry => currentEntry.DisplayMode =
                        (SceneToolbarDisplayMode)changeEvent.newValue));
            return field;
        }

        private static EnumField CreateIconSourceField(SceneToolbarEntry entry, string sceneGuid)
        {
            var field = new EnumField("Icon Source", entry.IconSource);
            field.AddToClassList("scene-toolbar-field");
            field.RegisterValueChangedCallback(changeEvent =>
                SceneToolbarSettingsRepository.Update(
                    sceneGuid,
                    "Change Scene Pin Icon Source",
                    currentEntry => currentEntry.IconSource =
                        (SceneToolbarIconSource)changeEvent.newValue));
            return field;
        }

        private static VisualElement CreateBuiltInIconControls(
            SceneToolbarEntry entry,
            string sceneGuid)
        {
            var row = new VisualElement();
            row.AddToClassList("scene-toolbar-icon-row");
            var iconNameField = new TextField("Built-in Icon")
            {
                value = entry.BuiltInIconName,
                isDelayed = true,
                tooltip = "Example: SceneAsset Icon"
            };
            iconNameField.AddToClassList("scene-toolbar-field");
            iconNameField.AddToClassList("scene-toolbar-grow");
            iconNameField.RegisterValueChangedCallback(changeEvent =>
                SceneToolbarSettingsRepository.Update(
                    sceneGuid,
                    "Change Scene Pin Built-in Icon",
                    currentEntry => currentEntry.BuiltInIconName = changeEvent.newValue));
            row.Add(iconNameField);

            var isValid = SceneAssetUtility.TryGetBuiltInIcon(entry.BuiltInIconName, out var icon);
            icon ??= SceneAssetUtility.GetDefaultSceneIcon();
            row.Add(CreateIconPreview(icon, "scene-toolbar-icon-preview"));
            row.Add(new Label(isValid ? "Valid" : "Using fallback")
                .WithClass(isValid ? "scene-toolbar-valid" : "scene-toolbar-warning"));
            return row;
        }

        private static VisualElement CreateCustomIconControls(
            SceneToolbarEntry entry,
            string sceneGuid)
        {
            var row = new VisualElement();
            row.AddToClassList("scene-toolbar-icon-row");
            var customIconField = new ObjectField("Custom Icon")
            {
                objectType = typeof(Texture2D),
                allowSceneObjects = false,
                value = entry.CustomIcon
            };
            customIconField.AddToClassList("scene-toolbar-field");
            customIconField.AddToClassList("scene-toolbar-grow");
            customIconField.RegisterValueChangedCallback(changeEvent =>
                SceneToolbarSettingsRepository.Update(
                    sceneGuid,
                    "Change Scene Pin Custom Icon",
                    currentEntry => currentEntry.CustomIcon = changeEvent.newValue as Texture2D));
            row.Add(customIconField);
            row.Add(CreateIconPreview(
                SceneAssetUtility.ResolveIcon(entry),
                "scene-toolbar-icon-preview"));
            if (entry.CustomIcon == null)
            {
                row.Add(new Label("Using fallback").WithClass("scene-toolbar-warning"));
            }

            return row;
        }

        private static Label CreateBadge(string text, string variantClass)
        {
            var badge = new Label(text);
            badge.AddToClassList("scene-toolbar-badge");
            badge.AddToClassList(variantClass);
            return badge;
        }

        private static Image CreateIconPreview(Texture2D icon, string className)
        {
            var preview = new Image
            {
                image = icon,
                scaleMode = ScaleMode.ScaleToFit
            };
            preview.AddToClassList(className);
            return preview;
        }

        private void ScheduleRebuild()
        {
            if (rebuildScheduled)
            {
                return;
            }

            rebuildScheduled = true;
            rootVisualElement.schedule.Execute(() =>
            {
                rebuildScheduled = false;
                Rebuild();
            });
        }

        private void OnActiveSceneChanged(Scene previousScene, Scene nextScene)
        {
            ScheduleRebuild();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            ScheduleRebuild();
        }
    }

    internal static class VisualElementExtensions
    {
        internal static T WithClass<T>(this T element, string className) where T : VisualElement
        {
            element.AddToClassList(className);
            return element;
        }
    }
}
