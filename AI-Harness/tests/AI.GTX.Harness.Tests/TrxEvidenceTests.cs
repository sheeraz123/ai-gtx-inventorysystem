using System.Xml;
using AI.GTX.Harness.Infrastructure;
using Xunit;

namespace AI.GTX.Harness.Tests;

public sealed class TrxEvidenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gtx-trx-test-" + Guid.NewGuid().ToString("N"));
    private void Write(string xml) { Directory.CreateDirectory(Path.Combine(root, "TestResults")); File.WriteAllText(Path.Combine(root, "TestResults", "tests.trx"), xml); }
    private static string Trx(string counters) => "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><ResultSummary><Counters " + counters + "/></ResultSummary></TestRun>";
    [Fact] public void ParsesActualNumericCounts()
    {
        Write(Trx("total=\"3\" executed=\"3\" passed=\"2\" failed=\"1\""));
        Assert.Equal((3, 2, 1), TrxEvidenceReader.Read(root));
    }
    [Theory] [InlineData("total=\"0\" executed=\"0\" passed=\"0\" failed=\"0\"")]
    [InlineData("total=\"3\" executed=\"2\" passed=\"2\" failed=\"0\"")]
    [InlineData("total=\"3\" executed=\"3\" passed=\"2\" failed=\"0\"")]
    public void MissingExecutionCannotBecomeSuccess(string counters) { Write(Trx(counters)); Assert.Throws<InvalidDataException>(() => TrxEvidenceReader.Read(root)); }
    [Fact] public void DtdAndExternalEntitiesAreForbidden()
    {
        Write("<!DOCTYPE TestRun [<!ENTITY x SYSTEM 'file:///never-read'>]><TestRun>&x;</TestRun>");
        Assert.Throws<XmlException>(() => TrxEvidenceReader.Read(root));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
