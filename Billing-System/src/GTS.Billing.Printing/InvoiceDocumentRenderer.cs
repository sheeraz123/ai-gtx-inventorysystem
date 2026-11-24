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

public static class InvoiceDocumentRenderer
{
    public static FixedDocument Render(InvoicePrintModel invoice, string? copyLabel = null, bool preview = false) =>
        invoice.Template.Code == InvoiceTemplates.GraftexClassic
            ? new ClassicInvoiceLayout(invoice, copyLabel, preview).Build()
            : new Layout(invoice, copyLabel, preview).Build();

    public static double MarginDip(InvoicePrintModel invoice, int? configured) =>
        (configured ?? (invoice.Template.Code == InvoiceTemplates.GraftexClassic ? 7 : 12)) * 96 / 25.4;

    private sealed class Layout
    {
        private static readonly Brush Ink=Brushes.Black;
        private static readonly Brush Pale=new SolidColorBrush(Color.FromRgb(236,240,244));
        private static readonly Brush Rule=new SolidColorBrush(Color.FromRgb(166,173,181));
        private static readonly CultureInfo India=CultureInfo.GetCultureInfo("en-IN");
        private readonly InvoicePrintModel invoice;
        private readonly InvoicePresentationOptions? options;
        private readonly InvoicePrintOptions? print;
        private readonly string? copyLabel;
        private bool Modern=>invoice.Template.Code==InvoiceTemplates.Modern;
        private bool Wholesale=>invoice.Template.Code==InvoiceTemplates.Wholesale;
        private bool Retail=>invoice.Template.Code==InvoiceTemplates.Retail;
        private static readonly Brush ModernAccent=Brushes.MidnightBlue;
        private readonly List<FixedPage> pages=[];
        private readonly double width,height,left,top,right,bottom,contentWidth,contentHeight,font;
        private StackPanel current=null!;
        private double used;
        private Func<FrameworkElement>? repeatingHeader;

        private readonly bool preview;
        private bool Output(string key)=>options?.SalesForm?.ShowOutput(key,preview)??true;

        internal Layout(InvoicePrintModel invoice, string? copyLabel, bool preview)
        {
            this.invoice=invoice;
            this.copyLabel=copyLabel;
            options=invoice.Seller.Presentation.Options;
            this.preview=preview;
            print=options?.Printing;
            width=Dip(invoice.Template.WidthMm);height=Dip(invoice.Template.HeightMm);
            left=Dip(print?.LeftMm??12);top=Dip(print?.TopMm??12);
            right=Dip(print?.RightMm??12);bottom=Dip(print?.BottomMm??12);
            contentWidth=width-left-right;
            contentHeight=height-top-bottom-22;
            font=invoice.Template.FontSize;
        }

