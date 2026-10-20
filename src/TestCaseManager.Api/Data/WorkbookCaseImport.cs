using System.IO.Compression;
using System.Xml.Linq;
using TestCaseManager.Api.Models;

namespace TestCaseManager.Api.Data;

// Reads case definitions from ATCM's Execution sheet. Offline outcomes are deliberately ignored.
public static class WorkbookCaseImport
{
    public sealed record Step(string Action, string ExpectedResult);
    public sealed record Case(string? SourceId, string Module, string Title, string Description,
        string Preconditions, TestPriority Priority, IReadOnlyList<Step> Steps);

    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static IReadOnlyList<Case> Parse(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        if (zip.Entries.Count > 1000 || zip.Entries.Sum(entry => entry.Length) > 25 * 1024 * 1024)
            throw new InvalidDataException("Workbook is too large to import.");
        var workbook = ReadXml(zip.GetEntry("xl/workbook.xml"));
        var relationships = ReadXml(zip.GetEntry("xl/_rels/workbook.xml.rels"));
        var sheet = workbook.Descendants(Main + "sheet").FirstOrDefault(item =>
            string.Equals((string?)item.Attribute("name"), "Execution", StringComparison.OrdinalIgnoreCase));
        if (sheet is null) throw new InvalidDataException("The workbook needs an Execution sheet exported by ATCM.");
        var relationshipId = (string?)sheet.Attribute(Relationship + "id");
        var target = (string?)relationships.Descendants().FirstOrDefault(item =>
            (string?)item.Attribute("Id") == relationshipId)?.Attribute("Target");
        if (string.IsNullOrWhiteSpace(target) || target.StartsWith('/') || target.Contains("..") || target.Contains(':'))
            throw new InvalidDataException("The Execution sheet is invalid.");
        var path = target.StartsWith("xl/", StringComparison.Ordinal) ? target : "xl/" + target;
        var document = ReadXml(zip.GetEntry(path.Replace('\\', '/')));
        var stringsEntry = zip.GetEntry("xl/sharedStrings.xml");
        string[] shared = stringsEntry is null ? [] : ReadXml(stringsEntry).Descendants(Main + "si")
            .Select(item => string.Concat(item.Descendants(Main + "t").Select(text => text.Value))).ToArray();
        var rows = document.Descendants(Main + "sheetData").Elements(Main + "row").Take(10002)
            .Select(row => Cells(row, shared)).ToList();
        if (rows.Count == 0 || rows.Count > 10001) throw new InvalidDataException("The Execution sheet must have at most 10,000 data rows.");
        var headers = rows[0].ToDictionary(item => item.Key, item => item.Value.Trim(), EqualityComparer<int>.Default);
        int Column(string name) => headers.FirstOrDefault(item => item.Value.Equals(name, StringComparison.OrdinalIgnoreCase)).Key;
        var required = new[] { "Module", "Case title", "Action", "Expected result" };
        if (required.Any(name => Column(name) == 0))
            throw new InvalidDataException("The Execution sheet needs Module, Case title, Action and Expected result columns.");
        string Value(Dictionary<int, string> row, string column) =>
            Column(column) is var index && index > 0 && row.TryGetValue(index, out var value) ? value.Trim() : "";
        var cases = new List<(string Key, string? SourceId, string Module, string Title, string Description,
            string Preconditions, TestPriority Priority, List<Step> Steps)>();
        foreach (var row in rows.Skip(1))
        {
            if (row.Count == 0 || row.Values.All(string.IsNullOrWhiteSpace)) continue;
            var module = Value(row, "Module");
            var title = Value(row, "Case title");
            var action = Value(row, "Action");
            var expected = Value(row, "Expected result");
            var description = Value(row, "Description");
            var preconditions = Value(row, "Preconditions");
            var sourceId = Value(row, "Case ID");
            var priorityText = Value(row, "Priority");
            if (module.Length is < 1 or > 120 || title.Length is < 1 or > 200 ||
                action.Length is < 1 or > 2000 || expected.Length is < 1 or > 2000 ||
                description.Length > 4000 || preconditions.Length > 4000 || sourceId.Length > 50 ||
                !Enum.TryParse<TestPriority>(priorityText.Length == 0 ? "Major" : priorityText, true, out var priority) ||
                !Enum.IsDefined(priority))
                throw new InvalidDataException("A case row has missing or invalid module, title, priority, action or expected result.");
            var key = sourceId.Length > 0 ? "id:" + sourceId : "name:" + module + "\u001f" + title;
            var index = cases.FindIndex(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                if (cases.Count >= 500) throw new InvalidDataException("Import at most 500 cases at once.");
                cases.Add((key, sourceId.Length == 0 ? null : sourceId, module, title, description,
                    preconditions, priority, [new Step(action, expected)]));
            }
            else
            {
                var existing = cases[index];
                if (!existing.Module.Equals(module, StringComparison.OrdinalIgnoreCase) ||
                    !existing.Title.Equals(title, StringComparison.OrdinalIgnoreCase) ||
                    existing.Steps.Count >= 100)
                    throw new InvalidDataException("A case has inconsistent details or more than 100 steps.");
                existing.Steps.Add(new Step(action, expected));
            }
        }
        if (cases.Count == 0) throw new InvalidDataException("The Execution sheet has no test cases to import.");
        return cases.Select(item => new Case(item.SourceId, item.Module, item.Title, item.Description,
            item.Preconditions, item.Priority, item.Steps)).ToList();
    }

    private static XDocument ReadXml(ZipArchiveEntry? entry)
    {
        if (entry is null) throw new InvalidDataException("The workbook is missing a required sheet.");
        using var stream = entry.Open();
        using var reader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings
        { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static Dictionary<int, string> Cells(XElement row, string[] shared)
    {
        var cells = new Dictionary<int, string>();
        foreach (var cell in row.Elements(Main + "c"))
        {
            var column = 0;
            foreach (var letter in ((string?)cell.Attribute("r") ?? "").TakeWhile(char.IsLetter))
                column = column * 26 + char.ToUpperInvariant(letter) - 'A' + 1;
            if (column == 0 || column > 100) continue;
            if (cell.Element(Main + "f") is not null)
                throw new InvalidDataException("Formula cells cannot be imported as test case definitions.");
            var value = (string?)cell.Element(Main + "v") ??
                string.Concat(cell.Descendants(Main + "is").Descendants(Main + "t").Select(item => item.Value));
            if ((string?)cell.Attribute("t") == "s")
            {
                if (!int.TryParse(value, out var index) || index < 0 || index >= shared.Length)
                    throw new InvalidDataException("A shared string in the workbook is invalid.");
                value = shared[index];
            }
            cells[column] = value;
        }
        return cells;
    }
}
