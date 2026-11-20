namespace GTS.Billing.Application;

public enum VariantAction { List, SaveOption, SaveVariant, OptionStatus, VariantStatus }

public sealed record VariantValue(Guid Id,long Version,string Axis,string Code,string Name,string? Hex=null,int SortOrder=0,bool Active=true)
{
    public string Label => $"{Axis} · {Code} · {Name}";
}

public sealed record VariantEntry(Guid Id,long Version,string Code,string? Sku,string? Barcode,decimal? Mrp,
    decimal? SalesRate,decimal? PurchaseRate,bool Active,IReadOnlyList<Guid> Options)
{
    public string Identity => Code + (Sku is null ? "" : " · "+Sku);
}

public sealed record VariantRequest(Guid ProductId,VariantAction Action=VariantAction.List,string? Search=null,
    VariantValue? Option=null,VariantEntry? Variant=null,Guid? ExpectedCompany=null);

public sealed record VariantResult(CompanyOutcome Outcome,string? Message=null,Guid? Company=null,
    IReadOnlyList<VariantValue>? Options=null,IReadOnlyList<VariantEntry>? Variants=null,
    bool CanCreate=false,bool CanEdit=false,bool CanStatus=false,bool CanPrice=false,bool CanManageOptions=false);

public interface IProductVariantStore
{
    Task<VariantResult> ExecuteAsync(VariantRequest request,AuthenticatedIdentity actor,CancellationToken ct);
}

public sealed class ProductVariantService(AuthenticationService auth,IApplicationOperationCoordinator lifetime,IProductVariantStore store)
{
    public async Task<VariantResult> ExecuteAsync(VariantRequest request,CancellationToken ct)
    {
        VariantResult result=new(CompanyOutcome.Unavailable);
        try
        {
            await lifetime.RunAsync(async token=>result=await auth.RunAuthenticatedAsync(async identity=>
            {
                var response=await store.ExecuteAsync(request,identity,token);
                if(response.Outcome==CompanyOutcome.SessionRequired) throw new UnauthorizedAccessException();
                return response;
            },token),ct);
        }
        catch(UnauthorizedAccessException) {return new(CompanyOutcome.SessionRequired);}
        return result;
    }
}
