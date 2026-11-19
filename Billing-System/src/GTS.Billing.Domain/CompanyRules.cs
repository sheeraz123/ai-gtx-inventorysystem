using System.Net.Mail;
using System.Text.RegularExpressions;

namespace GTS.Billing.Domain;

public sealed record CompanyDraft
{
    public string Code { get; init; } = "";
    public string LegalName { get; init; } = "";
    public string? TradeName { get; init; }
    public string? Tagline { get; init; }
    public string? BusinessType { get; init; }
    public string? EstablishedOn { get; init; }
    public string AddressLine1 { get; init; } = "";
    public string? AddressLine2 { get; init; }
    public string? Locality { get; init; }
    public string City { get; init; } = "";
    public string? District { get; init; }
    public string StateCode { get; init; } = "";
    public string Pin { get; init; } = "";
    public string Country { get; init; } = "IN";
    public string? Mobile { get; init; }
    public string? AlternateMobile { get; init; }
    public string? Landline { get; init; }
    public string? Email { get; init; }
    public string? Website { get; init; }
    public string? ContactPerson { get; init; }
    public string RegistrationType { get; init; } = "Unregistered";
    public string? Gstin { get; init; }
    public string? Pan { get; init; }
    public string? Tan { get; init; }
    public string? Cin { get; init; }
    public string? Udyam { get; init; }
    public string? BankName { get; init; }
    public string? AccountHolder { get; init; }
    public string? AccountNumber { get; init; }
    public string? Ifsc { get; init; }
    public string? Branch { get; init; }
    public string? AccountType { get; init; }
    public string? UpiId { get; init; }
    public string? InvoiceHeader { get; init; }
    public string? InvoiceFooter { get; init; }
    public string? SignatoryName { get; init; }
    public string? Terms { get; init; }
}

public sealed record CompanyValidation(CompanyDraft Value, IReadOnlyDictionary<string, string> Errors);
public static class CompanyRules
{
    // Source: https://docs.ewaybillgst.gov.in/apidocs/state-code.html.
    // Historical 25/28 are not offered for a new registration.
    public static IReadOnlyDictionary<string, string> States { get; } = new Dictionary<string, string>
    {
        ["01"]="Jammu and Kashmir", ["02"]="Himachal Pradesh", ["03"]="Punjab", ["04"]="Chandigarh",
        ["05"]="Uttarakhand", ["06"]="Haryana", ["07"]="Delhi", ["08"]="Rajasthan",
        ["09"]="Uttar Pradesh", ["10"]="Bihar", ["11"]="Sikkim", ["12"]="Arunachal Pradesh",
        ["13"]="Nagaland", ["14"]="Manipur", ["15"]="Mizoram", ["16"]="Tripura", ["17"]="Meghalaya",
        ["18"]="Assam", ["19"]="West Bengal", ["20"]="Jharkhand", ["21"]="Odisha", ["22"]="Chhattisgarh",
        ["23"]="Madhya Pradesh", ["24"]="Gujarat", ["26"]="Dadra and Nagar Haveli and Daman and Diu",
        ["27"]="Maharashtra", ["29"]="Karnataka", ["30"]="Goa", ["31"]="Lakshadweep", ["32"]="Kerala",
        ["33"]="Tamil Nadu", ["34"]="Puducherry", ["35"]="Andaman and Nicobar Islands",
        ["36"]="Telangana", ["37"]="Andhra Pradesh", ["38"]="Ladakh"
    };
    public static IReadOnlyList<string> RegistrationTypes { get; } = ["Unregistered", "Regular", "Composition"];
    private static bool Match(string value, string pattern) =>
        Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? Upper(string? value) => Text(value)?.ToUpperInvariant();

