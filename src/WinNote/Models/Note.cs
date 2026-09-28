using System.Text.Json.Serialization;

namespace WinNote.Models;

public sealed class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Plain text with '\n' line endings. Todo lines are literally "[ ] ..." / "[x] ...".</summary>
    public string Text { get; set; } = "";

    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public bool IsBlank => string.IsNullOrWhiteSpace(Text);
}

public sealed class NotesFile
{
    public int Version { get; set; } = 1;
    public List<Note> Notes { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(NotesFile))]
internal sealed partial class WinNoteJsonContext : JsonSerializerContext;
