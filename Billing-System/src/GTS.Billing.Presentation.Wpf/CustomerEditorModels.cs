using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTS.Billing.Domain;

namespace GTS.Billing.Presentation.Wpf;

public sealed record CustomerChoice(string Key,string Label);
public sealed class CustomerField(string name,string label,string kind="Text",IReadOnlyList<CustomerChoice>? choices=null) : ObservableObject
{
    public string Name {get;}=name;
    public string Label {get;}=label;
    private bool readOnly;
    public bool ReadOnly { get => readOnly; set => SetProperty(ref readOnly,value); }
    public string Kind {get;}=kind;
    public bool IsText=>Kind is "Text" or "Money" or "Integer" or "Multiline";
    public bool IsDate=>Kind=="Date";
    public bool IsCheck=>Kind=="Check";
    public bool IsChoice=>Kind=="Choice";
    public IReadOnlyList<CustomerChoice> Choices {get;}=choices ?? [];
    private string text="";
    public string Text
    {
        get=>text;
        set {if(SetProperty(ref text,value)){OnPropertyChanged(nameof(Checked));OnPropertyChanged(nameof(DateValue));Changed?.Invoke(this);}}
    }
    public bool Checked {get=>IsCheck&&bool.TryParse(Text,out var value)&&value;set {if(IsCheck) Text=value.ToString(CultureInfo.InvariantCulture);}}
    public DateTime? DateValue
    {
        get=>IsDate&&DateTime.TryParseExact(Text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var value)?value:null;
        set {if(IsDate) Text=value?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture) ?? "";}
    }
    private string error="";
    public string Error {get=>error;set=>SetProperty(ref error,value);}
    public event Action<CustomerField>? Changed;
}
public sealed record CustomerFieldSection(string Title,IReadOnlyList<CustomerField> Fields);
public static class CustomerEditorFields
{
    public static string ChoiceLabel(string value)=>value switch {"SezUnit"=>"SEZ Unit","SezDeveloper"=>"SEZ Developer",
        "GovernmentSpecial"=>"Government / Special Entity","Sms"=>"SMS","Whatsapp"=>"WhatsApp",_=>value};
    public static CustomerField Text(string name,string label)=>new(name,label);
    public static CustomerField Date(string name,string label)=>new(name,label,"Date");
    public static CustomerField Check(string name,string label)=>new(name,label,"Check");
    public static CustomerField Money(string name,string label)=>new(name,label,"Money");
    public static CustomerField Integer(string name,string label)=>new(name,label,"Integer");
    public static CustomerField Long(string name,string label)=>new(name,label,"Multiline");
    public static CustomerField Choice(string name,string label,IEnumerable<string> values,bool optional=false)=>
        new(name,label,"Choice",(optional?new[]{new CustomerChoice("","— Not specified —")}:[]).Concat(values.Select(x=>new CustomerChoice(x,ChoiceLabel(x)))).ToArray());
    public static CustomerField State(string name,string label,bool optional=false)=>
        new(name,label,"Choice",(optional?new[]{new CustomerChoice("","— Not specified —")}:[])
            .Concat(CompanyRules.States.OrderBy(x=>x.Value,StringComparer.CurrentCultureIgnoreCase)
                .Select(x=>new CustomerChoice(x.Key,x.Value+" ("+x.Key+")"))).ToArray());
    public static void Load(IEnumerable<CustomerField> fields,object value)
    {
        foreach(var field in fields)
        {
            var item=value.GetType().GetProperty(field.Name)?.GetValue(value);
            field.Text=item switch {null=>"",DateOnly date=>date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
                decimal money=>money.ToString("0.####",CultureInfo.CurrentCulture),_=>Convert.ToString(item,CultureInfo.CurrentCulture) ?? ""};
            field.Error="";
        }
    }
    public static T Read<T>(IEnumerable<CustomerField> fields,string prefix,Dictionary<string,string> errors) where T:new()
    {
        var result=new T();
        foreach(var field in fields)
        {
            var property=typeof(T).GetProperty(field.Name);
            if(property?.CanWrite!=true) continue;
            if(property.PropertyType==typeof(string)) property.SetValue(result,string.IsNullOrWhiteSpace(field.Text)?null:field.Text);
            else if(property.PropertyType==typeof(bool)) property.SetValue(result,field.Checked);
            else if(property.PropertyType==typeof(DateOnly?))
            {
                if(string.IsNullOrWhiteSpace(field.Text)) property.SetValue(result,null);
                else if(DateOnly.TryParseExact(field.Text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)) property.SetValue(result,date);
                else errors[prefix+field.Name]="Choose a valid date.";
            }
            else if(property.PropertyType==typeof(decimal) || property.PropertyType==typeof(decimal?))
            {
                if(property.PropertyType==typeof(decimal?) && string.IsNullOrWhiteSpace(field.Text)) property.SetValue(result,null);
                else if(decimal.TryParse(field.Text,NumberStyles.Number,CultureInfo.CurrentCulture,out var amount)) property.SetValue(result,amount);
                else errors[prefix+field.Name]="Enter a decimal amount.";
            }
            else if(property.PropertyType==typeof(int))
            {
                if(int.TryParse(field.Text,NumberStyles.Integer,CultureInfo.CurrentCulture,out var number)) property.SetValue(result,number);
                else errors[prefix+field.Name]="Enter a whole number.";
            }
        }
        return result;
    }
}
public sealed class CustomerEntryEditor : ObservableObject
{
    public Guid? Id {get;}
    public long Version {get;}
    public bool IsAddress {get;}
    public IReadOnlyList<CustomerField> Fields {get;}
    public IRelayCommand RetireCommand {get;}
    public string RetireLabel=>Id is null?"Discard unsaved entry":"Deactivate entry";
    public string Header=>Fields.First(x=>x.Name==(IsAddress?"Label":"Name")).Text is {Length:>0} name?name:"New entry";
    public CustomerField Field(string name)=>Fields.Single(x=>x.Name==name);
    public CustomerEntryEditor(CustomerAddressDraft address,Action<CustomerEntryEditor> retire)
    {
        Id=address.Id;Version=address.Version;IsAddress=true;
        Fields=[
            CustomerEditorFields.Text("Label","Address label *"),CustomerEditorFields.Choice("AddressType","Address type *",CustomerRules.AddressTypes),
            CustomerEditorFields.Text("RecipientName","Recipient name *"),CustomerEditorFields.Text("Organization","Organization"),
            CustomerEditorFields.Text("Line1","Address line 1 *"),CustomerEditorFields.Text("Line2","Address line 2"),
            CustomerEditorFields.Text("Landmark","Landmark"),CustomerEditorFields.Text("Locality","Area / locality"),
            CustomerEditorFields.Text("City","City *"),CustomerEditorFields.Text("District","District"),
            CustomerEditorFields.State("StateCode","State / UT (required for India)",true),CustomerEditorFields.Text("StateName","State / province (outside India)"),
            CustomerEditorFields.Text("PostalCode","PIN / postal code (optional)"),CustomerEditorFields.Text("Country","Country ISO code * (IN, GB, US…)"),
            CustomerEditorFields.Text("Mobile","Address mobile"),CustomerEditorFields.Text("Email","Address email"),
            CustomerEditorFields.Check("IsDefaultBilling","Default billing address"),CustomerEditorFields.Check("IsDefaultShipping","Default shipping address"),
            CustomerEditorFields.Check("IsActive","Address active")];
        CustomerEditorFields.Load(Fields,address);
        foreach(var field in Fields) field.Changed+=_=>OnPropertyChanged(nameof(Header));
        RetireCommand=new RelayCommand(()=>retire(this));
    }
    public CustomerEntryEditor(CustomerContactDraft contact,Action<CustomerEntryEditor> retire)
    {
        Id=contact.Id;Version=contact.Version;IsAddress=false;
        Fields=[CustomerEditorFields.Text("Name","Contact name *"),CustomerEditorFields.Text("Designation","Designation"),
            CustomerEditorFields.Text("Department","Department"),CustomerEditorFields.Text("Mobile","Mobile"),
            CustomerEditorFields.Text("AlternatePhone","Alternate phone"),CustomerEditorFields.Text("Email","Email"),
            CustomerEditorFields.Text("Whatsapp","WhatsApp number"),CustomerEditorFields.Check("IsPrimary","Primary contact"),
            CustomerEditorFields.Check("IsActive","Contact active"),CustomerEditorFields.Long("Notes","Contact notes")];
        CustomerEditorFields.Load(Fields,contact);
        foreach(var field in Fields) field.Changed+=_=>OnPropertyChanged(nameof(Header));
        RetireCommand=new RelayCommand(()=>retire(this));
    }
}

