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
        ConflictType.RoomConflict => "강의실 시간 중복",
        ConflictType.ProfessorConflict => "교수 시간 중복",
        ConflictType.ProfUnavailable => "교수 불가시간",
        ConflictType.LunchConflict => "점심시간 배치",
        ConflictType.SectionConflict => "분반 시간 중복",
        ConflictType.GradeConflict => "학년 시간 중복",
        ConflictType.FixedRoomViolation => "고정 강의실 위반",
        ConflictType.CourseUnavailableRoomViolation => "불가 강의실 위반",
        ConflictType.FixedTimeViolation => "고정 시간 위반",
        ConflictType.BlockStartViolation => "블록 시작 교시",
        ConflictType.SameCourseSameDayConflict => "같은 요일 중복 배치",
        ConflictType.ProfUnavailableRoomViolation => "교수 불가 강의실",
        ConflictType.ProfRoomInconsistent => "교수 강의실 일관성",
        _ => "제약조건 위반",
    };
}
