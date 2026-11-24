using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace GTS.Billing.Domain;

public sealed record CustomerValidation(CustomerDraft Value,IReadOnlyDictionary<string,string> Errors);
public static class CustomerRules
{
    public const decimal MoneyScale=10000m;
    public const decimal MaximumMoney=99999999999999.9999m;
    public static IReadOnlyList<string> Types {get;}=["Individual","Business","Government","Other"];
    public static IReadOnlyList<string> Statuses {get;}=["Active","Inactive","Blocked"];
    public static IReadOnlyList<string> Registrations {get;}=["Regular","Composition","Unregistered","Consumer","SezUnit","SezDeveloper","GovernmentSpecial"];
    public static IReadOnlyList<string> AddressTypes {get;}=["Billing","Shipping","Both"];
    public static IReadOnlyList<string> DeliveryMethods {get;}=["Print","Email","Sms","Whatsapp","Other"];
    public static IReadOnlyList<string> BalanceNatures {get;}=["Debit","Credit","Zero"];
    public static IReadOnlyList<string> Currencies {get;}=BuildCurrencies();
    public static string NormalizeName(string? text)=>(text ?? "").Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
    public static string? NormalizePhone(string? text)
    {
        if(string.IsNullOrWhiteSpace(text)) return null;
        var value=new string(text.Where(char.IsAsciiDigit).ToArray());
        // Compare common Indian display variants, without modifying the displayed number.
        if(value.Length==12 && value.StartsWith("91",StringComparison.Ordinal)) value=value[2..];
        if(value.Length==14 && value.StartsWith("0091",StringComparison.Ordinal)) value=value[4..];
        return value;
    }
    private static IReadOnlyList<string> BuildCurrencies()
    {
        var codes=new HashSet<string>(StringComparer.Ordinal) {"INR"};
        foreach(var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
            try {codes.Add(new RegionInfo(culture.Name).ISOCurrencySymbol);} catch(ArgumentException) { }
        return codes.Order(StringComparer.Ordinal).ToArray();
    }
    private static bool Match(string? text,string pattern)=>text is not null && Regex.IsMatch(text,pattern,RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static string? Clean(string? text)=>string.IsNullOrWhiteSpace(text)?null:text.Trim();
    private static string? Upper(string? text)=>Clean(text)?.ToUpperInvariant();
    private static void TrimStrings(object item)
    {
        foreach(var property in item.GetType().GetProperties().Where(x=>x.CanWrite && x.PropertyType==typeof(string)))
            property.SetValue(item,Clean((string?)property.GetValue(item)));
    }
    private static void Lengths(object item,string prefix,Dictionary<string,string> errors)
    {
        foreach(var property in item.GetType().GetProperties().Where(x=>x.PropertyType==typeof(string)))
        {
            var maximum=property.Name switch {
                "Code"=>40,"Notes" or "InternalRemarks" or "ExemptionNotes" or "Remarks"=>4000,
                "BillingInstructions" or "ReferenceNotes"=>2000,"Line1" or "Line2" or "PaymentTerms"=>500,
                "Email"=>254,_=>200};
            if(((string?)property.GetValue(item))?.Length>maximum) errors[prefix+property.Name]=$"Maximum {maximum} characters.";
            if(property.GetValue(item) is string text)
            {
                for(var i=0;i<text.Length;i++)
                {
                    if(char.IsHighSurrogate(text[i]))
                    {
                        if(i+1<text.Length&&char.IsLowSurrogate(text[i+1])) {i++;continue;}
                        errors[prefix+property.Name]="Text contains invalid Unicode.";break;
                    }
                    if(char.IsLowSurrogate(text[i])||text[i]=='\0') {errors[prefix+property.Name]="Text contains an unsupported character.";break;}
                }
            }
        }
    }
    private static void Required(string? text,string key,Dictionary<string,string> errors)
    {if(string.IsNullOrWhiteSpace(text)) errors[key]="This field is required.";}
    private static void Phone(string? value,string key,Dictionary<string,string> errors)
    {
        if(value is null) return;
        var digits=NormalizePhone(value)!;
        if(!Match(value,@"^\+?[0-9 ()\.\-]+$") || digits.Length is < 7 or > 15)
            errors[key]="Enter a reasonable telephone number (7–15 digits; display separators allowed).";
    }
    private static void Email(string? value,string key,Dictionary<string,string> errors)
    {if(value is not null && (!MailAddress.TryCreate(value,out var mail)||mail.Address!=value)) errors[key]="Enter a valid email address.";}
    private static bool Country(string value)
    {
        if(!Match(value,"^[A-Z]{2}$")) return false;
        try {return new RegionInfo(value).TwoLetterISORegionName==value;} catch(ArgumentException) {return false;}
    }
    public static bool ValidMoney(decimal amount)=>amount>=0 && amount<=MaximumMoney && decimal.Truncate(amount*MoneyScale)==amount*MoneyScale;
    public static CustomerValidation Validate(CustomerDraft input, bool assignCodeOnSave=false)
    {
        var errors=new Dictionary<string,string>();
        if(input.Credit is null || input.Opening is null || input.Addresses is null || input.Contacts is null ||
            input.Addresses.Count>50 || input.Contacts.Count>50 || input.Addresses.Any(x=>x is null) || input.Contacts.Any(x=>x is null))
            return new(input,new Dictionary<string,string>{{"General","Credit/opening data is required; at most 50 addresses and contacts are supported."}});
        var v=input with {Credit=input.Credit with {},Opening=input.Opening with {},Bank=input.Bank is null?null:input.Bank with {},
            Addresses=input.Addresses.Select(x=>x with {}).ToArray(),Contacts=input.Contacts.Select(x=>x with {}).ToArray()};
        TrimStrings(v);TrimStrings(v.Credit);TrimStrings(v.Opening);
        if(v.Bank is not null) TrimStrings(v.Bank);
        foreach(var contact in v.Contacts) TrimStrings(contact);
        v=v with {Code=Upper(v.Code) ?? "",LegalName=Clean(v.LegalName) ?? "",Gstin=Upper(v.Gstin),Pan=Upper(v.Pan),
            Credit=v.Credit with {Currency=Upper(v.Credit.Currency) ?? "INR"},
            Bank=v.Bank is null?null:v.Bank with {Ifsc=Upper(v.Bank.Ifsc)},
            Addresses=v.Addresses.Select(NormalizeAddress).ToArray(),
            Contacts=v.Contacts.Select(c=>c with {Name=c.Name ?? ""}).ToArray()};
        Lengths(v,"",errors);Lengths(v.Credit,"Credit.",errors);Lengths(v.Opening,"Opening.",errors);
        if(!assignCodeOnSave && !Match(v.Code,"^[A-Z0-9][A-Z0-9/_-]{0,39}$")) errors["Code"]="Use 1–40 letters, digits, slash, underscore or hyphen.";
        Required(v.LegalName,"LegalName",errors);
        if(v.AadhaarLastFour is { } suffix && (suffix.Length!=4 || !suffix.All(char.IsAsciiDigit)))
            errors[nameof(v.AadhaarLastFour)]="Enter only the last four Aadhaar digits, or leave the field blank.";
        if(!Types.Contains(v.CustomerType)) errors["CustomerType"]="Select a customer type.";
        if(!Statuses.Contains(v.Status)) errors["Status"]="Select a supported status.";
        if(!Registrations.Contains(v.RegistrationType)) errors["RegistrationType"]="Select a supported registration type.";
        Phone(v.Mobile,"Mobile",errors);Email(v.Email,"Email",errors);
        if(v.GstStateCode is not null && !CompanyRules.States.ContainsKey(v.GstStateCode)) errors["GstStateCode"]="Select a supported GST state/UT.";
        if(v.Credit.CreditDays is <0 or >36500) errors["Credit.CreditDays"]="Credit days must be between 0 and 36,500.";
        if(!ValidMoney(v.Credit.CreditLimit)) errors["Credit.CreditLimit"]="Use a nonnegative amount within the supported range and at most four decimal places.";
        if(!Currencies.Contains(v.Credit.Currency)) errors["Credit.Currency"]="Select a currency recognized by this installed .NET region data.";
        if(!DeliveryMethods.Contains(v.Credit.DeliveryMethod)) errors["Credit.DeliveryMethod"]="Select an invoice-delivery preference.";
        if(!ValidMoney(v.Opening.Amount)) errors["Opening.Amount"]="Use a nonnegative absolute amount with at most four decimal places.";
        if(!BalanceNatures.Contains(v.Opening.Nature) || (v.Opening.Amount==0 && v.Opening.Nature!="Zero") ||
            (v.Opening.Amount>0 && (v.Opening.Nature=="Zero" || v.Opening.AsOfDate is null)))
            errors["Opening.Nature"]="Zero amount requires Zero nature; a nonzero Debit/Credit draft requires an as-of date.";
        if(v.Bank is not null)
        {
            Lengths(v.Bank,"Bank.",errors);
            if(v.Bank.UpiId is not null && !Match(v.Bank.UpiId,"^[A-Za-z0-9._-]{2,100}@[A-Za-z0-9]{2,50}$")) errors["Bank.UpiId"]="Enter a valid UPI ID.";
        }
        if(v.Addresses.Where(x=>x.Id is not null).GroupBy(x=>x.Id).Any(g=>g.Count()>1)) errors["Addresses"]="An address cannot appear twice.";
        if(v.Contacts.Where(x=>x.Id is not null).GroupBy(x=>x.Id).Any(g=>g.Count()>1)) errors["Contacts"]="A contact cannot appear twice.";
        if(v.Addresses.Count(x=>x.IsDefaultBilling)>1) errors["Addresses"]="Choose at most one default billing address.";
        if(v.Addresses.Count(x=>x.IsDefaultShipping)>1) errors["Addresses"]="Choose at most one default shipping address.";
        if(v.Contacts.Count(x=>x.IsPrimary)>1) errors["Contacts"]="Choose at most one primary contact.";
        for(var i=0;i<v.Addresses.Count;i++)
        {
            ValidateAddressFields(v.Addresses[i],$"Addresses[{i}].",errors);
        }
        for(var i=0;i<v.Contacts.Count;i++)
        {
            var c=v.Contacts[i];var p=$"Contacts[{i}].";Lengths(c,p,errors);Required(c.Name,p+"Name",errors);
            if(c.IsPrimary&&!c.IsActive) errors[p+"IsPrimary"]="Primary contact must be active.";
            Phone(c.Mobile,p+"Mobile",errors);Phone(c.AlternatePhone,p+"AlternatePhone",errors);Phone(c.Whatsapp,p+"Whatsapp",errors);Email(c.Email,p+"Email",errors);
        }
        return new(v,errors);
    }

    private static CustomerAddressDraft NormalizeAddress(CustomerAddressDraft source)
    {
        var a=source with {};
        TrimStrings(a);
        var country=Upper(a.Country)??"";
        return a with {Label=a.Label??"",RecipientName=a.RecipientName??"",Line1=a.Line1??"",City=a.City??"",
            PostalCode=a.PostalCode??"",Country=country,StateCode=Upper(a.StateCode),
            StateName=country=="IN" && a.StateCode is not null?CompanyRules.States.GetValueOrDefault(a.StateCode,a.StateName??""):a.StateName};
    }

    public static (CustomerAddressDraft Value,IReadOnlyDictionary<string,string> Errors) ValidateAddress(CustomerAddressDraft source)
    {
        var address=NormalizeAddress(source);
        var errors=new Dictionary<string,string>();
        ValidateAddressFields(address,"",errors);
        return (address,errors);
    }

    private static void ValidateAddressFields(CustomerAddressDraft a,string p,Dictionary<string,string> errors)
    {
            Lengths(a,p,errors);
            Required(a.Label,p+"Label",errors);Required(a.RecipientName,p+"RecipientName",errors);
            Required(a.Line1,p+"Line1",errors);Required(a.City,p+"City",errors);
            if(!AddressTypes.Contains(a.AddressType)) errors[p+"AddressType"]="Choose Billing, Shipping or Both.";
            if(!Country(a.Country)) errors[p+"Country"]="Use a recognized two-letter ISO country code.";
            if(a.Country=="IN")
            {
                if(a.StateCode is null || !CompanyRules.States.ContainsKey(a.StateCode)) errors[p+"StateCode"]="Choose an Indian state/UT.";
            }
            if(a.IsDefaultBilling && (!a.IsActive || a.AddressType=="Shipping")) errors[p+"IsDefaultBilling"]="Default billing requires an active Billing/Both address.";
            if(a.IsDefaultShipping && (!a.IsActive || a.AddressType=="Billing")) errors[p+"IsDefaultShipping"]="Default shipping requires an active Shipping/Both address.";
            Phone(a.Mobile,p+"Mobile",errors);Email(a.Email,p+"Email",errors);
    }
}


