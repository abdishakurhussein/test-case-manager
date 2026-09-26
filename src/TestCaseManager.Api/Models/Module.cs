using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TestCaseManager.Api.Models
{
    public class Module
    {
        // Module Id
        public int Id { get; set; }
        // Module Name
        public string Name { get; set; } = string.Empty;
        // Project Id referencing project this module belongs to
        public int ProjectId { get; set; }
        public Project Project { get; set; } = null!;
        public List<TestCase> TestCases { get; set; } = new();
        // Module Description
        public string Description { get; set; } = string.Empty;

    }
}