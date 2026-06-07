import os
path = 'wpf/TimetableScheduler.Wpf/Views/ResultsView.xaml'
with open(path, 'r', encoding='utf-8') as f:
    c = f.read()

old_grid = '<Grid Margin=' + chr(34) + '0,0,0,6' + chr(34) + '>'
new_grid = '<Viewbox MaxHeight=' + chr(34) + '120' + chr(34) + ' Stretch=' + chr(34) + 'Uniform' + chr(34) + ' Margin=' + chr(34) + '0,0,0,6' + chr(34) + '>\n<Grid>'
c = c.replace(old_grid, new_grid)

old_end = '</Grid>\n                                    <DockPanel LastChildFill=' + chr(34) + 'False' + chr(34) + '>'
new_end = '</Grid>\n                                    </Viewbox>\n                                    <DockPanel LastChildFill=' + chr(34) + 'False' + chr(34) + '>'
c = c.replace(old_end, new_end)

with open(path, 'w', encoding='utf-8') as f:
    f.write(c)
