using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TestCaseManager.Api.Data;
using TestCaseManager.Api.Models;
using Xunit;

namespace TestCaseManager.Api.Tests;

public class DatabasePersistenceTests
{
    [Fact]
    public async Task SavedTestCase_CanBeLoadedAfterReopeningDatabase()
    {
        // Use an isolated file, never the application's real database.
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"testcasemanager-test-{Guid.NewGuid():N}.db");

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
            .Options;

        try
        {
            int savedCaseId;

            // First context: create the schema and save related records.
            await using (var writeContext = new AppDbContext(options))
            {
                await writeContext.Database.MigrateAsync();

                var testCase = new TestCase
                {
                    Title = "Login with valid credentials",
                    Preconditions = "An active account exists",
                    Module = new Module
                    {
                        Name = "Authentication",
                        Project = new Project
                        {
                            Name = "Customer Portal"
                        }
                    },
                    Steps = new List<TestStep>
                    {
                        new()
                        {
                            Position = 1,
                            Action = "Open the login page",
                            ExpectedResult = "The login form appears"
                        },
                        new()
                        {
                            Position = 2,
                            Action = "Submit valid credentials",
                            ExpectedResult = "The account page appears"
                        }
                    }
                };

                // EF saves the new case and its related objects together.
                writeContext.TestCases.Add(testCase);
                await writeContext.SaveChangesAsync();

                savedCaseId = testCase.Id;
                Assert.True(savedCaseId > 0);
            }

            // The first context and connection are now closed.
            await using (var readContext = new AppDbContext(options))
            {
                var savedCase = await readContext.TestCases
                    .AsNoTracking()
                    .Include(testCase => testCase.Module)
                        .ThenInclude(module => module.Project)
                    .Include(testCase => testCase.Steps)
                    .SingleAsync(testCase => testCase.Id == savedCaseId);

                Assert.Equal(
                    "Login with valid credentials",
                    savedCase.Title);

                Assert.Equal("Authentication", savedCase.Module.Name);
                Assert.Equal("Customer Portal", savedCase.Module.Project.Name);
                Assert.Equal(TestCaseStatus.Draft, savedCase.Status);

                // Database rows have no guaranteed order without sorting.
                var steps = savedCase.Steps
                    .OrderBy(step => step.Position)
                    .ToList();

                Assert.Equal(2, steps.Count);
                Assert.Equal(1, steps[0].Position);
                Assert.Equal("Open the login page", steps[0].Action);
                Assert.Equal(2, steps[1].Position);
                Assert.Equal(
                    "The account page appears",
                    steps[1].ExpectedResult);
            }
        }
        finally
        {
            // Remove only this test's temporary database and sidecar files.
            File.Delete(databasePath);
            File.Delete(databasePath + "-wal");
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-journal");
        }
    }

    [Fact]
public async Task Steps_InSameCase_CannotHaveDuplicatePositions()
{
    await using var connection = new SqliteConnection(
        "Data Source=:memory:;Foreign Keys=True");
    await connection.OpenAsync();

    var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connection)
        .Options;

    await using var db = new AppDbContext(options);
    await db.Database.MigrateAsync();

    var testCase = new TestCase
    {
        Title = "Login test",
        Module = new Module
        {
            Name = "Authentication",
            Project = new Project { Name = "Customer Portal" }
        },
        Steps = new List<TestStep>
        {
            new()
            {
                Position = 1,
                Action = "Open login page",
                ExpectedResult = "Login form appears"
            }
        }
    };

    db.TestCases.Add(testCase);
    await db.SaveChangesAsync();

    db.TestSteps.Add(new TestStep
    {
        TestCaseId = testCase.Id,
        Position = 1,
        Action = "Another action at position one",
        ExpectedResult = "This should be rejected"
    });

    await Assert.ThrowsAsync<DbUpdateException>(
        () => db.SaveChangesAsync());
}

[Fact]
public async Task SavedRun_KeepsOriginalStepSnapshot_WhenCaseStepChanges()
{
    await using var connection = new SqliteConnection(
        "Data Source=:memory:;Foreign Keys=True");
    await connection.OpenAsync();

    var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connection)
        .Options;

    int caseId;

    await using (var writeDb = new AppDbContext(options))
    {
        await writeDb.Database.MigrateAsync();

        var testCase = new TestCase
        {
            Title = "Login test",
            Status = TestCaseStatus.Ready,
            Module = new Module
            {
                Name = "Authentication",
                Project = new Project { Name = "Customer Portal" }
            },
            Steps = new List<TestStep>
            {
                new()
                {
                    Position = 1,
                    Action = "Open the login page",
                    ExpectedResult = "Login form appears"
                }
            }
        };

        writeDb.TestCases.Add(testCase);
        await writeDb.SaveChangesAsync();

        caseId = testCase.Id;
        var step = testCase.Steps.Single();

        writeDb.ManualRuns.Add(new ManualRun
        {
            TestCaseId = caseId,
            CompletedAt = new DateTime(
                2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Result = "Passed",
            Steps = new List<ManualStepResult>
            {
                new()
                {
                    OriginalStepId = step.Id,
                    Position = step.Position,
                    Action = step.Action,
                    ExpectedResult = step.ExpectedResult,
                    Outcome = "Passed"
                }
            }
        });

        await writeDb.SaveChangesAsync();

        step.Action = "Open the updated login page";
        await writeDb.SaveChangesAsync();
    }

    await using var readDb = new AppDbContext(options);

    var savedRun = await readDb.ManualRuns
        .AsNoTracking()
        .Include(run => run.Steps)
        .SingleAsync(run => run.TestCaseId == caseId);

    var currentStep = await readDb.TestSteps
        .AsNoTracking()
        .SingleAsync(step => step.TestCaseId == caseId);

    Assert.Equal("Open the login page", savedRun.Steps.Single().Action);
    Assert.Equal("Open the updated login page", currentStep.Action);
}

}