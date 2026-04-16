using System;
using System.Collections.Generic;

namespace A_Pair.Core.Models
{
    public class SeatingSnapshot
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Description { get; set; } = string.Empty;
        public string LayoutId { get; set; } = string.Empty;
        public Dictionary<string, string> SeatAssignments { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}
