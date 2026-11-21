# tilemappreview

Measures Unity 6000.0.71f1 Editor `Tilemap` editor-preview APIs:

- `SetEditorPreviewTile`, `HasEditorPreviewTile`, `GetEditorPreviewTile`, `GetEditorPreviewTile<T>`
- `GetEditorPreviewSprite`, `GetEditorPreviewTileFlags`
- `SetEditorPreviewColor`, `GetEditorPreviewColor` (including `TileFlags.LockColor` and empty/actual-only cells)
- `SetEditorPreviewTransformMatrix`, `GetEditorPreviewTransformMatrix` (including `TileFlags.LockTransform` and empty/actual-only cells)
- `EditorPreviewBoxFill` and `EditorPreviewFloodFill` (target matching against actual tiles, preview bounds growth, clamped/inverted/out-of-box regions, null fill, non-`TileBase` object, Z-layer isolation, pre-existing preview overlays)
- `ClearAllEditorPreviewTiles`, `editorPreviewOrigin`, `editorPreviewSize`, and interactions with `SetTile`, `CompressBounds`, `ResizeBounds`, `ClearAllTiles`, `tilemapTileChanged`, and `tilemapPositionsChanged`.

```sh
python3 tests/projects/tilemappreview/run.py record --label a
python3 tests/projects/tilemappreview/run.py record --label b
python3 tests/projects/tilemappreview/run.py publish a b
python3 tests/projects/tilemappreview/run.py gate
```
