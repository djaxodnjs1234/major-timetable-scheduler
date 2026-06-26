# Global WPF Exception Handling

## Goal
Prevent unexpected WPF UI exceptions from closing the screen by adding global application-level exception handlers.

## Assumptions
- Only WPF behavior is in scope.
- UI dispatcher exceptions can be handled and the app can continue.
- Fatal process-level exceptions cannot always be recovered, but should still show a clear message when possible.

## Steps
1. Register global exception handlers during app startup.
   - Verify: App startup source contains handlers for UI dispatcher and unobserved task exceptions.
2. Show a user-facing error message without rethrowing recoverable UI/task exceptions.
   - Verify: handler marks dispatcher exceptions handled and task exceptions observed.
3. Run targeted WPF tests and build.
   - Verify: tests pass and WPF compiles.
4. Archive this plan.
   - Verify: this file is moved to `docs/implementation/archive/`.
