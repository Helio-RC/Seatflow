using A_Pair.Core.Enums;
using A_Pair.Core.Utilities;

namespace A_Pair.Core.Models
{
    public class Student
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public float? Height { get; set; }
        public Gender? Gender { get; set; }
        public bool NeedsFrontRow { get; set; }

        // Simple circular buffer implementation for recent seat history
        public CircularHistory<string> RecentSeatHistory { get; set; } = new(3);

        public int FrontRowPreferenceScore { get; set; }

        public AttributeBag Extensions { get; set; } = new();
    }
}
