1. Inspect the WPF course/session pipeline to find where section metadata is expanded into multiple course rows.
   - Verify: identify the exact workspace/service path that converts a single imported course with `Section > 1` into multiple downstream course entries.
2. Add focused regression tests before changing logic.
   - Verify: tests cover no implicit fan-out from section-count metadata, explicit section rows remaining distinct, and block-structure-driven visual/session splitting.
3. Remove unintended section-count fan-out at the workspace boundary.
   - Verify: imported/snapshotted/session course lists keep one course row unless explicit separate rows already exist.
4. Preserve block-structure-driven splitting behavior.
   - Verify: a `3` block stays one 3-hour session, while `2+1` stays two sessions/blocks in rendering logic.
5. Run build and tests, then archive this plan.
   - Verify: `dotnet build wpf/TimetableScheduler.slnx` and `dotnet test wpf/TimetableScheduler.slnx` succeed, then move this file to `docs/implementation/archive/`.
