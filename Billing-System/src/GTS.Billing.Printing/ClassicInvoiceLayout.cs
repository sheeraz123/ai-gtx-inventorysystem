using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GTS.Billing.Application;

namespace GTS.Billing.Printing;

internal sealed class ClassicInvoiceLayout(InvoicePrintModel invoice, string? copyLabel)
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");
    private static readonly double[] Goods = [4, 29, 8, 7, 8, 8, 5, 7, 10, 7, 10];
    private const double PageWidth = 210 * 96 / 25.4;
    private const double PageHeight = 297 * 96 / 25.4;
    private const double Stroke = .7;
    private InvoicePresentationOptions? Options => invoice.Seller.Presentation.Options;
    private InvoicePrintOptions? Printing => Options?.Printing;
    private double Left => InvoiceDocumentRenderer.MarginDip(invoice, Printing?.LeftMm);
    private double Right => InvoiceDocumentRenderer.MarginDip(invoice, Printing?.RightMm);
    private double Top => InvoiceDocumentRenderer.MarginDip(invoice, Printing?.TopMm);
    private double Bottom => InvoiceDocumentRenderer.MarginDip(invoice, Printing?.BottomMm);
    private double InnerWidth => PageWidth - Left - Right - 2 * Stroke;
    private double InnerHeight => PageHeight - Top - Bottom - 2 * Stroke;

    public FixedDocument Build()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(PageWidth, PageHeight);
        var units = invoice.Lines.GroupBy(line => Unit(line)).ToArray();
        var quantity = units.Length == 1 ? DecimalText(units[0].Sum(line => line.Input.Quantity)) + " " + units[0].Key : "";
        var total = Row(Goods, ["", "Total", "", "", quantity, "", "", Money(invoice.Totals.Discount),
            Money(invoice.Totals.Taxable), "", Money(invoice.Totals.Total - invoice.Totals.RoundOff)], true, 20);
        var footer = Footer(total);
        var finalHeight = Measure(footer);
        var rows = invoice.Lines.Select(Line).ToArray();
        var heights = rows.Select(Measure).ToArray();
        var position = 0;
        while (true)
        {
            var body = Stack(Header(position != 0), GoodsHeading());
            var available = InnerHeight - Measure(body);
            if (finalHeight > available)
                throw new InvalidOperationException("The invoice summary exceeds the printable A4 area.");
            var start = position;
            var remainingHeight = heights.Skip(start).Sum();
            var final = remainingHeight + finalHeight <= available;

            while (position < rows.Length && heights[position] <= available - (final ? finalHeight : 0))
            {
                // When possible, keep at least one goods line with the final summary.
                if (!final && position == rows.Length - 1 && position > start) break;
                body.Children.Add(rows[position]);
                available -= heights[position++];
            }
            if (position == start && position != rows.Length)
                throw new InvalidOperationException("An invoice item exceeds the printable A4 area.");
            var strip = final ? finalHeight : 0;
            if (available - strip > .001)
                body.Children.Add(Rule(Columns(Goods, true, Goods.Select(_ => new Border()).ToArray()), available - strip));
            if (final) body.Children.Add(footer);

            var page = new FixedPage { Width = PageWidth, Height = PageHeight, Background = Brushes.White };
            var frame = new Border
            {
                Width = InnerWidth + 2 * Stroke, Height = InnerHeight + 2 * Stroke,
                BorderBrush = Brushes.Black, BorderThickness = new Thickness(Stroke), Child = body
            };
            FixedPage.SetLeft(frame, Left);
            FixedPage.SetTop(frame, Top);
            page.Children.Add(frame);
            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            document.Pages.Add(content);
            if (final) return document;
        }
    }

    private FrameworkElement Header(bool continuation)
    {
        var seller = invoice.Seller;
        var presentation = seller.Presentation;
        var name = CompanyName();
        var designation = copyLabel ?? Printing?.CopyLabel;
        var meta = Columns([38, 24, 38], false,
            Text(Lines(Field(Label("Gstin", "GSTIN"), seller.Gstin),
                Show("Iec")?Field("IEC No.",seller.Iec):null), 11, true),
            Text(Label("Title", "Tax Invoice").ToUpperInvariant(), 12, true, TextAlignment.Center),
            Text(Show("CompanyEmail")?Field("Email",seller.Email):null, 10, true, TextAlignment.Right));
        var identity = Stack(Text(name, 30, true, TextAlignment.Center, "Georgia"),
            Text(Lines(Options?.Tagline, presentation.Header), 11, true, TextAlignment.Center),
            Text(Lines(Show("CompanyAddress")?seller.Address:null, Join(Field("State code", seller.StateCode),
                Show("CompanyContact") && Show("CompanyPhone") ? Field("Phone", seller.Phone) : null)), 10, false, TextAlignment.Center));
        var branding = Columns([19, 62, 19], false, Logo(presentation.Primary), identity, Logo(presentation.Secondary));
        branding.MinHeight = 83;
        var number = Columns([14, 36, 10, 40], true,
            Reverse("Invoice No."), Text(invoice.Number, 13, true), Reverse("Date"),
            Text(invoice.Header.Date.ToString(Options?.DateFormat ?? "dd/MM/yyyy", India), 13, true));
        var result = Stack(Rule(Text(string.IsNullOrWhiteSpace(designation) ? "" : designation.ToUpperInvariant(),
                11, true, TextAlignment.Center), 19),
            Rule(Stack(meta, branding)), Rule(number, 21));
        if (!continuation)
            result.Children.Add(Rule(Columns([50, 50], true,
                Address("Details of Receiver (Billed to)", invoice.Customer.BillTo, true),
                Address("Details of Consignee (Shipped to)", invoice.Customer.ShipTo, false)), 126));
        return result;
    }

    private FrameworkElement Address(string title, CustomerAddressSnapshot address, bool billing) =>
        Stack(Text(title, 11, true), Text(billing ? invoice.Customer.LegalName : address.RecipientName, 12, true),
            Text(Lines(address.Organization, address.Line1, address.Line2, address.Landmark, address.Locality,
                Join(address.City, address.District, address.PostalCode), Join(Field("State", address.State),
                    Field("Code", address.StateCode), address.Country),
                Field("GSTIN", invoice.Customer.Gstin),
                Show("CustomerContact") ? Field("Phone", address.Mobile ?? (billing ? invoice.Customer.ContactNumber : null)) : null), 11));

    private FrameworkElement GoodsHeading()
    {
        var grid = GridFor(Goods);
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        string[] headings = ["Sr.\nNo.", "Description of Goods", "M.R.P.", "HSN /\nSAC", "Qty.", "Rate",
            "Discount", "", "Taxable\nAmount", "Tax Slab\n%", "Amount"];
        for (var index = 0; index < headings.Length; index++)
        {
            if (index == 7) continue;
            var discount = index == 6;
            Add(grid, Text(headings[index], 10, true, TextAlignment.Center), index,
                index == headings.Length - 1 ? 0 : Stroke, discount ? Stroke : 0, span: discount ? 2 : 1,
                rowSpan: discount ? 1 : 2);
        }
        Add(grid, Text("%", 10, true, TextAlignment.Center), 6, Stroke, 0, row: 1);
        Add(grid, Text("Amount", 10, true, TextAlignment.Center), 7, Stroke, 0, row: 1);
        return Rule(grid, 29);
    }

    private FrameworkElement Line(FinalSalesLine line)
    {
        var product = line.Product;
        var tax = line.Tax;
        return Row(Goods, [line.Number.ToString(India),
            Lines(product.Name, Join(product.Code, Show("SkuBarcode") ? Field("SKU", product.Sku) : null, product.Brand),
                Show("ProductDescription") ? product.Description : null),
            product.Mrp is { } mrp ? Money(mrp) : "", tax.Rate.Code, Join(DecimalText(line.Input.Quantity), Unit(line)), DecimalText(line.Input.Price),
            DecimalText(line.Input.DiscountPercent), Money(line.Amounts.Discount), Money(line.Amounts.Taxable),
            Slab(tax), Money(line.Amounts.Total)], false, 19);
    }

    private FrameworkElement Footer(FrameworkElement total)
    {
        var presentation = invoice.Seller.Presentation;
        var bank = presentation.Bank;
        var amounts = invoice.Totals;
        var totals = Stack(Total("Subtotal", amounts.Subtotal));
        if (amounts.Discount != 0) totals.Children.Add(Total("Discount", amounts.Discount));
        totals.Children.Add(Total("Taxable", amounts.Taxable));
        totals.Children.Add(Total("Total GST", amounts.Tax));
        totals.Children.Add(Total("Total", amounts.Total - amounts.RoundOff));
        if (amounts.RoundOff != 0) totals.Children.Add(Total("Round off", amounts.RoundOff));
        totals.Children.Add(Total("G. TOTAL", amounts.Total, true));
        var table = TaxSummary();
        var bankBlock = bank is null ? Stack() : Stack(Text(Label("BankDetails", "Bank Details"), 11, true),
            Text(Lines(Join(bank.BankName, bank.Branch), Field("A/c No.", bank.AccountNumber), Field("IFSC", bank.Ifsc),
                Field("UPI", bank.Upi), bank.Instructions), 11));
        var header = invoice.Header;
        var shipping = header.Transport;
        var transport = Columns([44, 56], true,
            Text(Lines(Field("G.R. No.", shipping?.GoodsReceipt), Field("Weight", shipping?.Weight),
                Field("Transport", shipping?.Carrier), Field("Pvt. Mark", shipping?.PrivateMark),
                Field("Document", shipping?.Document), Field("Reference", header.Reference), Field("Payment terms", header.Terms),
                header.DueDate is { } due ? Field("Due date", due.ToString("dd/MM/yyyy", India)) : null), 10),
            Text(Lines(Field("Vehicle No.", shipping?.Vehicle), Field("Order Through", shipping?.OrderThrough),
                Field("Prepared By", shipping?.PreparedBy), Field("Customer PO", Join(header.PurchaseOrder, header.PurchaseOrderDate?.ToString("dd/MM/yyyy", India))),
                Field("Place of supply", header.PlaceOfSupply)), 10));
        var terms = Text(Lines(Show("Terms")?presentation.Terms:null, header.Notes, Options?.Remarks,
            Show("FooterNotes") ? Options?.FooterNotes : null, Show("FooterNotes") ? presentation.Footer : null), 10);
        var signatureImage = Logo(Show("Signatory") ? presentation.Signature : null);
        signatureImage.Width = 120;
        signatureImage.Height = 32;
        signatureImage.Margin = new Thickness(0);
        var signature = Stack(Text("For " + CompanyName(), 11, true, TextAlignment.Center),
            signatureImage, Text(Show("Signatory") ? presentation.Signatory : null, 10, false, TextAlignment.Center),
            Text("Authorized Signatory", 10, false, TextAlignment.Center));
        var footer = Stack(total, Rule(Columns([43, 30, 27], true, table, bankBlock, totals), 92));
        if (Show("Transport")) footer.Children.Add(Rule(transport, 73));
        footer.Children.Add(Rule(Text("Rupees:  " + IndianAmountWords.Format(amounts.Total), 11, true), 19));
        var conditions=Stack(Text(Show("Terms")?Label("Terms", "Terms & Conditions"):"",11,true),terms);
        footer.Children.Add(Show("Signatory") || Options?.OptionalSections.ContainsKey("Invoice.Optional.Iec")!=true
            ? Columns([66,34],true,conditions,signature):conditions);
        return footer;
    }

    private FrameworkElement TaxSummary()
    {
        (string Name, Func<SalesAmounts, decimal> Value)[] components =
        [
            ("CGST", x => x.Cgst), ("SGST", x => x.Sgst), ("IGST", x => x.Igst),
            ("UTGST", x => x.Utgst), ("Cess", x => x.Cess)
        ];
        var shown = components.Where(component => invoice.Lines.Any(line => component.Value(line.Amounts) != 0)).ToArray();
        var useSeparateColumns = shown.Length <= 3;
        double[] widths = useSeparateColumns ? [25, 34, .. shown.Select(_ => 41d / shown.Length)] : [25, 31, 44];
        string[] headings = useSeparateColumns ? ["Tax %", "Taxable", .. shown.Select(x => Label(x.Name, x.Name))]
            : ["Tax %", "Taxable", "Tax Amount"];
        var table = Stack(Row(widths, headings, true, 17));
        var groups = invoice.Lines.GroupBy(line => Slab(line.Tax));
        foreach (var group in groups)
        {
            var componentTotals = shown.Select(x => (x.Name, Amount: group.Sum(line => x.Value(line.Amounts)))).ToArray();
            string[] taxes = useSeparateColumns ? componentTotals.Select(x => Money(x.Amount)).ToArray()
                : [Lines(componentTotals.Where(x => x.Amount != 0).Select(x => Field(Label(x.Name, x.Name), Money(x.Amount))).ToArray())];
            table.Children.Add(Row(widths, [group.Key, Money(group.Sum(x => x.Amounts.Taxable)), .. taxes], false, 18));
        }
        return table;
    }

    private FrameworkElement Total(string caption, decimal amount, bool grand = false) =>
        Columns([44, 56], false, Text(caption, grand ? 12 : 11, true),
            Text(Money(amount), grand ? 14 : 11, true, TextAlignment.Right));

    private static string Slab(AppliedTaxSnapshot tax) => tax.Rate.Treatment == GTS.Billing.Domain.TaxTreatment.Taxable
        ? DecimalText(tax.CgstPercentage + tax.SgstPercentage + tax.IgstPercentage + tax.UtgstPercentage) + "%"
        : tax.Rate.Treatment switch
        {
            GTS.Billing.Domain.TaxTreatment.NilRated => "Nil-rated",
            GTS.Billing.Domain.TaxTreatment.ZeroRated => "Zero-rated",
            GTS.Billing.Domain.TaxTreatment.NonGst => "Non-GST",
            _ => tax.Rate.Treatment.ToString()
        };

    private static FrameworkElement Logo(InvoiceLogoImage? logo)
    {
        if (logo is not { Content.IsDefaultOrEmpty: false }) return new Border();
        try
        {
            using var stream = new MemoryStream(logo.Content.ToArray(), false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxHeight = 65, Margin = new Thickness(6),
                VerticalAlignment = VerticalAlignment.Bottom };
        }
        catch (Exception error) when (error is IOException or ArgumentException or FormatException or NotSupportedException)
        {
            return new Border();
        }
    }

    private double Measure(FrameworkElement element)
    {
        element.Measure(new Size(InnerWidth, double.PositiveInfinity));
        return element.DesiredSize.Height;
    }

    private static Grid GridFor(double[] widths)
    {
        var grid = new Grid();
        foreach (var width in widths) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
        return grid;
    }

    private static Grid Columns(double[] widths, bool ruled, params FrameworkElement[] elements)
    {
        var grid = GridFor(widths);
        for (var index = 0; index < elements.Length; index++)
            Add(grid, elements[index], index, ruled && index < elements.Length - 1 ? Stroke : 0, 0);
        return grid;
    }

    private static void Add(Grid grid, FrameworkElement content, int column, double right, double bottom,
        int row = 0, int span = 1, int rowSpan = 1)
    {
        var cell = new Border { Child = content, BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 0, right, bottom) };
        Grid.SetColumn(cell, column);
        Grid.SetRow(cell, row);
        Grid.SetColumnSpan(cell, span);
        Grid.SetRowSpan(cell, rowSpan);
        grid.Children.Add(cell);
    }

    private static FrameworkElement Row(double[] widths, string[] values, bool bold, double minimum) =>
        Rule(Columns(widths, true, values.Select((value, index) => Text(value, 10, bold,
            widths == Goods ? (index == 1 ? TextAlignment.Left : index is 0 or 3 ? TextAlignment.Center : TextAlignment.Right)
                : index == 0 ? TextAlignment.Left : TextAlignment.Right)).ToArray()), minimum);

    private static Border Rule(FrameworkElement body, double height = 0) => new()
    {
        Child = body, MinHeight = height, BorderThickness = new Thickness(0, 0, 0, Stroke), BorderBrush = Brushes.Black
    };

    private static StackPanel Stack(params FrameworkElement[] children)
    {
        var stack = new StackPanel();
        foreach (var child in children) stack.Children.Add(child);
        return stack;
    }

    private static TextBlock Text(string? text, double size, bool bold = false,
        TextAlignment align = TextAlignment.Left, string face = "Arial") =>
        new() { Text = text ?? "", FontFamily = new FontFamily(face), FontSize = size, TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(4, 1, 4, 1), TextAlignment = align, Foreground = Brushes.Black,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal };

    private static TextBlock Reverse(string value)
    {
        var text = Text(value, 11, true);
        text.Foreground = Brushes.White;
        text.Background = Brushes.Black;
        return text;
    }

    private string CompanyName() => string.IsNullOrWhiteSpace(Options?.CompanyName) ? invoice.Seller.Name : Options.CompanyName;
    private string Label(string key, string fallback) => Options?.Captions.GetValueOrDefault("Invoice.Label." + key) ?? fallback;
    private bool Show(string section) => Options?.ShowSection("Invoice.Optional." + section, false) ?? true;
    private static string Unit(FinalSalesLine line) => line.Product.UnitSymbol ?? line.Product.Unit;
    private static string? Field(string label, string? value) => string.IsNullOrWhiteSpace(value) ? null : label + ": " + value;
    private static string Lines(params string?[] values) => string.Join("\n", values.Where(x => !string.IsNullOrWhiteSpace(x)));
    private static string Join(params string?[] values) => string.Join("  ", values.Where(x => !string.IsNullOrWhiteSpace(x)));
    private static string Money(decimal value) => value.ToString("#,##0.00##########################", India);
    private static string DecimalText(decimal value) => value.ToString("0.############################", India);
}
