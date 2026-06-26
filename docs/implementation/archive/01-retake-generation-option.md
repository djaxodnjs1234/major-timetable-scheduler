# Retake Generation Option

## Assumptions

- The requested "재수강생 고려" option should control the existing HC-17 retake constraint during automatic generation.
- The checkbox belongs in the WPF automatic generation advanced section.
- The default should be unchecked, so existing generation behavior stays unchanged unless the user opts in.
- The tooltip should explain that required-major retake courses are kept from overlapping with current-grade required-major courses, and generation may fail if that cannot be satisfied.

## Steps

1. Add a ViewModel option for retake consideration.
   - Verify: the option defaults to false in a focused ViewModel test.

2. Add the advanced WPF checkbox with the requested label and tooltip.
   - Verify: a WPF view test can find the checkbox and confirm it starts unchecked.

3. Pass the option into solver execution and gate HC-17.
   - Verify: solver tests show retake constraints are ignored when disabled and enforced when enabled.

4. Run project tests/build.
   - Verify: `dotnet test` and `dotnet build` complete successfully.

5. Archive this plan after the task is complete.
   - Verify: move this file to `docs/implementation/archive/`.
