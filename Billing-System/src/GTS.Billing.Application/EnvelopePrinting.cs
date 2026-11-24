using System.Text.Json;
using GTS.Billing.Domain;

namespace GTS.Billing.Application;

public sealed record EnvelopeMedia(double Width=0,double Height=0,double X=0,double Y=0,double TextWidth=0,
    double TextHeight=0,double Margin=0,string Font="Arial",double Size=12,double LineGap=1,
    string Alignment="Left",string Printer="",int Copies=1,string Orientation="Landscape");

public sealed record EnvelopeField(string Label,int Order,bool Visible=true,bool Editable=true,bool Required=false,
    bool Bold=false,string Prefix="",string Default="");

public static class EnvelopeConfiguration
{
    public const string Section="Envelope Printing Configuration";
    public static readonly SettingKey<EnvelopeMedia> Media=Definition("Media","Envelope media and position",
        new EnvelopeMedia(Width:220,Height:110,X:45,Y:30,TextWidth:160,TextHeight:65,Margin:5),
        Valid,
        "Enter valid envelope dimensions, text position, margins, font, alignment, orientation and copies.");
    public static IReadOnlyList<SettingKey<EnvelopeField>> Fields {get;}=[
        Field("Name",new("Customer name",10,Required:true,Bold:true)),
        Field("Street",new("Address",20,Required:true,Bold:true)),
        Field("State",new("State",30,Required:true)),
        Field("City",new("City",40,Required:true)),
        Field("Pin",new("PIN code",50,Required:true)),
        Field("Mobile",new("Mobile number",60,Prefix:"Mob: "))
    ];
    public static IEnumerable<SettingDefinition> Definitions=>Fields.Cast<SettingDefinition>().Prepend(Media);

    public static bool Calibrated(EnvelopeMedia media)=>Valid(media) && media.Width>0 && media.Height>0;

    private static bool Valid(EnvelopeMedia m)=>
        new[]{m.Width,m.Height,m.X,m.Y,m.TextWidth,m.TextHeight,m.Margin,m.Size,m.LineGap}.All(double.IsFinite) &&
        m.Width is >=0 and <=600 && m.Height is >=0 and <=600 && m.Margin is >=0 and <=50 &&
        m.X>=0 && m.Y>=0 && m.TextWidth>=0 && m.TextHeight>=0 && m.Size is >=6 and <=40 &&
        m.LineGap is >=0 and <=12 && m.Copies is >=1 and <=5 &&
        m.Alignment is "Left" or "Center" or "Right" && m.Orientation is "Portrait" or "Landscape" &&
        Plain(m.Font,80) && m.Font.Length>0 && Plain(m.Printer,150) &&
        (m.Width==0 && m.Height==0 || m.Width>=30 && m.Height>=30 && m.TextWidth>0 && m.TextHeight>0 &&
            m.X>=m.Margin && m.Y>=m.Margin && m.X+m.TextWidth+m.Margin<=m.Width &&
            m.Y+m.TextHeight+m.Margin<=m.Height &&
            (m.Orientation=="Landscape"?m.Width>=m.Height:m.Height>=m.Width));

    private static SettingKey<EnvelopeField> Field(string key,EnvelopeField defaults)=>Definition(key,defaults.Label,defaults,
        r=>Plain(r.Label,60) && r.Label.Length>0 && r.Order is >=0 and <=9999 && Plain(r.Prefix,32) && Plain(r.Default,200),
        "Use a valid label, display order, visibility, editability, requirement, format, prefix and default.");
    private static bool Plain(string? value,int max)=>value is not null && value.Length<=max && !value.Any(char.IsControl);
    private static SettingKey<T> Definition<T>(string key,string label,T defaults,Func<T,bool> valid,string help) where T:class
    {
        return new("Configuration.Envelope."+key,Section,label,help,SettingScope.Company,SettingKind.Text,defaults,
            text=>
            {
                try
                {
                    var value=JsonSerializer.Deserialize<T>(text);
                    return value is not null && valid(value)?(true,value):(false,defaults);
                }
                catch(JsonException){return (false,defaults);}
            },value=>JsonSerializer.Serialize(value));
    }
}

public interface IEnvelopePrintingStore
{
    Task<EnvelopeCustomers> LookupAsync(AuthenticatedIdentity actor,Guid company,string query,Guid? customer,CancellationToken ct);
    Task<CompanyOutcome> AuthorizeAsync(AuthenticatedIdentity actor,Guid company,Guid customer,EnvelopeMedia media,CancellationToken ct);
}

public sealed record EnvelopeCustomer(Guid Id,string Code,string Name)
{
    public string Label=>$"{Code} — {Name}";
}
public sealed record EnvelopeRecipient(string Name,string? Mobile,IReadOnlyList<CustomerAddressDraft> Addresses);
public sealed record EnvelopeCustomers(CompanyOutcome Outcome,IReadOnlyList<EnvelopeCustomer>? Matches=null,EnvelopeRecipient? Recipient=null);

public sealed class EnvelopePrintingService(AuthenticationService authentication,IApplicationOperationCoordinator lifetime,
    IEnvelopePrintingStore store)
{
    public async Task<EnvelopeCustomers> LookupAsync(Guid company,string query,Guid? customer,CancellationToken ct)
    {
        var result=new EnvelopeCustomers(CompanyOutcome.Unavailable);
        try
        {
            await lifetime.RunAsync(async token=>result=await authentication.RunAuthenticatedAsync(
                actor=>store.LookupAsync(actor,company,query,customer,token),token),ct);
        }
        catch(UnauthorizedAccessException){return new(CompanyOutcome.SessionRequired);}
        return result;
    }
    public async Task<CompanyOutcome> AuthorizeAsync(Guid company,Guid customer,EnvelopeMedia media,CancellationToken ct=default)
    {
        var outcome=CompanyOutcome.Unavailable;
        try
        {
            await lifetime.RunAsync(async token=>outcome=await authentication.RunAuthenticatedAsync(
                actor=>store.AuthorizeAsync(actor,company,customer,media,token),token),ct);
        }
        catch(UnauthorizedAccessException){return CompanyOutcome.SessionRequired;}
        return outcome;
    }
}
