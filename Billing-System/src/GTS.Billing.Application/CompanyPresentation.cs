using System.Collections.Immutable;

namespace GTS.Billing.Application;

public enum PresentationAction { ListLogos, GetLogo, SaveLogo, RemoveLogo, ListBanks, GetBank, SaveBank, GetDefaults, SaveDefaults, Snapshot, ListTax, GetTax, SaveTax, SetInvoiceBank }
public sealed record CompanyTaxDraft(string Gstin, string StateCode, bool Active=true, bool Default=false);
public sealed record CompanyTaxDetail(Guid Id, long Version, CompanyTaxDraft Draft)
{
    public string State => GTS.Billing.Domain.CompanyRules.States.GetValueOrDefault(Draft.StateCode, Draft.StateCode);
}
public sealed record LogoDraft(string Name, string Usage, int Order, bool Active, byte[]? Upload = null);
public sealed record BankDraft(string BankName, string AccountHolder, string AccountNumber, string Ifsc, string? Branch = null,
    string? AccountType = null, string? Upi = null, string? Address = null, string? Instructions = null,
    int Order = 0, bool Active = true);
public sealed record InvoiceDefaults(Guid? PrimaryLogo = null, Guid? SecondaryLogo = null, Guid? BankId = null,
    bool ShowPrimary = false, bool ShowSecondary = false, bool ShowBank = false, bool ShowUpi = false,
    string? Header = null, string? Footer = null, string? Signatory = null, string? Terms = null);
public sealed record LogoListItem(Guid Id, string Name, string Usage, int Order, string Status, string InvoiceSlot, byte[]? Thumbnail);
public sealed record LogoDetail(Guid Id, long Version, LogoDraft Draft, byte[]? Content, bool Retired);
public sealed record BankListItem(Guid Id, string BankName, string AccountHolder, string MaskedNumber, string Ifsc,
    string? Branch, string? AccountType, bool Active, bool Default, int Order);
public sealed record BankDetail(Guid Id, long Version, BankDraft Draft);
public sealed record PresentationRequest(PresentationAction Action, Guid CompanyId, Guid? Id = null, long? Version = null,
    LogoDraft? Logo = null, BankDraft? Bank = null, InvoiceDefaults? Defaults = null, string? Search = null, CompanyTaxDraft? Tax = null);
public sealed record PresentationResult(CompanyOutcome Outcome, string? Message = null, IReadOnlyList<LogoListItem>? Logos = null,
    IReadOnlyList<BankListItem>? Banks = null, LogoDetail? Logo = null, BankDetail? Bank = null, InvoiceDefaults? Defaults = null,
    long? CompanyVersion = null, CompanyInvoicePresentation? Snapshot = null,
    IReadOnlyList<CompanyTaxDetail>? TaxRows = null, CompanyTaxDetail? Tax = null);

public sealed record InvoiceLogoImage(Guid SourceId, string ContentType, ImmutableArray<byte> Content);
public sealed record InvoiceBankInformation(Guid SourceId, string? BankName, string? AccountHolder, string? AccountNumber,
    string? Ifsc, string? Branch, string? Address, string? Instructions, string? Upi);
public sealed record InvoiceSellerTax(Guid RegistrationId, string Gstin, string StateCode);
public sealed record CompanyInvoicePresentation(Guid CompanyId, DateTime CapturedUtc, InvoiceLogoImage? Primary,
    InvoiceLogoImage? Secondary, InvoiceBankInformation? Bank, string? Header, string? Footer, string? Signatory, string? Terms,
    InvoiceSellerTax? Tax = null,InvoicePresentationOptions? Options=null,InvoiceLogoImage? Signature=null);

public sealed record ManagedLogoImage(string ContentType, byte[] Content, byte[] Thumbnail);
public interface ICompanyImageProcessor
{
    ManagedLogoImage? Prepare(byte[] raw, CancellationToken cancellationToken);
}
public interface ICompanyPresentationStore
{
    Task<PresentationResult> ExecuteAsync(PresentationRequest request, AuthenticatedIdentity actor, ManagedLogoImage? image, CancellationToken ct);
}

