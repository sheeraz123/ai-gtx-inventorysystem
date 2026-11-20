using System.Collections.Immutable;
using System.Text.Json;

namespace GTS.Billing.Application;

public sealed record SalesFormRule(string Label="",string Placeholder="",string Default="",string Help="",int Order=0,
    bool Visible=true,bool Required=false,bool ReadOnly=false,bool Preview=true,bool Print=true,bool Validate=true);

public sealed record SalesFormField(string Key,string Section,string Title,int Order,bool Critical=false,bool Input=false,
    bool TextDefault=false,bool Output=false,bool IsSection=false)
{
    public SalesFormRule Baseline=>new(Title,Order:Order,Required:Critical,ReadOnly:!Input);
    public bool CanCollapseForReview=>Key=="TaxDetails" || Section=="TaxDetails";
    public bool CanHideInEntry=>!Critical && Key!="PurchaseOrder" || CanCollapseForReview ||
        Key is "UnitId" or "Total" or "GrandTotal";
    public SettingKey<SalesFormRule> Setting=>new("SalesForm."+Key,SalesFormConfiguration.Section,Title,
        "Configure the field from the Sales Invoice layout editor.",SettingScope.Company,SettingKind.Text,Baseline,Parse,
        value=>JsonSerializer.Serialize(value),Permission:"Settings.InvoiceEdit");

    public (bool Valid,SalesFormRule Value) Parse(string text)
    {
        try
        {
            var value=JsonSerializer.Deserialize<SalesFormRule>(text);
            if(value is null || !Valid(value.Label,80,true) || !Valid(value.Placeholder,80) ||
                !Valid(value.Help,130) || !Valid(value.Default,DefaultLimit) || value.Order is <0 or >9999 ||
                Critical && (!value.Visible && !CanHideInEntry || !value.Required || !value.Validate) ||
                (!TextDefault || Critical) && value.ReadOnly!=Baseline.ReadOnly ||
                Key=="PurchaseOrder" && (!value.Visible || value.ReadOnly) ||
                !TextDefault && (value.Default.Length!=0 || value.Required!=Critical || !value.Validate) ||
                !Output && (!value.Preview || !value.Print) ||
                value.Required && (!value.Visible && !CanHideInEntry || !value.Validate ||
                    Input && value.ReadOnly && value.Default.Length==0))
                return (false,Baseline);
            return (true,value with {Default=value.Default.Trim()});
        }
        catch(JsonException) {return (false,Baseline);}
    }

    private static bool Valid(string? text,int max,bool required=false)=>text is not null && text.Length<=max &&
        !text.Any(char.IsControl) && (!required || !string.IsNullOrWhiteSpace(text));
    private int DefaultLimit=>Key switch
    {
        "Reference" or "PurchaseOrder"=>100,
        "Terms" or "Notes" or "InternalRemarks"=>140,
        _=>120
    };
}

public sealed record SalesFormConfiguration(ImmutableDictionary<string,SalesFormRule> Rules)
{
    public const string Section="Sales Invoice Configuration";
    public static SalesFormConfiguration Empty {get;}=new(ImmutableDictionary<string,SalesFormRule>.Empty);

