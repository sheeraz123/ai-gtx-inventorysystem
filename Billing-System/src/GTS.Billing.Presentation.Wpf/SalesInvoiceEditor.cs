using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using GTS.Billing.Application;
using GTS.Billing.Domain;

namespace GTS.Billing.Presentation.Wpf;

internal static class SalesInvoiceEditor
{
    private static string Label(string key,string fallback)=>SettingsDisplay.Text("Sales."+key,fallback);
    private static DateOnly? Date(DatePicker picker)=>picker.SelectedDate is DateTime value?DateOnly.FromDateTime(value):null;
    private static Guid? Id(ComboBox selector)=>(selector.SelectedItem as SalesChoice)?.Id;
    private static string Decimal(decimal value)=>value.ToString("G29",CultureInfo.CurrentCulture);

    internal static void Configure(MeasureEditorDialog dialog,SalesResult data,SalesOptions options,string companyName,
        Func<SalesAction,SalesDraft,IReadOnlyList<SalesProductCompletion>,CancellationToken,Task<SalesResult>> send,
        Action<SalesRow>? finalize)
    {
        var draft=data.Draft!;
        dialog.Width=Math.Min(1450,SystemParameters.WorkArea.Width);
        dialog.Height=Math.Min(880,SystemParameters.WorkArea.Height);
        var header=new WrapPanel();
        dialog.AddCard("Invoice details",header);
        dialog.Text("Company",companyName,150,true,panel:header).Width=210;
        dialog.Text("Invoice type / status","Tax Invoice — Draft",60,true,panel:header).Width=150;
        var date=dialog.Date(Label("Date","Invoice date")+" *",draft.Date,panel:header);
        var isNew=data.Row is null;
        bool MatchesDate(SalesChoice value)=>Date(date) is { } day && SalesOptions.IncludesDate(value,day);
        var availableYears=options.Years.Where(x=>x.Active || x.Id==draft.YearId).ToArray();
        var year=Choice("Financial year *",availableYears,draft.YearId??(isNew?options.DefaultYear(draft.Date)?.Id:null),header);
        var registration=Choice("Supplier GST registration *",options.Registrations.Where(x=>x.Active || x.Id==draft.RegistrationId),
            draft.RegistrationId??(isNew?options.DefaultRegistration()?.Id:null),header);
        var series=Choice("Document series *",[],null,header);
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
        }
        ReloadSeries(draft.SeriesId);
        year.SelectionChanged+=(_,_)=>ReloadSeries();
        registration.SelectionChanged+=(_,_)=>ReloadSeries();
        series.SelectionChanged+=(_,_)=>
        {
            if(series.SelectedItem is not null) dialog.SetFieldWarning(series,null);
        };
        date.SelectedDateChanged+=(_,_)=>ReloadSeries();
        var parties=new Grid();
        parties.ColumnDefinitions.Add(new()); parties.ColumnDefinitions.Add(new());
        var billed=new StackPanel();
        var shipped=new StackPanel();
        Grid.SetColumn(shipped,1);
        parties.Children.Add(billed); parties.Children.Add(shipped);
        dialog.AddCard("Customer and addresses",parties);
        var customer=Choice("Customer",options.Customers,draft.CustomerId,billed);
        var bill=Choice("Bill to",options.Billing.Where(x=>x.Scope==draft.CustomerId),draft.BillingAddressId,billed);
        var ship=Choice("Ship to",options.Shipping.Where(x=>x.Scope==draft.CustomerId),draft.ShippingAddressId,shipped);
        var billingDetails=new TextBlock {TextWrapping=TextWrapping.Wrap};
        var shippingDetails=new TextBlock {TextWrapping=TextWrapping.Wrap};
        billed.Children.Add(billingDetails); shipped.Children.Add(shippingDetails);
        customer.SelectionChanged+=(_,_) =>
        {
            SearchableComboBox.SetSource(bill,options.Billing.Where(x=>x.Scope==Id(customer)).ToArray());
            SearchableComboBox.SetSource(ship,options.Shipping.Where(x=>x.Scope==Id(customer)).ToArray());
        };
        var taxFields=new WrapPanel();
        dialog.AddCard("GST treatment",taxFields);
        var places=CompanyRules.States.Select(x=>new Place(x.Key,x.Key+" — "+x.Value)).OrderBy(x=>x.Label).ToArray();
        var place=dialog.Choice("Reviewed place of supply",places,places.FirstOrDefault(x=>x.Code==draft.PlaceOfSupply),
            nameof(Place.Label),panel:taxFields,searchable:true);
        var jurisdiction=dialog.Choice("GST jurisdiction",new[]{GstJurisdiction.Unresolved,GstJurisdiction.IntraState,
            GstJurisdiction.IntraUnionTerritory,GstJurisdiction.Interstate},draft.Jurisdiction,panel:taxFields);
        var confirm=dialog.Choice("Standard domestic supply reviewed",new[]{"No","Yes"},draft.DomesticConfirmed?"Yes":"No",panel:taxFields);
        var notesFields=new WrapPanel();
        var rows=new StackPanel();
        var editors=new List<LineControls>();
        var preview=new MasterSearch();
        var closed=false;
        var summary=new TextBlock {Text="Complete the draft to calculate totals.",TextWrapping=TextWrapping.Wrap,
            HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(16,7,16,7),FontWeight=FontWeights.SemiBold};
        var summaryCard=new Border {Child=summary,CornerRadius=new CornerRadius(7),BorderThickness=new Thickness(1),
            Margin=new Thickness(16,4,16,4)};
        summaryCard.SetResourceReference(Border.BorderBrushProperty,"Palette.GridLine");
        dialog.DockSummary(summaryCard);
        var lineArea=new StackPanel();
        var lineButtons=new WrapPanel();
        var add=new Button {Content="Add product",Margin=new Thickness(0,7,8,7),Padding=new Thickness(10,4,10,4)};
        dialog.AddField("",add,lineButtons);
        lineArea.Children.Add(lineButtons);
        lineArea.Children.Add(new ScrollViewer {Content=rows,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=430});
        dialog.AddCard("Products",lineArea);
        dialog.AddCard("Additional details",new Expander {Header="Payment, transport and notes",Content=notesFields});
        var po=dialog.Text("Customer PO reference",draft.PurchaseOrder,100,panel:notesFields);
        var poDate=dialog.Date("Customer PO date",draft.PurchaseOrderDate,panel:notesFields);
        var reference=dialog.Text("Internal reference",draft.Reference,100,panel:notesFields);
        var due=dialog.Date("Due date",draft.DueDate,panel:notesFields);
        var terms=dialog.Text("Payment terms",draft.Terms,500,panel:notesFields);
        var notes=dialog.Text("Notes",draft.Notes,800,panel:notesFields);
        var internalRemarks=dialog.Text("Internal remarks",draft.InternalRemarks,800,panel:notesFields);
        var transport=draft.Transport;
        TextBox TransportField(string name,string? value)=>dialog.Text(name,value,120,panel:notesFields);
        var goodsReceipt=TransportField("G.R. number",transport?.GoodsReceipt);
        var vehicle=TransportField("Vehicle number",transport?.Vehicle);
        var weight=TransportField("Weight",transport?.Weight);
        var carrier=TransportField("Transport",transport?.Carrier);
        var orderThrough=TransportField("Order through",transport?.OrderThrough);
        var privateMark=TransportField("Private mark",transport?.PrivateMark);
        var document=TransportField("Transport document",transport?.Document);
        var preparedBy=TransportField("Prepared by",transport?.PreparedBy);
        void Add(SalesLineInput? value,int index)
        {
            var box=new WrapPanel {Margin=new Thickness(0,4,0,4)};
            var lineCard=new Border {Child=box,BorderThickness=new Thickness(0,index==0?0:1,0,0)};
            lineCard.SetResourceReference(Border.BorderBrushProperty,"Palette.GridLine");
            rows.Children.Add(lineCard);
            var number=new TextBlock {Text=(index+1).ToString(CultureInfo.CurrentCulture),Width=26,VerticalAlignment=VerticalAlignment.Center};
            box.Children.Add(number);
            var product=Choice("Product search",options.Products,value?.ProductId,box);
            product.Width=230;
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
                code.Text=selected?.Code??"";
                sku.Text=selected?.Sku??"";
                name.Text=selected?.ProductName??"";
                brand.Text=selected?.Brand??"";
                mrp.IsReadOnly=selected is null || selected.Mrp.HasValue || !options.CanFillMrp;
                mrp.IsTabStop=!mrp.IsReadOnly;
                mrp.Text=selected?.Mrp is decimal amount?Decimal(amount):"";
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
                box,searchable:true);
            mapping.Width=210;
            var total=Display("Line total",95);
            var item=new LineControls(product,unit,quantity,price,percent,fixedDiscount,inclusive,hsn,taxable,gst,total,number,mrp,mapping,
                gross,appliedDiscount,taxAmount);
            editors.Add(item);
            FillTax(false);
            void ShowMrpWarning()
            {
                var missing=product.SelectedItem is SalesChoice {Mrp:null} && string.IsNullOrWhiteSpace(mrp.Text);
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
                var canSelect=selected is not null && options.CanLinkTax && selected.TaxClassificationId is null;
                mapping.IsEnabled=canSelect && candidates.Length>0;
                mapping.Visibility=canSelect?Visibility.Visible:Visibility.Collapsed;
                SearchableComboBox.SetSource(mapping,candidates);
                mapping.SelectedItem=candidates.FirstOrDefault(x=>x.AssignmentId==previous);
                SetTaxFields();
                void SetTaxFields()
                {
                    var chosen=mapping.SelectedItem as SalesTaxChoice??(linked.Length==1?linked[0]:null);
                    hsn.Text=chosen?.Code??selected?.Hsn??"";
                    gst.Text=chosen is not null?Decimal(chosen.Rate):selected?.GstRate is decimal legacy?Decimal(legacy):"";
                    var needsTax=selected is not null && chosen is null;
                    dialog.SetFieldWarning(canSelect?mapping:gst,!needsTax?null:selected!.TaxClassificationId.HasValue
                        ?"The saved tax profile has no unique rate for this date."
                        :!options.CanLinkTax?"GST profile is missing. Product tax assignment permission is required."
                        :candidates.Length==0?"No matching approved HSN/SAC and GST mapping for this date."
                        :"Choose an approved HSN/SAC and GST mapping to link on Save.");
                    if(!canSelect) dialog.SetFieldWarning(mapping,null);
                    else dialog.SetFieldWarning(gst,needsTax && selected!.GstRate is null?"GST rate is missing.":null);
                    dialog.SetFieldWarning(hsn,needsTax && string.IsNullOrWhiteSpace(selected!.Hsn)?"HSN/SAC is missing.":null);
                }
            }
            mrp.TextChanged+=(_,_)=>ShowMrpWarning();
            mapping.SelectionChanged+=(_,_) =>
            {
                if(mapping.SelectedItem is not SalesTaxChoice selected) return;
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
                price.Text=selected?.Price is decimal defaultPrice?Decimal(defaultPrice):"";
                inclusive.SelectedItem=selected?.Inclusive==true?"Yes":"No";
                FillProduct();
                FillTax(false);
            };
            unit.SelectionChanged+=(_,_) =>
            {
                price.Text=Id(unit)==Guid.Empty && (product.SelectedItem as SalesChoice)?.Price is decimal defaultPrice
                    ?Decimal(defaultPrice):"";
            };
            var remove=new Button {Content="Remove",VerticalAlignment=VerticalAlignment.Center,Padding=new Thickness(8,4,8,4)};
            box.Children.Add(remove);
            remove.Click+=(_,_)=>{date.SelectedDateChanged-=invoiceDateChanged;editors.Remove(item);rows.Children.Remove(lineCard); for(var i=0;i<editors.Count;i++) editors[i].Number.Text=(i+1).ToString(CultureInfo.CurrentCulture); dialog.MarkChanged();Queue();};
        }

        var recalculate=dialog.FooterButton("Preview invoice");
        dialog.Closed+=(_,_)=>{closed=true;preview.Cancel();};
        var headerFields=new Dictionary<string,Control>
        {
            [nameof(SalesDraft.Date)]=date,[nameof(SalesDraft.YearId)]=year,[nameof(SalesDraft.SeriesId)]=series,
            [nameof(SalesDraft.RegistrationId)]=registration,[nameof(SalesDraft.CustomerId)]=customer,
            [nameof(SalesDraft.BillingAddressId)]=bill,[nameof(SalesDraft.ShippingAddressId)]=ship,
            [nameof(SalesDraft.PlaceOfSupply)]=place,[nameof(SalesDraft.Jurisdiction)]=jurisdiction,
            [nameof(SalesDraft.DomesticConfirmed)]=confirm,[nameof(SalesDraft.PurchaseOrder)]=po,
            [nameof(SalesDraft.Lines)]=add
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
                nameof(SalesLineInput.UnitId)=>line.Unit,
                nameof(SalesLineInput.Quantity)=>line.Quantity,
                nameof(SalesLineInput.Price)=>line.Price,
                nameof(Product.Mrp)=>line.Mrp,
                "TaxAssignment"=>line.Mapping.IsVisible && line.Mapping.IsEnabled?line.Mapping:line.Product,
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
                first??=group.Key;
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
            var scope=jurisdiction.SelectedItem is GstJurisdiction selectedScope?selectedScope:GstJurisdiction.Unresolved;
            var selectedPlace=(place.SelectedItem as Place)?.Code??"";
            var domestic=confirm.SelectedItem as string=="Yes";
            Required(day.HasValue,nameof(SalesDraft.Date),"Enter a valid invoice date.");
            if(saving) Required(year.SelectedItem is SalesChoice selectedYear && MatchesDate(selectedYear),
                nameof(SalesDraft.YearId),"Select an open financial year that includes the invoice date.");
            Required(Id(registration).HasValue,nameof(SalesDraft.RegistrationId),"Select the supplier GST registration.");
            if(saving) Required(Id(series).HasValue,nameof(SalesDraft.SeriesId),"Select a series for this year and registration.");
            Required(Id(customer).HasValue,nameof(SalesDraft.CustomerId),"Select a customer.");
            Required(selectedPlace.Length>0,nameof(SalesDraft.PlaceOfSupply),"Select the reviewed place of supply.");
            Required(scope!=GstJurisdiction.Unresolved,nameof(SalesDraft.Jurisdiction),"Select the applicable GST jurisdiction.");
            Required(domestic,nameof(SalesDraft.DomesticConfirmed),"Confirm that the standard domestic supply has been reviewed.");
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
                Required(quantityOk,nameof(SalesLineInput.Quantity),"Enter a numeric quantity.",n);
                Required(priceOk,nameof(SalesLineInput.Price),"Enter a numeric unit price for the selected unit.",n);
                Required(pctOk,nameof(SalesLineInput.DiscountPercent),"Enter a numeric percentage or leave blank.",n);
                Required(discountOk,nameof(SalesLineInput.DiscountAmount),"Enter a numeric amount or leave blank.",n);
                var unitId=Id(line.Unit);
                var input=new SalesLineInput(product.GetValueOrDefault(),unitId==Guid.Empty?null:unitId,
                    quantityOk?qty:1,priceOk?rate:0,line.Inclusive.SelectedItem as string=="Yes",pctOk?pct:0,discountOk?amount:0);
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
            return (new(day!.Value,Id(year),Id(series),Id(registration),Id(customer),Id(bill),Id(ship),selectedPlace,
                scope,domestic,po.Text,Date(poDate),reference.Text,terms.Text,Date(due),notes.Text,internalRemarks.Text,lines,
                new(goodsReceipt.Text.Trim(),vehicle.Text.Trim(),weight.Text.Trim(),carrier.Text.Trim(),orderThrough.Text.Trim(),
                    privateMark.Text.Trim(),document.Text.Trim(),preparedBy.Text.Trim())),issues,completions);
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
            summary.Text=snapshot is null?error:Summary(snapshot.Amounts);
            billingDetails.Text=snapshot is null?"":SalesInvoiceReadView.Address(snapshot.Customer.BillTo,snapshot.Customer.Gstin);
            shippingDetails.Text=snapshot is null?"":SalesInvoiceReadView.Address(snapshot.Customer.ShipTo,snapshot.Customer.Gstin);
            for(var i=0;i<editors.Count;i++)
            {
                var line=snapshot?.Lines.ElementAtOrDefault(i);
                if(line is not null) editors[i].Hsn.Text=line.Tax.Rate.Code;
                editors[i].Taxable.Text=line?.Amounts.Taxable.ToString("N2")??"";
                if(line is not null) editors[i].Gst.Text=Decimal(line.Tax.Rate.Total);
                editors[i].Total.Text=line?.Amounts.Total.ToString("N2")??"";
                editors[i].Gross.Text=line?.Amounts.Subtotal.ToString("N2")??"";
                editors[i].AppliedDiscount.Text=line?.Amounts.Discount.ToString("N2")??"";
                editors[i].TaxAmount.Text=line?.Amounts.Tax.ToString("N2")??"";
            }
        }
        recalculate.Click+=(_,_)=>Queue(false);
        dialog.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,e)=>{if(e.OriginalSource is TextBox {IsReadOnly:false}) Queue();}));
        dialog.AddHandler(SelectorEvent(),new SelectionChangedEventHandler((_,_)=>Queue()));
        dialog.AddHandler(DatePicker.SelectedDateChangedEvent,new EventHandler<SelectionChangedEventArgs>((_,_)=>Queue()));
        async Task<string?> SaveDraft(bool review)
        {
            preview.Cancel();
            attempted=requireNumbering=true;
            var (input,issues,completions)=Read(true);
            DisplayErrors(issues,true);
            if(input is null) return issues[0].Message;
            var response=await send(SalesAction.Save,input,completions,CancellationToken.None);
            if(response.Outcome!=CompanyOutcome.Success) DisplayErrors(response.FieldErrors??[],true);
            if(review && response.Row is { } saved && response.Outcome==CompanyOutcome.Success) finalize?.Invoke(saved);
            return response.Outcome==CompanyOutcome.Success?null:SalesInvoiceViewModel.Error(response);
        }
        dialog.Save(()=>SaveDraft(false),Label("SaveDraft","Save Draft"));
        if(finalize is not null) dialog.Save(()=>SaveDraft(true),"Save draft & review finalization");
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
    internal static string Summary(SalesAmounts a)=>
        $"Subtotal {a.Subtotal:N2} · {Label("Discount","Discount")} {a.Discount:N2} · {Label("Taxable","Taxable value")} {a.Taxable:N2}"+Environment.NewLine+
        $"{Label("Cgst","CGST")} {a.Cgst:N2} · {Label("Sgst","SGST")} {a.Sgst:N2} · {Label("Igst","IGST")} {a.Igst:N2} · UTGST {a.Utgst:N2} · {Label("Cess","Cess")} {a.Cess:N2}"+Environment.NewLine+
        $"{Label("RoundOff","Round off")} {a.RoundOff:N2} · {Label("Total","Grand total")} INR {a.Total:N2}";
    private sealed record Place(string Code,string Label);
    private sealed record LineControls(ComboBox Product,ComboBox Unit,TextBox Quantity,TextBox Price,TextBox Percent,TextBox Discount,
        ComboBox Inclusive,TextBox Hsn,TextBox Taxable,TextBox Gst,TextBox Total,TextBlock Number,TextBox Mrp,ComboBox Mapping,
        TextBox Gross,TextBox AppliedDiscount,TextBox TaxAmount);
}
