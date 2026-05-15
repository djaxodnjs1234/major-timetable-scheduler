using System.Text;
using System.Windows;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel.Services;

namespace TimetableScheduler.Wpf.Services;

public sealed class MessageBoxConflictDialogService : IConflictDialogService
{
    public bool ConfirmDespiteConflicts(IReadOnlyList<ConflictItem> newConflicts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("이 변경으로 다음 제약 사항이 위반됩니다:");
        sb.AppendLine();
        foreach (var c in newConflicts)
        {
            string severity = c.Severity == ConflictSeverity.Error ? "✖" : "⚠";
            sb.Append(severity).Append(' ').Append('[').Append(KoreanLabel(c.Type)).Append("] ");
            sb.AppendLine(c.Description);
        }
        sb.AppendLine();
        sb.AppendLine("계속하시겠습니까?");
        sb.AppendLine("  [확인] 변경을 그대로 적용 (위반 상태 유지)");
        sb.AppendLine("  [취소] 변경을 되돌림");

        var result = MessageBox.Show(
            sb.ToString(),
            "⚠ 제약 위반 경고",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        return result == MessageBoxResult.OK;
    }

    private static string KoreanLabel(ConflictType type) => type switch
    {
        ConflictType.RoomConflict => "HC-01 강의실 중복",
        ConflictType.ProfessorConflict => "HC-02 교수 중복",
        ConflictType.ProfUnavailable => "HC-03 교수 불가시간",
        ConflictType.LunchConflict => "HC-12 점심시간 금지",
        ConflictType.SectionConflict => "HC-08 분반 중복",
        ConflictType.GradeConflict => "HC-11 학년 중복",
        ConflictType.FixedRoomViolation => "HC-14 고정 강의실 위반",
        ConflictType.FixedTimeViolation => "HC-13 고정 시간표",
        ConflictType.BlockStartViolation => "HC-19 블록 시작 교시",
        ConflictType.ProfAllowedRoomViolation => "HC-21 허용 강의실",
        ConflictType.ProfRoomInconsistent => "HC-21 교수 강의실 일관성",
        _ => type.ToString(),
    };
}
