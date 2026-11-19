namespace GTS.Billing.Application;

public sealed record PermissionDefinition(string Code, string Module, string StoredModule, string StoredAction)
{
    public string Label => Code[(Code.IndexOf('.') + 1)..];
}

public static class PermissionCatalog
{
    // The original schema stores module/action pairs. Keep its established action vocabulary.
    public static IReadOnlyList<PermissionDefinition> All { get; } = Array.AsReadOnly<PermissionDefinition>(
    [
        new("Company.View","Company","company","View"),
        new("Company.Edit","Company","company","Edit"),
        new("Company.BankManage","Company","company","SelectBank"),
        new("Company.BankView","Company","company","ViewReports"),
        new("Company.BrandingManage","Company","company","ChangeSignature"),
        new("Company.InvoiceSettingsManage","Company","company","ManageSettings"),
        new("Customer.View","Customer","customer","View"),
        new("Customer.Create","Customer","customer","Create"),
        new("Customer.Edit","Customer","customer","Edit"),
        new("Customer.StatusChange","Customer","customer","Cancel"),
        new("Customer.CreditEdit","Customer","customer","ManageSettings"),
        new("Customer.BankView","Customer","customer","ViewReports"),
        new("Customer.BankEdit","Customer","customer","SelectBank"),
        new("Customer.InternalNotes","Customer","customer","ChangeSignature"),
        new("Category.View","Category","category","View"),
        new("Category.Create","Category","category","Create"),
        new("Category.Edit","Category","category","Edit"),
        new("Category.StatusChange","Category","category","Cancel"),
        new("Product.View","Product","product","View"),
        new("Product.Create","Product","product","Create"),
        new("Product.Edit","Product","product","Edit"),
        new("Product.StatusChange","Product","product","Cancel"),
        new("Product.PricingEdit","Product","product","ManageSettings"),
        new("Unit.View","Unit","unit","View"),
        new("Unit.Create","Unit","unit","Create"),
        new("Unit.Edit","Unit","unit","Edit"),
        new("Unit.StatusChange","Unit","unit","Cancel"),
        new("Brand.View","Brand","brand","View"),
        new("Brand.Create","Brand","brand","Create"),
        new("Brand.Edit","Brand","brand","Edit"),
        new("Brand.StatusChange","Brand","brand","Cancel"),
        new("TaxClassification.View","HSN/SAC","hsnsac","View"),
        new("TaxClassification.Create","HSN/SAC","hsnsac","Create"),
        new("TaxClassification.Edit","HSN/SAC","hsnsac","Edit"),
        new("TaxClassification.StatusChange","HSN/SAC","hsnsac","Cancel"),
        new("GstRate.View","GST Rate","tax","View"),
        new("GstRate.Create","GST Rate","tax","Create"),
        new("GstRate.Edit","GST Rate","tax","Edit"),
        new("GstRate.StatusChange","GST Rate","tax","Cancel"),
        new("GstRate.Assign","GST Rate","tax","ManageSettings"),
        new("FinancialYear.View","Financial Year","financialyear","View"),
        new("FinancialYear.Create","Financial Year","financialyear","Create"),
        new("FinancialYear.Edit","Financial Year","financialyear","Edit"),
        new("FinancialYear.SetCurrent","Financial Year","financialyear","ManageSettings"),
        new("FinancialYear.Close","Financial Year","financialyear","Cancel"),
        new("FinancialYear.Reopen","Financial Year","financialyear","CorrectClosedYear"),
        new("DocumentSeries.View","Document Series","documentseries","View"),
        new("DocumentSeries.Create","Document Series","documentseries","Create"),
        new("DocumentSeries.Edit","Document Series","documentseries","Edit"),
        new("DocumentSeries.SetDefault","Document Series","documentseries","ManageSettings"),
        new("DocumentSeries.StatusChange","Document Series","documentseries","Cancel"),
        new("SalesInvoice.View","Sales Invoice","invoice","View"),
        new("SalesInvoice.Create","Sales Invoice","invoice","Create"),
        new("SalesInvoice.EditDraft","Sales Invoice","invoice","Edit"),
        new("SalesInvoice.Finalize","Sales Invoice","invoice","ManageSettings"),
        new("SalesInvoice.ViewFinancials","Sales Invoice","invoice","ViewReports"),
        new("SalesInvoice.Print","Sales Invoice","invoice","Print"),
        new("SalesInvoice.ExportPdf","Sales Invoice","invoice","Export"),
        new("InvoiceTemplate.View","Invoice templates","invoice","BackupSettings"),
        new("InvoiceTemplate.Manage","Invoice templates","invoice","CorrectClosedYear"),
        new("User.View","Users","users","View"),
        new("Role.View","Roles","roles","View"),
        new("SecurityAudit.View","Security audit","security-audit","View"),
        new("Settings.View","Settings","settings","View"),
        new("Settings.CompanyBrandingEdit","Settings","settings","ChangeSignature"),
        new("Settings.BankEdit","Settings","settings","SelectBank"),
        new("Settings.InvoiceEdit","Settings","settings","Edit"),
        new("Settings.PrintEdit","Settings","settings","Print"),
        new("Settings.BusinessDefaultsEdit","Settings","settings","ManageSettings"),
        new("Settings.SecurityView","Settings","settings","ViewReports")
    ]);
}
public sealed record CompanyCapability(Guid CompanyId, IReadOnlySet<string> Permissions);
public sealed record AccessSnapshot(bool IsSuperAdmin, Guid? CurrentCompanyId, IReadOnlyList<CompanyCapability> Companies)
{
    public static AccessSnapshot Denied { get; } = new(false,null,[]);
    public bool Allows(string permission, Guid? companyId = null) =>
        IsSuperAdmin || Companies.Any(x => x.CompanyId == (companyId ?? CurrentCompanyId) && x.Permissions.Contains(permission));
    public bool EquivalentTo(AccessSnapshot other) => IsSuperAdmin==other.IsSuperAdmin &&
        CurrentCompanyId==other.CurrentCompanyId && Companies.Count==other.Companies.Count &&
        Companies.All(old=>other.Companies.Any(next=>next.CompanyId==old.CompanyId && next.Permissions.SetEquals(old.Permissions)));
    public bool CanNavigate(string module) => module switch
    {
        "dashboard" => true, "company" => IsSuperAdmin || Companies.Count > 0,
        "customer" => Allows("Customer.View"), "category" => Allows("Category.View"), "product" => Allows("Product.View"),
        "unit" => Allows("Unit.View"), "brand" => Allows("Brand.View"),
        "hsnsac" => Allows("TaxClassification.View"), "tax" or "taxassignments" => Allows("GstRate.View"),
        "financialyear" => Allows("FinancialYear.View"), "documentseries" => Allows("DocumentSeries.View"),
        "invoice" => Allows("SalesInvoice.View") && Allows("SalesInvoice.ViewFinancials"),
        "users" => Allows("User.View") || Allows("Role.View") || Allows("SecurityAudit.View"),
        "settings" => true,
        _ => IsSuperAdmin
    };
}
