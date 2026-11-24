using System.Windows;
using System.Windows.Controls;
using GTS.Billing.Application;
using GTS.Billing.Domain;

namespace GTS.Billing.Presentation.Wpf;

internal sealed class SalesInvoiceAddressCards
{
    private readonly MeasureEditorDialog owner;
    private readonly ComboBox customer;
    private readonly ComboBox bill;
    private readonly ComboBox ship;
    private readonly TextBlock billText=new() {TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock shipText=new() {TextWrapping=TextWrapping.Wrap};
    private readonly StackPanel billEditor=new(),shipEditor=new();
    internal bool IsEditing {get;private set;}
    private readonly NoticeBanner notice=new() {Margin=new Thickness(0,6,0,0)};
    private readonly Button billEdit=new() {Content="Edit Address",HorizontalAlignment=HorizontalAlignment.Left};
    private readonly Button shipEdit=new() {Content="Edit Address",HorizontalAlignment=HorizontalAlignment.Left};
    private readonly Button copy=new() {Content="Copy billing to shipping",HorizontalAlignment=HorizontalAlignment.Left};
    private readonly MasterSearch loading=new();
    private readonly Func<Guid,CancellationToken,Task<SalesResult>> load;
    private readonly Func<Guid,long,CustomerAddressDraft,Guid?,CancellationToken,Task<CustomerResult>> save;
    private readonly bool canEdit;
    private readonly SalesFormConfiguration form;
    private SalesCustomerAddresses? current;

    internal Guid? BillingId=>(bill.SelectedItem as SalesChoice)?.Id;
    internal Guid? ShippingId=>(ship.SelectedItem as SalesChoice)?.Id;
    internal Control BillingField=>bill;
    internal Control ShippingField=>ship;
    internal Panel Section {get;}
    internal bool IsLoading {get;private set;}
    internal event Action? Changed;

    internal SalesInvoiceAddressCards(MeasureEditorDialog owner,ComboBox customer,Guid? savedBill,Guid? savedShip,
        Func<Guid,CancellationToken,Task<SalesResult>> load,
        Func<Guid,long,CustomerAddressDraft,Guid?,CancellationToken,Task<CustomerResult>> save,bool canEdit,SalesFormConfiguration form)
    {
        this.owner=owner;this.customer=customer;this.load=load;this.save=save;this.canEdit=canEdit;this.form=form;
        var columns=new Grid();
        columns.ColumnDefinitions.Add(new());columns.ColumnDefinitions.Add(new());
        var left=new StackPanel();
        var right=new StackPanel();
        var billingFirst=form.Field("BillingDetails").Order<=form.Field("ShippingDetails").Order;
        AddCard(columns,left,form.Field("BillingDetails").Label,billingFirst?0:1);
        AddCard(columns,right,form.Field("ShippingDetails").Label,billingFirst?1:0);
        bill=owner.Choice<SalesChoice>("Saved billing address",[],null,nameof(SalesChoice.Label),left,searchable:true);
        ship=owner.Choice<SalesChoice>("Saved shipping address",[],null,nameof(SalesChoice.Label),right,searchable:true);
        owner.ConfigureField(bill,form.Field("BillingAddressId"),form.Rules.ContainsKey("BillingAddressId"));
        owner.ConfigureField(ship,form.Field("ShippingAddressId"),form.Rules.ContainsKey("ShippingAddressId"));
        left.Children.Add(billText);right.Children.Add(shipText);
        left.Children.Add(billEdit);
        var actions=new WrapPanel();
        actions.Children.Add(shipEdit);actions.Children.Add(copy);right.Children.Add(actions);
        left.Children.Add(billEditor);right.Children.Add(shipEditor);
        foreach(var button in new[]{billEdit,shipEdit,copy}) button.Margin=new Thickness(0,7,7,0);
        copy.Content=form.Field("CopyBilling").Label;
        copy.Visibility=form.Field("CopyBilling").Visible?Visibility.Visible:Visibility.Collapsed;
        billEdit.Visibility=form.Field("EditBilling").Visible?Visibility.Visible:Visibility.Collapsed;
        shipEdit.Visibility=form.Field("EditShipping").Visible?Visibility.Visible:Visibility.Collapsed;
        Section=new StackPanel {Tag=Math.Min(form.Field("BillingDetails").Order,form.Field("ShippingDetails").Order),
            Children={columns,notice}};
        owner.AddSection(Section);
        customer.SelectionChanged+=(_,_)=>Reload(null,null);
        bill.SelectionChanged+=(_,_)=>Render();
        ship.SelectionChanged+=(_,_)=>Render();
        billEdit.Click+=(_,_)=>Edit(true,false);
        shipEdit.Click+=(_,_)=>Edit(false,false);
        copy.Click+=(_,_)=>Edit(false,true);
        owner.Loaded+=(_,_)=>Reload(savedBill,savedShip);
        owner.Closed+=(_,_)=>loading.Cancel();
        Render();
    }

    private static void AddCard(Grid grid,StackPanel fields,string title,int column)
    {
        var body=new StackPanel();
        body.Children.Add(new TextBlock {Text=title,FontWeight=FontWeights.SemiBold,FontSize=16,Margin=new Thickness(0,0,0,8)});
        body.Children.Add(fields);
        var card=new Border {Child=body,CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),
            Padding=new Thickness(12),Margin=new Thickness(column==1?6:0,0,column==0?6:0,10)};
        card.SetResourceReference(Border.BorderBrushProperty,"Palette.GridLine");
        card.SetResourceReference(Border.BackgroundProperty,"Palette.Surface");
        SalesInvoiceEditor.StyleCard(card);
        Grid.SetColumn(card,column);
        grid.Children.Add(card);
    }

