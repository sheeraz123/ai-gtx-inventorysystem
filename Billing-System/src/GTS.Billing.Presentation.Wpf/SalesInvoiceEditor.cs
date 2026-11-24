using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using GTS.Billing.Application;
using GTS.Billing.Domain;

namespace GTS.Billing.Presentation.Wpf;

internal static class SalesInvoiceEditor
{
    private static string Label(string key,string fallback)=>SettingsDisplay.Text("Sales."+key,fallback);
    private static DateOnly? Date(DatePicker picker)=>picker.SelectedDate is DateTime value?DateOnly.FromDateTime(value):null;
    private static Guid? Id(ComboBox selector)=>(selector.SelectedItem as SalesChoice)?.Id;
    private static string Decimal(decimal value)=>value.ToString("G29",CultureInfo.CurrentCulture);

    internal static void StyleCard(Border card)
    {
        if(card.Child is not StackPanel body || body.Children[0] is not TextBlock title) return;
        body.Children.RemoveAt(0);
        title.Margin=new Thickness(0);
        var heading=new Border {Child=title,Padding=new Thickness(10,6,10,6),
            Margin=new Thickness(0,0,0,10),CornerRadius=new CornerRadius(4)};
        heading.SetResourceReference(Border.BackgroundProperty,SystemParameters.HighContrast
            ?SystemColors.ControlBrushKey:"Palette.InvoiceHeading");
        if(SystemParameters.HighContrast)
            title.SetResourceReference(TextBlock.ForegroundProperty,SystemColors.ControlTextBrushKey);
        body.Children.Insert(0,heading);
    }

