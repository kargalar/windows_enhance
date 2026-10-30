using System.IO.Compression;
using System.Text;
using WinForge.FileTools.Engine;
using WinForge.FileTools.UI;
using UglyToad.PdfPig;

internal static class OfficeRuntimeChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var toolRoot = Environment.GetEnvironmentVariable("NASHI_VERIFY_OFFICE_DIRECTORY")
            ?? throw new InvalidOperationException("Set NASHI_VERIFY_OFFICE_DIRECTORY to a disposable test directory.");
        var root = Path.Combine(Path.GetTempPath(), "WinForge-OfficeChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var runtime = new OfficeRuntime(toolRoot);
            await runtime.InstallAsync(null, CancellationToken.None);
            check(runtime.Executable is { } executable && File.Exists(executable), "Verified Office runtime is available without a system install.");

            var document = Path.Combine(root, "Nashi Office fixture.docx");
            CreateDocx(document, "Nashi Office conversion fixture");
            var converter = new DocumentConversion(runtime, new Toolchain(Path.Combine(toolRoot, "media")));
            var output = Path.Combine(root, "Exports");
            var pdfFiles = await converter.ConvertAsync([document], "pdf", output, new DocumentOptions(), null, CancellationToken.None);
            check(pdfFiles.Count == 1 && File.Exists(pdfFiles[0]), "A real DOCX converts to PDF through the private Office runtime.");
            using (var pdf = PdfDocument.Open(pdfFiles[0]))
                check(pdf.NumberOfPages == 1 && pdf.GetPage(1).Text.Contains("Nashi Office conversion fixture", StringComparison.Ordinal), "The Office PDF preserves the source text.");

            var csv = Path.Combine(root, "Table.csv");
            await File.WriteAllTextAsync(csv, "Name;Amount\r\nPear;42\r\n", new UTF8Encoding(false));
            var spreadsheet = await converter.ConvertAsync([csv], "xlsx", output, new DocumentOptions(), null, CancellationToken.None);
            using (var workbook = ZipFile.OpenRead(spreadsheet.Single()))
                check(workbook.Entries.Any(entry => entry.FullName == "xl/worksheets/sheet1.xml"), "A real CSV converts to a valid XLSX workbook.");
            var restored = await converter.ConvertAsync(spreadsheet, "csv", output, new DocumentOptions(), null, CancellationToken.None);
            check(File.ReadAllText(restored.Single()).Contains("Pear", StringComparison.Ordinal) && File.ReadAllText(restored.Single()).Contains("42", StringComparison.Ordinal), "Spreadsheet conversion preserves row values in a real round trip.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void CreateDocx(string path, string text)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Add("[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
            """);
        Add("_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
            """);
        Add("word/document.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>{System.Security.SecurityElement.Escape(text)}</w:t></w:r></w:p><w:sectPr/></w:body></w:document>
            """);

        void Add(string name, string contents)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(contents);
        }
    }
}
