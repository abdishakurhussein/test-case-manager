using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TestCaseManager.Api.Models
{
    // Describes preparation, not whether an execution passed.
public enum TestCaseStatus
    {
        Draft = 0,
        Ready = 1,
        Archived = 2
    }
}