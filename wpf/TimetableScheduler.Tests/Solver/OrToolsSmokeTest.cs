using Google.OrTools.Sat;

namespace TimetableScheduler.Tests.Solver;

public class OrToolsSmokeTest
{
    [Fact]
    public void TrivialFeasibilitySolves()
    {
        var model = new CpModel();
        var x = model.NewBoolVar("x");
        var y = model.NewBoolVar("y");
        model.Add(x + y == 1);

        var solver = new CpSolver();
        var status = solver.Solve(model);

        Assert.True(status is CpSolverStatus.Optimal or CpSolverStatus.Feasible);
        Assert.Equal(1, solver.Value(x) + solver.Value(y));
    }
}
