using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GTS.Billing.Application;

namespace GTS.Billing.Presentation.Wpf;

internal static class SalesInvoiceReadView
{
    internal static MeasureEditorDialog Create(SalesResult data, SalesOptions options)
    {
        var row = data.Row;
        var snapshot = data.Snapshot;
        var form=snapshot?.Seller.Presentation.Options?.SalesForm ??
            (row?.Finalized==true?SalesFormConfiguration.Empty:options.Form??SalesFormConfiguration.Empty);
        var draft = snapshot?.Draft ?? data.Draft!;
        var draftView=row is {Finalized:false};
        bool Show(string field)=>!draftView || form.Field(field).Visible;
        var dialog = new MeasureEditorDialog(row is null?"Unsaved invoice preview":(row.Number ?? "Draft") + " · Sales Invoice", true)
        {
            Width = Math.Min(1340, SystemParameters.WorkArea.Width),
            Height = Math.Min(820, SystemParameters.WorkArea.Height)
        };
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock { Text = row is null?"Unsaved invoice preview":(row.Number ?? "Draft invoice") + "  ·  " + row.Status,
            FontSize = 26, FontWeight = FontWeights.SemiBold });
        var customer=string.IsNullOrWhiteSpace(row?.Customer)?snapshot?.Customer.LegalName??Lookup(options.Customers,draft.CustomerId):row.Customer;
        var year=string.IsNullOrWhiteSpace(row?.Year)?Lookup(options.Years,draft.YearId):row.Year;
        var series=string.IsNullOrWhiteSpace(row?.Series)?Lookup(options.Series,draft.SeriesId):row.Series;
        heading.Children.Add(new TextBlock { Text = $"{draft.Date:d}  ·  {customer}  ·  {year}  ·  {series}",
            TextWrapping = TextWrapping.Wrap });
        dialog.AddCard("Invoice details",heading);
        var parties = new Grid();
        parties.ColumnDefinitions.Add(new());
        parties.ColumnDefinitions.Add(new());
        parties.ColumnDefinitions.Add(new());
        AddParty(0, "Supplier", snapshot is null ? options.CompanyName : Join(snapshot.Seller.Name,snapshot.Seller.Gstin,
            snapshot.Seller.Address,snapshot.Seller.Phone,snapshot.Seller.Email));
        AddParty(1, "Bill to", snapshot is null ? Lookup(options.Billing,draft.BillingAddressId)
            : Address(snapshot.Customer.BillTo,Show("CustomerGstin")?snapshot.Customer.Gstin:null));
        AddParty(2, "Ship to", snapshot is null ? Lookup(options.Shipping,draft.ShippingAddressId)
            : Address(snapshot.Customer.ShipTo,Show("CustomerGstin")?snapshot.Customer.Gstin:null));
        dialog.AddCard("Billing and shipping",parties);