        internal FixedDocument Build()
        {
            StartPage(false);
            Add(Branding());
            if(Modern) Add(new Border {Height=4,Background=ModernAccent,Margin=new Thickness(0,0,0,7)});
            AddText(Label("Title","Tax Invoice"),size:font+(Retail?4:7),bold:true);
            var presentation=invoice.Seller.Presentation;
            AddText(string.IsNullOrWhiteSpace(options?.CompanyName)?invoice.Seller.Name:options.CompanyName,size:font+3,bold:true);
            if(!string.IsNullOrWhiteSpace(invoice.Seller.DisplayName) &&
                invoice.Seller.DisplayName!=(options?.CompanyName??invoice.Seller.Name))
                AddText(invoice.Seller.DisplayName);
            AddText(options?.Tagline);
            AddText(presentation.Header);
            if(!NewLayout || Show("CompanyAddress")) AddText(invoice.Seller.Address);
            AddText(Join($"{Label("Gstin","GSTIN")} {invoice.Seller.Gstin}",
                $"State code {invoice.Seller.StateCode}",NewLayout && Show("Iec")?Value("IEC",invoice.Seller.Iec):null));
            if(Show("CompanyContact")) AddText(Join(!NewLayout || Show("CompanyPhone")?invoice.Seller.Phone:null,
                !NewLayout || Show("CompanyEmail")?invoice.Seller.Email:null));
            AddText($"{Label("InvoiceNumber","Invoice number")} {invoice.Number} · {Label("InvoiceDate","Invoice date")} {Date(invoice.Header.Date)}",bold:true);
            AddText(Join(Value("Financial year",invoice.FinancialYearCode),Value("Series",invoice.DocumentSeriesCode),
                Value(Label("PlaceOfSupply","Place of supply"),invoice.Header.PlaceOfSupply),copyLabel??print?.CopyLabel));
            if(print?.PrintTimestamp==true) AddText("Finalized " + invoice.Customer.CapturedUtc.ToString("u",CultureInfo.InvariantCulture));
            AddText(Join(Value("Customer PO",invoice.Header.PurchaseOrder),invoice.Header.PurchaseOrderDate is DateOnly po?Date(po):null,
                Value("Reference",Output("Reference")?invoice.Header.Reference:null)));
            if(print?.HeaderSpacingMm is >0) Add(new Border {Height=Dip(print.HeaderSpacingMm)});
            if(Modern)
            {
                Add(Cells([Label("BillTo","Bill To"),Label("ShipTo","Ship To")],[.50,.50],true));
                Add(Cells([string.Join(Environment.NewLine,new[]{invoice.Customer.LegalName,Address(invoice.Customer.BillTo),
                    Value(Label("Gstin","GSTIN"),invoice.Customer.Gstin),Show("CustomerContact")?
                        Join(invoice.Customer.ContactNumber,invoice.Customer.ContactEmail):null}.Where(x=>!string.IsNullOrWhiteSpace(x))),
                    Address(invoice.Customer.ShipTo)],[.50,.50]));
            }
            else
            {
                AddText(Label("BillTo","Bill To")+" — "+invoice.Customer.LegalName,bold:true);
                AddText(Address(invoice.Customer.BillTo));
                AddText(Value(Label("Gstin","GSTIN"),invoice.Customer.Gstin));
                if(Show("CustomerContact")) AddText(Join(invoice.Customer.ContactNumber,invoice.Customer.ContactEmail));
                AddText(Label("ShipTo","Ship To"),bold:true);
                AddText(Address(invoice.Customer.ShipTo));
            }

            var discounts=invoice.Lines.Any(x=>x.Amounts.Discount!=0) || Show("OptionalDiscount");
            double[] widths=Wholesale?[.09,.09,.18,.09,.07,.08,.07,.07,.07,.07,.07,.05]:
                Retail?[.28,.11,.09,.10,.10,.11,.07,.14]:
                discounts?[.32,.10,.12,.12,.10,.12,.12]:[.35,.11,.14,.13,.14,.13];
            string[] Headings()=>Wholesale?
                ["Code","SKU","Product","Brand","HSN/SAC","MRP","Qty / unit","Rate","Discount","Taxable","GST","Total"]:
                Retail?["Product","HSN/SAC","Qty","Rate","Discount","Taxable","GST","Total"]:discounts?
                [Label("Product","Product"),Label("HsnSac","HSN/SAC"),Label("Quantity","Quantity")+" / "+Label("Unit","Unit"),
                    Label("Rate","Rate"),Label("Discount","Discount"),Label("TaxableValue","Taxable"),"Line total"]:
                [Label("Product","Product"),Label("HsnSac","HSN/SAC"),Label("Quantity","Quantity")+" / "+Label("Unit","Unit"),
                    Label("Rate","Rate"),Label("TaxableValue","Taxable"),"Line total"];
            Func<FrameworkElement> lineHeader=()=>Cells(Headings(),widths,true);
            var first=true;
            foreach(var line in invoice.Lines)
            {
                var product=line.Product;
                var identity=Join($"{line.Number}. {product.Name}",Output("ProductCode")?product.Code:null,Output("Brand")?product.Brand:null);
                var rate=Number(line.Input.Price,options?.RateDecimals??2);
                var qty=Number(line.Input.Quantity,options?.QuantityDecimals??2)+" "+product.Unit;
                var value=line.Amounts;
                string[] cells=Wholesale?
                    [Output("ProductCode")?product.Code:"",Output("Sku")?product.Sku??"":"",product.Name,Output("Brand")?product.Brand??"":"",line.Tax.Rate.Code,
                        Output("Mrp") && product.Mrp is decimal mrp?Amount(mrp):"",qty,rate,Amount(value.Discount),Amount(value.Taxable),Amount(value.Tax),Amount(value.Total)]:
                    Retail?[product.Name,line.Tax.Rate.Code,qty,rate,Amount(value.Discount),Amount(value.Taxable),Amount(value.Tax),Amount(value.Total)]:discounts?
                    new[]{identity,line.Tax.Rate.Code,qty,rate,Amount(value.Discount),Amount(value.Taxable),Amount(value.Total)}:
                    new[]{identity,line.Tax.Rate.Code,qty,rate,Amount(value.Taxable),Amount(value.Total)};
                if(first)
                {
                    var heading=lineHeader();
                    var row=Cells(cells,widths);
                    if(used+Measure(heading)+Measure(row)>contentHeight) StartPage(true);
                    Add(heading);
                    repeatingHeader=lineHeader;
                    Add(row);
                    first=false;
                }
                else Add(Cells(cells,widths));
                if(Show("ProductDescription")) AddText(product.Description, size:font-1);
                if(invoice.Template.Detailed && Show("SkuBarcode"))
                    AddText(Wholesale?Value("Barcode",product.Barcode):Join(Value("SKU",product.Sku),Value("Barcode",product.Barcode)),size:font-1);
                if(invoice.Template.Detailed || Show("OptionalTaxColumns"))
                    AddText(TaxLabel(line.Tax),size:font-1);
            }
            repeatingHeader=null;
            RenderSummary();
            var document=new FixedDocument();
            document.DocumentPaginator.PageSize=new Size(width,height);
            for(var index=0;index<pages.Count;index++)
            {
                var page=pages[index];
                if(print?.PageNumbers!=false)
                {
                    var footer=Text($"{index+1} / {pages.Count}",font-1);
                    footer.Width=contentWidth;footer.TextAlignment=TextAlignment.Right;
                    FixedPage.SetTop(footer,height-bottom-16);FixedPage.SetLeft(footer,left);page.Children.Add(footer);
                }
                var content=new PageContent();((IAddChild)content).AddChild(page);document.Pages.Add(content);
            }
            return document;
        }

