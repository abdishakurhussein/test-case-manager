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

public class TestCaseTimestampTests
{
    [Theory]
    [InlineData("create", false)]
    [InlineData("edit-case", false)]
    [InlineData("add-step", false)]
    [InlineData("edit-step", false)]
    [InlineData("delete-step", false)]
    [InlineData("no-change", false)]
    [InlineData("edit-case", true)]
    public async Task Save_MaintainsExpectedTimestamps(
        string action,
        bool useSynchronousSave)
    {
        // An open connection keeps this isolated SQLite database alive.
        await using var connection = new SqliteConnection(
            "Data Source=:memory:;Foreign Keys=True");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var initialTime = new DateTimeOffset(
            2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

        var clock = new ManualClock(initialTime);

        await using var context = new AppDbContext(options, clock);
        await context.Database.MigrateAsync();

        var testCase = new TestCase
        {
            Title = "Login",
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
                    Action = "Open login",
                    ExpectedResult = "Login form appears"
                }
            }
        };

        context.TestCases.Add(testCase);
        await context.SaveChangesAsync();

        // New records should receive identical creation and update times.
        Assert.Equal(initialTime.UtcDateTime, testCase.CreatedAt);
        Assert.Equal(initialTime.UtcDateTime, testCase.UpdatedAt);

        clock.Advance(TimeSpan.FromMinutes(5));

        switch (action)
        {
            case "edit-case":
                testCase.Title = "Login with valid credentials";

                // Creation time must not be overwritten during an edit.
                testCase.CreatedAt = initialTime.UtcDateTime.AddYears(-1);
                break;

            case "add-step":
                testCase.Steps.Add(new TestStep
                {
                    Position = 2,
                    Action = "Enter credentials",
                    ExpectedResult = "Credentials are accepted"
                });
                break;

            case "edit-step":
                testCase.Steps[0].Action = "Open the customer login page";
                break;

            case "delete-step":
                context.TestSteps.Remove(testCase.Steps[0]);
                break;

            // "create" and "no-change" make no further modifications.
        }

        if (useSynchronousSave)
        {
            context.SaveChanges();
        }
        else
        {
            await context.SaveChangesAsync();
        }

        // Read from the database rather than asserting only on memory.
        await using var readContext = new AppDbContext(options, clock);

        var saved = await readContext.TestCases
            .AsNoTracking()
            .SingleAsync(item => item.Id == testCase.Id);

        var expectedUpdatedAt =
            action is "create" or "no-change"
                ? initialTime.UtcDateTime
                : clock.GetUtcNow().UtcDateTime;

        Assert.Equal(initialTime.UtcDateTime, saved.CreatedAt);
        Assert.Equal(expectedUpdatedAt, saved.UpdatedAt);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualClock(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount)
        {
            _now = _now.Add(amount);
        }
    }
}