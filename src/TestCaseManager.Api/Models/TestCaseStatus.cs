using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TestCaseManager.Api.Models
{
    // Describes the test case workflow, not whether an execution passed.
public enum TestCaseStatus
    {
        Draft = 0,
        Ready = 1,
        Archived = 2,
        // Keep the existing numeric values stable for databases already in use.
        Complete = 3
    }
}
