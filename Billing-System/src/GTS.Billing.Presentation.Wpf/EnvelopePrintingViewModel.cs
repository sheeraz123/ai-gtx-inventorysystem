using CommunityToolkit.Mvvm.ComponentModel;
using GTS.Billing.Application;
using GTS.Billing.Domain;

namespace GTS.Billing.Presentation.Wpf;

public sealed partial class EnvelopeInput(string key,EnvelopeField rule):ObservableObject
{
    public string Key {get;}=key;
    public EnvelopeField Field {get;}=rule;
    [ObservableProperty] private string _value=rule.Default;
}

public sealed partial class EnvelopePrintingViewModel(EnvelopePrintingService printing):ObservableObject
{
    private readonly MasterSearch search=new(),selection=new();
    private AccessSnapshot access=AccessSnapshot.Denied;
    private bool active;
    public Guid? Company=>access.CurrentCompanyId;
    public bool CanConfigure=>access.IsSuperAdmin && Company.HasValue;
    public bool CanPrint=>active && Company.HasValue && access.CanNavigate("envelope-printing");
    public EnvelopeMedia Media {get;private set;}=new();
    public IReadOnlyList<EnvelopeInput> Fields {get;private set;}=[];
    public event Action? PreviewChanged;
    [ObservableProperty] private string _query="";
    [ObservableProperty] private string _message="";
    [ObservableProperty] private IReadOnlyList<EnvelopeCustomer> _matches=[];
    [ObservableProperty] private EnvelopeCustomer? _selected;
    [ObservableProperty] private IReadOnlyList<CustomerAddressDraft> _shipping=[];
    [ObservableProperty] private CustomerAddressDraft? _selectedShipping;
    private EnvelopeRecipient? recipient;

    internal void SetAccess(AccessSnapshot next)
    {
        var reset=Company!=next.CurrentCompanyId || !next.CanNavigate("envelope-printing");
        access=next;
        if(reset) Reset();
        OnPropertyChanged(nameof(CanConfigure));
        OnPropertyChanged(nameof(CanPrint));
    }

    internal void Open(bool visible)
    {
        active=visible;
        Reset();
        if(visible) LoadConfiguration();
    }

    internal void LoadConfiguration()
    {
        Media=SettingsDisplay.Current.Get(EnvelopeConfiguration.Media);
        var previous=Fields.ToDictionary(x=>x.Key,x=>x.Value);
        Fields=EnvelopeConfiguration.Fields.Select(key=>new EnvelopeInput(key.Key.Split('.')[^1],SettingsDisplay.Current.Get(key)))
            .OrderBy(x=>x.Field.Order).ToArray();
        foreach(var field in Fields)
        {
            if(previous.TryGetValue(field.Key,out var value)) field.Value=value;
            field.PropertyChanged+=(_,_)=>PreviewChanged?.Invoke();
        }
        OnPropertyChanged(nameof(Fields));
        PreviewChanged?.Invoke();
    }

    private void Reset()
    {
        search.Cancel();selection.Cancel();Matches=[];Selected=null;Query="";Message="";
        recipient=null;Shipping=[];SelectedShipping=null;Fields=[];
        OnPropertyChanged(nameof(Fields));
        PreviewChanged?.Invoke();
    }

    partial void OnQueryChanged(string? oldValue,string newValue)
    {
        var query=MasterSearch.Query(newValue);
        if(query==MasterSearch.Query(oldValue)) return;
        search.Cancel();
        if(!CanPrint || Company is not Guid company || query.Length==0){Matches=Selected is { } current?[current]:[];return;}
        _=search.ScheduleAsync(false,()=>false,async ct=>
        {
            var result=await Task.Run(()=>printing.LookupAsync(company,query,null,ct),ct);
            if(ct.IsCancellationRequested || Company!=company || !CanPrint) return;
            var matches=result.Matches??[];
            Matches=Selected is { } selected?[selected,..matches.Where(x=>x.Id!=selected.Id)]:matches;
            Message=result.Outcome==CompanyOutcome.Success?
                (matches.Count==0?"No customers match the search.":""):LookupError(result.Outcome);
        });
    }