    public static CompanyValidation Validate(CompanyDraft input)
    {
        var values = typeof(CompanyDraft).GetProperties().ToDictionary(p => p.Name, p => Text((string?)p.GetValue(input)));
        var v = input with
        {
            Code=Upper(input.Code) ?? "", LegalName=Text(input.LegalName) ?? "",
            TradeName=Text(input.TradeName), Tagline=Text(input.Tagline), BusinessType=Text(input.BusinessType),
            EstablishedOn=Text(input.EstablishedOn), AddressLine1=Text(input.AddressLine1) ?? "",
            AddressLine2=Text(input.AddressLine2), Locality=Text(input.Locality), City=Text(input.City) ?? "",
            District=Text(input.District), StateCode=Text(input.StateCode) ?? "", Pin=Text(input.Pin) ?? "",
            Country=Upper(input.Country) ?? "IN", Mobile=Text(input.Mobile), AlternateMobile=Text(input.AlternateMobile),
            Landline=Text(input.Landline), Email=Text(input.Email), Website=Text(input.Website),
            ContactPerson=Text(input.ContactPerson), Gstin=Upper(input.Gstin), Pan=Upper(input.Pan),
            Tan=Upper(input.Tan), Cin=Upper(input.Cin), Udyam=Upper(input.Udyam),
            BankName=Text(input.BankName), AccountHolder=Text(input.AccountHolder), AccountNumber=Text(input.AccountNumber),
            Ifsc=Upper(input.Ifsc), Branch=Text(input.Branch), AccountType=Text(input.AccountType),
            UpiId=Text(input.UpiId), InvoiceHeader=Text(input.InvoiceHeader), InvoiceFooter=Text(input.InvoiceFooter),
            SignatoryName=Text(input.SignatoryName), Terms=Text(input.Terms)
        };
        var e = new Dictionary<string,string>();
        foreach (var (key, text) in values)
            if (text?.Length > (key == nameof(CompanyDraft.Terms) ? 8000 : key is nameof(CompanyDraft.InvoiceHeader) or nameof(CompanyDraft.InvoiceFooter) or nameof(CompanyDraft.Tagline) ? 1000 : 200))
                e[key] = "Value is too long.";
        if (!Match(v.Code, "^[A-Z0-9][A-Z0-9_-]{0,29}$")) e[nameof(v.Code)]="Use 1–30 letters, digits, underscores or hyphens.";
        if (v.LegalName.Length is < 1 or > 200) e[nameof(v.LegalName)]="Legal name is required (maximum 200 characters).";
        if (v.AddressLine1.Length == 0) e[nameof(v.AddressLine1)]="Address line 1 is required.";
        if (v.City.Length == 0) e[nameof(v.City)]="City is required.";
        if (!States.ContainsKey(v.StateCode)) e[nameof(v.StateCode)]="Select a supported Indian state/UT.";
        if (!Match(v.Pin, "^[1-9][0-9]{5}$")) e[nameof(v.Pin)]="Enter a six-digit Indian PIN.";
        if (v.Country != "IN") e[nameof(v.Country)]="Stage 4A supports Indian company addresses only.";
        if (v.EstablishedOn is not null && (!DateOnly.TryParseExact(v.EstablishedOn,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,out var established) || established > DateOnly.FromDateTime(DateTime.Today)))
            e[nameof(v.EstablishedOn)]="Use a valid date YYYY-MM-DD, no later than today.";
        if (!RegistrationTypes.Contains(v.RegistrationType)) e[nameof(v.RegistrationType)]="Select a supported registration type.";
        if (v.Pan is not null && !Match(v.Pan, "^[A-Z]{5}[0-9]{4}[A-Z]$")) e[nameof(v.Pan)]="PAN must contain five letters, four digits and one letter.";
        foreach (var name in new[]{nameof(v.Mobile),nameof(v.AlternateMobile),nameof(v.Landline)})
        {
            var number = (string?)typeof(CompanyDraft).GetProperty(name)!.GetValue(v);
            if (number is null) continue;
            var digits = new string(number.Where(char.IsAsciiDigit).ToArray());
            if (!Match(number,@"^\+?[0-9 ()-]+$") || (name==nameof(v.Landline) ? digits.Length is < 6 or > 15 :
                !(digits.Length==10 && digits[0] is >= '6' and <= '9' || digits.Length==12 && digits.StartsWith("91") && digits[2] is >= '6' and <= '9')))
                e[name]="Enter a valid contact number; spaces, brackets and hyphens are allowed.";
        }
        if (v.Email is not null && (!MailAddress.TryCreate(v.Email,out var mail) || mail.Address!=v.Email))
            e[nameof(v.Email)]="Enter a valid email address.";
        if (v.Website is not null && (!Uri.TryCreate(v.Website,UriKind.Absolute,out var uri) || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo)))
            e[nameof(v.Website)]="Use an absolute http/https URL without credentials.";
        var bankPresent = new[]{v.BankName,v.AccountHolder,v.AccountNumber,v.Ifsc,v.Branch,v.AccountType,v.UpiId}.Any(x=>x is not null);
        if (bankPresent)
        {
            if (v.BankName is null) e[nameof(v.BankName)]="Bank name is required when entering bank details.";
            if (v.AccountHolder is null) e[nameof(v.AccountHolder)]="Account holder is required.";
            if (v.AccountNumber is null || !Match(v.AccountNumber,"^[0-9]{6,34}$")) e[nameof(v.AccountNumber)]="Enter 6–34 account digits.";
            if (v.UpiId is not null && !Match(v.UpiId,"^[A-Za-z0-9._-]{2,100}@[A-Za-z0-9]{2,50}$")) e[nameof(v.UpiId)]="Enter a valid UPI ID.";
        }
        return new(v,e);
    }

}

