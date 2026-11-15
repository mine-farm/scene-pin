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
        private const string IconDirectoryPath =
            "Packages/com.minefarm.scene-pin/Editor/Settings/Icons/";

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
            window.ApplyWindowTitle();
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
            ApplyWindowTitle();
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }

            rootVisualElement.name = "sceneToolbarRoot";
            rootVisualElement.AddToClassList("scene-toolbar-root");
            Rebuild();
        }

        private void ApplyWindowTitle()
        {
            titleContent = new GUIContent(
                "Scene Pin",
                SceneAssetUtility.GetDefaultSceneIcon());
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
            rootVisualElement.Add(CreatePinnedOrganizer(currentSettings));

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

            var rescanButton = CreateActionButton(
                Rebuild,
                "refresh",
                "Rescan all SceneAsset files under Assets",
                "scene-toolbar-secondary-button");
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

        private VisualElement CreatePinnedOrganizer(SceneToolbarSettings settings)
        {
            var organizer = new VisualElement
            {
                name = "pinnedOrganizer"
            };
            organizer.AddToClassList("scene-toolbar-organizer");

            var header = new VisualElement();
            header.AddToClassList("scene-toolbar-organizer-header");
            header.Add(new Label("Pinned order").WithClass("scene-toolbar-organizer-title"));
            header.Add(new Label(
                    "Drag scene icons here, move pins left or right, or drop one on Unpin.")
                .WithClass("scene-toolbar-organizer-description"));
            organizer.Add(header);

            var body = new VisualElement();
            body.AddToClassList("scene-toolbar-organizer-body");

            var scrollView = new ScrollView(ScrollViewMode.Horizontal)
            {
                name = "pinnedOrganizerScroll",
                horizontalScrollerVisibility = ScrollerVisibility.Auto,
                verticalScrollerVisibility = ScrollerVisibility.Hidden
            };
            scrollView.AddToClassList("scene-toolbar-organizer-scroll");

            var strip = new VisualElement
            {
                name = "pinnedDropStrip"
            };
            strip.AddToClassList("scene-toolbar-organizer-strip");
            if (settings.Entries.Count == 0)
            {
                strip.Add(new Label("Drag a project scene icon here to pin it.")
                    .WithClass("scene-toolbar-organizer-empty"));
            }
            else
            {
                for (var index = 0; index < settings.Entries.Count; index++)
                {
                    var entry = settings.Entries[index];
                    if (entry != null)
                    {
                        strip.Add(CreatePinnedOrganizerChip(entry, index));
                    }
                }
            }

            ConfigurePinnedDropStrip(strip, settings);
            scrollView.Add(strip);
            body.Add(scrollView);

            var unpinZone = new VisualElement
            {
                name = "unpinDropZone",
                tooltip = "Drop a pinned scene here to remove it from the main toolbar"
            };
            unpinZone.AddToClassList("scene-toolbar-unpin-drop-zone");
            var unpinIcon = new Image
            {
                vectorImage = AssetDatabase.LoadAssetAtPath<VectorImage>(
                    $"{IconDirectoryPath}pin.svg"),
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore,
                tintColor = GetDangerIconColor()
            };
            unpinIcon.AddToClassList("scene-toolbar-unpin-drop-icon");
            unpinZone.Add(unpinIcon);
            unpinZone.Add(new Label("Unpin").WithClass("scene-toolbar-unpin-drop-label"));
            ConfigureUnpinDropZone(unpinZone);
            body.Add(unpinZone);

            organizer.Add(body);
            return organizer;
        }

        private VisualElement CreatePinnedOrganizerChip(SceneToolbarEntry entry, int index)
        {
            var sceneGuid = entry.SceneGuid;
            var scene = SceneAssetUtility.ResolveScene(sceneGuid);
            var displayName = GetSceneDisplayName(entry, scene);
            var chip = new VisualElement
            {
                userData = sceneGuid,
                tooltip = $"Drag {displayName} to reorder or unpin it"
            };
            chip.AddToClassList("scene-toolbar-organizer-chip");
            chip.Add(new Label((index + 1).ToString())
                .WithClass("scene-toolbar-organizer-position"));
            chip.Add(CreateIconPreview(
                SceneAssetUtility.ResolveIcon(entry) ?? SceneAssetUtility.GetDefaultSceneIcon(),
                "scene-toolbar-organizer-icon"));
            chip.Add(new Label(displayName).WithClass("scene-toolbar-organizer-name"));
            chip.AddManipulator(new SceneToolbarDragManipulator(
                new SceneToolbarDragData(sceneGuid, scene.SceneAsset, true)));
            return chip;
        }

        private void ConfigurePinnedDropStrip(
            VisualElement strip,
            SceneToolbarSettings settings)
        {
            var marker = new VisualElement
            {
                name = "pinnedInsertionMarker",
                pickingMode = PickingMode.Ignore
            };
            marker.AddToClassList("scene-toolbar-drop-marker");

            void ClearDropState()
            {
                marker.RemoveFromHierarchy();
                strip.RemoveFromClassList("scene-toolbar-organizer-strip-active");
            }

            strip.RegisterCallback<DragUpdatedEvent>(dragEvent =>
            {
                var dragData = SceneToolbarDragData.Current;
                var canAccept = dragData != null &&
                                (dragData.IsPinned ||
                                 settings.Entries.Count < SceneToolbarSettings.MaxEntries);
                if (!canAccept)
                {
                    ClearDropState();
                    DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                    return;
                }

                var insertionIndex = GetPinnedInsertionIndex(
                    strip,
                    dragData.SceneGuid,
                    dragEvent.mousePosition);
                ShowInsertionMarker(strip, marker, insertionIndex, dragData.SceneGuid);
                strip.AddToClassList("scene-toolbar-organizer-strip-active");
                DragAndDrop.visualMode = dragData.IsPinned
                    ? DragAndDropVisualMode.Move
                    : DragAndDropVisualMode.Copy;
                dragEvent.StopPropagation();
            });

            strip.RegisterCallback<DragPerformEvent>(dragEvent =>
            {
                var dragData = SceneToolbarDragData.Current;
                if (dragData == null)
                {
                    return;
                }

                var insertionIndex = GetPinnedInsertionIndex(
                    strip,
                    dragData.SceneGuid,
                    dragEvent.mousePosition);
                DragAndDrop.AcceptDrag();
                if (dragData.IsPinned)
                {
                    SceneToolbarSettingsRepository.MoveTo(
                        dragData.SceneGuid,
                        insertionIndex);
                }
                else
                {
                    SceneToolbarSettingsRepository.AddSceneAt(
                        dragData.SceneAsset,
                        insertionIndex);
                }

                ClearDropState();
                dragEvent.StopPropagation();
            });
            strip.RegisterCallback<DragLeaveEvent>(_ => ClearDropState());
            strip.RegisterCallback<DragExitedEvent>(_ => ClearDropState());
        }

        private static void ConfigureUnpinDropZone(VisualElement unpinZone)
        {
            void SetActive(bool active)
            {
                unpinZone.EnableInClassList(
                    "scene-toolbar-unpin-drop-zone-active",
                    active);
            }

            unpinZone.RegisterCallback<DragUpdatedEvent>(dragEvent =>
            {
                var canUnpin = SceneToolbarDragData.Current?.IsPinned == true;
                SetActive(canUnpin);
                DragAndDrop.visualMode = canUnpin
                    ? DragAndDropVisualMode.Move
                    : DragAndDropVisualMode.Rejected;
                if (canUnpin)
                {
                    dragEvent.StopPropagation();
                }
            });
            unpinZone.RegisterCallback<DragPerformEvent>(dragEvent =>
            {
                var dragData = SceneToolbarDragData.Current;
                if (dragData?.IsPinned != true)
                {
                    return;
                }

                DragAndDrop.AcceptDrag();
                SceneToolbarSettingsRepository.Remove(dragData.SceneGuid);
                SetActive(false);
                dragEvent.StopPropagation();
            });
            unpinZone.RegisterCallback<DragLeaveEvent>(_ => SetActive(false));
            unpinZone.RegisterCallback<DragExitedEvent>(_ => SetActive(false));
        }

        private static int GetPinnedInsertionIndex(
            VisualElement strip,
            string draggedSceneGuid,
            Vector2 mousePosition)
        {
            var chips = strip.Children()
                .Where(child =>
                    child.ClassListContains("scene-toolbar-organizer-chip") &&
                    !string.Equals(
                        child.userData as string,
                        draggedSceneGuid,
                        StringComparison.Ordinal))
                .ToList();
            for (var index = 0; index < chips.Count; index++)
            {
                if (mousePosition.x < chips[index].worldBound.center.x)
                {
                    return index;
                }
            }

            return chips.Count;
        }

        private static void ShowInsertionMarker(
            VisualElement strip,
            VisualElement marker,
            int insertionIndex,
            string draggedSceneGuid)
        {
            marker.RemoveFromHierarchy();
            var chips = strip.Children()
                .Where(child =>
                    child.ClassListContains("scene-toolbar-organizer-chip") &&
                    !string.Equals(
                        child.userData as string,
                        draggedSceneGuid,
                        StringComparison.Ordinal))
                .ToList();
            if (insertionIndex < chips.Count)
            {
                strip.Insert(strip.IndexOf(chips[insertionIndex]), marker);
            }
            else
            {
                var emptyHint = strip.Q<Label>(className: "scene-toolbar-organizer-empty");
                strip.Insert(emptyHint != null ? strip.IndexOf(emptyHint) : strip.childCount, marker);
            }
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
            heading.Add(new Label("Search by name or path, then drag a scene icon into Pinned order.")
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

            var sceneIcon = CreateIconPreview(
                SceneAssetUtility.GetDefaultSceneIcon(),
                "scene-toolbar-scene-icon");
            sceneIcon.tooltip = isPinned
                ? "Drag to reorder or unpin this scene"
                : "Drag to add this scene to Pinned order";
            sceneIcon.AddToClassList("scene-toolbar-scene-drag-source");
            sceneIcon.AddManipulator(new SceneToolbarDragManipulator(
                new SceneToolbarDragData(scene.Guid, scene.SceneAsset, isPinned)));
            row.Add(sceneIcon);

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
            var locateButton = CreateActionButton(
                () =>
                {
                    Selection.activeObject = scene.SceneAsset;
                    EditorGUIUtility.PingObject(scene.SceneAsset);
                },
                "locate",
                "Select this scene in the Project window",
                "scene-toolbar-small-button");
            actions.Add(locateButton);

            var openButton = CreateActionButton(
                () => SceneOpenAction.Open(scene.Guid),
                "open",
                isOnlyOpenScene
                    ? "This is already the only open scene"
                    : "Open this scene in Single mode",
                "scene-toolbar-small-button");
            openButton.SetEnabled(SceneAssetUtility.CanOpenScenes && !isOnlyOpenScene);
            actions.Add(openButton);

            var pinButton = CreateActionButton(
                () =>
                {
                    if (isPinned)
                    {
                        SceneToolbarSettingsRepository.Remove(scene.Guid);
                    }
                    else
                    {
                        SceneToolbarSettingsRepository.AddScenes(new[] { scene.SceneAsset });
                    }
                },
                "pin",
                isPinned
                    ? "Remove this scene from the main toolbar"
                    : "Add this scene beside the Play Mode controls",
                "scene-toolbar-small-button",
                isPinned ? GetDangerIconColor() : Color.white);
            pinButton.SetEnabled(
                isPinned || currentSettings.Entries.Count < SceneToolbarSettings.MaxEntries);
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
            heading.Add(new Label("Edit appearance below. Drag Pinned order above to rearrange.")
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
                empty.Add(new Label("Drag a project scene icon into Pinned order above.")
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
                            index));
                    }
                }
            }

            panel.Add(scrollView);
            return panel;
        }

        private VisualElement CreatePinnedSceneCard(
            SceneToolbarEntry entry,
            int index)
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
            var displayName = GetSceneDisplayName(entry, scene);
            information.Add(new Label(displayName).WithClass("scene-toolbar-pinned-name"));
            information.Add(new Label(scene.Path)
            {
                tooltip = scene.Path
            }.WithClass("scene-toolbar-scene-path"));
            header.Add(information);

            var actions = new VisualElement();
            actions.AddToClassList("scene-toolbar-row-actions");
            var openButton = CreateActionButton(
                () => SceneOpenAction.Open(sceneGuid),
                "open",
                "Open this scene",
                "scene-toolbar-small-button");
            openButton.SetEnabled(
                scene.IsValid &&
                SceneAssetUtility.CanOpenScenes &&
                !SceneAssetUtility.IsOnlyOpenScene(scene.Path));
            actions.Add(openButton);

            var removeButton = CreateActionButton(
                () => SceneToolbarSettingsRepository.Remove(sceneGuid),
                "pin",
                "Remove this scene from the main toolbar",
                "scene-toolbar-small-button",
                GetDangerIconColor());
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

        private static string GetSceneDisplayName(
            SceneToolbarEntry entry,
            ResolvedSceneAsset scene)
        {
            return !string.IsNullOrWhiteSpace(entry.DisplayName)
                ? entry.DisplayName
                : !string.IsNullOrWhiteSpace(scene.AssetName)
                    ? scene.AssetName
                    : "Missing Scene";
        }

        private static Button CreateActionButton(
            Action action,
            string iconName,
            string tooltip,
            string buttonClass,
            Color? iconTint = null)
        {
            var button = new Button(action)
            {
                tooltip = tooltip
            };
            button.AddToClassList(buttonClass);

            var icon = new Image
            {
                vectorImage = AssetDatabase.LoadAssetAtPath<VectorImage>(
                    $"{IconDirectoryPath}{iconName}.svg"),
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore,
                tintColor = iconTint ?? GetDefaultIconColor()
            };
            icon.AddToClassList("scene-toolbar-button-icon");
            button.Add(icon);
            return button;
        }

        private static Color GetDefaultIconColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color32(205, 205, 205, 255)
                : new Color32(55, 55, 55, 255);
        }

        private static Color GetDangerIconColor()
        {
            return new Color32(217, 108, 114, 255);
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
