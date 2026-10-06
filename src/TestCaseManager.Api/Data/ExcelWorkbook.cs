using System.IO.Compression;
using System.Text;
using System.Xml;

namespace TestCaseManager.Api.Data;

// Writes literal cell text (never formulas) so user-supplied case text is safe in Excel.
public static class ExcelWorkbook
{
    public sealed record Sheet(string Name, string[] Headers, IEnumerable<string?[]> Rows,
        int[]? EditableColumns = null, int? OutcomeColumn = null);

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static byte[] Create(params Sheet[] sheets)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml", xml =>
            {
                xml.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
                xml.WriteStartElement("Default"); xml.WriteAttributeString("Extension", "rels"); xml.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml"); xml.WriteEndElement();
                xml.WriteStartElement("Default"); xml.WriteAttributeString("Extension", "xml"); xml.WriteAttributeString("ContentType", "application/xml"); xml.WriteEndElement();
                Override(xml, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                Override(xml, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
                for (var i = 0; i < sheets.Length; i++) Override(xml, $"/xl/worksheets/sheet{i + 1}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                xml.WriteEndElement();
            });
            Write(zip, "_rels/.rels", xml =>
            {
                xml.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
                Relationship(xml, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml");
                xml.WriteEndElement();
            });
            Write(zip, "xl/workbook.xml", xml =>
            {
                xml.WriteStartElement("workbook", Main); xml.WriteAttributeString("xmlns", "r", null, Rel);
                xml.WriteStartElement("sheets", Main);
                for (var i = 0; i < sheets.Length; i++)
                {
                    xml.WriteStartElement("sheet", Main); xml.WriteAttributeString("name", sheets[i].Name);
                    xml.WriteAttributeString("sheetId", (i + 1).ToString()); xml.WriteAttributeString("r", "id", Rel, $"rId{i + 1}"); xml.WriteEndElement();
                }
                xml.WriteEndElement(); xml.WriteEndElement();
            });
            Write(zip, "xl/_rels/workbook.xml.rels", xml =>
            {
                xml.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
                for (var i = 0; i < sheets.Length; i++) Relationship(xml, $"rId{i + 1}", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", $"worksheets/sheet{i + 1}.xml");
                Relationship(xml, $"rId{sheets.Length + 1}", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml");
                xml.WriteEndElement();
            });
            Write(zip, "xl/styles.xml", Styles);
            for (var i = 0; i < sheets.Length; i++)
            {
                var sheet = sheets[i];
                Write(zip, $"xl/worksheets/sheet{i + 1}.xml", xml => Worksheet(xml, sheet));
            }
        }
        return stream.ToArray();
    }

    private static void Worksheet(XmlWriter xml, Sheet sheet)
    {
        var rows = sheet.Rows.ToList();
        xml.WriteStartElement("worksheet", Main);
        xml.WriteStartElement("sheetViews", Main); xml.WriteStartElement("sheetView", Main); xml.WriteAttributeString("workbookViewId", "0");
        xml.WriteStartElement("pane", Main); xml.WriteAttributeString("ySplit", "1"); xml.WriteAttributeString("topLeftCell", "A2");
        xml.WriteAttributeString("activePane", "bottomLeft"); xml.WriteAttributeString("state", "frozen"); xml.WriteEndElement();
        xml.WriteEndElement(); xml.WriteEndElement();
        xml.WriteStartElement("cols", Main);
        for (var i = 0; i < sheet.Headers.Length; i++)
        {
            xml.WriteStartElement("col", Main); xml.WriteAttributeString("min", (i + 1).ToString()); xml.WriteAttributeString("max", (i + 1).ToString());
            xml.WriteAttributeString("width", sheet.Headers[i].Contains("Result") || sheet.Headers[i].Contains("Action") ? "48" : "24");
            xml.WriteAttributeString("customWidth", "1"); xml.WriteEndElement();
        }
        xml.WriteEndElement();
        xml.WriteStartElement("sheetData", Main);
        Row(xml, 1, sheet.Headers, sheet.EditableColumns, true);
        for (var i = 0; i < rows.Count; i++) Row(xml, i + 2, rows[i], sheet.EditableColumns, false);
        xml.WriteEndElement();
        xml.WriteStartElement("autoFilter", Main); xml.WriteAttributeString("ref", $"A1:{Column(sheet.Headers.Length)}{rows.Count + 1}"); xml.WriteEndElement();
        if (sheet.OutcomeColumn is int outcome)
        {
            xml.WriteStartElement("dataValidations", Main); xml.WriteAttributeString("count", "1");
            xml.WriteStartElement("dataValidation", Main); xml.WriteAttributeString("type", "list"); xml.WriteAttributeString("allowBlank", "1");
            xml.WriteAttributeString("sqref", $"{Column(outcome)}2:{Column(outcome)}{Math.Max(2, rows.Count + 1)}");
            xml.WriteElementString("formula1", Main, "\"Not run,Passed,Failed\""); xml.WriteEndElement(); xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }

    private static void Row(XmlWriter xml, int number, string?[] values, int[]? editable, bool header)
    {
        xml.WriteStartElement("row", Main); xml.WriteAttributeString("r", number.ToString());
        for (var i = 0; i < values.Length; i++)
        {
            xml.WriteStartElement("c", Main); xml.WriteAttributeString("r", $"{Column(i + 1)}{number}");
            xml.WriteAttributeString("t", "inlineStr");
            xml.WriteAttributeString("s", header ? "1" : editable?.Contains(i + 1) == true ? "2" : "0");
            xml.WriteStartElement("is", Main); xml.WriteElementString("t", Main, values[i] ?? ""); xml.WriteEndElement(); xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }

    private static string Column(int number)
    {
        var result = "";
        while (number > 0) { number--; result = (char)('A' + number % 26) + result; number /= 26; }
        return result;
    }

    private static void Styles(XmlWriter xml)
    {
        xml.WriteStartElement("styleSheet", Main);
        xml.WriteRaw("<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Aptos\"/></font><font><b/><color rgb=\"FFFFFFFF\"/><sz val=\"11\"/><name val=\"Aptos\"/></font></fonts>");
        xml.WriteRaw("<fills count=\"4\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF183153\"/><bgColor indexed=\"64\"/></patternFill></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFF2CC\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>");
        xml.WriteRaw("<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
        xml.WriteRaw("<cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"><alignment vertical=\"top\" wrapText=\"1\"/></xf><xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"0\" fontId=\"0\" fillId=\"3\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf></cellXfs>");
        xml.WriteEndElement();
    }

    private static void Write(ZipArchive zip, string path, Action<XmlWriter> write)
    {
        using var entry = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
        using var xml = XmlWriter.Create(entry, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false });
        xml.WriteStartDocument(); write(xml); xml.WriteEndDocument();
    }

    private static void Override(XmlWriter xml, string name, string type)
    {
        xml.WriteStartElement("Override"); xml.WriteAttributeString("PartName", name); xml.WriteAttributeString("ContentType", type); xml.WriteEndElement();
    }

    private static void Relationship(XmlWriter xml, string id, string type, string target)
    {
        xml.WriteStartElement("Relationship"); xml.WriteAttributeString("Id", id); xml.WriteAttributeString("Type", type); xml.WriteAttributeString("Target", target); xml.WriteEndElement();
    }
}