    internal static void Configure(MeasureEditorDialog dialog,SalesResult data,SalesOptions options,string companyName,
        Func<SalesAction,SalesDraft,IReadOnlyList<SalesProductCompletion>,CancellationToken,Task<SalesResult>> send,
        Action<SalesRow>? finalize,Func<Guid,CancellationToken,Task<SalesResult>> loadAddresses,
        Func<Guid,long,CustomerAddressDraft,Guid?,CancellationToken,Task<CustomerResult>> saveAddress,bool canEditAddress)
    {
        var draft=data.Draft!;
        var form=options.Form??SalesFormConfiguration.Empty;
        void Apply(string key,Control control)=>dialog.ConfigureField(control,form.Field(key),form.Rules.ContainsKey(key));
        FrameworkElement Card(string key,FrameworkElement body)
        {
            var card=(Border)dialog.AddConfiguredCard(form.Field(key),body);
            StyleCard(card);
            return card;
        }
        if(!SystemParameters.HighContrast)
            dialog.SetResourceReference(Window.BackgroundProperty,"Palette.Canvas");
        var entryLayout=options.EntryLayout=="Company default" && options.GraftexEntry?"Compact Full-Width":options.EntryLayout;
        var entry=draft.EntryDetails??new SalesEntryDetails();
        var newInvoice=data.Row is null;
        string? Initial(string key,string? original)=>newInvoice && form.Field(key).Default.Length>0?form.Field(key).Default:original;
        dialog.Width=Math.Min(1450,SystemParameters.WorkArea.Width);
        dialog.Height=Math.Min(880,SystemParameters.WorkArea.Height);
        var header=new WrapPanel();
        var headerCard=Card("InvoiceDetails",header);
        var companyInput=dialog.Text("Company",companyName,150,true,panel:header); companyInput.Width=210; Apply("Company",companyInput);
        var statusInput=dialog.Text("Invoice type / status","Tax Invoice — Draft",60,true,panel:header); statusInput.Width=150; Apply("InvoiceNumber",statusInput);
        var date=dialog.Date(Label("Date","Invoice date")+" *",draft.Date,panel:header);
        var saleTypes=new[]{"Local Sale","Inter-State Sale"};
        var savedIntent=entry.Intent??(draft.Jurisdiction switch
        {
            GstJurisdiction.IntraState=>SalesIntent.Local,GstJurisdiction.Interstate=>SalesIntent.Interstate,_=>(SalesIntent?)null
        });
        var saleType=dialog.Choice<string>("Sale type *",saleTypes,savedIntent switch
        {
            SalesIntent.Local=>saleTypes[0],SalesIntent.Interstate=>saleTypes[1],_=>null
        },panel:header);
        Apply("SaleIntent",saleType);
        SalesIntent? CurrentSaleType()=>saleType.SelectedIndex switch {0=>SalesIntent.Local,1=>SalesIntent.Interstate,_=>null};
        var payment=dialog.Choice("Payment mode",new[]{"","Cash","Credit"},entry.PaymentMode,panel:header);
        Apply("PaymentMode",payment);
        if(!options.GraftexEntry && !form.Rules.ContainsKey("PaymentMode")) dialog.ShowConfiguredField(payment,false);
        var isNew=data.Row is null;
        bool MatchesDate(SalesChoice value)=>Date(date) is { } day && SalesOptions.IncludesDate(value,day);
        var availableYears=options.Years.Where(x=>x.Active || x.Id==draft.YearId).ToArray();
        var year=Choice("Financial year *",availableYears,draft.YearId??(isNew?options.DefaultYear(draft.Date)?.Id:null),header);
        var registration=Choice("Supplier GST registration *",options.Registrations.Where(x=>x.Active || x.Id==draft.RegistrationId),
            draft.RegistrationId??(isNew?options.DefaultRegistration()?.Id:null),header);
        var series=Choice("Document series *",[],null,header);
        var nextNumber=dialog.Text("Bill no. (preview)","",100,true,panel:header);
        nextNumber.FontWeight=FontWeights.Bold;
        nextNumber.MinWidth=135;
        if(!SystemParameters.HighContrast)
        {
            nextNumber.SetResourceReference(Control.BackgroundProperty,"Palette.InvoiceHeading");
            nextNumber.SetResourceReference(Control.BorderBrushProperty,"Palette.InvoiceAccent");
        }
        Apply("NextNumber",nextNumber);
        if(!options.GraftexEntry && !form.Rules.ContainsKey("NextNumber")) dialog.ShowConfiguredField(nextNumber,false);
        void RefreshNumber()=>nextNumber.Text=year.SelectedItem is SalesChoice selected && MatchesDate(selected) &&
            series.SelectedItem is SalesChoice {Active:true,NextNumberPreview:{ } number}?number:"";
        void ReloadSeries(Guid? preserve=null)
        {
            var current=preserve??Id(series);
            var matching=options.Series.Where(x=>x.Scope==Id(year) && x.SecondaryScope==Id(registration) &&
                (x.Active || !isNew && x.Id==draft.SeriesId)).ToArray();
            SearchableComboBox.SetSource(series,matching);
            series.SelectedItem=matching.FirstOrDefault(x=>x.Id==current)??(isNew?options.DefaultSeries(Id(year),Id(registration)):null);
            dialog.SetFieldWarning(series,!matching.Any(x=>x.Active)?"No active series for this year and registration.":
                series.SelectedItem is null?"Choose a document series. No unique default is configured.":null);
            dialog.SetFieldWarning(year,year.SelectedItem is SalesChoice choice && MatchesDate(choice)?null:
                availableYears.Any(MatchesDate)?"Select an open year that includes the invoice date.":"No open financial year includes the invoice date.");
            RefreshNumber();
        }
        Apply("Date",date);Apply("YearId",year);Apply("RegistrationId",registration);Apply("SeriesId",series);
        MeasureEditorDialog.OrderConfigured(header);
        ReloadSeries(draft.SeriesId);
        year.SelectionChanged+=(_,_)=>ReloadSeries();
        registration.SelectionChanged+=(_,_)=>ReloadSeries();
        series.SelectionChanged+=(_,_)=>
        {
            if(series.SelectedItem is not null) dialog.SetFieldWarning(series,null);
            RefreshNumber();
        };
        date.SelectedDateChanged+=(_,_)=>ReloadSeries();
        var customerPanel=new WrapPanel();
        var customerCard=Card("CustomerDetails",customerPanel);
        var customer=Choice("Customer *",options.Customers,draft.CustomerId,customerPanel);
        Apply("CustomerId",customer);
        var po=dialog.Text(form.Field("PurchaseOrder").Label,Initial("PurchaseOrder",draft.PurchaseOrder),100,panel:customerPanel);
        Apply("PurchaseOrder",po);
        void UpdatePurchaseOrderVisibility()=>dialog.ShowConfiguredField(po,form.Field("PurchaseOrder").Visible ||
            customer.SelectedItem is SalesChoice {PurchaseOrderRequired:true});
        customer.SelectionChanged+=(_,_)=>UpdatePurchaseOrderVisibility();
        UpdatePurchaseOrderVisibility();
        var addresses=new SalesInvoiceAddressCards(dialog,customer,draft.BillingAddressId,draft.ShippingAddressId,
            loadAddresses,saveAddress,canEditAddress,form);
        var notesFields=new WrapPanel();
        var rows=new StackPanel();
        var editors=new List<LineControls>();
        var preview=new MasterSearch();
        var closed=false;
        var summary=new TextBlock {Text="Complete the draft to calculate totals.",TextWrapping=TextWrapping.Wrap,
            HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(16,7,16,7),FontWeight=FontWeights.SemiBold};
        var summaryContent=new StackPanel();
        if(form.Rules.TryGetValue("Summary",out var summaryRule))
            summaryContent.Children.Add(new TextBlock {Text=summaryRule.Label,FontWeight=FontWeights.Bold,
                HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(16,5,16,0)});
        summaryContent.Children.Add(summary);
        var summaryCard=new Border {Child=summaryContent,CornerRadius=new CornerRadius(7),BorderThickness=new Thickness(1),
            Margin=new Thickness(16,4,16,4)};
        summaryCard.SetResourceReference(Border.BorderBrushProperty,"Palette.GridLine");
        if(!SystemParameters.HighContrast)
        {
            summaryCard.SetResourceReference(Border.BackgroundProperty,"Palette.InvoiceAccent");
            TextElement.SetForeground(summaryContent,System.Windows.Media.Brushes.White);
        }
        dialog.DockSummary(summaryCard);
        var lineArea=new StackPanel();
        var lineButtons=new WrapPanel();
        var add=new Button {Content="Add product",Margin=new Thickness(0,7,8,7),Padding=new Thickness(10,4,10,4)};
        BillingIcon.Set(add,"Add product","\uE710");
        dialog.AddField("",add,lineButtons);
        lineArea.Children.Add(lineButtons);
        lineArea.Children.Add(new ScrollViewer {Content=rows,HorizontalScrollBarVisibility=options.GraftexEntry?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=430});
        var productCard=Card("Products",lineArea);
        Card("AdditionalDetails",entryLayout=="Modern Compact Billing"?
            new Expander {Header="Order and transport",Content=notesFields}:notesFields);
        TextBox Detail(string key,string? value,int max)
        {
            var field=dialog.Text(form.Field(key).Label,Initial(key,value),max,panel:notesFields);
            Apply(key,field);
            return field;
        }
        var poDate=dialog.Date("Customer PO date",draft.PurchaseOrderDate,panel:notesFields);
        Apply("PurchaseOrderDate",poDate);
        var reference=Detail("Reference",draft.Reference,100);
        var due=dialog.Date("Due date",draft.DueDate,panel:notesFields);
        Apply("DueDate",due);
        var terms=Detail("Terms",draft.Terms,500);
        var notes=Detail("Notes",draft.Notes,800);
        var internalRemarks=Detail("InternalRemarks",draft.InternalRemarks,800);
        var transport=draft.Transport;
        var goodsReceipt=Detail("GoodsReceipt",transport?.GoodsReceipt,120);
        var vehicle=Detail("Vehicle",transport?.Vehicle,120);
        var weight=Detail("Weight",transport?.Weight,120);
        var carrier=Detail("Carrier",transport?.Carrier,120);
        var orderThrough=dialog.Text(form.Field("OrderThrough").Label,Initial("OrderThrough",transport?.OrderThrough),120,
            panel:options.GraftexEntry?customerPanel:notesFields);
        Apply("OrderThrough",orderThrough);
        MeasureEditorDialog.OrderConfigured(customerPanel);
        var privateMark=Detail("PrivateMark",transport?.PrivateMark,120);
        var document=Detail("Document",transport?.Document,120);
        TextBox OptionalLegacy(string key,string? value)
        {
            var control=Detail(key,value,120);
            if(!options.GraftexEntry && !form.Rules.ContainsKey(key)) dialog.ShowConfiguredField(control,false);
            return control;
        }
        var distance=OptionalLegacy("Distance",transport?.Distance);
        var transportId=OptionalLegacy("TransportId",transport?.TransportId);
        var tcs=OptionalLegacy("Tcs",entry.Tcs);
        var reversal=OptionalLegacy("Reversal",entry.Reversal);
        var ern=OptionalLegacy("Ern",entry.Ern);
        var preparedBy=dialog.Text("Prepared by",options.PreparedBy,120,locked:true,panel:options.GraftexEntry?header:notesFields);
        Apply("PreparedBy",preparedBy);
        MeasureEditorDialog.OrderConfigured(notesFields);
        dialog.OrderConfiguredSections();
        dialog.ApplyInvoiceEntryLayout(entryLayout,headerCard,productCard,options.GraftexEntry?customerCard:null,addresses.Section);
        void Add(SalesLineInput? value,int index)
        {
            Panel box=options.GraftexEntry?new StackPanel {Orientation=Orientation.Horizontal}:new WrapPanel();
            box.Margin=new Thickness(0,4,0,4);
            var requiredOptions=new WrapPanel();
            var lineCard=new Border {Child=options.GraftexEntry?
                new StackPanel {Children={box,requiredOptions}}:box,BorderThickness=new Thickness(0,index==0?0:1,0,0)};
            lineCard.SetResourceReference(Border.BorderBrushProperty,"Palette.GridLine");
            rows.Children.Add(lineCard);
            var number=new TextBlock {Text=(index+1).ToString(CultureInfo.CurrentCulture),Width=26,VerticalAlignment=VerticalAlignment.Center};
            box.Children.Add(number);
            var product=Choice("Product search",options.Products,value?.ProductId,box);
            product.Width=230;
            var variant=dialog.Choice<SalesVariantChoice>("Variant",[],null,nameof(SalesVariantChoice.Label),
                options.GraftexEntry?requiredOptions:box,searchable:true);
            variant.Width=190;
            void LoadVariants(Guid? keep=null,bool allowDefault=true)
            {
                var matches=(options.Variants??[]).Where(x=>x.ProductId==Id(product) && (x.Active || x.Id==keep)).ToArray();
                SearchableComboBox.SetSource(variant,matches);
                variant.SelectedItem=matches.FirstOrDefault(x=>x.Id==keep)??
                    (allowDefault && matches.Length==1 && matches[0].Active?matches[0]:null);
                if(variant.Parent is Border frame && frame.Parent is FrameworkElement field)
                    field.Visibility=matches.Length==0?Visibility.Collapsed:Visibility.Visible;
            }
            LoadVariants(value?.ProductVariantId,allowDefault:value is null);
            TextBox Display(string name,int width)
            {
                var field=dialog.Text(name,"",200,true,panel:box);
                field.Width=width;
                field.IsTabStop=false;
                return field;
            }
            var code=Display("Product code",85);
            var sku=Display("Article / SKU",95);
            var name=Display("Product name",155);
            var brand=Display("Brand",90);
            var hsn=Display("HSN / SAC",80);
            var mrp=dialog.Text("MRP","",24,panel:box);
            mrp.Width=100;
            void FillProduct()
            {
                var selected=product.SelectedItem as SalesChoice;
                var selectedVariant=variant.SelectedItem as SalesVariantChoice;
                code.Text=selected?.Code??"";
                sku.Text=selectedVariant?.Sku??selected?.Sku??"";
                name.Text=selected?.ProductName??"";
                brand.Text=selected?.Brand??"";
                mrp.IsReadOnly=selected is null || selected.Mrp.HasValue || selectedVariant?.Mrp is not null || !options.CanFillMrp;
                mrp.IsTabStop=!mrp.IsReadOnly;
                mrp.Text=(selectedVariant?.Mrp??selected?.Mrp) is decimal amount?Decimal(amount):"";
                ShowMrpWarning();
            }
            FillProduct();
            SalesChoice[] Units(Guid? productId)
            {
                var units=options.Units.Where(x=>x.Scope==productId).ToList();

                return [new(Guid.Empty,"Default sales unit"),..units];
            }
            var unit=Choice(Label("Unit","Unit"),Units(value?.ProductId),value?.UnitId??Guid.Empty,box);
            unit.Width=130;

            var quantity=dialog.Text(Label("Quantity","Quantity"),Decimal(value?.Quantity??1),20,panel:box);
            var price=dialog.Text(Label("Rate","Unit price"),value is null?"":Decimal(value.Price),20,panel:box);
            var percent=dialog.Text(Label("Discount","Discount")+" %",Decimal(value?.DiscountPercent??0),12,panel:box);
            var fixedDiscount=dialog.Text(Label("Discount","Discount")+" amount",Decimal(value?.DiscountAmount??0),16,panel:box);
            var inclusive=dialog.Choice("Price includes GST",new[]{"No","Yes"},value?.Inclusive==true?"Yes":"No",panel:box);
            var taxable=Display("Taxable",95);
            var gross=Display("Gross amount",95);
            var appliedDiscount=Display("Applied discount",105);
            var taxAmount=Display("GST amount",95);
            var gst=Display("GST %",85);
            var mapping=dialog.Choice<SalesTaxChoice>("HSN/SAC and GST mapping",[],null,nameof(SalesTaxChoice.Label),
                options.GraftexEntry?requiredOptions:box,searchable:true);
            mapping.Width=210;
            var total=Display("Line total",95);
            var sgstPercent=Display("SGST %",66);
            var sgstAmount=Display("SGST amount",92);
            var cgstPercent=Display("CGST %",66);
            var cgstAmount=Display("CGST amount",92);
            var igstPercent=Display("IGST %",66);
            var igstAmount=Display("IGST amount",92);
            var item=new LineControls(product,unit,quantity,price,percent,fixedDiscount,inclusive,hsn,taxable,gst,total,number,mrp,mapping,
                gross,appliedDiscount,taxAmount,variant,sgstPercent,sgstAmount,cgstPercent,cgstAmount,igstPercent,igstAmount);
            editors.Add(item);
            void ShowMrpWarning()
            {
                var missing=product.SelectedItem is SalesChoice {Mrp:null} &&
                    (variant.SelectedItem as SalesVariantChoice)?.Mrp is null && string.IsNullOrWhiteSpace(mrp.Text);
                dialog.ShowConfiguredField(mrp,form.Field("Mrp").Visible || !mrp.IsReadOnly);
                dialog.SetFieldWarning(mrp,!missing?null:options.CanFillMrp?"MRP is missing. Enter it to update this product on Save.":
                    "MRP is missing. Product pricing permission is required to add it.");
            }
            void FillTax(bool keepSelection)
            {
                var selected=product.SelectedItem as SalesChoice;
                var day=Date(date);
                var previous=keepSelection?(mapping.SelectedItem as SalesTaxChoice)?.AssignmentId:null;
                var choices=(options.TaxChoices??[]).Where(x=>selected is not null && day.HasValue && x.Applies(day.Value) &&
                    x.Type==(selected.ProductKind=="Services"?"SAC":"HSN")).ToArray();
                var candidates=choices.Where(x=>x.Selectable &&
                    (string.IsNullOrWhiteSpace(selected?.Hsn) || selected.Hsn.Trim()==x.Code) &&
                    (selected?.GstRate is null || selected.GstRate==x.Rate)).ToArray();
                var linked=selected?.TaxClassificationId is Guid classification
                    ?choices.Where(x=>x.ClassificationId==classification && x.Context==selected.TaxContext).Take(2).ToArray():[];
                var byHsn=selected?.TaxClassificationId is null && !string.IsNullOrWhiteSpace(selected?.Hsn)
                    ?choices.Where(x=>x.Selectable && x.Code==selected.Hsn.Trim()).Take(2).ToArray():[];
                var canSelect=selected is not null && options.CanLinkTax && selected.TaxClassificationId is null;
                mapping.IsEnabled=canSelect && byHsn.Length!=1 && candidates.Length>0;
                dialog.ShowConfiguredField(mapping,canSelect && byHsn.Length!=1 && form.Field("TaxAssignment").Visible);
                SearchableComboBox.SetSource(mapping,candidates);
                mapping.SelectedItem=mapping.IsEnabled?candidates.FirstOrDefault(x=>x.AssignmentId==previous):null;
                SetTaxFields();
                void SetTaxFields()
                {
                    var chosen=mapping.SelectedItem as SalesTaxChoice??(linked.Length==1?linked[0]:byHsn.Length==1?byHsn[0]:null);
                    var manual=canSelect && chosen is null && byHsn.Length==0 && selected!.ProductKind=="Goods";
                    var keepManual=manual && keepSelection && !gst.IsReadOnly;
                    hsn.IsReadOnly=!manual || !string.IsNullOrWhiteSpace(selected?.Hsn);
                    gst.IsReadOnly=!manual;
                    hsn.IsTabStop=!hsn.IsReadOnly;
                    gst.IsTabStop=manual;
                    hsn.MaxLength=8;
                    dialog.ShowConfiguredField(hsn,manual || form.Field("Hsn").Visible);
                    dialog.ShowConfiguredField(gst,manual || form.Field("Gst").Visible);
                    if(!keepManual || hsn.IsReadOnly)
                        hsn.Text=chosen?.Code??selected?.Hsn??(keepSelection?value?.ManualHsn:null)??"";
                    if(!keepManual)
                        gst.Text=chosen is not null?Decimal(chosen.Rate):
                            (keepSelection?value?.ManualGstRate??selected?.GstRate:selected?.GstRate) is decimal fallback?Decimal(fallback):"";
                    var needsTax=selected is not null && chosen is null;
                    dialog.SetFieldWarning(canSelect?mapping:gst,!needsTax || manual?null:selected!.TaxClassificationId.HasValue
                        ?"The saved tax profile has no unique rate for this date."
                        :!options.CanLinkTax?"GST profile is missing. Product tax assignment permission is required."
                        :candidates.Length==0?"No matching approved HSN/SAC and GST mapping for this date."
                        :"Choose an approved HSN/SAC and GST mapping to link on Save.");
                    if(!canSelect) dialog.SetFieldWarning(mapping,null);
                    else dialog.SetFieldWarning(gst,manual?"Enter the GST rate for this invoice only.":null);
                    dialog.SetFieldWarning(hsn,needsTax && string.IsNullOrWhiteSpace(selected!.Hsn)?"HSN/SAC is missing.":null);
                }
            }
            mrp.TextChanged+=(_,_)=>ShowMrpWarning();
            mapping.SelectionChanged+=(_,_) =>
            {
                if(mapping.SelectedItem is not SalesTaxChoice selected) return;
                hsn.IsReadOnly=gst.IsReadOnly=true;
                hsn.IsTabStop=gst.IsTabStop=false;
                dialog.ShowConfiguredField(gst,form.Field("Gst").Visible);
                hsn.Text=selected.Code;
                gst.Text=Decimal(selected.Rate);
                dialog.SetFieldWarning(mapping,null);
                dialog.SetFieldWarning(gst,null);
                dialog.SetFieldWarning(hsn,null);
            };
            EventHandler<SelectionChangedEventArgs> invoiceDateChanged=(_,_)=>FillTax(true);
            date.SelectedDateChanged+=invoiceDateChanged;
            product.SelectionChanged+=(_,_) =>
            {
                var selected=product.SelectedItem as SalesChoice;
                SearchableComboBox.SetSource(unit,Units(selected?.Id));
                unit.SelectedIndex=0;
                LoadVariants();
                ResetPrice();
                inclusive.SelectedItem=selected?.Inclusive==true?"Yes":"No";
                FillProduct();
                FillTax(false);
            };
            unit.SelectionChanged+=(_,_) =>
            {
                ResetPrice();
            };
            variant.SelectionChanged+=(_,_)=>{ResetPrice();FillProduct();};
            void ResetPrice()
            {
                var rate=(variant.SelectedItem as SalesVariantChoice)?.SalesRate??(product.SelectedItem as SalesChoice)?.Price;
                price.Text=Id(unit)==Guid.Empty && rate is decimal amount?Decimal(amount):"";
            }
            Apply("ProductId",product);Apply("VariantId",variant);Apply("ProductCode",code);Apply("Sku",sku);
            Apply("ProductName",name);Apply("Brand",brand);Apply("Hsn",hsn);Apply("Mrp",mrp);Apply("UnitId",unit);
            Apply("Quantity",quantity);Apply("Price",price);Apply("DiscountPercent",percent);Apply("DiscountAmount",fixedDiscount);
            Apply("Inclusive",inclusive);Apply("TaxAssignment",mapping);Apply("Taxable",taxable);Apply("Gross",gross);
            Apply("AppliedDiscount",appliedDiscount);Apply("Gst",gst);Apply("TaxAmount",taxAmount);Apply("Total",total);
            TaxComponent("Sgst",sgstPercent,sgstAmount,CurrentSaleType()==SalesIntent.Local);
            TaxComponent("Cgst",cgstPercent,cgstAmount,CurrentSaleType()==SalesIntent.Local);
            TaxComponent("Igst",igstPercent,igstAmount,CurrentSaleType()==SalesIntent.Interstate);
            void TaxComponent(string component,TextBox percentage,TextBox amount,bool applicable)
            {
                var percentKey="Line"+component+"Percent";
                var amountKey="Line"+component+"Amount";
                Apply(percentKey,percentage);Apply(amountKey,amount);
                dialog.ShowConfiguredField(percentage,options.GraftexEntry && applicable && form.Field(percentKey).Visible);
                dialog.ShowConfiguredField(amount,options.GraftexEntry && applicable && form.Field(amountKey).Visible);
            }
            FillTax(true);
            if(variant.Items.Count==0 && variant.Parent is Border frame && frame.Parent is FrameworkElement field)
                field.Visibility=Visibility.Collapsed;
            MeasureEditorDialog.OrderConfigured(box);
            var remove=new Button {Content="Remove",VerticalAlignment=VerticalAlignment.Center,Padding=new Thickness(8,4,8,4)};
            box.Children.Add(remove);
            remove.Click+=(_,_)=>{date.SelectedDateChanged-=invoiceDateChanged;editors.Remove(item);rows.Children.Remove(lineCard); for(var i=0;i<editors.Count;i++) editors[i].Number.Text=(i+1).ToString(CultureInfo.CurrentCulture); dialog.MarkChanged();Queue();};
        }

        var recalculate=dialog.FooterButton("Preview invoice");
        BillingIcon.Set(recalculate,"Preview invoice","\uE890");
        dialog.Closed+=(_,_)=>{closed=true;preview.Cancel();};
        var headerFields=new Dictionary<string,Control>
        {
            [nameof(SalesDraft.Date)]=date,[nameof(SalesDraft.YearId)]=year,[nameof(SalesDraft.SeriesId)]=series,
            [nameof(SalesDraft.RegistrationId)]=registration,[nameof(SalesDraft.CustomerId)]=customer,
            ["SaleIntent"]=saleType,
            [nameof(SalesDraft.BillingAddressId)]=addresses.BillingField,[nameof(SalesDraft.ShippingAddressId)]=addresses.ShippingField,
            [nameof(SalesDraft.Lines)]=add,["PurchaseOrder"]=po,["Reference"]=reference,["Terms"]=terms,["Notes"]=notes,
            ["InternalRemarks"]=internalRemarks,["GoodsReceipt"]=goodsReceipt,["Vehicle"]=vehicle,["Weight"]=weight,
            ["Carrier"]=carrier,["OrderThrough"]=orderThrough,["PrivateMark"]=privateMark,["Document"]=document,["PreparedBy"]=preparedBy
        };
        var visibleErrors=new HashSet<Control>();
        var attempted=false;
        var requireNumbering=false;
        var index=0;
        foreach(var line in draft.Lines??[]) Add(line,index++);
        add.Click+=(_,_)=>{Add(null,editors.Count);dialog.MarkChanged();Queue();editors[^1].Product.Focus();};

        Control? ResolveField(SalesFieldError issue)
        {
            if(issue.Line is not int number) return headerFields.GetValueOrDefault(issue.Field);
            if(number<1 || number>editors.Count) return null;
            var line=editors[number-1];
            return issue.Field switch
            {
                nameof(SalesLineInput.ProductId)=>line.Product,
                nameof(SalesLineInput.ProductVariantId)=>line.Variant,
                nameof(SalesLineInput.UnitId)=>line.Unit,
                nameof(SalesLineInput.Quantity)=>line.Quantity,
                nameof(SalesLineInput.Price)=>line.Price,
                nameof(Product.Mrp)=>line.Mrp,
                "TaxAssignment"=>line.Mapping.IsVisible && line.Mapping.IsEnabled?line.Mapping:line.Product,
                nameof(SalesLineInput.ManualHsn)=>line.Hsn,
                nameof(SalesLineInput.ManualGstRate)=>line.Gst,
                nameof(SalesLineInput.DiscountPercent)=>line.Percent,
                nameof(SalesLineInput.DiscountAmount)=>line.Discount,
                _=>null
            };
        }

        void DisplayErrors(IEnumerable<SalesFieldError> issues,bool focus,bool keepNumbering=false)
        {
            foreach(var field in visibleErrors.ToArray())
            {
                if(keepNumbering && (field==year || field==series)) continue;
                dialog.SetFieldError(field,null);
                visibleErrors.Remove(field);
            }
            Control? first=null;
            foreach(var group in issues.Select(issue=>(Field:ResolveField(issue),issue.Message))
                .Where(item=>item.Field is not null).GroupBy(item=>item.Field!))
            {
                dialog.SetFieldError(group.Key,string.Join(" ",group.Select(item=>item.Message).Distinct()));
                visibleErrors.Add(group.Key);
                if(group.Key.IsVisible) first??=group.Key;
            }
            if(focus && first is not null) dialog.FocusInvalidField(first);
        }

        (SalesDraft? Input,List<SalesFieldError> Issues,List<SalesProductCompletion> Completions) Read(bool saving)
        {
            var issues=new List<SalesFieldError>();
            void Required(bool valid,string field,string message,int? line=null)
            {
                if(!valid) issues.Add(new(field,message,line));
            }
            var day=Date(date);
            Required(day.HasValue,nameof(SalesDraft.Date),"Enter a valid invoice date.");
            if(saving) Required(year.SelectedItem is SalesChoice selectedYear && MatchesDate(selectedYear),
                nameof(SalesDraft.YearId),"Select an open financial year that includes the invoice date.");
            Required(Id(registration).HasValue,nameof(SalesDraft.RegistrationId),"Select the supplier GST registration.");
            Required(CurrentSaleType().HasValue,"SaleIntent","Select a sale type.");
            if(saving) Required(Id(series).HasValue,nameof(SalesDraft.SeriesId),"Select a series for this year and registration.");
            Required(Id(customer).HasValue,nameof(SalesDraft.CustomerId),"Select a customer.");
            if(Id(customer).HasValue)
            {
                if(addresses.IsLoading) Required(false,nameof(SalesDraft.CustomerId),"Wait for the customer addresses to load.");
                else
                {
                    Required(addresses.BillingId.HasValue,nameof(SalesDraft.BillingAddressId),"Select or add a billing address.");
                    Required(addresses.ShippingId.HasValue,nameof(SalesDraft.ShippingAddressId),"Select or add a shipping address.");
                }
            }
            Required(editors.Count>0,nameof(SalesDraft.Lines),"Add at least one product line.");
            var lines=new List<SalesLineInput>();
            var completions=new List<SalesProductCompletion>();
            for(var i=0;i<editors.Count;i++)
            {
                var line=editors[i];
                var n=i+1;
                var product=Id(line.Product);
                var quantityOk=Number(line.Quantity,out var qty);
                var priceOk=Number(line.Price,out var rate);
                var pctOk=OptionalNumber(line.Percent,out var pct);
                var discountOk=OptionalNumber(line.Discount,out var amount);
                Required(product.HasValue,nameof(SalesLineInput.ProductId),"Select a product.",n);
                var selectedVariant=line.Variant.SelectedItem as SalesVariantChoice;
                if(product.HasValue && (options.Variants??[]).Any(x=>x.ProductId==product && x.Active))
                    Required(selectedVariant is {Active:true},nameof(SalesLineInput.ProductVariantId),"Select an active variant.",n);
                Required(quantityOk,nameof(SalesLineInput.Quantity),"Enter a numeric quantity.",n);
                Required(priceOk,nameof(SalesLineInput.Price),"Enter a numeric unit price for the selected unit.",n);
                Required(pctOk,nameof(SalesLineInput.DiscountPercent),"Enter a numeric percentage or leave blank.",n);
                Required(discountOk,nameof(SalesLineInput.DiscountAmount),"Enter a numeric amount or leave blank.",n);
                var unitId=Id(line.Unit);
                decimal? manualGst=null;
                if(!line.Gst.IsReadOnly && Number(line.Gst,out var enteredGst)) manualGst=enteredGst;
                var input=new SalesLineInput(product.GetValueOrDefault(),unitId==Guid.Empty?null:unitId,
                    quantityOk?qty:1,priceOk?rate:0,line.Inclusive.SelectedItem as string=="Yes",pctOk?pct:0,discountOk?amount:0,
                    selectedVariant?.Id,line.Gst.IsReadOnly?null:line.Hsn.Text.Trim(),manualGst);
                issues.AddRange(SalesCalculation.ValidateLine(input,n,validateSubtotal:quantityOk && priceOk));
                lines.Add(input);
                decimal? enteredMrp=null;
                if(!line.Mrp.IsReadOnly && !string.IsNullOrWhiteSpace(line.Mrp.Text))
                {
                    if(Number(line.Mrp,out var mrp))
                    {
                        var errors=new Dictionary<string,string>();
                        CatalogRules.CheckDecimal(nameof(Product.Mrp),mrp,errors);
                        if(errors.TryGetValue(nameof(Product.Mrp),out var error)) issues.Add(new(nameof(Product.Mrp),error,n));
                        else enteredMrp=mrp;
                    }
                    else issues.Add(new(nameof(Product.Mrp),"Enter a valid MRP amount.",n));
                }
                var mapping=line.Mapping.SelectedItem as SalesTaxChoice;
                if(enteredMrp.HasValue || mapping is not null)
                    completions.Add(new(n,enteredMrp,mapping?.AssignmentId));
            }
            if(issues.Count>0) return (null,issues,completions);
            return (new(day!.Value,Id(year),Id(series),Id(registration),Id(customer),addresses.BillingId,addresses.ShippingId,PurchaseOrder:po.Text,
                PurchaseOrderDate:Date(poDate),Reference:reference.Text,Terms:terms.Text,DueDate:Date(due),Notes:notes.Text,InternalRemarks:internalRemarks.Text,Lines:lines,
                Transport:new(goodsReceipt.Text.Trim(),vehicle.Text.Trim(),weight.Text.Trim(),carrier.Text.Trim(),orderThrough.Text.Trim(),
                    privateMark.Text.Trim(),document.Text.Trim(),options.PreparedBy??"",distance.Text.Trim(),transportId.Text.Trim()),
                EntryDetails:new(CurrentSaleType(),payment.SelectedItem as string??"",tcs.Text.Trim(),reversal.Text.Trim(),ern.Text.Trim())),issues,completions);
        }

        void Queue(bool wait=true)
        {
            if(dialog.IsSaving) return;
            var explicitPreview=!wait;
            if(explicitPreview) attempted=true;
            _=preview.ScheduleAsync(wait,()=>false,async ct=>
            {
                var (input,issues,completions)=Read(false);
                if(attempted)
                {
                    if(requireNumbering) issues.AddRange(Read(true).Issues.Where(issue=>
                        issue.Field is nameof(SalesDraft.YearId) or nameof(SalesDraft.SeriesId)));
                    DisplayErrors(issues,explicitPreview);
                }
                if(input is null)
                {
                    ShowCalculated(null,"Totals unavailable until the required fields are complete.");
                    dialog.ShowMessage(issues[0].Message,explicitPreview?NoticeKind.Error:NoticeKind.Warning);
                    return;
                }
                ShowCalculated(null,"Calculating totals…");
                var response=await send(SalesAction.Preview,input,completions,ct);
                if(closed || ct.IsCancellationRequested) return;
                ShowCalculated(response.Snapshot,"Totals unavailable. Review the highlighted fields or message.");
                DisplayErrors(response.FieldErrors??[],explicitPreview,keepNumbering:requireNumbering);
                dialog.ShowMessage(SalesInvoiceViewModel.Error(response),NoticeKind.Error);
                if(explicitPreview && response.Outcome==CompanyOutcome.Success && response.Snapshot is not null)
                {
                    var window=SalesInvoiceReadView.Create(response,options);
                    window.Owner=dialog;
                    window.ShowDialog();
                }
            });
        }
        void ShowCalculated(SalesInvoiceSnapshot? snapshot,string error)
        {
            var actualIntent=snapshot?.Draft.EntryDetails?.Intent;
            summary.Text=snapshot is not null?Summary(snapshot.Amounts,form,entry:true,
                intent:options.GraftexEntry?actualIntent:null):error;
            for(var i=0;i<editors.Count;i++)
            {
                var line=snapshot?.Lines.ElementAtOrDefault(i);
                if(line is not null && editors[i].Hsn.IsReadOnly) editors[i].Hsn.Text=line.Tax.Rate.Code;
                var split=line?.Tax;
                var amounts=line?.Amounts;
                if(options.GraftexEntry && split is not null)
                {
                    var interstate=split.Jurisdiction==GstJurisdiction.Interstate;
                    var row=editors[i];
                    dialog.ShowConfiguredField(row.IgstPercent,interstate && form.Field("LineIgstPercent").Visible);
                    dialog.ShowConfiguredField(row.IgstAmount,interstate && form.Field("LineIgstAmount").Visible);
                    dialog.ShowConfiguredField(row.SgstPercent,!interstate && form.Field("LineSgstPercent").Visible);
                    dialog.ShowConfiguredField(row.SgstAmount,!interstate && form.Field("LineSgstAmount").Visible);
                    dialog.ShowConfiguredField(row.CgstPercent,!interstate && form.Field("LineCgstPercent").Visible);
                    dialog.ShowConfiguredField(row.CgstAmount,!interstate && form.Field("LineCgstAmount").Visible);
                }
                editors[i].SgstPercent.Text=split?.SgstPercentage.ToString("0.##")??"";
                editors[i].SgstAmount.Text=amounts?.Sgst.ToString("N2")??"";
                editors[i].CgstPercent.Text=split?.CgstPercentage.ToString("0.##")??"";
                editors[i].CgstAmount.Text=amounts?.Cgst.ToString("N2")??"";
                editors[i].IgstPercent.Text=split?.IgstPercentage.ToString("0.##")??"";
                editors[i].IgstAmount.Text=amounts?.Igst.ToString("N2")??"";
                editors[i].Taxable.Text=amounts?.Taxable.ToString("N2")??"";
                if(line is not null && editors[i].Gst.IsReadOnly) editors[i].Gst.Text=Decimal(line.Tax.Rate.Total);
                editors[i].Total.Text=amounts?.Total.ToString("N2")??"";
                editors[i].Gross.Text=amounts?.Subtotal.ToString("N2")??"";
                editors[i].AppliedDiscount.Text=amounts?.Discount.ToString("N2")??"";
                editors[i].TaxAmount.Text=amounts?.Tax.ToString("N2")??"";
            }
        }
        recalculate.Click+=(_,_)=>Queue(false);
        addresses.Changed+=()=>Queue();
        dialog.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,e)=>{if(e.OriginalSource is TextBox {IsReadOnly:false}) Queue();}));
        dialog.AddHandler(SelectorEvent(),new SelectionChangedEventHandler((_,_)=>Queue()));
        dialog.AddHandler(DatePicker.SelectedDateChangedEvent,new EventHandler<SelectionChangedEventArgs>((_,_)=>Queue()));
        async Task<string?> SaveDraft(bool review)
        {
            if(addresses.IsEditing) return "Save or cancel the address edit before saving the invoice.";
            preview.Cancel();
            attempted=requireNumbering=true;
            var (input,issues,completions)=Read(true);
            DisplayErrors(issues,true);
            if(input is null) return issues[0].Message;
            if(options.GraftexEntry && completions.Count>0)
            {
                var changes=string.Join(Environment.NewLine,completions.Select(change=>
                    $"Line {change.Line}: "+string.Join(", ",new[]{
                        change.Mrp is decimal mrp?$"set missing MRP to {mrp:N2}":null,
                        change.TaxAssignmentId.HasValue?"link the selected HSN/GST assignment":null
                    }.OfType<string>())));
                var consent=MessageBox.Show(dialog,changes+Environment.NewLine+Environment.NewLine+
                    "Yes saves the draft and updates Product Master. No saves without these master changes. Cancel returns to editing.",
                    "Confirm Product Master updates",MessageBoxButton.YesNoCancel,MessageBoxImage.Question,MessageBoxResult.Cancel);
                if(consent==MessageBoxResult.Cancel) return "Save canceled. Your invoice edits are still open.";
                if(consent==MessageBoxResult.No) completions.Clear();
            }
            var response=await send(SalesAction.Save,input,completions,CancellationToken.None);
            if(response.Outcome!=CompanyOutcome.Success) DisplayErrors(response.FieldErrors??[],true);
            if(review && response.Row is { } saved && response.Outcome==CompanyOutcome.Success)
                finalize?.Invoke(saved);
            return response.Outcome==CompanyOutcome.Success?null:SalesInvoiceViewModel.Error(response);
        }
        dialog.Save(()=>SaveDraft(false),Label("SaveDraft","Save Draft"),primary:true,icon:"\uE74E");
        if(finalize is not null) dialog.Save(()=>SaveDraft(true),"Save draft & review finalization",icon:"\uE73E");
        Queue();

        ComboBox Choice(string label,IEnumerable<SalesChoice> values,Guid? selected,Panel? panel=null)
        {
            var list=values.ToArray();
            return dialog.Choice(label,list,list.FirstOrDefault(x=>x.Id==selected),nameof(SalesChoice.Label),panel,searchable:true);
        }
    }

    private static RoutedEvent SelectorEvent()=>System.Windows.Controls.Primitives.Selector.SelectionChangedEvent;
    private static bool Number(TextBox input,out decimal value)=>decimal.TryParse(input.Text,NumberStyles.Number,CultureInfo.CurrentCulture,out value);
    private static bool OptionalNumber(TextBox input,out decimal value)
    {
        value=0;
        return string.IsNullOrWhiteSpace(input.Text) || Number(input,out value);
    }
    internal static string Summary(SalesAmounts a,SalesFormConfiguration? form=null,bool entry=false,SalesIntent? intent=null)=>
        $"Subtotal {a.Subtotal:N2} · {Label("Discount","Discount")} {a.Discount:N2} · {Label("Taxable","Taxable value")} {a.Taxable:N2}"+Environment.NewLine+
        string.Join(" · ",new[]{
            intent!=SalesIntent.Interstate || a.Sgst!=0?$"{Label("Sgst","SGST")} {a.Sgst:N2}":null,
            intent!=SalesIntent.Interstate || a.Cgst!=0?$"{Label("Cgst","CGST")} {a.Cgst:N2}":null,
            intent!=SalesIntent.Local || a.Igst!=0?$"{Label("Igst","IGST")} {a.Igst:N2}":null,
            intent is null || a.Utgst!=0?$"UTGST {a.Utgst:N2}":null,
            intent is null || a.Cess!=0?$"{Label("Cess","Cess")} {a.Cess:N2}":null
        }.OfType<string>())+Environment.NewLine+
        $"{Label("RoundOff","Round off")} {a.RoundOff:N2}"+
        (entry && form?.Field("GrandTotal").Visible==false?"":
            $" · {form?.Caption("GrandTotal",Label("Total","Grand total"))??Label("Total","Grand total")} INR {a.Total:N2}");
    private sealed record LineControls(ComboBox Product,ComboBox Unit,TextBox Quantity,TextBox Price,TextBox Percent,TextBox Discount,
        ComboBox Inclusive,TextBox Hsn,TextBox Taxable,TextBox Gst,TextBox Total,TextBlock Number,TextBox Mrp,ComboBox Mapping,
        TextBox Gross,TextBox AppliedDiscount,TextBox TaxAmount,ComboBox Variant,
        TextBox SgstPercent,TextBox SgstAmount,TextBox CgstPercent,TextBox CgstAmount,TextBox IgstPercent,TextBox IgstAmount);
}
