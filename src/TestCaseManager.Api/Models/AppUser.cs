using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace TestCaseManager.Api.Models;

public class AppUser : IdentityUser
{
    public bool MustChangePassword { get; set; } = true;
}