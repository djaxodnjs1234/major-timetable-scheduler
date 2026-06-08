# WPF Course Room Operations UI

1. Add course multi-room editing.
   - Verify: course operation info shows professor, team teaching, unavailable rooms, multi rooms, and fixed time in that order.

2. Define room picker relationship.
   - Verify: selecting rooms as unavailable removes them from multi rooms, and selecting multi rooms removes them from unavailable rooms.

3. Add unavailable-room select-all.
   - Verify: the course unavailable-room picker can select every room and clears incompatible multi-room selections.

4. Remove professor unavailable-room UI.
   - Verify: professor management no longer displays or edits unavailable rooms.

5. Add tests and validate.
   - Verify: focused ViewModel/WPF tests and WPF solution build pass, then archive this plan.
