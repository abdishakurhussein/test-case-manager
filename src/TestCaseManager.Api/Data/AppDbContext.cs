using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TestCaseManager.Api.Models;


namespace TestCaseManager.Api.Data
{
    public class AppDbContext : DbContext
    {
        // Receives the database settings configured in Program.cs
        // Tests can supply a controllable clock; the application uses real time.
        private readonly TimeProvider _clock;
        public AppDbContext(
            DbContextOptions<AppDbContext> options,
            TimeProvider? clock = null)
            : base(options)
        {
            _clock = clock ?? TimeProvider.System;
        }

        public DbSet<Project> Projects => Set<Project>();
        public DbSet<Module> Modules => Set<Module>();
        public DbSet<TestCase> TestCases => Set<TestCase>();
        public DbSet<TestStep> TestSteps => Set<TestStep>();
        public DbSet<ManualRun> ManualRuns => Set<ManualRun>();
        public DbSet<ManualStepResult> ManualStepResults => Set<ManualStepResult>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            //Each Module belongs to one project
            modelBuilder.Entity<Module>()
                .HasOne(module => module.Project)
                .WithMany(project => project.Modules)
                .HasForeignKey(module => module.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            //Each test case belongs to one module
            modelBuilder.Entity<TestCase>()
                .HasOne(testCase => testCase.Module)
                .WithMany(module => module.TestCases)
                .HasForeignKey(testCase => testCase.ModuleId)
                .OnDelete(DeleteBehavior.Restrict);

            // Steps belong to their case and are removed if it is deleted
            modelBuilder.Entity<TestStep>()
                .HasOne(testStep => testStep.TestCase)
                .WithMany(testCase => testCase.Steps)
                .HasForeignKey(testStep => testStep.TestCaseId)
                .OnDelete(DeleteBehavior.Cascade);

            // A case cannot contain two steps within the same position
            modelBuilder.Entity<TestStep>()
                .HasIndex(testStep => new { testStep.TestCaseId, testStep.Position })
                .IsUnique();

            // Step numbering must start at 1 or higher
            modelBuilder.Entity<TestStep>()
            .ToTable("TestSteps", table => table.HasCheckConstraint("CK_TestSteps_Position", "\"Position\" >= 1"));

            modelBuilder.Entity<ManualRun>()
                .HasOne(run => run.TestCase)
                .WithMany()
                .HasForeignKey(run => run.TestCaseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ManualStepResult>()
                .HasOne(result => result.ManualRun)
                .WithMany(run => run.Steps)
                .HasForeignKey(result => result.ManualRunId)
                .OnDelete(DeleteBehavior.Cascade);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            UpdateTimestamps();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            UpdateTimestamps();

            return base.SaveChangesAsync(
                acceptAllChangesOnSuccess,
                cancellationToken);
        }

        private void UpdateTimestamps()
        {
            // Identify changes made since these objects were loaded.
            ChangeTracker.DetectChanges();

            var caseEntries = ChangeTracker.Entries<TestCase>().ToList();

            var changedSteps = ChangeTracker.Entries<TestStep>()
                .Where(entry =>
                    entry.State == EntityState.Added ||
                    entry.State == EntityState.Modified ||
                    entry.State == EntityState.Deleted)
                .ToList();

            var casesWithStepChanges = new HashSet<TestCase>();

            foreach (var stepEntry in changedSteps)
            {
                // Moving existing steps between cases is not supported yet.
                if (stepEntry.State == EntityState.Modified &&
                    stepEntry.Property(step => step.TestCaseId).IsModified)
                {
                    throw new InvalidOperationException(
                        "Moving a step between test cases is not supported.");
                }

                var parentEntry = caseEntries.SingleOrDefault(entry =>
                    ReferenceEquals(entry.Entity, stepEntry.Entity.TestCase) ||
                    (entry.Entity.Id > 0 &&
                     entry.Entity.Id == stepEntry.Entity.TestCaseId));

                // Fail clearly instead of silently leaving the parent timestamp stale.
                if (parentEntry is null)
                {
                    throw new InvalidOperationException(
                        "Load the test case before changing its steps.");
                }

                casesWithStepChanges.Add(parentEntry.Entity);
            }

            var now = _clock.GetUtcNow().UtcDateTime;

            foreach (var entry in caseEntries)
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                }
                else if (entry.State == EntityState.Modified ||
                         (entry.State == EntityState.Unchanged &&
                          casesWithStepChanges.Contains(entry.Entity)))
                {
                    // Preserve the original creation time for existing records.
                    var createdAt = entry.Property(testCase => testCase.CreatedAt);
                    createdAt.CurrentValue = createdAt.OriginalValue;
                    createdAt.IsModified = false;

                    entry.Entity.UpdatedAt = now;
                    entry.Property(testCase => testCase.UpdatedAt).IsModified = true;
                }
            }
        }
    }
}