        private void RenderSummary()
        {
            var hsnWidths=new[]{.28,.32,.20,.20};
            Func<FrameworkElement> hsnHeader=()=>Cells([Label("HsnSac","HSN/SAC")+" · Treatment","Rates",
                Label("TaxableValue","Taxable amount"),Label("TotalTax","Total tax")],hsnWidths,true);
            AddText(Label("TaxSummary","Tax Summary"),bold:true,size:font+2);
            var groups=invoice.Lines.GroupBy(x=>new {x.Tax.Rate.Type,x.Tax.Rate.Code,x.Tax.Rate.Treatment,
                x.Tax.CgstPercentage,x.Tax.SgstPercentage,x.Tax.IgstPercentage,x.Tax.UtgstPercentage,x.Tax.CessPercentage});
            var first=true;
            foreach(var group in groups)
            {
                var key=group.Key;
                var values=group.Select(x=>x.Amounts).ToArray();
                string? Component(string label,decimal percent,decimal sum)=>percent==0 && sum==0?null:
                    $"{Label(label,label)} {percent:G29}% · {Amount(sum)}";
                var taxBreakdown=string.Join(Environment.NewLine,new[]{
                    Component("CGST",key.CgstPercentage,values.Sum(x=>x.Cgst)),
                    Component("SGST",key.SgstPercentage,values.Sum(x=>x.Sgst)),
                    Component("IGST",key.IgstPercentage,values.Sum(x=>x.Igst)),
                    Component("UTGST",key.UtgstPercentage,values.Sum(x=>x.Utgst)),
                    Component("Cess",key.CessPercentage,values.Sum(x=>x.Cess))
                }.Where(x=>x is not null));
                var row=Cells([$"{key.Type} {key.Code} · {Treatment(key.Treatment)}",
                    taxBreakdown,Amount(values.Sum(x=>x.Taxable)),Amount(values.Sum(x=>x.Tax))],hsnWidths);
                if(first)
                {
                    var header=hsnHeader();
                    if(used+Measure(header)+Measure(row)>contentHeight) StartPage(true);
                    Add(header);repeatingHeader=hsnHeader;first=false;
                }
                Add(row);
            }
            repeatingHeader=null;

            var totals=invoice.Totals;
            var totalsBlock=new StackPanel {Margin=new Thickness(0,6,0,7)};
            void Total(string label,decimal value) => totalsBlock.Children.Add(Cells([label,Amount(value)],[.58,.42]));
            Total("Subtotal",totals.Subtotal);
            if(totals.Discount!=0) Total(Label("Discount","Discount"),totals.Discount);
            Total(Label("TaxableValue","Taxable Amount"),totals.Taxable);
            if(totals.Cgst!=0) Total(Label("CGST","CGST"),totals.Cgst);
            if(totals.Sgst!=0) Total(Label("SGST","SGST"),totals.Sgst);
            if(totals.Igst!=0) Total(Label("IGST","IGST"),totals.Igst);
            if(totals.Utgst!=0) Total(Label("UTGST","UTGST"),totals.Utgst);
            if(totals.Cess!=0) Total(Label("Cess","Cess"),totals.Cess);
            Total(Label("TotalTax","Total Tax"),totals.Tax);
            if(totals.RoundOff!=0) Total(Label("RoundOff","Round off"),totals.RoundOff);
            var currency=options?.CurrencySymbol;
            totalsBlock.Children.Add(Cells([Label("Total","Grand Total"),
                Join(currency=="No symbol"?null:currency??"INR",Amount(totals.Total))],[.58,.42],true));
            Add(totalsBlock);
            AddText("Amount in words: Rupees "+IndianAmountWords.Format(totals.Total),bold:true);
            if(Output("Terms")) AddText(Value(Label("PaymentTerms","Payment terms"),invoice.Header.Terms));
            if(invoice.Header.DueDate is DateOnly due) AddText("Due date — "+Date(due));
            if(invoice.Seller.Presentation.Bank is { } bank)
            {
                AddText(Label("BankDetails","Bank details"),bold:true);
                AddText(Join(bank.BankName,bank.AccountHolder,Value("Account",bank.AccountNumber),Value("IFSC",bank.Ifsc),bank.Branch,bank.Address));
                AddText(Value(Label("Upi","UPI"),bank.Upi));
                AddText(bank.Instructions);
            }
            if(!NewLayout || Show("Terms")) AddText(Value(Label("Terms","Terms and conditions"),invoice.Seller.Presentation.Terms));
            if(NewLayout && Show("Transport") && invoice.Header.Transport is { } shipment)
                AddText(Join(Value("Transport",shipment.Carrier),Value("Vehicle",shipment.Vehicle),Value("G.R.",shipment.GoodsReceipt),
                    Value("Weight",shipment.Weight),Value("Order through",shipment.OrderThrough),Value("Prepared by",shipment.PreparedBy),
                    Value("Private mark",shipment.PrivateMark),Value("Document",shipment.Document)));
            if(Output("Notes")) AddText(invoice.Header.Notes);
            AddText(options?.Remarks);
            if(Show("Signatory")) AddText(Value(Label("AuthorizedSignatory","Authorized signatory"),invoice.Seller.Presentation.Signatory));
            if(print?.FooterSpacingMm is >0) Add(new Border {Height=Dip(print.FooterSpacingMm)});
            if(Show("FooterNotes"))
            {
                AddText(options?.FooterNotes);
                AddText(invoice.Seller.Presentation.Footer);
            }
            if(options?.Captions.ContainsKey("Invoice.Label.Footer")==true) AddText(Label("Footer","Thank you."));
        }

