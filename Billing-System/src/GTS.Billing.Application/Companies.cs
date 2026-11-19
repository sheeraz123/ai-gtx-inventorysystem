using GTS.Billing.Domain;

namespace GTS.Billing.Application;

public enum CompanyOutcome { Success, InvalidInput, SessionRequired, Forbidden, NotFound, Conflict, Unavailable }
public enum CompanyAction { List, Get, Create, Update, SetActive, Select, Current }
public sealed record CompanyRequest(CompanyAction Action, Guid? Id=null, CompanyDraft? Draft=null,
    long? ExpectedVersion=null, bool Active=true, string? Search=null, byte[]? Logo=null, bool RemoveLogo=false,
    bool ProfileOnly=false,int Page=0,int PageSize=200);
public sealed record CompanyDetails(Guid Id, long Version, bool Active, bool IsCurrent, CompanyDraft Draft,
    DateTime CreatedUtc, Guid? CreatedBy, DateTime? UpdatedUtc, Guid? UpdatedBy, byte[]? Logo=null);
public sealed record CompanyResult(CompanyOutcome Outcome, IReadOnlyList<CompanyDetails>? Companies=null,
    IReadOnlyDictionary<string,string>? Errors=null,bool More=false)
{
    public CompanyDetails? Company => Companies?.FirstOrDefault();
}
public interface ICompanyStore
{
    Task<CompanyResult> ExecuteAsync(CompanyRequest request, AuthenticatedIdentity actor, CancellationToken ct);
}

public sealed class CompanyService(AuthenticationService authentication, ICompanyStore store)
{
    public async Task<CompanyResult> ExecuteAsync(CompanyRequest request, CancellationToken ct=default)
    {
        try
        {
            return await authentication.RunAuthenticatedAsync(async actor =>
            {
                if(request.Page is <0 or >100000 || request.PageSize is <10 or >200)
                    return new CompanyResult(CompanyOutcome.InvalidInput);
                if (request.Action is CompanyAction.Create or CompanyAction.Update)
                {
                    if (request.Draft is null) return new CompanyResult(CompanyOutcome.InvalidInput);
                    var validation=CompanyRules.Validate(request.Draft);
                    if(validation.Errors.Count>0) return new CompanyResult(CompanyOutcome.InvalidInput,Errors:validation.Errors);
                    request=request with {Draft=validation.Value};
                    if(request.RemoveLogo && request.Logo is not null) return new CompanyResult(CompanyOutcome.InvalidInput);
                    if(request.Logo is not null && !CompanyLogo.IsBoundedPng(request.Logo))
                        return new CompanyResult(CompanyOutcome.InvalidInput,Errors:new Dictionary<string,string>{{"Logo","Use a PNG up to 1 MiB and 2048 × 2048 pixels."}});
                }
                var result = await store.ExecuteAsync(request,actor,ct);
                if (result.Outcome == CompanyOutcome.SessionRequired) throw new UnauthorizedAccessException();
                return result;
            },ct);
        }
        catch(UnauthorizedAccessException) { return new(CompanyOutcome.SessionRequired); }
        catch(Exception e) when(e is System.Data.Common.DbException or IOException or InvalidOperationException)
        { return new(CompanyOutcome.Unavailable); }
    }
}

public static class CompanyLogo
{
    public const int MaximumBytes=1024*1024;
    public static bool IsBoundedPng(byte[] bytes)
    {
        if(bytes.Length is < 45 or > MaximumBytes || !bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) ||
            !bytes.AsSpan(12,4).SequenceEqual("IHDR"u8) ||
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8,4))!=13) return false;
        var width=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16,4));
        var height=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20,4));
        return width is >0 and <=2048 && height is >0 and <=2048 &&
            bytes.AsSpan(bytes.Length-8,4).SequenceEqual("IEND"u8);
    }
}
