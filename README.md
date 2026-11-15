# Undo

**Current release: 1.0.2 — for Kingdom Inc game version 1.0.14.**

Undo and redo factory edits with **Ctrl+Z** and **Ctrl+Shift+Z**.

## Important: future built-in Undo/Redo

The game developer has stated that built-in Undo/Redo is planned for a future update. **Uninstall this mod when the official Undo/Redo feature is released.** Close the game and remove `BepInEx/plugins/Undo/` before using the built-in system.

## Features

- Undo and redo building placement, movement, deletion, stacking, mirroring, and upgrade changes.
- Multi-select moves and grouped deletes each take one Undo, including affected connections and pipe handles.
- Supports Better Deconstruct box-delete confirmations and category filters when that optional mod is installed.
- Restore pipe connections, routing handles, and decoration placement, movement, resizing, labels, styles, locks, and anchoring.
- Reverse the exact cost or refund of the recorded edit while keeping income earned afterward.

## Controls

- **Ctrl+Z:** Undo the latest edit.
- **Ctrl+Shift+Z:** Redo the latest undone edit.
- Either left or right Ctrl and Shift works.
- During a drag or placement, the shortcut first cancels the active interaction. Press it again to use history.
- Shortcuts leave focused text fields to their own text editing controls.

## Requirements and installation

Kingdom Inc for Windows with BepInEx 5 and its bundled Harmony. Tested with game version **1.0.14** and **BepInEx 5.4.23.6**. [Foundation](https://www.nexusmods.com/kingdominc/mods/18) 0.14.4 or newer is required. Better Deconstruct is optional.

Close the game and extract the archive into the Kingdom Inc folder. The installed files are `BepInEx/plugins/Undo/Undo.dll` and `BepInEx/plugins/Undo/mod.json`.

## History and limitations

History keeps the last **50 actions** by default. Set `MaximumActions` from 1 to 200 in `BepInEx/config/urakka.kingdominc.undo.cfg` after the first launch.

History stays in memory and clears when loading a save, changing the factory list, or restarting the game. A new edit clears Redo. Production, research, recipes, combat, and progression are outside Undo history.

Redo requires enough currency to pay the recorded cost; undoing a refund requires enough currency to return it. If items transferred by an edit have since changed, replay is refused to avoid duplicating or losing those items. Unrecorded structural changes clear stale history.

## Compatibility

| Class.Method | Hook Type | Impact |
|---|---|---|
| DeconstructOverlay.Update | Foundation execution observer | Records a completed native delete and its refund. |
| DeconstructSelection.ExecuteDeconstruct | Optional Foundation execution observer | When Better Deconstruct is installed, records each confirmed box-delete as one edit, including category filters and refunds. |
| DecoOptionsPopup.UpdateAnchor | Foundation execution observer | Records decoration label, lock, and anchor changes. |
| DecoOptionsPopup.UpdateLabel | Foundation execution observer | Records decoration label, lock, and anchor changes. |
| DecoOptionsPopup.UpdateLock | Foundation execution observer | Records decoration label, lock, and anchor changes. |
| DecoOptionsStyle.StyleSelected | Foundation execution observer | Records decoration style changes. |
| DragDropManager._dropTarget | Foundation execution observer | Groups complete drag operations into single history actions. |
| DragDropManager.CancelDragDrop | Foundation execution observer | Groups complete drag operations into single history actions. |
| DragDropManager.SetDragDropTarget | Foundation execution observer | Groups complete drag operations into single history actions. |
| GameUI.ToggleDeconstructMode | Foundation execution observer | Records drag-to-delete, including grouped deletion and refunds. |
| WorldFlowDecoMover.OnDragDropCancel | Foundation execution observer | Groups decoration and multi-select movement, including connected pipe handles. |
| WorldFlowDecoMover.OnDragDropEnd | Foundation execution observer | Groups decoration and multi-select movement, including connected pipe handles. |
| WorldFlowDecoMover.StartDragDrop | Foundation execution observer | Groups decoration and multi-select movement, including connected pipe handles. |
| WorldFlowDecoResizer.OnDragDropCancel | Foundation execution observer | Groups decoration resizing and cancellation. |
| WorldFlowDecoResizer.OnDragDropEnd | Foundation execution observer | Groups decoration resizing and cancellation. |
| WorldFlowDecoResizer.StartDragDrop | Foundation execution observer | Groups decoration resizing and cancellation. |
| WorldFlowElement.AttemptStackElement | Foundation execution observer | Records stacking and mirroring, including their costs. |
| WorldFlowElement.OnPointerClick | Foundation execution observer | Records stacking and mirroring, including their costs. |
| WorldFlowGhost.OnDragDropEnd | Foundation execution observer | Records a completed placement and its cost as one edit. |
| WorldFlowIO._startDragDrop | Foundation execution observer | Groups pipe connection, disconnection, and drag edits. |
| WorldFlowIO.OnBeginDrag | Foundation execution observer | Groups pipe connection, disconnection, and drag edits. |
| WorldFlowIO.OnDragDropCancel | Foundation execution observer | Groups pipe connection, disconnection, and drag edits. |
| WorldFlowIO.OnDragDropEnd | Foundation execution observer | Groups pipe connection, disconnection, and drag edits. |
| WorldFlowIO.OnPointerClick | Foundation execution observer | Groups pipe connection, disconnection, and drag edits. |
| WorldFlowLabel.OnBeginDrag | Foundation execution observer | Groups building movement and cancellation. |
| WorldFlowLabel.OnDragDropCancel | Foundation execution observer | Groups building movement and cancellation. |
| WorldFlowLabel.OnDragDropEnd | Foundation execution observer | Groups building movement and cancellation. |
| WorldFlowLabel.OnPointerClick | Foundation execution observer | Groups building movement and cancellation. |
| WorldFlowPipe.AddNewNode | Foundation execution observer | Records pipe-handle insertion and removal. |
| WorldFlowPipe.RemoveNode | Foundation execution observer | Records pipe-handle insertion and removal. |
| WorldFlowPipeNode.OnDragDropCancel | Foundation execution observer | Groups pipe-handle movement and cancellation. |
| WorldFlowPipeNode.OnDragDropEnd | Foundation execution observer | Groups pipe-handle movement and cancellation. |
| WorldFlowPipeNode.StartDragDrop | Foundation execution observer | Groups pipe-handle movement and cancellation. |
| WorldFlowUpgrade.SelectNewUpgrade | Foundation execution observer | Records upgrade changes and their exact cost/refund. |

## Removal

Close the game and remove BepInEx/plugins/Undo/. History adds no extra save fields.

## Credits

Created by Urakka. Game artwork belongs to its respective owners.
