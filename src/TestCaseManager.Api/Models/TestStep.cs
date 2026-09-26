using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TestCaseManager.Api.Models
{
    public class TestStep
    {
        public int Id { get; set; }
        public int TestCaseId { get; set; }
        public TestCase TestCase { get; set; } = null!;

        //Records the intended sequence, 1, 2, 3, and so on.
        public int Position { get; set; }
        public string Action { get; set; } = string.Empty;
        public string ExpectedResult { get; set; } = string.Empty;
    }
}