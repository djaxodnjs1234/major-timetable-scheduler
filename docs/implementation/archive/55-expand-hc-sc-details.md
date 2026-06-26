# Expand HC/SC Documentation Details

## Goal

Improve `docs/hc_sc_설정값.md` so that each hard/soft constraint explains not only the rule, but also when it applies, what input fields affect it, common examples, and what users should change when generation fails.

## Steps

1. Review the active WPF constraint implementation and current documentation.
   - Verify: identify all currently documented HC/SC entries and any removed behavior that should not be reintroduced.
2. Rewrite the document details in clear Korean.
   - Verify: every active HC/SC has purpose, applies-to/exceptions, examples, and troubleshooting notes where relevant.
3. Check the document for stale references and formatting problems.
   - Verify: search for removed professor allowed-room wording and confirm Markdown structure.
4. Archive this plan after verification.
   - Verify: move this file to `docs/implementation/archive/`.
