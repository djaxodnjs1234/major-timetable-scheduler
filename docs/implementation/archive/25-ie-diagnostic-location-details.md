# IE Diagnostic Location Details

1. Audit all input-error diagnostics emitted by the WPF generation flow and identify messages that omit the affected course, professor, room, Cross group, grade, or section. Verify the audit against the IE error-case document.
2. Add the missing affected-item labels to those diagnostics while preserving the existing error IDs and validation behavior. Add focused tests for representative course, professor, room, Cross, and grade-capacity messages.
3. Update the IE error-case documentation, run the focused diagnostics and ViewModel tests, and build WPF before archiving this plan.
