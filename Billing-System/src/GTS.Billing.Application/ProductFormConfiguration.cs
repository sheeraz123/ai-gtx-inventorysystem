namespace GTS.Billing.Application;

public static class ProductFormConfiguration
{
    public const string Section = "Form & Field Configuration - Product";
    public const string Legacy = "Legacy Billing";
    private const string Prefix = "Configuration.ProductForm.";

    public static readonly SettingKey<string> Profile = Choice("Profile", "Product form profile", "Standard", ["Standard", Legacy]);
    public static readonly SettingKey<string> DefaultCategory = new(Prefix + "DefaultCategory", Section, "Default product category code",
        "Enter an active category code to use for new products when Category is hidden.", SettingScope.Company, SettingKind.Text, "",
        value => (value.Length <= 30 && !value.Any(char.IsControl), value.Trim().ToUpperInvariant()), value => value);

    private static readonly (string Tab, bool LegacyVisible, (string Key, string Label)[] Fields)[] Tabs =
    [
        ("General", true, [("Code","Item Code"), ("Name","Name"), ("CategoryId","Category"), ("ShortName","Short name"),
            ("Description","Description"), ("Kind","Product type"), ("Active","Active")]),
        ("Identification", false, [("Brand","Brand"), ("Sku","SKU"), ("Barcode","Barcode"), ("PartNumber","Manufacturer part number"),
            ("ModelNumber","Model number")]),
        ("Units", true, [("BaseUnit","Unit"), ("SalesUnit","Sales unit"), ("SalesFactor","Sales conversion factor"),
            ("PurchaseUnit","Purchase unit"), ("PurchaseFactor","Purchase conversion factor"), ("QuantityPrecision","Quantity decimals")]),
        ("GST & Tax", true, [("TaxClassification","HSN Code"), ("TaxContext","Tax"), ("TaxPreviewDate","Tax preview date"),
            ("Hsn","Stored legacy HSN/SAC"), ("GstRate","Stored legacy GST rate"), ("TaxExempt","Legacy exemption flag"),
            ("TaxNotes","Tax classification notes")]),
        ("Pricing", true, [("PurchaseRate","Purchase rate"), ("SalesRate","Sale Price"), ("Mrp","M.R.P."),
            ("SalesInclusive","Sales price includes tax"), ("PurchaseInclusive","Purchase price includes tax"), ("Currency","Currency"),
            ("PricingDate","Pricing effective date")]),
        ("Inventory", false, [("StockTracked","Track stock"), ("ReorderLevel","Reorder level"), ("MinimumStock","Minimum stock"),
            ("MaximumStock","Maximum stock"), ("StorageLocation","Storage location")]),
        ("Image & Details", false, [("Image","Product image"), ("Specifications","Specifications"), ("Warranty","Warranty"), ("Remarks","Internal remarks")])
    ];

    private static readonly HashSet<string> LegacyFields =
        ["Code", "Name", "BaseUnit", "TaxClassification", "TaxContext", "SalesRate", "Mrp"];

    public static IReadOnlyList<SettingDefinition> Definitions { get; } =
    [
        Profile, DefaultCategory,
        ..Tabs.SelectMany(tab => new[] { Visibility("Tab." + tab.Tab, tab.Tab + " tab") }
            .Concat(tab.Fields.SelectMany(field => new SettingDefinition[] {
                Visibility("Field." + field.Key, tab.Tab + " / " + field.Label),
                new SettingKey<string>(Prefix+"Caption."+field.Key,Section,field.Label+" caption",
                    "Leave blank for the default caption.",SettingScope.Company,SettingKind.Text,"",
                    value=>(value.Length<=80 && !value.Any(char.IsControl),value.Trim()),value=>value)
            }))),
        ..new[]{"SalesRate","Mrp","TaxClassification","TaxContext"}
            .Select(field=>Choice("Mode."+field,field+" requirement","Optional",["Optional","Required","Read-only"]))
    ];

    public static bool IsLegacy(ResolvedSettings settings) => settings.Get(Profile) == Legacy;

    public static bool TabVisible(ResolvedSettings settings, string tab) =>
        Resolve(settings, "Tab." + tab, !IsLegacy(settings) || Tabs.Any(entry => entry.Tab == tab && entry.LegacyVisible));

    public static bool FieldVisible(ResolvedSettings settings, string field, bool standardDefault = true) =>
        Resolve(settings, "Field." + field, IsLegacy(settings) ? LegacyFields.Contains(field) : standardDefault);
    public static bool EditorFieldVisible(ResolvedSettings settings,string field)=>
        Tabs.FirstOrDefault(tab=>tab.Fields.Any(item=>item.Key==field)).Tab is not { } tab ||
        TabVisible(settings,tab) && FieldVisible(settings,field);
    public static string Mode(ResolvedSettings settings,string field)=>settings.Text(Prefix+"Mode."+field,"Optional");
    public static string Caption(ResolvedSettings settings,string field,string fallback)=>
        settings.Text(Prefix+"Caption."+field,"") is {Length:>0} caption?caption:fallback;
    public static string? LayoutError(ResolvedSettings settings)
    {
        foreach(var field in new[]{"Name","BaseUnit"})
            if(!EditorFieldVisible(settings,field)) return $"Keep {Caption(settings,field,field)} visible for product creation.";
        if(!IsLegacy(settings) && !EditorFieldVisible(settings,"Code")) return "Keep product code visible for the Standard profile.";
        foreach(var field in new[]{"SalesRate","Mrp","TaxClassification","TaxContext"})
            if(Mode(settings,field)=="Required" && !EditorFieldVisible(settings,field))
                return $"Show {Caption(settings,field,field)} before making it required.";
        if(new[]{"TaxClassification","TaxContext"}.Any(field=>Mode(settings,field)=="Required") &&
            new[]{"TaxClassification","TaxContext"}.Any(field=>!EditorFieldVisible(settings,field) || Mode(settings,field)=="Read-only"))
            return "A required tax selection needs both editable classification and rate context controls.";
        return null;
    }

    private static bool Resolve(ResolvedSettings settings, string key, bool fallback) =>
        settings.Text(Prefix + key, "Profile default") switch { "Show" => true, "Hide" => false, _ => fallback };

    private static SettingKey<string> Visibility(string key, string label) =>
        Choice(key, label, "Profile default", ["Profile default", "Show", "Hide"]);

    private static SettingKey<string> Choice(string key, string label, string baseline, string[] choices) =>
        new(Prefix + key, Section, label, "Select a supported value.", SettingScope.Company, SettingKind.Choice, baseline,
            value => (choices.Contains(value, StringComparer.Ordinal), value), value => value, Options: Array.AsReadOnly(choices));
}
