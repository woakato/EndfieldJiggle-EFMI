# Unified Alt Input

Version v0.2.4 (October 17, 2026) starts with touch enabled and no status message.
The configurator's startup checkbox can disable that default.

- Hold Alt and press the left mouse button on a surface to drag.
- Release the left mouse button to let go; Alt may remain held for the next grab.
- Each new mouse press selects a new surface. Pressing empty space does not latch later.
- Ctrl+Shift+F9 toggles touch off/on; startup is enabled.
- Ctrl+Shift+F7 disables touch.
- Ctrl+Shift+F8 toggles the optional diagnostic overlay.

Both scenes use the same input and mapping. The previous Ctrl+Shift+F6
WORLD TEST / CHARACTER MENU switch is removed. There is no automatic scene
classification. The common mapping is the accepted character-menu mapping.

The solver uses the cursor and viewport captured by the picker in that
frame. A press waits for a pick sampled while the drag chord was held.
Mouse release takes effect immediately even when Alt remains held.

The native/outfit hooks, both vertex pickers and the picker pixel shader
remain unchanged. The original press-only solver is restored, removing the
previous candidate's Alt-only first-hit wait. Cancellation or enabling
while already held does not start a new grab.
The configurator now
defaults to Alt plus left mouse and preserves that chord when saving.

The previous Alt-only binding ignored mouse release while Alt was held,
so clicking a lower point could retain the first upper-body anchor.
The corrected mouse chord restores release and reselection. Synthetic
tests cover picking and recapturing at multiple vertical positions.
The user confirmed that the corrected installed build works and accepted
publication in all three release packages. The offline GPU checks cover
5,760 assertions. No new world frame capture or complete character/outfit
matrix was supplied; this feedback does not establish universal coverage.
