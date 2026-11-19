using System.Xml;
using System.Xml.Linq;
using AI.GTX.Harness.Core;
using AI.GTX.Harness.Tools;

namespace AI.GTX.Harness.Infrastructure;

public static class TrxEvidenceReader
{
    // This parses output only; it never launches tests or asserts that a sandbox was used.
    public static (int Total, int Passed, int Failed) Read(string isolatedWorkspace)
    {
        var root = new WorkspaceReader(isolatedWorkspace).Resolve("TestResults");
        if (!Directory.Exists(root)) throw new InvalidDataException("No test results were produced.");
        var entries = Directory.GetFiles(root, "*.trx", SearchOption.TopDirectoryOnly);
        if (entries.Length != 1) throw new InvalidDataException("Exactly one fixed-template test result is required.");
        var path = new WorkspaceReader(root).Resolve(Path.GetFileName(entries[0]));
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_000_000 };
        using var reader = XmlReader.Create(path, settings); var document = XDocument.Load(reader);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var counters = document.Root?.Element(ns + "ResultSummary")?.Element(ns + "Counters") ?? throw new InvalidDataException("TRX counters missing.");
        int Count(string name) => int.TryParse(counters.Attribute(name)?.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count) ? count : throw new InvalidDataException("Invalid TRX counter.");
        var total = Count("total"); var passed = Count("passed"); var failed = Count("failed"); var executed = Count("executed");
        if (total < 1 || executed != total || passed + failed != total) throw new InvalidDataException("Incomplete or inconsistent test execution.");
        return (total, passed, failed);
    }
}
