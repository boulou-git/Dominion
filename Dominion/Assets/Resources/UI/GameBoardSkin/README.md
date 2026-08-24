# GameBoard skin assets

Place the supplied low-poly UI PNGs in this folder with these exact filenames:

- `game_background.png`
- `board.png`
- `hand_board.png`
- `turn_board.png`
- `game_text.png`
- `buttons.png`
- `separators.png`

`GameBoardSkinApplier` loads these textures from Resources at runtime, crops the source canvases, builds 9-sliced panel/title/button sprites, and slices `buttons.png` into Normal / Hover / Pressed states. The existing `GameScreen` anchors and gameplay hierarchy remain authoritative.
