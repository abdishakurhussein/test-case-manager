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
}