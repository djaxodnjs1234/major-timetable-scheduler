import os
path = 'wpf/TimetableScheduler.Wpf/Views/DataInputView.xaml'
with open(path, 'r', encoding='utf-8') as f:
    c = f.read()

c = c.replace('<TextBlock Text="세부정보는 수정 버튼을 누른 뒤 편집할 수 있습니다."\\n                                       VerticalAlignment="Center" FontSize="11"\\n                                       Foreground="{StaticResource OnSurfaceVariant}" />', '')
c = c.replace('<TextBlock Text="세부정보는 수정 버튼을 누른 뒤 편집할 수 있습니다."\\n                               Margin="0,0,0,8"\\n                               FontSize="11"\\n                               Foreground="{StaticResource OnSurfaceVariant}" />\\n                    ', '')

with open(path, 'w', encoding='utf-8') as f:
    f.write(c)
