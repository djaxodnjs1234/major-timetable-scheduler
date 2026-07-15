# Full validation fixed time detail

## Goal

Show fixed-time violation details in full validation as the original fixed
position, for example `원래 고정위치: 수 4시`.

## Assumptions

- This request targets the full-validation expanded detail text, not the
  right-side manual conflict panel.
- The previous reason-line behavior remains unchanged.
- Existing fixed-time detection behavior should not change.

## Plan

1. Add a full-validation fixed-time detail formatter.
   - Verify: fixed-time detail conflicts use `원래 고정위치: ...`.
2. Update focused tests for fixed-time full validation detail text.
   - Verify: tests assert the short `시` format and no old detector prose.
3. Run focused tests and build, then archive this plan.
   - Verify: targeted tests and solution build pass.