    public static IReadOnlyList<SalesFormField> Fields {get;}=Array.AsReadOnly<SalesFormField>(
    [
        new("InvoiceDetails","","Invoice details",10,true,true,IsSection:true),
        new("CustomerDetails","","Customer",20,true,true,IsSection:true),
        new("BillingDetails","Addresses","Billing Address",30,true,true,IsSection:true),
        new("ShippingDetails","Addresses","Shipping Address",40,true,true,IsSection:true),
        new("TaxDetails","","GST treatment",50,true,true,IsSection:true),
        new("Products","","Products",60,true,true,IsSection:true),
        new("AdditionalDetails","","Payment, transport and notes",70,Input:true,IsSection:true),
        new("Summary","Summary","Invoice summary",80,true,true,IsSection:true),
        new("Company","InvoiceDetails","Company",10,true),
        new("InvoiceNumber","InvoiceDetails","Invoice type / status",20,true),
        new("Date","InvoiceDetails","Invoice date *",30,true,true),
        new("YearId","InvoiceDetails","Financial year *",40,true,true),
        new("RegistrationId","InvoiceDetails","Supplier GST registration *",50,true,true),
        new("SeriesId","InvoiceDetails","Document series *",60,true,true),
        new("CustomerId","CustomerDetails","Customer *",10,true,true),
        new("BillingAddressId","Addresses","Saved billing address",10,true,true),
        new("ShippingAddressId","Addresses","Saved shipping address",20,true,true),
        new("CopyBilling","Addresses","Copy billing to shipping",30),
        new("EditBilling","Addresses","Edit billing address",40),
        new("EditShipping","Addresses","Edit shipping address",50),
        new("CustomerGstin","Addresses","GSTIN",60),
        new("PlaceOfSupply","TaxDetails","Reviewed place of supply",10,true,true),
        new("Jurisdiction","TaxDetails","GST jurisdiction",20,true,true),
        new("DomesticConfirmed","TaxDetails","Standard domestic supply reviewed",30,true,true),
        new("ProductId","Products","Product search",10,true,true),
        new("VariantId","Products","Variant",20,true,true),
        new("ProductCode","Products","Product code",30,Output:true),
        new("Sku","Products","Article / SKU",40,Output:true),
        new("ProductName","Products","Product name",50,true),
        new("Brand","Products","Brand",60,Output:true),
        new("Hsn","Products","HSN / SAC",70),
        new("Mrp","Products","MRP",80,Input:true,Output:true),
        new("UnitId","Products","Unit",90,true,true),
        new("Quantity","Products","Quantity",100,true,true),
        new("Price","Products","Unit price",110,true,true),
        new("DiscountPercent","Products","Discount %",120,Input:true),
        new("DiscountAmount","Products","Discount amount",130,Input:true),
        new("Inclusive","Products","Price includes GST",140,true,true),
        new("TaxAssignment","Products","HSN/SAC and GST mapping",150,true,true),
        new("Taxable","Products","Taxable",160),
        new("Gross","Products","Gross amount",170),
        new("AppliedDiscount","Products","Applied discount",180),
        new("Gst","Products","GST %",190),
        new("TaxAmount","Products","GST amount",200),
        new("Total","Products","Line total",210,true),
        new("PurchaseOrder","CustomerDetails","Customer PO reference",20,Input:true,TextDefault:true),
        new("PurchaseOrderDate","AdditionalDetails","Customer PO date",20,Input:true),
        new("Reference","AdditionalDetails","Internal reference",30,Input:true,TextDefault:true,Output:true),
        new("DueDate","AdditionalDetails","Due date",40,Input:true),
        new("Terms","AdditionalDetails","Payment terms",50,Input:true,TextDefault:true,Output:true),
        new("Notes","AdditionalDetails","Notes",60,Input:true,TextDefault:true,Output:true),
        new("InternalRemarks","AdditionalDetails","Internal remarks",70,Input:true,TextDefault:true),
        new("GoodsReceipt","AdditionalDetails","G.R. number",80,Input:true,TextDefault:true),
        new("Vehicle","AdditionalDetails","Vehicle number",90,Input:true,TextDefault:true),
        new("Weight","AdditionalDetails","Weight",100,Input:true,TextDefault:true),
        new("Carrier","AdditionalDetails","Transport",110,Input:true,TextDefault:true),
        new("OrderThrough","AdditionalDetails","Order through",120,Input:true,TextDefault:true),
        new("PrivateMark","AdditionalDetails","Private mark",130,Input:true,TextDefault:true),
        new("Document","AdditionalDetails","Transport document",140,Input:true,TextDefault:true),
        new("PreparedBy","AdditionalDetails","Prepared by",150),
        new("GrandTotal","Summary","Grand total",10,true)
    ]);
    public SalesFormRule Field(string key)=>Rules.TryGetValue(key,out var rule)?rule:Fields.Single(x=>x.Key==key).Baseline;
    public static SalesFormConfiguration From(ResolvedSettings settings)=>new(Fields
        .Select(field=>(Field:field,Rule:settings.Get(field.Setting)))
        .Where(x=>x.Rule!=x.Field.Baseline).ToImmutableDictionary(x=>x.Field.Key,x=>x.Rule,StringComparer.Ordinal));
    public string Caption(string key,string fallback)=>Rules.TryGetValue(key,out var rule)?rule.Label:fallback;
    public bool ShowOutput(string key,bool preview)=>!Rules.TryGetValue(key,out var rule) || (preview?rule.Preview:rule.Print);
    public static bool IsGstInput(string key)=>
        key is nameof(SalesDraft.PlaceOfSupply) or nameof(SalesDraft.Jurisdiction) or nameof(SalesDraft.DomesticConfirmed);
    public const string GstReviewPendingMessage="Taxable, GST and line totals are estimates until GST jurisdiction is reviewed.";
    public SalesFormField? HiddenRequiredField()=>Field("AdditionalDetails").Visible?null:
        Fields.FirstOrDefault(field=>field.Section=="AdditionalDetails" && Field(field.Key).Required);

    public IReadOnlyList<SalesFieldError> ValidateDraft(SalesDraft draft,SalesDraft? original)
    {
        string? Value(SalesDraft? value,string key)=>key switch
        {
            "PurchaseOrder"=>value?.PurchaseOrder,"Reference"=>value?.Reference,"Terms"=>value?.Terms,
            "Notes"=>value?.Notes,"InternalRemarks"=>value?.InternalRemarks,"GoodsReceipt"=>value?.Transport?.GoodsReceipt,
            "Vehicle"=>value?.Transport?.Vehicle,"Weight"=>value?.Transport?.Weight,"Carrier"=>value?.Transport?.Carrier,
            "OrderThrough"=>value?.Transport?.OrderThrough,"PrivateMark"=>value?.Transport?.PrivateMark,
            "Document"=>value?.Transport?.Document,_=>null
        };
        var errors=new List<SalesFieldError>();
        foreach(var field in Fields.Where(x=>x.TextDefault))
        {
            var rule=Field(field.Key);
            var value=Value(draft,field.Key)??"";
            if(rule.Required && string.IsNullOrWhiteSpace(value))
                errors.Add(new(field.Key,rule.Label+" is required."));
            if(rule.ReadOnly && !string.Equals(value,original is null?rule.Default:Value(original,field.Key)??"",StringComparison.Ordinal))
                errors.Add(new(field.Key,rule.Label+" is read-only."));
        }
        return errors;
    }
}
