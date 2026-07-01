# Allow Graduate Manual Daytime Move

## Goal

Allow graduate classes to be moved into daytime periods during manual timetable
editing. This should affect manual editing only, not automatic solver generation
or input fixed-time validation.

## Steps

1. Locate the manual move time-band validation.
   - Verify: identify the exact check that blocks graduate classes outside night
     periods.
2. Relax that validation for manual edit moves.
   - Verify: graduate assignments can move to daytime periods while other manual
     conflict checks still run.
3. Add or update manual edit tests.
   - Verify: graduate daytime move succeeds, and undergraduate/night behavior is
     unchanged unless already allowed by the same manual edit policy.
4. Run focused build/tests, then full tests if feasible.
   - Verify: changed project builds and test blockers are documented if existing
     compile errors prevent full execution.
5. Archive this plan after implementation and verification.
   - Verify: this file is moved to `docs/implementation/archive/`.