        private void StartPage(bool continuation)
        {
            var page=new FixedPage {Width=width,Height=height,Background=Brushes.White};
            pages.Add(page);
            current=new StackPanel {Width=contentWidth};
            FixedPage.SetLeft(current,left);FixedPage.SetTop(current,top);page.Children.Add(current);
            used=0;
            if(continuation)
            {
                AddRaw(Text($"{Label("Title","Tax Invoice")} · {invoice.Number} · {Date(invoice.Header.Date)}",font,true));
                if(repeatingHeader is not null) AddRaw(repeatingHeader());
            }
        }

        private void Add(FrameworkElement element)
        {
            if(used+Measure(element)>contentHeight) StartPage(true);
            if(used+Measure(element)>contentHeight) throw new InvalidOperationException("A print section exceeds the selected page size.");
            AddRaw(element);
        }

        private void AddRaw(FrameworkElement element)
        {
            used+=Measure(element);
            current.Children.Add(element);
        }

        private double Measure(FrameworkElement element)
        {
            element.Measure(new Size(contentWidth,double.PositiveInfinity));
            return Math.Ceiling(element.DesiredSize.Height);
        }

        private void AddText(string? value,double? size=null,bool bold=false)
        {
            if(string.IsNullOrWhiteSpace(value)) return;
            var parsed=new StringInfo(value);
            AddRange(parsed,0,parsed.LengthInTextElements);
            void AddRange(StringInfo text,int start,int count)
            {
                var block=Text(text.SubstringByTextElements(start,count),size??font,bold);
                if(Measure(block)<=contentHeight-100 || count<=1) {Add(block);return;}
                var middle=count/2;
                AddRange(text,start,middle);AddRange(text,start+middle,count-middle);
            }
        }