    private void Reload(Guid? preferredBill,Guid? preferredShip,string? savedMessage=null)
    {
        loading.Cancel();
        current=null;
        var customerId=(customer.SelectedItem as SalesChoice)?.Id;
        IsLoading=customerId.HasValue;
        SearchableComboBox.SetSource(bill,Array.Empty<SalesChoice>());
        SearchableComboBox.SetSource(ship,Array.Empty<SalesChoice>());
        notice.Text="";
        Render();
        if(customerId is not Guid id){Changed?.Invoke();return;}
        _=loading.ScheduleAsync(false,()=>false,async ct=>
        {
            var result=await load(id,ct);
            if(ct.IsCancellationRequested || (customer.SelectedItem as SalesChoice)?.Id!=id) return;
            current=result.CustomerAddresses;
            if(current is null)
            {
                notice.Kind=NoticeKind.Error;
                notice.Text=savedMessage is null?SalesInvoiceViewModel.Error(result):savedMessage+" Reload the customer to see it.";
            }
            else
            {
                Select(bill,true,preferredBill);
                Select(ship,false,preferredShip);
                if(savedMessage is not null){notice.Kind=NoticeKind.Success;notice.Text=savedMessage;}
            }
            IsLoading=false;
            Render();
            Changed?.Invoke();
        });
    }

    private void Select(ComboBox combo,bool billing,Guid? preferred)
    {
        var available=current!.Addresses.Where(a=>a.AddressType=="Both" || a.AddressType==(billing?"Billing":"Shipping")).ToArray();
        var choices=available.Select(a=>new SalesChoice(a.Id!.Value,a.Label+" · "+a.City)).ToArray();
        SearchableComboBox.SetSource(combo,choices);
        var selected=available.FirstOrDefault(x=>x.Id==preferred && preferred.HasValue)??
            available.FirstOrDefault(x=>billing?x.IsDefaultBilling:x.IsDefaultShipping)??
            (available.Length==1?available[0]:null);
        combo.SelectedItem=choices.FirstOrDefault(x=>x.Id==selected?.Id);
    }

    private CustomerAddressDraft? Address(bool billing)=>
        current?.Addresses.FirstOrDefault(a=>a.Id==(billing?BillingId:ShippingId));

