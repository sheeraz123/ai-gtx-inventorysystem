namespace GTS.Billing.Domain;

public sealed class Customer : CompanyRecord
{
    public string Code {get;set;}="";
    public string LegalName {get;set;}="";
    public string NormalizedName {get;set;}="";
    public string? TradeName {get;set;}
    public string? NormalizedTradeName {get;set;}
    public string? DisplayName {get;set;}
    public string CustomerType {get;set;}="Business";
    public string? GroupLabel {get;set;}
    public string? CategoryLabel {get;set;}
    public string Status {get;set;}="Active";
    public DateOnly? SinceDate {get;set;}
    public string? Mobile {get;set;}
    public string? NormalizedMobile {get;set;}
    public string? Email {get;set;}
    public string RegistrationType {get;set;}="Unregistered";
    public string? Gstin {get;set;}
    public string? Pan {get;set;}
    public string? GstLegalName {get;set;}
    public string? GstStateCode {get;set;}
    public string? SupplyClassification {get;set;}
    public string? ExemptionNotes {get;set;}
    public DateOnly? GstEffectiveDate {get;set;}
    public string? PaymentTerms {get;set;}
    public int CreditDays {get;set;}
    public decimal CreditLimit {get;set;}
    public bool CreditHold {get;set;}
    public string? PaymentMode {get;set;}
    public string DeliveryMethod {get;set;}="Print";
    public string Currency {get;set;}="INR";
    public bool PurchaseOrderRequired {get;set;}
    public string? BillingInstructions {get;set;}
    public string? Notes {get;set;}
    public string? InternalRemarks {get;set;}
    public DateTime CreatedUtc {get;set;}=DateTime.UtcNow;
    public DateTime UpdatedUtc {get;set;}=DateTime.UtcNow;
    public Guid CreatedByUserId {get;set;}
    public Guid UpdatedByUserId {get;set;}
}
public abstract class CustomerRecord : CompanyRecord
{
    public Guid CustomerId {get;set;}
}
public sealed class CustomerAddress : CustomerRecord
{
    public string Label {get;set;}="";
    public string AddressType {get;set;}="Billing";
    public string RecipientName {get;set;}="";
    public string? Organization {get;set;}
    public string Line1 {get;set;}="";
    public string? Line2 {get;set;}
    public string? Landmark {get;set;}
    public string? Locality {get;set;}
    public string City {get;set;}="";
    public string? District {get;set;}
    public string? StateName {get;set;}
    public string? StateCode {get;set;}
    public string PostalCode {get;set;}="";
    public string Country {get;set;}="IN";
    public string? Mobile {get;set;}
    public string? Email {get;set;}
    public bool IsDefaultBilling {get;set;}
    public bool IsDefaultShipping {get;set;}
    public bool IsActive {get;set;}=true;
}
public sealed class CustomerContact : CustomerRecord
{
    public string Name {get;set;}="";
    public string? Designation {get;set;}
    public string? Department {get;set;}
    public string? Mobile {get;set;}
    public string? NormalizedMobile {get;set;}
    public string? AlternatePhone {get;set;}
    public string? Email {get;set;}
    public string? Whatsapp {get;set;}
    public bool IsPrimary {get;set;}
    public bool IsActive {get;set;}=true;
    public string? Notes {get;set;}
}
public sealed class CustomerBankDetail : CustomerRecord
{
    public string? BankName {get;set;}
    public string? AccountHolder {get;set;}
    public string? AccountNumber {get;set;}
    public string? Ifsc {get;set;}
    public string? Branch {get;set;}
    public string? UpiId {get;set;}
    public string? ReferenceNotes {get;set;}
    public bool IsActive {get;set;}=true;
}
public sealed class CustomerOpeningBalanceDraft : CustomerRecord
{
    public decimal Amount {get;set;}
    public string Nature {get;set;}="Zero";
    public DateOnly? AsOfDate {get;set;}
    public string? Remarks {get;set;}
    public string? Reference {get;set;}
}

public sealed record CustomerCreditDraft
{
    public string? PaymentTerms {get;init;}
    public int CreditDays {get;init;}
    public decimal CreditLimit {get;init;}
    public bool CreditHold {get;init;}
    public string? PaymentMode {get;init;}
    public string DeliveryMethod {get;init;}="Print";
    public string Currency {get;init;}="INR";
    public bool PurchaseOrderRequired {get;init;}
    public string? BillingInstructions {get;init;}
}
public sealed record CustomerOpeningDraft
{
    public decimal Amount {get;init;}
    public string Nature {get;init;}="Zero";
    public DateOnly? AsOfDate {get;init;}
    public string? Remarks {get;init;}
    public string? Reference {get;init;}
}
public sealed record CustomerBankDraft
{
    public string? BankName {get;init;}
    public string? AccountHolder {get;init;}
    public string? AccountNumber {get;init;}
    public string? Ifsc {get;init;}
    public string? Branch {get;init;}
    public string? UpiId {get;init;}
    public string? ReferenceNotes {get;init;}
}
public sealed record CustomerAddressDraft
{
    public Guid? Id {get;init;}
    public long Version {get;init;}
    public string Label {get;init;}="";
    public string AddressType {get;init;}="Billing";
    public string RecipientName {get;init;}="";
    public string? Organization {get;init;}
    public string Line1 {get;init;}="";
    public string? Line2 {get;init;}
    public string? Landmark {get;init;}
    public string? Locality {get;init;}
    public string City {get;init;}="";
    public string? District {get;init;}
    public string? StateName {get;init;}
    public string? StateCode {get;init;}
    public string PostalCode {get;init;}="";
    public string Country {get;init;}="IN";
    public string? Mobile {get;init;}
    public string? Email {get;init;}
    public bool IsDefaultBilling {get;init;}
    public bool IsDefaultShipping {get;init;}
    public bool IsActive {get;init;}=true;
}
public sealed record CustomerContactDraft
{
    public Guid? Id {get;init;}
    public long Version {get;init;}
    public string Name {get;init;}="";
    public string? Designation {get;init;}
    public string? Department {get;init;}
    public string? Mobile {get;init;}
    public string? AlternatePhone {get;init;}
    public string? Email {get;init;}
    public string? Whatsapp {get;init;}
    public bool IsPrimary {get;init;}
    public bool IsActive {get;init;}=true;
    public string? Notes {get;init;}
}
public sealed record CustomerDraft
{
    public string Code {get;init;}="";
    public string LegalName {get;init;}="";
    public string? TradeName {get;init;}
    public string? DisplayName {get;init;}
    public string CustomerType {get;init;}="Business";
    public string? GroupLabel {get;init;}
    public string? CategoryLabel {get;init;}
    public string Status {get;init;}="Active";
    public DateOnly? SinceDate {get;init;}
    public string? Mobile {get;init;}
    public string? Email {get;init;}
    public string RegistrationType {get;init;}="Unregistered";
    public string? Gstin {get;init;}
    public string? Pan {get;init;}
    public string? GstLegalName {get;init;}
    public string? GstStateCode {get;init;}
    public string? SupplyClassification {get;init;}
    public string? ExemptionNotes {get;init;}
    public DateOnly? GstEffectiveDate {get;init;}
    public string? Notes {get;init;}
    public string? InternalRemarks {get;init;}
    public CustomerCreditDraft Credit {get;init;}=new();
    public CustomerOpeningDraft Opening {get;init;}=new();
    public CustomerBankDraft? Bank {get;init;}
    public IReadOnlyList<CustomerAddressDraft> Addresses {get;init;}=[];
    public IReadOnlyList<CustomerContactDraft> Contacts {get;init;}=[];
}