    partial void OnSelectedChanged(EnvelopeCustomer? value)
    {
        selection.Cancel();recipient=null;Shipping=[];SelectedShipping=null;
        SetRecipient(null);
        if(value is null || !CanPrint || Company is not Guid company) return;
        _=selection.ScheduleAsync(false,()=>false,async ct=>
        {
            var result=await Task.Run(()=>printing.LookupAsync(company,"",value.Id,ct),ct);
            if(ct.IsCancellationRequested || Company!=company || !CanPrint || Selected?.Id!=value.Id) return;
            recipient=result.Recipient;
            if(recipient is null){Message=LookupError(result.Outcome);return;}
            Shipping=recipient.Addresses;
            var defaults=Shipping.Where(x=>x.IsDefaultShipping).Take(2).ToArray();
            SelectedShipping=defaults.Length==1?defaults[0]:Shipping.Count==1?Shipping[0]:null;
            SetRecipient(SelectedShipping);
            Message=SelectedShipping is not null?"":Shipping.Count>1?"Choose a shipping address.":
                "No shipping address is saved. Enter a temporary address for this envelope.";
        });
    }

    partial void OnSelectedShippingChanged(CustomerAddressDraft? value)=>SetRecipient(value);

    private void SetRecipient(CustomerAddressDraft? address)
    {
        foreach(var field in Fields)
        {
            var value=field.Key switch
            {
                "Name"=>recipient?.Name,
                "Street"=>address is null?null:string.Join(", ",new[]{address.Line1,address.Line2,address.Landmark,address.Locality}
                    .Where(x=>!string.IsNullOrWhiteSpace(x))),
                "City"=>address?.City,
                "State"=>address?.StateName??(address?.StateCode is { } code?CompanyRules.States.GetValueOrDefault(code):null),
                "Pin"=>address?.PostalCode,
                "Mobile"=>address?.Mobile??recipient?.Mobile,
                _=>null
            };
            field.Value=string.IsNullOrWhiteSpace(value)?field.Field.Default:value;
        }
    }

    private static string LookupError(CompanyOutcome outcome)=>outcome switch
    {
        CompanyOutcome.SessionRequired=>"Your session expired. Sign in again.",
        CompanyOutcome.Forbidden=>"Customer View and Envelope Printing permissions are required.",
        CompanyOutcome.Conflict=>"The selected company changed. Reopen Envelope Printing.",
        CompanyOutcome.NotFound=>"That customer is no longer available in this company.",
        _=>"The customer lookup could not complete. Check the database connection and try again."
    };

    internal string? ValidateRecipient()
    {
        if(Selected is null || recipient is null) return "Select a customer first.";
        var missing=Fields.FirstOrDefault(x=>x.Field.Visible && x.Field.Required && string.IsNullOrWhiteSpace(x.Value));
        if(!Fields.Any(x=>x.Field.Visible && !string.IsNullOrWhiteSpace(x.Value))) return "Enter recipient details.";
        return missing is null?null:$"Enter {missing.Field.Label}.";
    }

    internal IEnumerable<(EnvelopeField Field,string Text)> Lines()
    {
        yield return (new EnvelopeField("Recipient",0,Bold:true),"To,");
        var visible=Fields.Where(x=>x.Field.Visible).ToArray();
        for(var index=0;index<visible.Length;index++)
        {
            var item=visible[index];
            if(item.Key=="City" && index+1<visible.Length && visible[index+1].Key=="Pin" &&
                string.IsNullOrEmpty(visible[index+1].Field.Prefix) && item.Field.Bold==visible[index+1].Field.Bold)
            {
                var pin=visible[++index];
                yield return (item.Field,string.Join("-",new[]{item.Value.Trim(),pin.Value.Trim()}.Where(value=>value.Length>0)));
            }
            else yield return (item.Field,item.Value.Trim());
        }
    }

    internal Task<CompanyOutcome> AuthorizeAsync(Guid company,Guid customer,EnvelopeMedia media)=>CanPrint && Company==company &&
        (customer==Guid.Empty?CanConfigure:Selected?.Id==customer)
        ?printing.AuthorizeAsync(company,customer,media):Task.FromResult(CompanyOutcome.Forbidden);
}
