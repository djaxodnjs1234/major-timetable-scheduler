const fs = require('fs'); let c = fs.readFileSync('wpf/TimetableScheduler.Wpf/Views/DataInputView.xaml', 'utf8'); console.log(c.includes('교수 ID'));
