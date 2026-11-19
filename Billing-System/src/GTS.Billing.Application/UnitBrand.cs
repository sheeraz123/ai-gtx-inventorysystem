using GTS.Billing.Domain;

namespace GTS.Billing.Application;

public enum MeasureAction { List, View, Save, Status, Product, SaveProduct }
public sealed record MeasureMaster(Guid Id, long Version, string Code, string Name, bool Active, string? Description,
    int SortOrder, string? Symbol = null, string UnitType = "Count", int Precision = 0, string? Uqc = null,
    string? Manufacturer = null, byte[]? Logo = null, DateTime CreatedUtc = default, DateTime UpdatedUtc = default);
public sealed record MeasureChoice(Guid Id, string Code, string Name, bool Active)
{
    public string Label => $"{Code} — {Name}" + (Active ? "" : " (inactive)");
}
public sealed record ConversionInput(Guid UnitId, decimal Factor, bool Sales, bool Purchase, bool Active);
public sealed record ProductMeasures(Guid ProductId, long Version, Guid? BrandId, Guid? BaseUnitId,
    string? Classification, IReadOnlyList<ConversionInput> Conversions);
public sealed record MeasureRequest(MeasureAction Action, bool Brands = false, Guid? Company = null, Guid? Id = null,
    long? Version = null, string? Search = null, bool? Active = null, MeasureMaster? Master = null,
    ProductMeasures? Product = null, byte[]? Upload = null, bool RemoveLogo = false);
public sealed record MeasureResult(CompanyOutcome Outcome, Guid? Company = null, string? Message = null,
    IReadOnlyList<MeasureMaster>? Rows = null, ProductMeasures? Product = null, IReadOnlyList<MeasureChoice>? Units = null,
    IReadOnlyList<MeasureChoice>? Brands = null, IReadOnlyList<string>? Legacy = null);

public interface IUnitBrandStore
{
    Task<MeasureResult> ExecuteAsync(MeasureRequest request, AuthenticatedIdentity identity, CancellationToken ct);
}

public sealed class UnitBrandService(AuthenticationService authentication, IApplicationOperationCoordinator lifetime,
    IUnitBrandStore store, ICompanyImageProcessor images)
{
    public static IReadOnlyList<string> UnitTypes { get; } =
        ["Count", "Pair", "Set", "Packaging", "Weight", "Length", "Area", "Volume", "Service"];
    public static IReadOnlyList<string> Classifications { get; } =
        ["Raw Material", "Component", "Work in Progress", "Finished Goods", "Packaging Material", "Consumable", "Trading Goods", "Service"];

    public async Task<MeasureResult> ExecuteAsync(MeasureRequest request, CancellationToken ct = default)
    {
        MeasureResult result = new(CompanyOutcome.Unavailable);
        try
        {
            await lifetime.RunAsync(async token =>
            {
                result = await authentication.RunAuthenticatedAsync(async actor =>
                {
                    if (request.Search?.Length > 100) return new MeasureResult(CompanyOutcome.InvalidInput);
                    if (request.Upload is { } upload)
                    {
                        if (!request.Brands || request.Action != MeasureAction.Save || request.RemoveLogo ||
                            upload.Length is < 45 or > CompanyLogo.MaximumBytes)
                            return new MeasureResult(CompanyOutcome.InvalidInput, Message:"Choose a PNG or JPEG up to 1 MiB.");
                        var logo = (await Task.Run(() => images.Prepare(upload.ToArray(), token), token))?.Content;
                        if (logo is null) return new MeasureResult(CompanyOutcome.InvalidInput, Message:"The logo must be a readable PNG or JPEG.");
                        request = request with { Upload = logo };
                    }
                    var response = await store.ExecuteAsync(request, actor, token);
                    if (response.Outcome == CompanyOutcome.SessionRequired) throw new UnauthorizedAccessException();
                    return response;
                }, token);
            }, ct);
        }
        catch (UnauthorizedAccessException) { return new(CompanyOutcome.SessionRequired); }
        catch (ArgumentException) { return new(CompanyOutcome.InvalidInput); }
        catch (Exception e) when (e is System.Data.Common.DbException or IOException or InvalidOperationException)
        { return new(CompanyOutcome.Unavailable); }
        return result;
    }
}
