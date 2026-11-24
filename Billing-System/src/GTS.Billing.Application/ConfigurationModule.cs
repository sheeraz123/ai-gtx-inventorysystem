namespace GTS.Billing.Application;

public static class ConfigurationModule
{
    public const string General="General Configuration";
    public const string Menus="Menu Configuration";
    public const string Billing="Billing Configuration";
    public const string History="Configuration Audit History";
    public const string Shortcuts="Keyboard Shortcuts";
    public const string CustomerFields="Form & Field Configuration — Customer";
    public static IReadOnlyList<string> Sections {get;}=[General,SalesFormConfiguration.Section,CustomerFields,ProductFormConfiguration.Section,Menus,Billing,History,Shortcuts];

    public static readonly SettingKey<string> DateFormat=Choice("Date",General,"Date format","dd-MM-yyyy",
        ["dd-MM-yyyy","dd/MM/yyyy","yyyy-MM-dd","dd MMM yyyy"],restart:true);
    public static readonly SettingKey<string> TimeFormat=Choice("Time",General,"Time format","hh:mm tt",["hh:mm tt","HH:mm"],restart:true);
    public static readonly SettingKey<string> Grouping=Choice("Grouping",General,"INR number grouping","Indian",["Indian","International"],restart:true);
    public static readonly SettingKey<string> Precision=Choice("Precision",General,"Currency display decimals","2",["0","2","3"],restart:true);
    public static readonly SettingKey<string> DocumentType=Choice("DocumentType",Billing,"Default document type","TaxInvoice",["TaxInvoice","NonGstVoucher"]);
    public static readonly SettingKey<string> GstMethod=Choice("GstMethod",Billing,"Default GST method","Tax Exclusive",["Tax Exclusive","Tax Inclusive"]);
    public static readonly SettingKey<string> RoundOff=Choice("RoundOff",Billing,"Invoice round-off","Nearest Rupee",["Nearest Rupee","Off"]);
    public static readonly SettingKey<string> DiscountType=Choice("DiscountType",Billing,"Default discount type","Percentage",["Percentage","Fixed Amount"]);
    public static readonly SettingKey<string> CustomerRequired=Choice("CustomerRequired",Billing,"Customer selection mandatory","On",["On","Off"]);
    public static readonly SettingKey<string> WalkIn=Choice("WalkIn",Billing,"Allow walk-in billing","Off",["Off","On"]);

    private static readonly (string Id,string Name)[] MenuItems=
    [
        ("master","Master"),("transactions","Transactions"),("administration","Administration"),("reports","Reports"),
        ("company","Company Master"),("customer","Customer Master"),("category","Product Category Master"),
        ("product","Product / Item Master"),("unit","Unit Master"),("brand","Brand Master"),("hsnsac","HSN/SAC Master"),
        ("tax","GST Rate Master"),("taxassignments","Classification rate assignments"),("financialyear","Financial Year Master"),
        ("documentseries","Document Series Master"),("price","Price List"),("payment","Payment Terms"),
        ("quotation","Quotation"),("proforma","Proforma Invoice"),("invoice","Sales Invoice"),("retail","Retail Invoice"),
        ("credit","Credit Note"),("debit","Debit Note"),("users","User Management"),("backup","Backup and Restore"),("settings","Settings")
    ];
    private static readonly (string Code,string Precision)[] Units=
        [("PCS","0"),("PAIR","0"),("BOX","0"),("KG","3"),("MTR","2"),("LTR","3")];
    public static IReadOnlyList<(string Key,string Label)> CustomerFieldCatalog {get;}=
        [("TradeName","Trade name"),("DisplayName","Display name"),("GroupLabel","Customer group"),("CategoryLabel","Customer classification")];
    public static readonly SettingKey<string> CustomerProfile=Choice("Customer.Profile",CustomerFields,"Customer layout",
        "Standard",["Standard","Graftex"]);
    private static readonly (string Key,string Caption)[] CustomerTabs=
    [("Tax","GST & Tax"),("Addresses","Billing & Shipping"),("Bank","Bank Details"),("Contacts","Contacts"),
        ("Credit","Terms & Credit"),("Opening","Opening Draft"),("Notes","Notes & Audit")];
    private static readonly string[] GraftexHiddenFields=["CustomerType","SinceDate","Email","GstLegalName",
        "GstStateCode","GstEffectiveDate","SupplyClassification","ExemptionNotes","Bank.AccountHolder","Bank.Branch","Bank.UpiId","Bank.ReferenceNotes"];
    public static bool GraftexCustomer(ResolvedSettings settings)=>settings.Get(CustomerProfile)=="Graftex";
    public static bool CustomerTabVisible(ResolvedSettings settings,string tab)=>tab=="General" ||
        settings.Text($"Configuration.Customer.Tab.{tab}","Profile default") switch
        {
            "Show"=>true,"Hide"=>false,
            _=>!GraftexCustomer(settings) || tab is "Tax" or "Addresses" or "Bank"
        };
    public static bool CustomerSupplementVisible(ResolvedSettings settings,string field)=>
        !GraftexHiddenFields.Contains(field) || settings.Text($"Configuration.Customer.Field.{field}.Visibility","Profile default") switch
        {
            "Show"=>true,"Hide"=>false,_=>!GraftexCustomer(settings)
        };

