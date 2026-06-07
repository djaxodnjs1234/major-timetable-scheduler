1. Strengthen room-change command refresh -> verify: room selection property changes raise ApplyRoomChangeCommand CanExecuteChanged.
2. Make ComboBox source updates explicit -> verify: single and multi room ComboBoxes update ViewModel properties immediately.
3. Add focused command tests -> verify: single and multi commands become executable and still change working rooms.
4. Run build and tests -> verify: WPF project builds and test project passes.