        private static TextBlock Text(string value,double size,bool bold=false)=>new()
        {
            Text=value,FontFamily=new FontFamily("Segoe UI"),FontSize=size,Foreground=Ink,
            FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,Padding=new Thickness(0,2,0,2)
        };

        private FrameworkElement Cells(IReadOnlyList<string> values,double[] widths,bool header=false)
        {
            var grid=new Grid {Background=header?(Modern?ModernAccent:Pale):Brushes.White};
            foreach(var ratio in widths) grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(ratio,GridUnitType.Star)});
            for(var i=0;i<values.Count;i++)
            {
                var label=Text(values[i],font-1,header);
                if(header && Modern) label.Foreground=Brushes.White;
                var cell=new Border {Padding=Retail?new Thickness(2,0,2,0):new Thickness(4,2,4,2),BorderBrush=Rule,
                    BorderThickness=new Thickness(i==0?1:0,header?1:0,1,1),Child=label};
                Grid.SetColumn(cell,i);grid.Children.Add(cell);
            }
            return grid;
        }

        private FrameworkElement Branding()
        {
            var grid=new Grid {Margin=new Thickness(0,0,0,5)};
            grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());
            var entries=new[]{invoice.Seller.Presentation.Primary,invoice.Seller.Presentation.Secondary};
            for(var i=0;i<entries.Length;i++)
            {
                if(entries[i] is not {Content.IsDefaultOrEmpty:false} logo) continue;
                try
                {
                    using var bytes=new MemoryStream(logo.Content.ToArray(),writable:false);
                    var bitmap=new BitmapImage();
                    bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=bytes;bitmap.EndInit();bitmap.Freeze();
                    var image=new Image {Source=bitmap,Stretch=Stretch.Uniform,MaxHeight=65,MaxWidth=contentWidth*.35,
                        HorizontalAlignment=i==0?HorizontalAlignment.Left:HorizontalAlignment.Right};
                    Grid.SetColumn(image,i);grid.Children.Add(image);
                }
                catch(Exception error) when(error is IOException or NotSupportedException or ArgumentException or FormatException) { }
            }
            return grid;
        }

        private string Label(string key,string fallback)=>options?.Captions.GetValueOrDefault("Invoice.Label."+key)??fallback;
        private bool NewLayout=>Modern || Wholesale || Retail;
        private bool Show(string section)=>options?.ShowSection("Invoice.Optional."+section,false)??true;
        private string Date(DateOnly date)=>date.ToString(options?.DateFormat??"dd MMM yyyy",India);
        private string Amount(decimal value)=>Number(value,options?.AmountDecimals??2);
        private string Number(decimal value,int places)
        {
            // Display precision is a minimum. Preserve all significant stored digits for quantity and unit price.
            var actual=0;
            while(actual<28 && decimal.Round(value,actual)!=value) actual++;
            return value.ToString((options?.GroupThousands==false?"F":"N")+Math.Max(places,actual),India);
        }
        private string Rates(AppliedTaxSnapshot tax)=>Join(
            Rate("CGST",tax.CgstPercentage),Rate("SGST",tax.SgstPercentage),Rate("IGST",tax.IgstPercentage),
            Rate("UTGST",tax.UtgstPercentage),Rate("Cess",tax.CessPercentage));
        private string? Rate(string key,decimal value)=>value==0?null:$"{Label(key,key)} {value:G29}%";
        private string TaxLabel(AppliedTaxSnapshot tax)=>Join(Treatment(tax.Rate.Treatment),Rates(tax));
        private static string Treatment(GTS.Billing.Domain.TaxTreatment treatment)=>treatment switch
        {
            GTS.Billing.Domain.TaxTreatment.NilRated=>"Nil-rated",GTS.Billing.Domain.TaxTreatment.ZeroRated=>"Zero-rated",
            GTS.Billing.Domain.TaxTreatment.NonGst=>"Non-GST",_=>treatment.ToString()
        };
        private static string Address(CustomerAddressSnapshot address)=>Join(address.RecipientName,address.Organization,address.Line1,
            address.Line2,address.Landmark,address.Locality,address.City,address.State,address.StateCode,address.PostalCode,address.Country);
        private static string? Value(string label,string? value)=>string.IsNullOrWhiteSpace(value)?null:label+" — "+value;
        private static string Join(params string?[] values)=>string.Join(" · ",values.Where(x=>!string.IsNullOrWhiteSpace(x)));
        private static double Dip(double millimetres)=>millimetres*96/25.4;
    }
}