    public static IReadOnlyList<SettingDefinition> Definitions {get;}=
    [
        ..ProductFormConfiguration.Definitions,
        DateFormat,TimeFormat,Grouping,Precision,DocumentType,GstMethod,RoundOff,DiscountType,CustomerRequired,WalkIn,CustomerProfile,
        ..CustomerTabs.Select(tab=>Choice("Customer.Tab."+tab.Key,CustomerFields,tab.Caption+" tab visibility","Profile default",["Profile default","Show","Hide"])),
        ..GraftexHiddenFields.Select(field=>Choice("Customer.Field."+field+".Visibility",CustomerFields,field+" visibility","Profile default",
            ["Profile default","Show","Hide"])),
        ..Units.Select(unit=>Choice($"Unit.{unit.Code}",General,unit.Code+" quantity precision",unit.Precision,
            ["0","1","2","3","4","5","6"],restart:true)),
        ..CustomerFieldCatalog.SelectMany(field=>new SettingDefinition[]
        {
            Choice($"Customer.{field.Key}.Mode",CustomerFields,field.Label+" mode","Profile default",
                ["Profile default","Optional","Required","Read-only","Hidden"]),
            new SettingKey<string>($"Configuration.Customer.{field.Key}.Caption",CustomerFields,field.Label+" caption",
                "Leave blank to use the existing caption.",SettingScope.Company,SettingKind.Text,"",
                value=>(value.Length<=80 && !value.Any(char.IsControl),value.Trim()),value=>value)
        }),
        ..MenuItems.SelectMany(item=>new SettingDefinition[]
        {
            Choice($"Menu.{item.Id}.Visibility",Menus,item.Name+" visibility","Show",["Show","Hide"]),
            new SettingKey<string>($"Configuration.Menu.{item.Id}.Caption",Menus,item.Name+" caption",
                "Leave blank to use the existing caption.",SettingScope.Company,SettingKind.Text,"",
                value=>(value.Length<=80 && !value.Any(char.IsControl),value.Trim()),value=>value)
        }),
        ..MenuItems.Prepend(("dashboard","Dashboard")).Select(item=>Choice($"Shortcut.{item.Item1}",Shortcuts,
            item.Item2+" shortcut","Disabled",["Disabled",..Enumerable.Range(1,12).Select(number=>$"F{number}"),
                ..Enumerable.Range('A',26).Select(letter=>$"Ctrl+Shift+{(char)letter}")]))
    ];

    public static bool IsAdministrative(SettingDefinition setting)=>
        setting.Key.StartsWith("Configuration.",StringComparison.Ordinal) || setting.Section==SalesFormConfiguration.Section;

    public static string MenuCaption(ResolvedSettings settings,string id,string fallback)=>
        settings.Text($"Configuration.Menu.{id}.Caption","") is {Length:>0} caption?caption:fallback;

    public static bool MenuVisible(ResolvedSettings settings,string id)=>
        settings.Text($"Configuration.Menu.{id}.Visibility","Show")!="Hide";
    public static string CustomerMode(ResolvedSettings settings,string field)=>
        settings.Text($"Configuration.Customer.{field}.Mode","Profile default") is "Profile default"
            ? GraftexCustomer(settings)?"Hidden":"Optional"
            : settings.Text($"Configuration.Customer.{field}.Mode","Optional");
    public static string CustomerCaption(ResolvedSettings settings,string field,string fallback)=>
        settings.Text($"Configuration.Customer.{field}.Caption","") is {Length:>0} caption?caption:fallback;

    public static int? UnitPrecision(ResolvedSettings settings,string code)
    {
        var unit=Units.FirstOrDefault(item=>string.Equals(item.Code,code,StringComparison.OrdinalIgnoreCase));
        return unit.Code is null?null:int.Parse(settings.Text($"Configuration.Unit.{unit.Code}",unit.Precision),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static SettingKey<string> Choice(string key,string section,string label,string baseline,string[] choices,bool restart=false)=>
        new("Configuration."+key,section,label,section==Billing?
            "Saved for the future billing workflow. Current billing behavior is unchanged.":
            "Select a supported value.",SettingScope.Company,SettingKind.Choice,baseline,
            value=>(choices.Contains(value,StringComparer.Ordinal),value),value=>value,Restart:restart,Options:Array.AsReadOnly(choices));
}