public sealed class CompanyPresentationService(AuthenticationService authentication, IApplicationOperationCoordinator lifetime,
    ICompanyPresentationStore store, ICompanyImageProcessor images)
{
    public static IReadOnlyList<string> LogoUsages { get; } =
        Array.AsReadOnly(new[] { "Primary company", "Secondary company", "Invoice header", "Invoice footer", "Other / custom" });

    public Task<PresentationResult> ResolveInvoicePresentationAsync(Guid companyId, CancellationToken ct = default) =>
        ExecuteAsync(new(PresentationAction.Snapshot, companyId), ct);

    public async Task<PresentationResult> ExecuteAsync(PresentationRequest request, CancellationToken ct = default)
    {
        PresentationResult result = new(CompanyOutcome.Unavailable);
        try
        {
            await lifetime.RunAsync(async token =>
            {
                result = await authentication.RunAuthenticatedAsync(async actor =>
                {
                    ManagedLogoImage? image = null;
                    if (request.Action == PresentationAction.SaveLogo)
                    {
                        if (request.Logo is not { } logo || !TextOk(logo.Name, 120, required: true) ||
                            !LogoUsages.Contains(logo.Usage) || logo.Order is < 0 or > 99999)
                            return Invalid("Enter a logo name, usage and order from 0 to 99999.");
                        if (logo.Upload is { } upload)
                        {
                            if (upload.Length is < 45 or > CompanyLogo.MaximumBytes)
                                return Invalid("Choose a PNG or JPEG up to 1 MiB and 2048 × 2048 pixels.");
                            image = await Task.Run(() => images.Prepare(upload.ToArray(), token), token);
                            if (image is null) return Invalid("The image could not be decoded or exceeds the logo size limits.");
                        }
                    }
                    if (request.Action == PresentationAction.SaveBank && !BankFieldsOk(request.Bank))
                        return Invalid("Enter bank name, account holder and account number. Check field lengths and display order.");
                    if (request.Action == PresentationAction.SaveTax &&
                        (request.Tax is not { } tax || !TextOk(tax.Gstin, 100, true) ||
                         !GTS.Billing.Domain.CompanyRules.States.ContainsKey(tax.StateCode ?? "") || tax.Default && !tax.Active))
                        return Invalid("Enter a GSTIN, choose a state, and keep a default registration active.");
                    if (request.Action == PresentationAction.SaveDefaults &&
                        (request.Defaults is not { } defaults || !TextOk(defaults.Header, 1000) || !TextOk(defaults.Footer, 1000) ||
                            !TextOk(defaults.Terms, 8000) || !TextOk(defaults.Signatory, 200)))
                        return Invalid("Header and footer allow 1000 characters, terms 8000 and signatory 200.");
                    if (request.Search?.Length > 100) return Invalid("Search supports up to 100 characters.");
                    var response = await store.ExecuteAsync(request, actor, image, token);
                    if (response.Outcome == CompanyOutcome.SessionRequired) throw new UnauthorizedAccessException();
                    return response;
                }, token);
            }, ct);
        }
        catch (UnauthorizedAccessException) { return new(CompanyOutcome.SessionRequired); }
        catch (Exception e) when (e is System.Data.Common.DbException or IOException or InvalidOperationException)
        { return new(CompanyOutcome.Unavailable); }
        return result;
    }

    private static bool BankFieldsOk(BankDraft? bank) => bank is not null &&
        TextOk(bank.BankName, 200, true) && TextOk(bank.AccountHolder, 200, true) && TextOk(bank.AccountNumber, 200, true) &&
        TextOk(bank.Ifsc, 200) && TextOk(bank.Branch, 200) && TextOk(bank.AccountType, 200) && TextOk(bank.Upi, 200) &&
        TextOk(bank.Address, 500) && TextOk(bank.Instructions, 2000) && bank.Order is >= 0 and <= 99999;

    private static bool TextOk(string? value, int maximum, bool required = false) =>
        !(required && string.IsNullOrWhiteSpace(value)) && (value is null || value.Length <= maximum && !value.Contains('\0'));

    private static PresentationResult Invalid(string message) => new(CompanyOutcome.InvalidInput, message);
}
