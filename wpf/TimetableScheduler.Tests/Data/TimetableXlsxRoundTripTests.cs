using ClosedXML.Excel;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Tests.Data;

public class TimetableXlsxRoundTripTests
{
    [Fact]
    public void FormattedExport_Then_Import_PreservesRows()
    {
        var rows = new List<TimetableAssignmentRow>
        {
            new("GA1004-01", 0, 1, "D327"),
            new("GA1004-01", 0, 2, "D327"),
            new("GA1005-01", 1, 3, "D438"),
            new("GA1006-01", 2, 10, "D500"),
        };
        var courses = new List<Course>
        {
            new() { Id = "GA1004-01", Name = "자료구조", Grade = 2, Section = 1, HoursPerWeek = 4 },
            new() { Id = "GA1005-01", Name = "컴퓨터구조", Grade = 2, Section = 1, HoursPerWeek = 3 },
            new() { Id = "GA1006-01", Name = "Graduate", Grade = AcademicLevels.GraduateGrade, Section = 1, HoursPerWeek = 1 },
        };
        var profs = new List<Professor>();

        var path = Path.Combine(Path.GetTempPath(), $"roundtrip_{Guid.NewGuid():N}.xlsx");
        try
        {
            FormattedTimetableExporter.Export("테스트", rows, courses, profs, path);
            var imported = TimetableXlsxService.Import(path);

            Assert.Equal(rows.Count, imported.Count);
            foreach (var orig in rows)
                Assert.Contains(imported, r =>
                    r.CourseId == orig.CourseId && r.Day == orig.Day &&
                    r.Period == orig.Period && r.RoomId == orig.RoomId);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExcelExport_RoomId_IsRenderedAsRoomName()
    {
        var text = ExportAndReadVisibleText(
            new List<TimetableAssignmentRow> { new("C1", 0, 1, "3") },
            new List<Course> { new() { Id = "C1", Name = "자료구조", Grade = 2, Section = 1 } },
            rooms: new List<Room> { new() { Id = "3", Name = "컴퓨터실습실" } });

        Assert.Contains("컴퓨터실습실", text);
        Assert.Contains("자료구조\n컴퓨터실습실", text);
        Assert.DoesNotContain("자료구조\n3", text);
    }

    [Fact]
    public void ExcelExport_RendersSchoolFixedCoursesFromSnapshot()
    {
        var text = ExportAndReadVisibleText(
            new List<TimetableAssignmentRow> { new("C1", 0, 1, "R1") },
            new List<Course>
            {
                new() { Id = "C1", Name = "Normal", Grade = 2, Section = 1 },
                new()
                {
                    Id = "SF-01",
                    Name = "School Fixed",
                    Grade = 1,
                    HoursPerWeek = 1,
                    ProfessorId = "P1",
                    FixedRooms = new List<string> { "R2" },
                    IsFixed = true,
                    FixedSlots = new List<TimeSlot> { new(0, 2) },
                    IsSchoolFixed = true,
                    SchoolFixedTargetGrade = SchoolFixedTimePolicy.AllGrades,
                },
                new()
                {
                    Id = "SF-G3",
                    Name = "Grade Fixed",
                    Grade = 3,
                    HoursPerWeek = 1,
                    ProfessorId = "P2",
                    FixedRooms = new List<string> { "R3" },
                    IsFixed = true,
                    FixedSlots = new List<TimeSlot> { new(0, 3) },
                    IsSchoolFixed = true,
                    SchoolFixedTargetGrade = 3,
                },
            },
            rooms: new List<Room>
            {
                new() { Id = "R1", Name = "Room 1" },
                new() { Id = "R2", Name = "School Room" },
                new() { Id = "R3", Name = "Grade Room" },
            });

        Assert.Contains("[학교고정] School Fixed", text);
        Assert.Contains("[학년고정] Grade Fixed", text);
        Assert.DoesNotContain("School Room", text);
        Assert.DoesNotContain("Grade Room", text);
    }

    [Fact]
    public void ExcelExport_MultipleRoomIds_AreRenderedAsRoomNames()
    {
        var text = ExportAndReadVisibleText(
            new List<TimetableAssignmentRow>
            {
                new("C1", 0, 1, "2"),
                new("C1", 0, 1, "3"),
            },
            new List<Course> { new() { Id = "C1", Name = "캡스톤디자인", Grade = 4, Section = 1 } },
            rooms: new List<Room>
            {
                new() { Id = "2", Name = "공학관 201호" },
                new() { Id = "3", Name = "컴퓨터실습실" },
            });

        Assert.Contains("공학관 201호", text);
        Assert.Contains("컴퓨터실습실", text);
        Assert.Contains("캡스톤디자인\n공학관 201호 / 컴퓨터실습실", text);
        Assert.DoesNotContain("캡스톤디자인\n2 / 3", text);
    }

    [Fact]
    public void ExcelExport_SectionA_IsRenderedAsASection()
    {
        Assert.Equal("A분반", FormattedTimetableExporter.FormatSectionForExport("A"));
    }

    [Fact]
    public void ExcelExport_SectionWithDot_IsRenderedWithoutDotAndWithSuffix()
    {
        Assert.Equal("A분반", FormattedTimetableExporter.FormatSectionForExport("A."));
    }

    [Fact]
    public void ExcelExport_SectionAlreadyHasSuffix_IsNotDuplicated()
    {
        Assert.Equal("A분반", FormattedTimetableExporter.FormatSectionForExport("A분반"));
        Assert.NotEqual("A분반분반", FormattedTimetableExporter.FormatSectionForExport("A분반"));
    }

    [Fact]
    public void ExcelExport_EmptySection_RemainsEmpty()
    {
        Assert.Equal(string.Empty, FormattedTimetableExporter.FormatSectionForExport(null));
        Assert.Equal(string.Empty, FormattedTimetableExporter.FormatSectionForExport(""));
        Assert.Equal(string.Empty, FormattedTimetableExporter.FormatSectionForExport(" "));
    }

    [Fact]
    public void ExcelExport_VisibleSectionUsesExportSectionSuffix()
    {
        var text = ExportAndReadVisibleText(
            new List<TimetableAssignmentRow> { new("C-A", 0, 1, "R1") },
            new List<Course>
            {
                new() { Id = "C-A", Name = "웹서버프로그래밍", Grade = 3, Section = 1, ProfessorId = "P1" },
                new() { Id = "C-B", Name = "웹서버프로그래밍", Grade = 3, Section = 2 },
            },
            professors: new List<Professor> { new() { Id = "P1", Name = "김교수" } },
            rooms: new List<Room> { new() { Id = "R1", Name = "A관 302호" } });

        Assert.Contains("A분반", text);
        Assert.Contains("웹서버프로그래밍\nA분반 · 김교수\nA관 302호", text);
        Assert.DoesNotContain("\nA\n", text);
    }

    [Fact]
    public void ExcelExport_CoteachProfessor_DoesNotDuplicatePrimaryProfessor()
    {
        var text = ExportAndReadVisibleText(
            new List<TimetableAssignmentRow> { new("C-A", 0, 1, "R1") },
            new List<Course>
            {
                new()
                {
                    Id = "C-A",
                    Name = "운영체제",
                    Grade = 3,
                    Section = 1,
                    ProfessorId = "P1",
                    CoteachProfs = new List<string> { "P1", "P2" },
                },
                new() { Id = "C-B", Name = "운영체제", Grade = 3, Section = 2 },
            },
            professors: new List<Professor>
            {
                new() { Id = "P1", Name = "김교수" },
                new() { Id = "P2", Name = "이교수" },
            },
            rooms: new List<Room> { new() { Id = "R1", Name = "A관 302호" } });

        Assert.Contains("A분반 · 김교수, 이교수", text);
        Assert.DoesNotContain("김교수, 김교수", text);
    }

    [Fact]
    public void ExcelExport_HeaderColors_AreLowSaturation()
    {
        ExportAndInspectWorksheet(
            new List<TimetableAssignmentRow> { new("C1", 0, 1, "R1") },
            new List<Course> { new() { Id = "C1", Name = "자료구조", Grade = 2, Section = 1 } },
            ws =>
            {
                Assert.Equal("#6F879F", ColorHex(ws.Cell(1, 1).Style.Fill.BackgroundColor));
                Assert.Equal("#E6EEF6", ColorHex(ws.Cell(4, 2).Style.Fill.BackgroundColor));
                Assert.Equal("#F8FAFC", ColorHex(ws.Cell(5, 2).Style.Fill.BackgroundColor));
            });
    }

    [Fact]
    public void ExcelExport_DayGroupBorders_AreVisible()
    {
        ExportAndInspectWorksheet(
            new List<TimetableAssignmentRow>
            {
                new("MON", 0, 1, "R1"),
                new("TUE", 1, 1, "R1"),
            },
            new List<Course>
            {
                new() { Id = "MON", Name = "월요일수업", Grade = 1, Section = 1 },
                new() { Id = "TUE", Name = "화요일수업", Grade = 1, Section = 1 },
            },
            ws =>
            {
                var mondayBoundary = ws.Cell(6, 2).Style.Border;
                var mondayNightBoundary = ws.Cell(SchedulePeriods.LastPeriod + 5, 2).Style.Border;
                var tuesdayInnerGrid = ws.Cell(6, 3).Style.Border;

                Assert.Equal(XLBorderStyleValues.Medium, mondayBoundary.RightBorder);
                Assert.Equal("#8FA3B8", ColorHex(mondayBoundary.RightBorderColor));
                Assert.Equal(XLBorderStyleValues.Medium, mondayNightBoundary.RightBorder);
                Assert.Equal("#8FA3B8", ColorHex(mondayNightBoundary.RightBorderColor));
                Assert.Equal(XLBorderStyleValues.Thin, tuesdayInnerGrid.LeftBorder);
                Assert.NotEqual(ColorHex(tuesdayInnerGrid.LeftBorderColor), ColorHex(mondayBoundary.RightBorderColor));
            });
    }

    [Fact]
    public void ExcelExport_NightSeparator_IsVisibleBeforePeriod10()
    {
        ExportAndInspectWorksheet(
            new List<TimetableAssignmentRow> { new("NIGHT", 0, SchedulePeriods.FirstNightPeriod, "R1") },
            new List<Course> { new() { Id = "NIGHT", Name = "Night", Grade = AcademicLevels.GraduateGrade } },
            ws =>
            {
                int nightRow = SchedulePeriods.FirstNightPeriod + 5;
                var nightSeparator = ws.Cell(nightRow, 2).Style.Border;

                Assert.Equal(XLBorderStyleValues.Medium, nightSeparator.TopBorder);
                Assert.Equal("#8FA3B8", ColorHex(nightSeparator.TopBorderColor));
            });
    }

    [Fact]
    public void ExcelExport_RemovesExamplePrefixFromSheetNamesAndTitles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"example_prefix_{Guid.NewGuid():N}.xlsx");
        try
        {
            FormattedTimetableExporter.Export(
                "예시-김교수",
                new List<TimetableAssignmentRow> { new("C1", 0, 1, "R1") },
                new List<Course>
                {
                    new() { Id = "C1", Name = "자료구조", Grade = 1, Section = 1, ProfessorId = "P1" },
                },
                new List<Professor> { new() { Id = "P1", Name = "예시-김교수" } },
                path,
                new List<Room> { new() { Id = "R1", Name = "예시-101호" } });

            using var wb = new XLWorkbook(path);
            var sheetNames = wb.Worksheets.Select(ws => ws.Name).ToList();
            var visibleTitles = wb.Worksheets
                .Where(ws => ws.Visibility == XLWorksheetVisibility.Visible)
                .Select(ws => ws.Cell(1, 1).GetString())
                .ToList();

            Assert.DoesNotContain(sheetNames, name => name.Contains("예시-", StringComparison.Ordinal));
            Assert.DoesNotContain(visibleTitles, title => title.Contains("예시-", StringComparison.Ordinal));
            Assert.Contains("교수별_김교수", sheetNames);
            Assert.Contains("강의실별_101호", sheetNames);
            Assert.Contains("김교수", visibleTitles);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExcelExport_PeriodLabelsIncludeTimeRanges()
    {
        ExportAndInspectWorksheet(
            new List<TimetableAssignmentRow> { new("C1", 0, 1, "R1") },
            new List<Course> { new() { Id = "C1", Name = "Test", Grade = 1 } },
            ws =>
            {
                var firstPeriodLabel = ws.Cell(6, 1).GetString();
                Assert.Contains("1", firstPeriodLabel);
                Assert.Contains("09:00~10:00", firstPeriodLabel);
                Assert.Contains("14:00~15:00", ws.Cell(11, 1).GetString());
            });
    }

    [Fact]
    public void ExcelExport_NightPeriodUsesEveningTimeRangeWithoutOverwritingRemarks()
    {
        ExportAndInspectWorksheet(
            new List<TimetableAssignmentRow> { new("C1", 0, 10, "R1") },
            new List<Course> { new() { Id = "C1", Name = "Night", Grade = AcademicLevels.GraduateGrade } },
            ws =>
            {
                Assert.Contains("10", ws.Cell(15, 1).GetString());
                Assert.Contains("18:00~19:00", ws.Cell(15, 1).GetString());
                Assert.False(string.IsNullOrWhiteSpace(ws.Cell(19, 1).GetString()));
            });
    }

    [Fact]
    public void ExcelExport_CourseBlockBordersAreSlightlyDarker()
    {
        ExportAndInspectWorksheet(
            new List<TimetableAssignmentRow> { new("C1", 0, 1, "R1") },
            new List<Course> { new() { Id = "C1", Name = "자료구조", Grade = 2, Section = 1 } },
            ws =>
            {
                var border = ws.Cell(6, 2).Style.Border;

                Assert.Equal(XLBorderStyleValues.Thin, border.TopBorder);
                Assert.Equal("#AAB7C6", ColorHex(border.TopBorderColor));
            });
    }

    [Fact]
    public void ExcelExport_CreatesMultiSheetWorkbook_WithSafeVisibleSheetNames()
    {
        var rows = new List<TimetableAssignmentRow>
        {
            new("COURSE-ID-1", 0, 1, "ROOM-ID-1"),
            new("COURSE-ID-2", 1, 3, "ROOM-ID-2"),
            new("COURSE-ID-3", 2, 6, "ROOM-ID-3"),
        };
        var courses = new List<Course>
        {
            new()
            {
                Id = "COURSE-ID-1",
                Name = "자료구조",
                Grade = 1,
                Section = 1,
                ProfessorId = "PROF-ID-1",
                CoteachProfs = new List<string> { "PROF-ID-1", "PROF-ID-2" },
            },
            new()
            {
                Id = "COURSE-ID-2",
                Name = "운영체제",
                Grade = 2,
                Section = 1,
                ProfessorId = "PROF-ID-3",
            },
            new()
            {
                Id = "COURSE-ID-3",
                Name = "컴퓨터구조",
                Grade = 3,
                Section = 1,
                ProfessorId = "PROF-ID-4",
            },
        };
        var professors = new List<Professor>
        {
            new() { Id = "PROF-ID-1", Name = "김/교수님:아주긴이름아주긴이름아주긴이름" },
            new() { Id = "PROF-ID-2", Name = "이교수" },
            new() { Id = "PROF-ID-3", Name = "동명이교수" },
            new() { Id = "PROF-ID-4", Name = "동명이교수" },
        };
        var rooms = new List<Room>
        {
            new() { Id = "ROOM-ID-1", Name = "101/강의실:아주긴이름아주긴이름아주긴이름" },
            new() { Id = "ROOM-ID-2", Name = "공용강의실" },
            new() { Id = "ROOM-ID-3", Name = "공용강의실" },
        };

        var path = Path.Combine(Path.GetTempPath(), $"multi_sheet_{Guid.NewGuid():N}.xlsx");
        try
        {
            FormattedTimetableExporter.Export("테스트", rows, courses, professors, path, rooms);

            using var wb = new XLWorkbook(path);
            var sheetNames = wb.Worksheets.Select(ws => ws.Name).ToList();

            Assert.Contains("통합 시간표", sheetNames);
            Assert.Contains(sheetNames, name => name.StartsWith("학년별_1학년", StringComparison.Ordinal));
            Assert.Contains(sheetNames, name => name.StartsWith("교수별_", StringComparison.Ordinal));
            Assert.Contains(sheetNames, name => name.StartsWith("강의실별_", StringComparison.Ordinal));
            Assert.True(wb.Worksheets.TryGetWorksheet("데이터", out var dataSheet));
            Assert.Equal(XLWorksheetVisibility.Hidden, dataSheet.Visibility);

            Assert.All(sheetNames, name => Assert.True(name.Length <= 31, $"{name} is too long."));
            Assert.DoesNotContain(sheetNames, name => name.Any(ch => "\\/?*[]:".Contains(ch)));
            Assert.Contains(sheetNames, name => name.StartsWith("교수별_동명이교수(", StringComparison.Ordinal));
            Assert.Contains(sheetNames, name => name.StartsWith("강의실별_공용강의실(", StringComparison.Ordinal));

            var visibleText = string.Join(
                "\n",
                wb.Worksheets
                    .Where(ws => ws.Visibility == XLWorksheetVisibility.Visible)
                    .SelectMany(ws => ws.CellsUsed().Select(cell => cell.GetString())));

            Assert.DoesNotContain("COURSE-ID", visibleText);
            Assert.DoesNotContain("ROOM-ID", visibleText);
            Assert.DoesNotContain("PROF-ID", visibleText);
            Assert.DoesNotContain("SectionId", visibleText);
            Assert.Contains("김/교수님:아주긴이름아주긴이름아주긴이름, 이교수", visibleText);
            Assert.DoesNotContain("김/교수님:아주긴이름아주긴이름아주긴이름, 김/교수님:아주긴이름아주긴이름아주긴이름", visibleText);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExcelExport_VisibleSheetsUseUnknownLabelsInsteadOfFallbackIds()
    {
        var rows = new List<TimetableAssignmentRow>
        {
            new("COURSE-ID-1", 0, 1, "ROOM-ID-1"),
            new("MISSING-COURSE-ID", 1, 2, "ROOM-ID-1"),
        };
        var courses = new List<Course>
        {
            new()
            {
                Id = "COURSE-ID-1",
                Name = "자료구조",
                Grade = 1,
                Section = 1,
                ProfessorId = "PROF-ID-1",
                CoteachProfs = new List<string> { "PROF-ID-2" },
            },
        };
        var professors = new List<Professor>
        {
            new() { Id = "PROF-ID-1", Name = "" },
            new() { Id = "PROF-ID-2", Name = "" },
        };
        var rooms = new List<Room> { new() { Id = "ROOM-ID-1", Name = "" } };

        var path = Path.Combine(Path.GetTempPath(), $"unknown_labels_{Guid.NewGuid():N}.xlsx");
        try
        {
            FormattedTimetableExporter.Export("테스트", rows, courses, professors, path, rooms);

            using var wb = new XLWorkbook(path);
            var visibleText = string.Join(
                "\n",
                wb.Worksheets
                    .Where(ws => ws.Visibility == XLWorksheetVisibility.Visible)
                    .SelectMany(ws => ws.CellsUsed().Select(cell => cell.GetString())));
            var visibleSheetNames = string.Join("\n", wb.Worksheets
                .Where(ws => ws.Visibility == XLWorksheetVisibility.Visible)
                .Select(ws => ws.Name));
            var dataText = string.Join("\n", wb.Worksheet("데이터").CellsUsed().Select(cell => cell.GetString()));

            Assert.Contains("알 수 없는 교수", visibleText);
            Assert.Contains("알 수 없는 강의실", visibleText);
            Assert.Contains("알 수 없는 수업", visibleText);
            Assert.Contains("교수별_알 수 없는 교수", visibleSheetNames);
            Assert.Contains("강의실별_알 수 없는 강의실", visibleSheetNames);
            Assert.DoesNotContain("PROF-ID", visibleText);
            Assert.DoesNotContain("ROOM-ID", visibleText);
            Assert.DoesNotContain("MISSING-COURSE-ID", visibleText);
            Assert.DoesNotContain("PROF-ID", visibleSheetNames);
            Assert.DoesNotContain("ROOM-ID", visibleSheetNames);

            Assert.Contains("COURSE-ID-1", dataText);
            Assert.Contains("MISSING-COURSE-ID", dataText);
            Assert.Contains("ROOM-ID-1", dataText);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExcelExport_BreakdownSheetsContainOnlyMatchingAssignments()
    {
        var rows = new List<TimetableAssignmentRow>
        {
            new("G1-P1-R1", 0, 1, "R1"),
            new("G2-P2-R2-COTEACH-P1", 0, 3, "R2"),
            new("G1-P3-R1", 1, 6, "R1"),
        };
        var courses = new List<Course>
        {
            new()
            {
                Id = "G1-P1-R1",
                Name = "1학년대표교수수업",
                Grade = 1,
                Section = 1,
                ProfessorId = "P1",
            },
            new()
            {
                Id = "G2-P2-R2-COTEACH-P1",
                Name = "2학년팀티칭수업",
                Grade = 2,
                Section = 1,
                ProfessorId = "P2",
                CoteachProfs = new List<string> { "P1" },
            },
            new()
            {
                Id = "G1-P3-R1",
                Name = "1학년다른교수수업",
                Grade = 1,
                Section = 1,
                ProfessorId = "P3",
            },
        };
        var professors = new List<Professor>
        {
            new() { Id = "P1", Name = "김교수" },
            new() { Id = "P2", Name = "이교수" },
            new() { Id = "P3", Name = "박교수" },
        };
        var rooms = new List<Room>
        {
            new() { Id = "R1", Name = "101호" },
            new() { Id = "R2", Name = "202호" },
        };

        var path = Path.Combine(Path.GetTempPath(), $"breakdown_filter_{Guid.NewGuid():N}.xlsx");
        try
        {
            FormattedTimetableExporter.Export("테스트", rows, courses, professors, path, rooms);

            using var wb = new XLWorkbook(path);
            var unified = SheetText(wb.Worksheet("통합 시간표"));
            var grade1 = SheetText(wb.Worksheet("학년별_1학년"));
            var professor1 = SheetText(wb.Worksheet("교수별_김교수"));
            var room1 = SheetText(wb.Worksheet("강의실별_101호"));

            Assert.Contains("1학년대표교수수업", unified);
            Assert.Contains("2학년팀티칭수업", unified);
            Assert.Contains("1학년다른교수수업", unified);

            Assert.Contains("1학년대표교수수업", grade1);
            Assert.Contains("1학년다른교수수업", grade1);
            Assert.DoesNotContain("2학년팀티칭수업", grade1);

            Assert.Contains("1학년대표교수수업", professor1);
            Assert.Contains("2학년팀티칭수업", professor1);
            Assert.DoesNotContain("1학년다른교수수업", professor1);

            Assert.Contains("1학년대표교수수업", room1);
            Assert.Contains("1학년다른교수수업", room1);
            Assert.DoesNotContain("2학년팀티칭수업", room1);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExcelExport_UnifiedAndGradeSheetsUseGradeColumnGroupsOnly()
    {
        var rows = new List<TimetableAssignmentRow>
        {
            new("G1", 0, 1, "R1"),
            new("G2", 0, 1, "R2"),
        };
        var courses = new List<Course>
        {
            new() { Id = "G1", Name = "1학년수업", Grade = 1, Section = 1, ProfessorId = "P1" },
            new() { Id = "G2", Name = "2학년수업", Grade = 2, Section = 1, ProfessorId = "P2" },
        };
        var professors = new List<Professor>
        {
            new() { Id = "P1", Name = "김교수" },
            new() { Id = "P2", Name = "이교수" },
        };
        var rooms = new List<Room>
        {
            new() { Id = "R1", Name = "101호" },
            new() { Id = "R2", Name = "202호" },
        };

        var path = Path.Combine(Path.GetTempPath(), $"grade_columns_{Guid.NewGuid():N}.xlsx");
        try
        {
            FormattedTimetableExporter.Export("테스트", rows, courses, professors, path, rooms);

            using var wb = new XLWorkbook(path);
            var unifiedHeader = RowText(wb.Worksheet("통합 시간표"), 5);
            var professorHeader = RowText(wb.Worksheet("교수별_김교수"), 5);
            var roomHeader = RowText(wb.Worksheet("강의실별_101호"), 5);
            var gradeHeader = RowText(wb.Worksheet("학년별_1학년"), 5);

            Assert.Contains("1학년", unifiedHeader);
            Assert.Contains("2학년", unifiedHeader);
            Assert.DoesNotContain("1학년", professorHeader);
            Assert.DoesNotContain("1학년", roomHeader);
            Assert.Contains("1학년", gradeHeader);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExcelExport_GraduateCourse_UsesGraduateHeaderAndSheet()
    {
        var rows = new List<TimetableAssignmentRow>
        {
            new("GR", 0, 1, "R1"),
        };
        var courses = new List<Course>
        {
            new()
            {
                Id = "GR",
                Name = "Graduate Seminar",
                Grade = AcademicLevels.GraduateGrade,
                Section = 1,
                ProfessorId = "P1",
            },
        };
        var professors = new List<Professor> { new() { Id = "P1", Name = "Professor" } };
        var rooms = new List<Room> { new() { Id = "R1", Name = "Room" } };

        var path = Path.Combine(Path.GetTempPath(), $"graduate_export_{Guid.NewGuid():N}.xlsx");
        try
        {
            FormattedTimetableExporter.Export("테스트", rows, courses, professors, path, rooms, expandAllGrades: true);

            using var wb = new XLWorkbook(path);
            var unifiedHeader = RowText(wb.Worksheet("통합 시간표"), 5);
            Assert.Contains("대학원", unifiedHeader);
            Assert.Contains(wb.Worksheets.Select(ws => ws.Name), name => name.StartsWith("학년별_대학원", StringComparison.Ordinal));
            var graduateSheet = wb.Worksheets.Single(ws => ws.Name.StartsWith("학년별_대학원", StringComparison.Ordinal));
            Assert.Contains("대학원", RowText(graduateSheet, 5));
            Assert.Contains("Graduate Seminar", SheetText(graduateSheet));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string ExportAndReadVisibleText(
        IReadOnlyList<TimetableAssignmentRow> rows,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor>? professors = null,
        IReadOnlyList<Room>? rooms = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"export_display_{Guid.NewGuid():N}.xlsx");
        try
        {
            FormattedTimetableExporter.Export(
                "테스트",
                rows,
                courses,
                professors ?? new List<Professor>(),
                path,
                rooms);

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet("통합 시간표");
            return string.Join("\n", ws.CellsUsed().Select(c => c.GetString()));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void ExportAndInspectWorksheet(
        IReadOnlyList<TimetableAssignmentRow> rows,
        IReadOnlyList<Course> courses,
        Action<IXLWorksheet> inspect)
    {
        var path = Path.Combine(Path.GetTempPath(), $"export_style_{Guid.NewGuid():N}.xlsx");
        try
        {
            FormattedTimetableExporter.Export(
                "테스트",
                rows,
                courses,
                new List<Professor>(),
                path,
                new List<Room> { new() { Id = "R1", Name = "강의실 1" } });

            using var wb = new XLWorkbook(path);
            inspect(wb.Worksheet("통합 시간표"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string ColorHex(XLColor color) =>
        $"#{color.Color.ToArgb() & 0x00FFFFFF:X6}";

    private static string SheetText(IXLWorksheet worksheet) =>
        string.Join("\n", worksheet.CellsUsed().Select(cell => cell.GetString()));

    private static string RowText(IXLWorksheet worksheet, int row) =>
        string.Join("\n", worksheet.Row(row).CellsUsed().Select(cell => cell.GetString()));
}
