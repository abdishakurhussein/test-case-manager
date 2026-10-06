using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TestCaseManager.Api.Data;
using TestCaseManager.Api.Models;
using Xunit;

namespace TestCaseManager.Api.Tests;

public class ExcelAndAttachmentTests
{
    [Fact]
    public void ExcelExport_WritesReadableWorkbookPartsAndLiteralUserText()
    {
        var bytes = ExcelWorkbook.Create(new ExcelWorkbook.Sheet(
            "Execution", ["Case", "Action", "Outcome"],
            new List<string?[]> { new string?[] { "TC-001", "=2+2", "Not run" } },
            [3], 3));
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
        Assert.NotNull(zip.GetEntry("xl/styles.xml"));
        using var contentTypesStream = zip.GetEntry("[Content_Types].xml")!.Open();
        var contentTypes = XDocument.Load(contentTypesStream);
        XNamespace package = "http://schemas.openxmlformats.org/package/2006/content-types";
        Assert.NotEmpty(contentTypes.Root!.Elements(package + "Override"));
        using var relationshipStream = zip.GetEntry("_rels/.rels")!.Open();
        var relationships = XDocument.Load(relationshipStream);
        XNamespace packageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        Assert.NotEmpty(relationships.Root!.Elements(packageRelationships + "Relationship"));
        using var sheetStream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var sheet = XDocument.Load(sheetStream);
        Assert.Contains("=2+2", sheet.ToString());
        Assert.DoesNotContain("<f>", sheet.ToString());
        Assert.Contains("dataValidation", sheet.ToString());
    }

    [Fact]
    public async Task AttachmentMigration_CreatesTableAndPersistsWorkbook()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        int attachmentId;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            var project = new Project { Name = "Test project" };
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            var attachment = new StoredAttachment
            {
                ProjectId = project.Id, Kind = "Workbook", FileName = "results.xlsx",
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                Data = [1, 2, 3], UploadedAt = DateTime.UtcNow, UploadedBy = "testuser"
            };
            db.StoredAttachments.Add(attachment);
            await db.SaveChangesAsync();
            attachmentId = attachment.Id;
        }
        await using var readDb = new AppDbContext(options);
        var saved = await readDb.StoredAttachments.AsNoTracking().SingleAsync(item => item.Id == attachmentId);
        Assert.Equal("results.xlsx", saved.FileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, saved.Data);
        var listedSize = await readDb.StoredAttachments.AsNoTracking()
            .Where(item => item.Id == attachmentId)
            .Select(item => item.Data.Length).SingleAsync();
        Assert.Equal(3, listedSize);
    }
}
