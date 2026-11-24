using System.Globalization;

namespace GTS.Billing.Application;

public static class SalesListConfiguration
{
    public const string Filters="Invoice List Filters";
    public const string Columns="Invoice List Columns";
    public const string Actions="Invoice List Actions";
    public const string Classic="Classic Compact";
    public const string Modern="Modern Professional";
    public const string Split="Split View Advanced";
    public static readonly string[] Sections=[Filters,Columns,Actions];
    public sealed record Rule(bool Visible,int Order,string Label,int Width=0,string Default="");
    public static IReadOnlyList<SettingKey<Rule>> Items {get;}=
    [
        Item(Filters,"Category","Invoice category",10),Item(Filters,"From","From date",20),Item(Filters,"To","To date",30),
        Item(Filters,"Customer","Customer",40),Item(Filters,"Number","Invoice number",50),
        Item(Filters,"Year","Financial year",60),Item(Filters,"Series","Series",70),Item(Filters,"Status","Status",80),
        Item(Filters,"Search","Search all invoice fields",90),
        Item(Columns,"Date","Invoice date",10,width:115),Item(Columns,"Number","Invoice number",20,width:140),
        Item(Columns,"Customer","Customer / Party",30,width:230),Item(Columns,"TaxType","Tax type",40,width:125),
        Item(Columns,"Total","Grand total",50,width:120),Item(Columns,"Status","Status",60,width:105),
        Item(Columns,"Year","Financial year",70,false,110),Item(Columns,"Series","Series",80,false,105),
        Item(Columns,"Taxable","Taxable value",90,false,115),Item(Columns,"Tax","GST",100,false,105),
        Item(Actions,"New","New invoice",10),Item(Actions,"View","View",20),Item(Actions,"PrintPreview","Print preview",30),
        Item(Actions,"Edit","Edit draft",40),Item(Actions,"Finalize","Finalize draft",50),Item(Actions,"Refresh","Refresh",60)
    ];

    private static SettingKey<Rule> Item(string section,string id,string caption,int order,bool visible=true,int width=0)
    {
        var baseline=new Rule(visible,order,caption,width);
        var key=section.Replace("Invoice List ","",StringComparison.Ordinal);
        return new($"SalesList.{key}.{id}",section,caption,"Format: show or hide | order | label"+
            (width>0?" | width in pixels":id is "Category" or "Status" && section==Filters?" | default (All, Local, Interstate, Draft, or Finalized)":""),
            SettingScope.Company,SettingKind.Text,baseline,Parse,Format,"Settings.InvoiceEdit");
        string Format(Rule rule)=>FormattableString.Invariant(
            $"{(rule.Visible?"show":"hide")}|{rule.Order}|{rule.Label}")+
            (width>0?"|"+rule.Width.ToString(CultureInfo.InvariantCulture):rule.Default.Length>0?"|"+rule.Default:"");
        (bool,Rule) Parse(string value)
        {
            var parts=value.Split('|',StringSplitOptions.TrimEntries);
            if(parts.Length is <3 or >4 || parts[0] is not ("show" or "hide") ||
                !int.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out var rank) || rank>9999 ||
                parts[2].Length is <1 or >60 || parts[2].Any(char.IsControl)) return (false,baseline);
            var pixels=width;
            var fallback=parts.Length==4 && width==0?parts[3]:"";
            if(width>0 && parts.Length==4 && (!int.TryParse(parts[3],out pixels) || pixels is <60 or >600) ||
                fallback.Length>0 && (section!=Filters || id switch
                {
                    "Category"=>fallback is not ("All" or "Local" or "Interstate"),
                    "Status"=>fallback is not ("All" or "Draft" or "Finalized"),
                    _=>true
                })) return (false,baseline);
            return (true,new(parts[0]=="show",rank,parts[2],pixels,fallback));
        }
    }
}
