# Overflow tab hover failure

A report described an intermittent popup when moving the pointer over a strip with 10+ tabs, followed by difficulty closing tabs. The reported application version was 1.8.2.0 and the original exception was not captured.

Review found the relevant custom Light-theme hit-testing code unchanged between `sp-1.8.2`, `sp-1.8.3` and the pre-fix current source. A Windows regression fixture with 20 document tabs reproduced `System.ArgumentException: Parameter is not valid` from `GraphicsPath.AddPath`, through `GetTabOutline_Document` and `HitTest`. Dark Modern reproduced the same failure. This is consistent with the report, but the original popup cannot be identified conclusively without its stack trace.

Overflow or clipped tabs can legitimately have an empty outline. GDI+ rejects adding that empty path to another path. Hit testing traverses hidden tabs when no earlier visible shape matches, including gaps and rounded tab corners, so ordinary pointer movement can repeatedly throw. Close handling also depended on the previously cached hover rectangle, making it unreliable when hover processing had not completed or layout had changed.

The fix skips empty path additions and visibility tests, returns empty outlines for invalid or invisible tabs, and refreshes layout geometry before hit testing. Hover and click handling use event coordinates and current tab geometry rather than previously painted/hovered rectangles. Close-button targeting no longer requires a successful prior hover; mouse-up clears drag suppression. Disposed strips are excluded from pointer hit testing.

`SuperPuttyUnitTests/TabStripInteractionTests.cs` covers both Light and Dark Modern with 20 tabs: hit testing and actual mouse-move handlers across the strip, overflow outlines and stale indices, and closing all tabs without prior hover while resizing between closes. All four cases failed before the fix; all four pass afterward. The complete non-network suite passed **307/307** tests. Evidence is retained locally under `artifacts/tab-hover/` (`before`, `after`, and `full`).

The fix is not in the published 1.8.3 installers. For another popup, the existing exception handler also records its stack trace in `%TEMP%\SuperPuTTY.log` on the machine running the application; rotated copies may be named `SuperPuTTY.log.1`, etc.
