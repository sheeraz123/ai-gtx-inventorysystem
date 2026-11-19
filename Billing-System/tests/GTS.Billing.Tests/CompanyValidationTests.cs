using GTS.Billing.Domain;
using Xunit;

namespace GTS.Billing.Tests;

public sealed class CompanyValidationTests
{
    internal static CompanyDraft Valid(string code="SYNTHETIC")=>new() {Code=code,LegalName="Synthetic Company",AddressLine1="Synthetic address",
        City="Mumbai",StateCode="27",Pin="400001"};
    [Theory]
    [InlineData("LegalName"," ")]
    [InlineData("Code"," ")]
    [InlineData("Code","bad code")]
    [InlineData("AddressLine1","")]
    [InlineData("City","")]
    [InlineData("StateCode","99")]
    [InlineData("StateCode","28")]
    [InlineData("Pin","12345")]
    [InlineData("Pin","abcdef")]
    [InlineData("Pan","INVALID")]
    [InlineData("Mobile","123")]
    [InlineData("AlternateMobile","+1 123 456 7890")]
    [InlineData("Landline","invalid")]
    [InlineData("Email","not-an-email")]
    [InlineData("Website","file:///private")]
    [InlineData("Country","US")]
    [InlineData("EstablishedOn","3000-01-01")]
    [InlineData("RegistrationType","Unknown")]
    public void InvalidFieldsAreRejected(string field,string value)
    {
        var d=Valid();
        typeof(CompanyDraft).GetProperty(field)!.SetValue(d,value);
        Assert.NotEmpty(CompanyRules.Validate(d).Errors);
    }
    [Fact]
    public void OptionalFieldsAndFormattedBusinessContactsAreAcceptedAndNormalized()
    {
        var d=Valid(" case-code ") with {LegalName=" Synthetic Company ",Mobile="+91 (98765) 43210",Landline="(022) 2345-6789",
            Email="test@example.invalid",Website="https://example.invalid"};
        var result=CompanyRules.Validate(d);
        Assert.Empty(result.Errors);Assert.Equal("CASE-CODE",result.Value.Code);Assert.Equal("Synthetic Company",result.Value.LegalName);
        Assert.Empty(CompanyRules.Validate(Valid()).Errors);
    }
    internal static string Gstin(string prefix="27AAAAA0000A1Z")
    {
        const string chars="0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var sum=0;var factor=2;
        for(var i=13;i>=0;i--) {var product=chars.IndexOf(prefix[i])*factor;sum+=product/36+product%36;factor=3-factor;}
        return prefix+chars[(36-sum%36)%36];
    }
    [Fact]
    public void GstinIsOptionalAndHasNoFormatChecksumPanOrStateValidation()
    {
        var value=Gstin();
        Assert.Equal("27AAAAA0000A1Z2",value); // Fixed synthetic mod-36 test vector.
        Assert.Empty(CompanyRules.Validate(Valid() with {RegistrationType="Regular",Gstin=value.ToLowerInvariant(),Pan="aaaaa0000a"}).Errors);
        Assert.Empty(CompanyRules.Validate(Valid() with {RegistrationType="Regular"}).Errors);
        Assert.Empty(CompanyRules.Validate(Valid() with {RegistrationType="Regular",Gstin=value,Pan="BBBBB0000B"}).Errors);
        Assert.Empty(CompanyRules.Validate(Valid() with {RegistrationType="Regular",Gstin="free text",StateCode="29"}).Errors);
        Assert.Empty(CompanyRules.Validate(Valid() with {Gstin=value}).Errors);
    }
    [Fact]
    public void PartialBankAndOversizedValuesAreRejected()
    {
        Assert.NotEmpty(CompanyRules.Validate(Valid() with {BankName="Synthetic bank"}).Errors);
        Assert.NotEmpty(CompanyRules.Validate(Valid() with {LegalName=new string('x',201)}).Errors);
        Assert.Empty(CompanyRules.Validate(Valid() with {BankName="Synthetic bank",AccountHolder="Synthetic",AccountNumber="1234567890",
            Ifsc="TEST0000001",UpiId="synthetic@bank"}).Errors);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("free-form")]
    [InlineData("X")]
    public void IfscIsOptionalAndHasNoFormatValidation(string? ifsc)
    {
        Assert.Empty(CompanyRules.Validate(Valid() with {BankName="Synthetic bank",AccountHolder="Synthetic",
            AccountNumber="1234567890",Ifsc=ifsc}).Errors);
    }
}