    private void Render()
    {
        var billing=Address(true);var shipping=Address(false);
        billText.Text=Describe(billing,"billing");
        shipText.Text=Describe(shipping,"shipping");
        bill.IsEnabled=ship.IsEnabled=current is not null && !IsLoading && !IsEditing;
        billEdit.IsEnabled=shipEdit.IsEnabled=canEdit && current is not null && !IsLoading && !IsEditing;
        copy.IsEnabled=shipEdit.IsEnabled && billing is not null;
        billEdit.Content=billing is null?"Add Address":form.Caption("EditBilling","Edit Address");
        shipEdit.Content=shipping is null?"Add Address":form.Caption("EditShipping","Edit Address");
    }

    private string Describe(CustomerAddressDraft? address,string side)
    {
        if(IsLoading) return "Loading saved addresses…";
        if(current is null) return "Select a customer to see saved addresses.";
        if(address is null) return "Select or add a "+side+" address.";
        var state=address.StateName??(address.StateCode is { } code?CompanyRules.States.GetValueOrDefault(code,code):null);
        return string.Join(Environment.NewLine,new[]{current.Name,address.RecipientName,address.Organization,address.Line1,
            address.Line2,address.Landmark,address.Locality,address.City,address.District,state,address.PostalCode,address.Country,
            form.Field("CustomerGstin").Visible && current.Gstin is {Length:>0} gstin?
                form.Field("CustomerGstin").Label+": "+gstin:null}.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct());
    }

