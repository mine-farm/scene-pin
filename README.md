# Scene Pin

Scene Pin is an Editor-only utility for Unity 6000.3 and newer. It keeps your
frequently used scenes immediately beside the Play Mode controls so they are one
click away.

## Install

In Unity, open **Window > Package Management > Package Manager**, choose
**Install package from git URL**, and enter:

```text
https://github.com/mine-farm/scene-pin.git
```

To pin a release, append its tag, for example `#v0.1.6`.

## Use

1. Click the settings button immediately left of Play Mode. Scene Pin scans every
   `SceneAsset` under `Assets` and supports live search by scene name or path.
2. Use the icon actions to open a scene, locate it in the Project window, or pin it
   to the main toolbar. Hover an icon for its description. Up to eight scenes can
   be pinned.
3. You can also select scene assets in the Project window and use
   **Assets > Scene Pin > Add to Toolbar**.
4. Reorder pinned scenes, edit labels, choose built-in or custom icons, and select
   icon-only, text-only, or combined display from the settings window.

Opening a scene uses `OpenSceneMode.Single`. Unity shows its standard save prompt
first when the current scenes contain unsaved changes. Scene buttons are disabled
during Play Mode and while the selected scene is already the only open scene.

## Project settings

Pinned scenes are stored per consuming project at:

```text
Assets/Settings/ScenePinSettings.asset
```

Commit that asset and its `.meta` file to share pins with the team. Entries store
scene GUIDs, so scene moves and renames remain valid as long as their `.meta` files
keep the same GUID.

Projects migrating from the original in-project version automatically move the
legacy settings asset from
`Assets/Scripts/SceneToolbar/Editor/SceneToolbarSettings.asset` when the settings
window is first opened or a scene is first pinned.

## Notes

- The package is Editor-only and is excluded from player builds.
- Mixed selections containing non-scene assets keep the context actions disabled.
- Missing scenes remain visible for explicit cleanup and have disabled toolbar buttons.
- The toolbar is limited to eight entries to avoid overflowing Unity's main toolbar.
- The package uses Unity's public main-toolbar registration and Overlay APIs. A small
  reflection-based placement helper calls Unity's internal `DockBefore` method to keep
  Scene Pin immediately left of the Play Mode controls.

## Tests

The EditMode tests are in `Editor/Tests/Editor`. Enable package tests for
`com.minefarm.scene-pin` in the consuming project, then run them with Unity Test Runner.
