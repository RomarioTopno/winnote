using System.Text.Json;
using WinNote.Services;

namespace WinNote.Tests;

public sealed class AtomicFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("winnote-tests-").FullName;
    private string PathOf(string name) => Path.Combine(_dir, name);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed record Doc(int Value);

    private static Doc? Parse(string json) => JsonSerializer.Deserialize<Doc>(json);

    [Fact]
    public void Writes_new_file_without_leaving_temp_files()
    {
        string path = PathOf("notes.json");
        AtomicFile.WriteAllText(path, "one");

        Assert.Equal("one", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void Replacing_keeps_previous_version_as_bak()
    {
        string path = PathOf("notes.json");
        AtomicFile.WriteAllText(path, "one");
        AtomicFile.WriteAllText(path, "two");

        Assert.Equal("two", File.ReadAllText(path));
        Assert.Equal("one", File.ReadAllText(path + ".bak"));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Writes_utf8_without_losing_characters()
    {
        string path = PathOf("notes.json");
        const string text = "[ ] Café ☕ — 日本語 ✓";
        AtomicFile.WriteAllText(path, text);
        Assert.Equal(text, File.ReadAllText(path));
    }

    [Fact]
    public void Read_returns_null_when_nothing_exists()
    {
        Assert.Null(AtomicFile.Read(PathOf("missing.json"), Parse));
    }

    [Fact]
    public void Read_falls_back_to_bak_when_main_file_is_corrupt()
    {
        string path = PathOf("notes.json");
        File.WriteAllText(path + ".bak", """{ "Value": 1 }""");
        File.WriteAllText(path, """{ "Value": """); // truncated, e.g. by a crash mid-write elsewhere

        Assert.Equal(1, AtomicFile.Read(path, Parse)?.Value);
    }

    [Fact]
    public void Read_falls_back_to_bak_when_main_file_is_empty()
    {
        string path = PathOf("notes.json");
        File.WriteAllText(path, "");
        File.WriteAllText(path + ".bak", """{ "Value": 2 }""");

        Assert.Equal(2, AtomicFile.Read(path, Parse)?.Value);
    }

    [Fact]
    public void Read_prefers_main_file()
    {
        string path = PathOf("notes.json");
        AtomicFile.WriteAllText(path, """{ "Value": 1 }""");
        AtomicFile.WriteAllText(path, """{ "Value": 2 }""");

        Assert.Equal(2, AtomicFile.Read(path, Parse)?.Value);
    }
}
