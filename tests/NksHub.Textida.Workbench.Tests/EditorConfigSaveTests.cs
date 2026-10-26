using NksHub.Textida.Core.Documents;
using NksHub.Textida.Document.Contract;
using NksHub.Textida.Workbench;
using NksHub.Textida.Workbench.Settings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Workbench.Tests;

public class EditorConfigSaveTests
{
    [Fact]
    public void Saving_an_existing_file_follows_editorconfig_for_edited_lines_only_and_never_converts_line_endings()
    {
        string dir = Path.Combine(Path.GetTempPath(), "textida-ecs-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, ".editorconfig"),
                "root = true\n[*.txt]\nend_of_line = lf\ncharset = utf-8\ntrim_trailing_whitespace = true\ninsert_final_newline = true\n");
            string path = Path.Combine(dir, "a.txt");
            File.WriteAllBytes(path, "old  \r\nuntouched  \r\nedited"u8.ToArray());
            using var doc = Doc.Open(path, ReferenceBackendFactory.Instance);

            var stack = new SettingsStack(WorkbenchHost.CreateBuiltin().Settings);
            var result = EditorConfigResolver.Resolve(path, new DiskEditorConfigFiles());
            EditorConfigSettings.Apply(stack, result);
            Assert.Equal(SettingSource.EditorConfig, stack.Resolve("files.trimTrailingWhitespace")!.Source);
            Assert.EndsWith(".editorconfig", stack.Resolve("files.eol")!.LayerName);

            doc.Insert(0, "new "u8.ToArray());
            var options = new SaveTransformOptions(stack.GetBool("files.trimTrailingWhitespace"), stack.GetBool("files.insertFinalNewline"));
            SaveTransforms.Apply(doc, options);
            Assert.True(doc.Save().Saved);

            Assert.Equal("new old\r\nuntouched  \r\nedited\r\n", File.ReadAllText(path));
            Assert.Equal(("lf", "utf-8"), EditorConfigSettings.NewFileDefaults(result));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
