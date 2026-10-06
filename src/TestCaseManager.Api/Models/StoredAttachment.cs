namespace TestCaseManager.Api.Models;

// The database holds both metadata and bytes so backups include evidence and
// uploaded workbooks. Attachments never change the official run result.
public sealed class StoredAttachment
{
    public int Id { get; set; }
    public int? ProjectId { get; set; }
    public int? ManualStepResultId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public byte[] Data { get; set; } = [];
}
