using Xunit;

namespace A_Pair.Core.Tests
{
    public class SeatingWorkspaceTests
    {
        [Fact]
        public void SeatingWorkspace_PreventsDuplicateAssignment ()
        {
            var students = new System.Collections.Generic.List<A_Pair.Core.Models.Student>
            {
                new A_Pair.Core.Models.Student { Id = "s1", Name = "A" },
                new A_Pair.Core.Models.Student { Id = "s2", Name = "B" }
            };

            var seats = new System.Collections.Generic.List<A_Pair.Core.Models.Seat>
            {
                new A_Pair.Core.Models.GridSeat { Id = "seat1", Row = 1, Column = 1 },
                new A_Pair.Core.Models.GridSeat { Id = "seat2", Row = 1, Column = 2 }
            };

            var workspace = new A_Pair.Core.Workspace.SeatingWorkspace(students , seats);
            Assert.True(workspace.TryAssignSeat("seat1" , "s1" , out var err1));
            Assert.False(workspace.TryAssignSeat("seat2" , "s1" , out var err2));
            Assert.Equal("Student already assigned to another seat" , err2);
        }
    }
}