    private void Edit(bool billing,bool copyBilling)
    {
        if(!canEdit || IsLoading || IsEditing || current is not { } selected) return;
        var existing=Address(billing);
        var original=copyBilling?Address(true):existing;
        if(copyBilling && original is null) return;
        var side=billing?"Billing":"Shipping";
        var dedicated=existing?.AddressType==side?existing:null;
        var input=(original??new CustomerAddressDraft {RecipientName=selected.Name,Label=side}) with
        {
            Id=dedicated?.Id,Version=dedicated?.Version??0,AddressType=side,
            Label=dedicated?.Label??(existing is null && !copyBilling?original?.Label??side:side),
            IsDefaultBilling=false,IsDefaultShipping=false
        };
        var editor=billing?billEditor:shipEditor;
        var summary=billing?billText:shipText;
        var savedBilling=BillingId;
        var savedShipping=ShippingId;
        var customerWasEnabled=customer.IsEnabled;
        IsEditing=true;
        customer.IsEnabled=false;
        summary.Visibility=Visibility.Collapsed;
        Render();
        if(existing is {AddressType:"Both"})
            editor.Children.Add(new TextBlock {Text="Saving creates a separate address for this side.",
                TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,8)});
        var fields=new Dictionary<string,Control>();
        TextBox Field(string property,string label,string? text,int limit)
        {
            var field=owner.Text(label,text,limit,panel:editor);
            fields.Add(property,field);
            return field;
        }
        var label=Field(nameof(input.Label),"Label *",input.Label,100);
        var recipient=Field(nameof(input.RecipientName),"Recipient name *",input.RecipientName,160);
        var organization=Field(nameof(input.Organization),"Organization",input.Organization,160);
        var line1=Field(nameof(input.Line1),"Address line 1 *",input.Line1,250);
        var line2=Field(nameof(input.Line2),"Address line 2",input.Line2,250);
        var landmark=Field(nameof(input.Landmark),"Landmark",input.Landmark,120);
        var locality=Field(nameof(input.Locality),"Locality",input.Locality,120);
        var city=Field(nameof(input.City),"City *",input.City,120);
        var district=Field(nameof(input.District),"District",input.District,120);
        var country=Field(nameof(input.Country),"Country code *",input.Country,2);
        var states=CompanyRules.States.Select(x=>new SalesChoice(Guid.Empty,x.Value,Code:x.Key)).OrderBy(x=>x.Label).ToArray();
        var state=owner.Choice("Indian state / UT",states,states.FirstOrDefault(x=>x.Code==input.StateCode),
            nameof(SalesChoice.Label),searchable:true);
        fields.Add(nameof(input.StateCode),state);
        var stateName=Field(nameof(input.StateName),"State or province outside India",input.StateName,120);
        var postal=Field(nameof(input.PostalCode),"PIN / postal code",input.PostalCode,20);
        var mobile=Field(nameof(input.Mobile),"Mobile",input.Mobile,50);
        var email=Field(nameof(input.Email),"Email",input.Email,160);
        var editorActions=new WrapPanel();
        editor.Children.Add(editorActions);
        var refreshAfterConflict=false;
        owner.Save(async ()=>
        {
            var proposal=input with {Label=label.Text,RecipientName=recipient.Text,Organization=organization.Text,Line1=line1.Text,
                Line2=line2.Text,Landmark=landmark.Text,Locality=locality.Text,City=city.Text,District=district.Text,
                Country=country.Text,StateCode=(state.SelectedItem as SalesChoice)?.Code,StateName=stateName.Text,
                PostalCode=postal.Text,Mobile=mobile.Text,Email=email.Text};
            var validated=CustomerRules.ValidateAddress(proposal);
            ApplyErrors(validated.Errors);
            if(validated.Errors.Count>0) return "Correct the highlighted address fields.";
            var verb=proposal.Id.HasValue?"Update":"Add";
            if(MessageBox.Show(owner,$"{verb} this {side.ToLowerInvariant()} address in Customer Master? This saves it immediately.",
                "Confirm customer address",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)
                return "Address was not saved.";
            var result=await save(selected.CustomerId,selected.Version,validated.Value,
                existing is {AddressType:"Both"}?existing.Id:null,CancellationToken.None);
            if(result.Outcome==CustomerOutcome.Success && result.AddressId is Guid id)
            {
                CloseEditor();
                Reload(billing?id:savedBilling,billing?savedShipping:id,side+" address saved to Customer Master.");
                return null;
            }
            refreshAfterConflict=result.Outcome==CustomerOutcome.Conflict;
            if(result.Errors is {Count:>0} errors)
            {
                ApplyErrors(errors);
                return errors.Values.First();
            }
            return result.Outcome switch
            {
                CustomerOutcome.Conflict=>"The customer or address changed. Cancel address edit to reload before trying again.",
                CustomerOutcome.Forbidden=>"Customer View and Edit permissions are required to change the address.",
                CustomerOutcome.SessionRequired=>"Your session expired. Sign in again.",
                CustomerOutcome.CompanyChanged=>"The active company changed. Reopen the invoice.",
                _=>"The address could not be saved."
            };
        },"Save to Customer Master",panel:editorActions);

        void ApplyErrors(IReadOnlyDictionary<string,string> errors)
        {
            Control? first=null;
            foreach(var (key,field) in fields)
            {
                errors.TryGetValue(key,out var error);
                owner.SetFieldError(field,error);
                if(error is not null) first??=field;
            }
            if(first is not null) owner.FocusInvalidField(first);
        }
        foreach(var field in fields.Values.OfType<TextBox>()) field.TextChanged+=(_,_)=>owner.SetFieldError(field,null);
        state.SelectionChanged+=(_,_)=>owner.SetFieldError(state,null);

        var cancel=new Button {Content="Cancel address edit",Margin=new Thickness(4),Padding=new Thickness(9,4,9,4)};
        editorActions.Children.Add(cancel);
        cancel.Click+=(_,_) =>
        {
            if(MessageBox.Show(owner,"Discard changes to this address?","Cancel address edit",
                MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes) return;
            CloseEditor();
            if(refreshAfterConflict) Reload(savedBilling,savedShipping);
        };
        owner.FocusInvalidField(line1);

        void CloseEditor()
        {
            owner.RemoveEditorFields(fields.Values);
            editor.Children.Clear();
            summary.Visibility=Visibility.Visible;
            customer.IsEnabled=customerWasEnabled;
            IsEditing=false;
            owner.ShowMessage("");
            Render();
            Changed?.Invoke();
        }
    }
}
