using System.Collections.Immutable;

namespace GTS.Billing.Application;

public sealed record LoginPresentation(ResolvedSettings Text,byte[]? Logo=null,byte[]? Background=null)
{
    public static LoginPresentation Default {get;}=new(ResolvedSettings.Empty);
    public string Get(string id)=>Text.Get(LoginPage.Keys[id]);
}

public static class LoginPage
{
    public const string Section="Login Page Configuration";
    public static ImmutableDictionary<string,SettingKey<string>> Keys {get;}=new (string Id,string Label,string Default)[]
    {
        ("ApplicationName","Application name","GTS Billing System"),
        ("ClientName","Client name",""), ("Title","Login page title","Sign in to GTS Billing"),
        ("Subtitle","Login subtitle","Use your GTS application credentials."),
        ("Username","Username label","Username"), ("UsernameHint","Username placeholder","Enter your username"),
        ("Password","Password label","Password"), ("PasswordHint","Password placeholder","Enter your password"),
        ("Submit","Sign-in button","Sign in"), ("Busy","Loading text","Signing in…"),
        ("Reveal","Show password label","Show password"), ("Recovery","Forgot password label","Recover Super Admin password"),
        ("Footer","Footer text",""), ("Copyright","Copyright text",""), ("Welcome","Welcome message",""),
        ("Invalid","Invalid-credentials message","Invalid username or password."),
        ("RequiredUsername","Required username message","Enter your username."),
        ("RequiredPassword","Required password message","Enter your password."),
        ("Success","Login success message","Signed in successfully."),
        ("Failure","Login failure message","Unable to sign in. Check availability and try again."),
        ("Primary","Primary color (#RRGGBB)","#15263C"), ("Secondary","Secondary color (#RRGGBB)","#285987"),
        ("Logo","Login logo asset",""), ("Background","Login background asset","")
    }.ToImmutableDictionary(x=>x.Id,x=>new SettingKey<string>("Login."+x.Id,Section,x.Label,
        x.Id is "Logo" or "Background"?"Choose a validated Company Master image or leave blank.":
        x.Id is "Primary" or "Secondary"?"Enter a hexadecimal color such as #15263C.":x.Label,
        SettingScope.Application,SettingKind.Text,x.Default,
        input=>Normalize(x.Id,input,x.Default.Length==0),value=>value),StringComparer.Ordinal);

    private static (bool Valid,string Value) Normalize(string id,string raw,bool optional)
    {
        var text=raw.Trim();
        if(text.Length==0) return (optional,text);
        if(id is "Logo" or "Background")
            return Guid.TryParse(text,out var asset) && asset!=Guid.Empty?(true,asset.ToString()):(false,"");
        if(id is "Primary" or "Secondary")
            return (text.Length==7 && text[0]=='#' && text.Skip(1).All(char.IsAsciiHexDigit),text.ToUpperInvariant());
        return (text.Length<=180 && !text.Any(char.IsControl),text);
    }
}
