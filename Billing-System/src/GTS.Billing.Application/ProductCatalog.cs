using GTS.Billing.Domain;

namespace GTS.Billing.Application;

public enum CatalogAction { Context, List, Get, Save, Status, Lookup, Snapshot }
public enum CatalogUnit { Sales, Purchase, Base }
public sealed record CatalogRequest(CatalogAction Action, bool Products, Guid? Company = null, Guid? Id = null,
    long? Version = null, string? Search = null, Guid? Category = null, string? Kind = null, bool? Active = null,
    int Page = 0, CategoryDraft? CategoryDraft = null, ProductDraft? ProductDraft = null,
    byte[]? Upload = null, bool RemoveImage = false, bool AcceptDuplicates = false, CatalogUnit Unit = CatalogUnit.Sales);
public sealed record CatalogRow(Guid Id, long Version, string Code, string Name, bool Active, string? Category,
    string? Description = null, int? SortOrder = null, string? Brand = null, string? Kind = null, string? BaseUnit = null,
    string? Hsn = null, decimal? SalesRate = null, decimal? Mrp = null, bool? StockTracked = null,
    string? SalesUnit = null, decimal? DefaultGstRate = null, bool? TaxExempt = null)
{
    public string Status => Active ? "Active" : "Inactive";
}
public sealed record CatalogCategory(Guid? Id, string Label, bool Active);
public sealed record CatalogDetail(CatalogRow Row, CategoryDraft? Category, ProductDraft? Product, byte[]? Image,
    DateTime CreatedUtc, DateTime UpdatedUtc, string CreatedBy, string UpdatedBy);
public sealed record ProductInvoiceSnapshot(Guid CompanyId, Guid ProductId, long ProductVersion, string Code, string Name,
    string? Description, string Kind, string? Sku, string? Barcode, string? Hsn, string Unit, int QuantityPrecision,
    decimal Factor, decimal? DefaultRate, string Currency, bool TaxInclusive, decimal? ProvisionalGstRate, bool TaxExempt, string? TaxNotes);
public sealed record CatalogResult(CompanyOutcome Outcome, Guid? Company = null, IReadOnlyList<CatalogRow>? Rows = null,
    IReadOnlyList<CatalogCategory>? Categories = null, CatalogDetail? Detail = null, bool More = false,
    IReadOnlyDictionary<string,string>? Errors = null, string? Message = null, ProductInvoiceSnapshot? Snapshot = null,
    bool DuplicateWarning = false);

public interface IProductCatalogStore
{
    Task<CatalogResult> ExecuteAsync(CatalogRequest request, AuthenticatedIdentity actor, byte[]? image, CancellationToken ct);
}

public sealed class ProductCatalogService(AuthenticationService auth, IApplicationOperationCoordinator lifetime,
    IProductCatalogStore store, ICompanyImageProcessor images)
{
    public Task<CatalogResult> LookupAsync(Guid company, string? search, int page = 0, CancellationToken ct = default) =>
        ExecuteAsync(new(CatalogAction.Lookup, true, company, Search:search, Page:page), ct);

    public Task<CatalogResult> ResolveSnapshotAsync(Guid company, Guid product, CatalogUnit unit, CancellationToken ct = default) =>
        ExecuteAsync(new(CatalogAction.Snapshot, true, company, product, Unit:unit), ct);

    public async Task<CatalogResult> ExecuteAsync(CatalogRequest request, CancellationToken ct = default)
    {
        CatalogResult result = new(CompanyOutcome.Unavailable);
        try
        {
            await lifetime.RunAsync(async token =>
            {
                result = await auth.RunAuthenticatedAsync(async actor =>
                {
                    if (!actor.IsSuperAdmin) return new CatalogResult(CompanyOutcome.Forbidden);
                    if (request.Page is < 0 or > 1000000 || request.Search?.Length > 100 ||
                        request.Action == CatalogAction.Status && request.Active is null ||
                        request.Action is CatalogAction.Lookup or CatalogAction.Snapshot && !request.Products ||
                        request.Action == CatalogAction.Snapshot && !Enum.IsDefined(request.Unit))
                        return new CatalogResult(CompanyOutcome.InvalidInput);
                    var errors = new Dictionary<string,string>();
                    byte[]? image = null;
                    if (request.Action == CatalogAction.Save)
                    {
                        if (request.Products && request.ProductDraft is { } product)
                        {
                            CatalogRules.ValidateProduct(product, errors);
                            if (request.Upload is { } raw)
                            {
                                if (request.RemoveImage || raw.Length is < 45 or > CompanyLogo.MaximumBytes)
                                    errors["Image"] = "Choose a PNG or JPEG up to 1 MiB.";
                                else if (errors.Count == 0)
                                {
                                    image = (await Task.Run(() => images.Prepare(raw.ToArray(), token), token))?.Content;
                                    if (image is null) errors["Image"] = "Choose a decodable PNG or JPEG up to 2048 × 2048 pixels.";
                                }
                            }
                        }
                        else if (!request.Products && request.CategoryDraft is { } category)
                        {
                            CatalogRules.ValidateIdentity(category.Code, category.Name, errors);
                            CatalogRules.CheckText("Description", category.Description, 500, errors);
                            if (category.SortOrder < 0) errors["SortOrder"] = "Enter a nonnegative order.";
                        }
                        else return new CatalogResult(CompanyOutcome.InvalidInput);
                    }
                    if (errors.Count != 0) return new CatalogResult(CompanyOutcome.InvalidInput, Errors:errors);
                    var response = await store.ExecuteAsync(request, actor, image, token);
                    if (response.Outcome == CompanyOutcome.SessionRequired) throw new UnauthorizedAccessException();
                    return response;
                }, token);
            }, ct);
        }
        catch (UnauthorizedAccessException) { return new(CompanyOutcome.SessionRequired); }
        catch (ArgumentException) { return new(CompanyOutcome.InvalidInput, Message:"Check the entered characters and try again."); }
        catch (Exception e) when (e is System.Data.Common.DbException or IOException or InvalidOperationException)
        { return new(CompanyOutcome.Unavailable); }
        return result;
    }
}