        var table = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false,
            CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column,
            MaxHeight = 330, Margin = new Thickness(0,12,0,12) };
        var lines=snapshot is null
            ? (draft.Lines??[]).Select(DraftLine).ToArray()
            : snapshot.Lines.Select(line=>new Line(line.Number,draftView?line.Product.Name:WithVariant(line.Product),
                line.Product.Code,line.Product.Sku,line.Product.Brand,line.Tax.Rate.Code,line.Product.Mrp,
                line.Input.Quantity,line.Product.Unit,line.Input.Price,line.Input.DiscountPercent,line.Amounts.Discount,
                line.Amounts.Taxable,line.Tax.CgstPercentage+line.Tax.SgstPercentage+line.Tax.IgstPercentage+
                line.Tax.UtgstPercentage,line.Amounts.Total,line.Amounts.Subtotal,line.Amounts.Tax)).ToArray();
        AddColumn("No.",nameof(Line.Number),45);
        AddColumn("Product",nameof(Line.Product),230);
        AddColumn("Code",nameof(Line.Code),90);
        AddColumn("SKU",nameof(Line.Sku),90);
        AddColumn("Brand",nameof(Line.Brand),95);
        AddColumn("HSN/SAC",nameof(Line.Hsn),80);
        AddColumn("MRP",nameof(Line.Mrp),80);
        AddColumn("Quantity",nameof(Line.Quantity),75);
        AddColumn("Unit",nameof(Line.Unit),95);
        AddColumn("Rate",nameof(Line.Rate),95);
        AddColumn("Gross",nameof(Line.Gross),95);
        AddColumn("Discount %",nameof(Line.DiscountPercent),85);
        AddColumn("Discount",nameof(Line.Discount),95);
        AddColumn("Taxable",nameof(Line.Taxable),100);
        AddColumn("GST %",nameof(Line.Gst),80);
        AddColumn("GST amount",nameof(Line.Tax),95);
        AddColumn("Total",nameof(Line.Total),110);
        table.ItemsSource=lines;
        dialog.AddCard("Products and tax",table);
        var details = new WrapPanel();
        AddDetail("Payment / order",Join(draft.PurchaseOrder,
            Show("AdditionalDetails")?Join(Entry("Terms",draft.Terms),Entry("DueDate",draft.DueDate?.ToString("d")),
                Entry("Reference",draft.Reference),draftView?null:snapshot?.Seller.Presentation.Bank?.BankName):null),300);
        if(Show("AdditionalDetails"))
        {
            var transport=draft.Transport;
            AddDetail("Transport",Join(Entry("Carrier",transport?.Carrier),Caption("Vehicle",Entry("Vehicle",transport?.Vehicle)),
                Caption("G.R.",Entry("GoodsReceipt",transport?.GoodsReceipt)),Caption("Weight",Entry("Weight",transport?.Weight)),
                Caption("Order through",Entry("OrderThrough",transport?.OrderThrough)),Caption("Prepared by",Entry("PreparedBy",transport?.PreparedBy)),
                Caption("Private mark",Entry("PrivateMark",transport?.PrivateMark)),Caption("Document",Entry("Document",transport?.Document))),300);
            AddDetail(form.Caption("Notes","Notes"),Entry("Notes",draft.Notes),270);
        }
        if(details.Children.Count>0) dialog.AddSection(details);
        if(data.Audit is { Count: > 0 }) dialog.Text("Audit history", string.Join(Environment.NewLine,data.Audit),5000,true);
        dialog.DockSummary(new TextBlock { Text = snapshot is null
            ? data.PendingGstLines is null?"Draft · amounts become authoritative at finalization":"Draft estimates · GST jurisdiction pending"
            : SalesInvoiceEditor.Summary(snapshot.Amounts,form,entry:draftView), TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16,6,16,8), FontWeight = FontWeights.SemiBold });
        return dialog;

        string? Entry(string key,string? value)=>Show(key) && form.ShowOutput(key,true)?value:null;
        void AddDetail(string label,string? value,double width)
        {
            if(!draftView || !string.IsNullOrWhiteSpace(value)) dialog.Text(label,value,4000,true,panel:details).Width=width;
        }

        Line DraftLine(SalesLineInput line,int index)
        {
            var product=options.Products.FirstOrDefault(x=>x.Id==line.ProductId);
            var variant=(options.Variants??[]).FirstOrDefault(x=>x.Id==line.ProductVariantId);
            var estimate=data.PendingGstLines?.ElementAtOrDefault(index);
            var unit=line.UnitId is null?"Default sales unit":Lookup(options.Units.Where(x=>x.Scope==line.ProductId).ToArray(),line.UnitId);
            return new(index+1,product?.ProductName??product?.Label,product?.Code,variant?.Sku??product?.Sku,product?.Brand,
                product?.Hsn,variant?.Mrp??product?.Mrp,line.Quantity,unit,line.Price,line.DiscountPercent,
                estimate?.Discount??line.DiscountAmount,estimate?.Taxable,product?.GstRate,estimate?.Total,estimate?.Gross,estimate?.Tax);
        }

        void AddParty(int column,string title,string? value)
        {
            var panel=new StackPanel {Margin=new Thickness(0,0,16,0)};
            panel.Children.Add(new TextBlock {Text=title,FontWeight=FontWeights.Bold});
            panel.Children.Add(new TextBlock {Text=value,TextWrapping=TextWrapping.Wrap});
            Grid.SetColumn(panel,column);
            parties.Children.Add(panel);
        }
        void AddColumn(string title,string property,double width)
        {
            var field=property switch
            {
                nameof(Line.Product)=>"ProductName", nameof(Line.Code)=>"ProductCode",
                nameof(Line.Unit)=>"UnitId", nameof(Line.Rate)=>"Price",
                nameof(Line.Discount)=>"DiscountAmount", nameof(Line.Tax)=>"TaxAmount", _=>property
            };
            if((property==nameof(Line.Number) || Show(field)) && form.ShowOutput(field,true) &&
                (!draftView || lines.Any(line=>typeof(Line).GetProperty(property)?.GetValue(line) is { } value &&
                    (value is not string text || !string.IsNullOrWhiteSpace(text)))))
                table.Columns.Add(new DataGridTextColumn { Header=form.Caption(field,title), Binding=new Binding(property),
                    Width=new DataGridLength(width) });
        }
    }

    internal static string Address(CustomerAddressSnapshot address,string? gstin)=>Join(address.RecipientName,address.Organization,
        address.Line1,address.Line2,address.Locality,address.City,address.State,address.PostalCode,
        Caption("GSTIN",gstin),address.Mobile,address.Email);
    private static string? Caption(string label,string? value)=>string.IsNullOrWhiteSpace(value)?null:label+": "+value;
    private static string Join(params string?[] values)=>string.Join(Environment.NewLine,values.Where(x=>!string.IsNullOrWhiteSpace(x)));
    private static string? Lookup(IReadOnlyList<SalesChoice> choices,Guid? id)=>choices.FirstOrDefault(x=>x.Id==id)?.Label;
    private static string WithVariant(ProductInvoiceSnapshot product)=>product.Variant is not {} variant?product.Name:
        $"{product.Name} · {variant.Code} · {string.Join(" / ",variant.Attributes.Select(x=>x.Name))}";

    private sealed record Line(int Number,string? Product,string? Code,string? Sku,string? Brand,string? Hsn,decimal? Mrp,
        decimal Quantity,string? Unit,decimal Rate,decimal DiscountPercent,decimal Discount,decimal? Taxable,decimal? Gst,decimal? Total,
        decimal? Gross,decimal? Tax);
}
