import os
path = 'wpf/TimetableScheduler.Wpf/Views/ResultsView.xaml'
with open(path, 'r', encoding='utf-8') as f:
    c = f.read()

old_grid = '<Grid Margin="0,0,0,6">\\n                                        <Grid.RowDefinitions>'
new_grid = '<Viewbox MaxHeight="80" Stretch="Uniform" Margin="0,0,0,6">\\n                                    <Grid>\\n                                        <Grid.RowDefinitions>'

old_grid_end = '                                    </Grid>\\n                                    <DockPanel LastChildFill="False">'
new_grid_end = '                                    </Grid>\\n                                    </Viewbox>\\n                                    <DockPanel LastChildFill="False">'

c = c.replace(old_grid, new_grid)
c = c.replace(old_grid_end, new_grid_end)

with open(path, 'w', encoding='utf-8') as f:
    f.write(c)
