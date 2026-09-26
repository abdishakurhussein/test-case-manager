using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TestCaseManager.Api.Models
{
    public class TestCase
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Preconditions { get; set; } = string.Empty;
        public TestPriority Priority { get; set; } = TestPriority.Major;
        public TestCaseStatus Status { get; set; } = TestCaseStatus.Draft;
        public int ModuleId { get; set; }
        public Module Module { get; set; } = null!;
        public List<TestStep> Steps { get; set; } = new();
        // Timestamps in UTC so that they are not affected by timezones
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}