using TimetableScheduler.Data;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Tests.Data;

public class LunchPolicyPersistenceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"lunch_policy_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public void MissingSetting_LoadsLegacyPeriod5Default()
    {
        var repository = new SqliteRepository(_dbPath);
        repository.EnsureCreated();

        var loaded = repository.LoadAll();

        Assert.Equal(LunchPolicyMode.BanPeriod5, loaded.SchedulePolicy.LunchMode);
    }

    [Fact]
    public void SaveAll_RoundTripsSchedulePolicy()
    {
        var repository = new SqliteRepository(_dbPath);
        repository.EnsureCreated();
        var input = AppData.Empty() with
        {
            SchedulePolicy = new SchedulePolicy
            {
                LunchMode = LunchPolicyMode.BanOneOfPeriods4And5,
            },
        };

        repository.SaveAll(input);
        var loaded = repository.LoadAll();

        Assert.Equal(
            LunchPolicyMode.BanOneOfPeriods4And5,
            loaded.SchedulePolicy.LunchMode);
    }

    [Fact]
    public void SavedTimetable_RoundTripsPerDayLunchChoices()
    {
        var repository = new SqliteRepository(_dbPath);
        repository.EnsureCreated();
        var lunchPeriods = new Dictionary<int, int>
        {
            [0] = 4,
            [1] = 5,
            [2] = 4,
            [3] = 5,
            [4] = 4,
        };
        repository.UpsertSavedTimetable(new SavedTimetableRecord(
            "T1",
            "mixed lunch",
            DateTime.UtcNow,
            Array.Empty<TimetableAssignmentRow>(),
            LunchPeriodsByDay: lunchPeriods));

        var loaded = Assert.Single(repository.LoadSavedTimetables());

        Assert.Equal(lunchPeriods, loaded.LunchPeriodsByDay);
    }
}
