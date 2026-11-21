# tilemappreview

Measures Unity 6000.0.71f1 Editor `Tilemap` editor-preview APIs:

- `SetEditorPreviewTile`, `HasEditorPreviewTile`, `GetEditorPreviewTile`, `GetEditorPreviewTile<T>`
- `GetEditorPreviewSprite`, `GetEditorPreviewTileFlags`
- `SetEditorPreviewColor`, `GetEditorPreviewColor` (including `TileFlags.LockColor` and empty/actual-only cells)
- `SetEditorPreviewTransformMatrix`, `GetEditorPreviewTransformMatrix` (including `TileFlags.LockTransform` and empty/actual-only cells)
- `EditorPreviewBoxFill` and `EditorPreviewFloodFill` (target matching against actual tiles, preview bounds growth, clamped/inverted/out-of-box regions, null fill, non-`TileBase` object, Z-layer isolation, pre-existing preview overlays)
- `ClearAllEditorPreviewTiles`, `editorPreviewOrigin`, `editorPreviewSize`, and interactions with `SetTile`, `CompressBounds`, `ResizeBounds`, `ClearAllTiles`, `tilemapTileChanged`, and `tilemapPositionsChanged`.
- The internal `CompressBoundsKeepEditorPreview` (through reflection).
- Callback order (`07.*`, `08.*`): the cells `BoxFill`, `FloodFill`, `EditorPreviewBoxFill` and
  `EditorPreviewFloodFill` visit and in which order, the old and new tiles' `RefreshTile`, the
  `GetTileData`, `StartUp` and `GetTileAnimationData` that follow (for four or fewer and for five
  or more changed cells, with tiles that ask to refresh their neighbours), `RefreshTile` of a cell
  holding both a preview and an actual tile, single preview sets and `ClearAllEditorPreviewTiles`,
  and the order of the cells in `tilemapTileChanged` and `tilemapPositionsChanged`.

```sh
python3 tests/projects/tilemappreview/run.py record --label a
python3 tests/projects/tilemappreview/run.py record --label b
python3 tests/projects/tilemappreview/run.py publish a b
python3 tests/projects/tilemappreview/run.py gate
```
