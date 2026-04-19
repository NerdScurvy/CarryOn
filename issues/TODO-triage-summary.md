# TODO triage summary

This doc summarizes the TODO comments found under `src/` and provides a recommended priority and next steps for each.

Scan date: 2025-10-05

Summary:

- Total TODOs found: 12
- Files touched: InteractionLogic, BlockPlacer, CloseBlockEntityDialog, PackAdjustmentHandler, CarryHandler, HudOverlayRenderer, EntityCarryRenderer, RackEmUp subproject

Recommended workflow:

1. Prioritise high-impact items (possible bugs that affect game mechanics or can crash). Implement fixes and add automated tests where feasible.
2. Medium items: performance and UX improvements — implement when convenient or in a small follow-up PR.
3. Low items: cosmetic or future polish — file issues and defer to later milestones.

I created individual issue files (`issues/0001-0012-*.md`) with context and recommendations. You can use them as the basis for GitHub issues.

If you want I can:
- Open a PR implementing any of the medium/high items.
- Create GitHub issues via API (requires token and permission).
- Implement the easiest ones now (e.g., smoothing HUD progress or wiring MaxInteractionDistance to config).
