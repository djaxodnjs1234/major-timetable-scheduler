# Manual Edit Block Add Advanced Settings

## Goal

Add collapsed advanced settings to manual edit block creation so users can configure block structure and section count.

## Assumptions

- Advanced settings are optional and collapsed by default.
- Block structure is entered as comma/space separated block lengths, e.g. `2,1`.
- When block structure is empty, the simple class-hour value creates one block.
- Section count creates separate section courses and staged blocks for each section.
- Adding with block structure and section count creates one staged block per section/block part.

## Steps

1. Inspect the current simple manual block add command and staged block model.
   - Verify: identify where to parse advanced settings and add multiple staged blocks.
2. Add ViewModel properties and parsing for advanced block structure and section count.
   - Verify: invalid values produce a user-facing manual block status message.
3. Update simple manual block add to create section/block combinations.
   - Verify: generated staged blocks have expected section labels and row spans.
4. Add a collapsed advanced settings expander to the WPF block add form.
   - Verify: WPF builds.
5. Add focused tests and run targeted tests.
   - Verify: manual edit focused tests pass.

