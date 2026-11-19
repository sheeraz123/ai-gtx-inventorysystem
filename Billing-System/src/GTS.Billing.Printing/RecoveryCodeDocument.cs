namespace GTS.Billing.Printing;

public static class RecoveryCodeDocument
{
    public static string Format(IReadOnlyList<string> codes)
    {
        if (codes.Count != 10) throw new InvalidOperationException("No current recovery code set is available.");
        return "GTS Billing — Super Admin recovery codes" + Environment.NewLine +
            "SENSITIVE: Keep offline and private. Each code works once. Regeneration revokes unused older codes." +
            Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, codes) + Environment.NewLine;
    }

    public static bool Print(IReadOnlyList<string> codes)
    {
        var dialog = new System.Windows.Controls.PrintDialog();
        if (dialog.ShowDialog() != true) return false;
        var document = new System.Windows.Documents.FlowDocument(
            new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(Format(codes))))
        {
            PageWidth = dialog.PrintableAreaWidth,
            PageHeight = dialog.PrintableAreaHeight,
            PagePadding = new System.Windows.Thickness(36),
            FontSize = 12,
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        };
        dialog.PrintDocument(((System.Windows.Documents.IDocumentPaginatorSource)document).DocumentPaginator,
            "GTS Billing recovery codes — sensitive");
        return true;
    }
}
