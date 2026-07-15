# Fixed time original position in reason

## Goal

Show the original fixed position directly in the reason line for fixed-time
violations in both full validation and the right-side manual conflict panel.

## Assumptions

- Only `FixedTimeViolation` needs this extra reason text.
- The requested display format is short Korean day/period text, such as
  `목 2시`, with ranges shown as `목 2~3시`.
- Existing detail and conflict detection behavior should not change.

## Plan

1. Add a shared ViewModel-side reason formatter for manual conflict panel lines.
   - Verify: right-side panel reason becomes `고정시간 이탈 / 원래 고정위치: ...`.
2. Add a WPF dialog-side reason formatter for full-validation expanded details.
   - Verify: full-validation reason line uses the same short fixed-position
     format.
3. Add focused tests for fixed-time reason text in both paths.
   - Verify: tests assert `목 2시` style text and no old `교시` wording in the
     reason.
4. Run focused tests and build, then archive this plan.
   - Verify: targeted tests and solution build pass.
