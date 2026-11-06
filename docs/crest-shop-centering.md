# Crest shop label centering

Design record for centering the crest shop row labels (`Item cost`, replaced with the
crest display name) on the list column. Code: `ShopCosmetics.CenterName` and its
helpers in `ShopCosmetics.cs`.

## Goal

Every row label must sit at the same world X, and that X must be the centre of the
list column the player sees — not the centre of any single object.

## What failed and why

- **`Item List Group` / `Window` RectTransform as the column.** These are layout
  containers that size themselves to their scroll content: roughly 100 world units
  wide, far wider than the visible column. The visible column is carved out by sprite
  masks and by rows fading at the edges, so rect centre lands outside the drawn column.

- **`Item cost` root renderer bounds.** The root renderer reports zero-size bounds.
  The actual glyph geometry lives on a TMP submesh child named `New Game Object`.

- **Name filter on `Item cost`.** Filtering out transforms named `Item cost` did not
  remove that submesh from the bounds union. Row "bounds" then contained only our own
  already-shifted text, so the computed target and the moved object were the same
  thing — a closed loop. The shift measured ~0.12 px and the labels never moved.

- **`Item List Group/selector` sprite.** The highlight sprite has transparent padding,
  so its `bounds.center` is offset by the padding and produces an outlier.

- **Re-measuring the target every frame.** The containers fit themselves to their
  contents (TMP container fitter). Moving the text grows the fitted container, which
  moves the re-measured target, which moves the text again: positive feedback, visible
  as drift across the session.

## Final solution

- **Anchor source `columns`**: `targetX = (Renderer.bounds.min.x of
  Window/Shop_Backboard + RectTransform world corner min.x of
  Item List Group/Item Details) / 2`. Both anchors are structural geometry in the same
  world space as the label, independent of the text, so the value is stable.
  No fallback is taken when either anchor is missing.
- **Target cached once per shop session**, cleared in Begin/End together with the
  baseline dictionaries.
- **Shift without accumulation**: `position.x = baselinePos + (targetX - baselineCenterX)`,
  where `baselineCenterX` is the drawn text centre and `baselinePos` the position, both
  measured before the first shift. Re-applying the same formula is idempotent, so
  repeated frames cannot drift.
- **Residual check**: one frame after the first shift, the actual centre of the TMP
  submesh is measured again and logged. Residual above `0.05` world units raises a
  warning ("scale or pivot issue") — that means the shifted transform and the drawn
  text are no longer in the same world space.

## Reference numbers

Measured at 1920×1080, HudCamera ortho size 8.71, scale ≈ 62 px per world unit:

| Quantity | World X |
|---|---|
| column edges | `-6.32 .. -0.64` |
| column centre (`targetX`) | `-3.48` |
| vanilla price label position | `-0.98` |
| required shift | `-2.4` |

Use these to sanity-check a new anchor source before trusting the log line
`[SHOPFX] centre '<name>' rows=N target=... baseline=... pos=... src=columns`.

## History

- `63a2142` — list-based label centering; dot diameter from measured silhouette height.
- `73af98e` — configurable label centre source with candidate logging
  (`General/TextCentreSource`).
- `1beb3b6` — cache label centre target and baseline to stop drift.
- `042f8de` — column centre source (list left edge to details left edge).
- `4999a0f` — row bars as centre source, valid-until-measured cache.
- `c6782fd` — **working state**: structural centre anchor
  `(backboard min + details min) / 2` with residual check.
