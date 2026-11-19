using GTS.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GTS.Billing.Infrastructure;

internal static class CustomerMoneyStorage
{
    public static long Encode(decimal value)
    {
        if(!CustomerRules.ValidMoney(value)) throw new InvalidOperationException("Unsupported monetary precision or range.");
        return checked((long)(value*CustomerRules.MoneyScale));
    }
    public static decimal Decode(long value)=>value/CustomerRules.MoneyScale;
}
internal static class SchemaV4
{
    private static EntityTypeBuilder<T> Record<T>(ModelBuilder m,string table) where T:CompanyRecord
    {
        var e=m.Entity<T>();e.ToTable(table);e.HasKey(x=>x.Id);e.Property(x=>x.Id).ValueGeneratedNever();
        e.Property(x=>x.Version).IsConcurrencyToken();
        e.HasAlternateKey(x=>new{x.CompanyId,x.Id});
        e.HasOne<Company>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        return e;
    }
    private static EntityTypeBuilder<T> Child<T>(ModelBuilder m,string table) where T:CustomerRecord
    {
        var e=Record<T>(m,table);
        e.HasAlternateKey(x=>new{x.CompanyId,x.CustomerId,x.Id});
        e.HasOne<Customer>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.CustomerId})
            .HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        return e;
    }
    private static void Check<T>(EntityTypeBuilder<T> e,string name,string sql) where T:class=>e.ToTable(t=>t.HasCheckConstraint(name,sql));
    public static void Configure(ModelBuilder m)
    {
        SchemaV3.Configure(m);
        var c=Record<Customer>(m,"Customers");
        c.HasIndex(x=>new{x.CompanyId,x.Code}).IsUnique();
        c.HasIndex(x=>new{x.CompanyId,x.NormalizedName});
        c.HasIndex(x=>new{x.CompanyId,x.Gstin});
        c.HasIndex(x=>new{x.CompanyId,x.Pan});
        c.HasIndex(x=>new{x.CompanyId,x.NormalizedMobile});
        c.HasIndex(x=>new{x.CompanyId,x.Status,x.RegistrationType});
        c.HasOne<User>().WithMany().HasForeignKey(x=>x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        c.HasOne<User>().WithMany().HasForeignKey(x=>x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        c.Property(x=>x.CreditLimit).HasConversion(v=>CustomerMoneyStorage.Encode(v),v=>CustomerMoneyStorage.Decode(v))
            .HasColumnType("INTEGER").HasPrecision(19,4);
        Check(c,"CK_Customer_Identity","length(Code) BETWEEN 1 AND 40 AND Code = upper(trim(Code)) AND Code NOT GLOB '*[^A-Z0-9/_-]*' AND substr(Code,1,1) GLOB '[A-Z0-9]' AND length(trim(LegalName)) BETWEEN 1 AND 200 AND length(NormalizedName) > 0");
        Check(c,"CK_Customer_Status","Status IN ('Active','Inactive','Blocked')");
        Check(c,"CK_Customer_Type","CustomerType IN ('Individual','Business','Government','Other')");
        Check(c,"CK_Customer_Registration","RegistrationType IN ('Regular','Composition','Unregistered','Consumer','SezUnit','SezDeveloper','GovernmentSpecial')");
        Check(c,"CK_Customer_Credit","typeof(CreditLimit) = 'integer' AND CreditLimit BETWEEN 0 AND 999999999999999999 AND typeof(CreditDays) = 'integer' AND CreditDays BETWEEN 0 AND 36500");
        Check(c,"CK_Customer_Flags","CreditHold IN (0,1) AND PurchaseOrderRequired IN (0,1)");
        Check(c,"CK_Customer_Currency","length(Currency) = 3 AND Currency NOT GLOB '*[^A-Z]*'");
        Check(c,"CK_Customer_Delivery","DeliveryMethod IN ('Print','Email','Sms','Whatsapp','Other')");
        var address=Child<CustomerAddress>(m,"CustomerAddresses");
        address.HasIndex(x=>new{x.CompanyId,x.CustomerId,x.IsActive});
        address.HasIndex(x=>new{x.CompanyId,x.CustomerId}).HasDatabaseName("UX_CustomerAddress_DefaultBilling")
            .IsUnique().HasFilter("IsDefaultBilling = 1");
        address.HasIndex(x=>new{x.CompanyId,x.CustomerId,x.IsDefaultShipping}).HasDatabaseName("UX_CustomerAddress_DefaultShipping")
            .IsUnique().HasFilter("IsDefaultShipping = 1");
        Check(address,"CK_CustomerAddress_Type","AddressType IN ('Billing','Shipping','Both')");
        Check(address,"CK_CustomerAddress_Required","length(trim(Label)) > 0 AND length(trim(RecipientName)) > 0 AND length(trim(Line1)) > 0 AND length(trim(City)) > 0 AND length(Country) = 2 AND Country NOT GLOB '*[^A-Z]*'");
        Check(address,"CK_CustomerAddress_DefaultBilling","IsDefaultBilling = 0 OR (IsActive = 1 AND AddressType IN ('Billing','Both'))");
        Check(address,"CK_CustomerAddress_DefaultShipping","IsDefaultShipping = 0 OR (IsActive = 1 AND AddressType IN ('Shipping','Both'))");
        Check(address,"CK_CustomerAddress_IndianState","Country <> 'IN' OR (StateCode IS NOT NULL AND length(StateCode) = 2)");
        Check(address,"CK_CustomerAddress_Flags","IsActive IN (0,1) AND IsDefaultBilling IN (0,1) AND IsDefaultShipping IN (0,1)");
        var contact=Child<CustomerContact>(m,"CustomerContacts");
        contact.HasIndex(x=>new{x.CompanyId,x.CustomerId,x.NormalizedMobile});
        contact.HasIndex(x=>new{x.CompanyId,x.CustomerId}).HasDatabaseName("UX_CustomerContact_Primary")
            .IsUnique().HasFilter("IsPrimary = 1");
        Check(contact,"CK_CustomerContact_Name","length(trim(Name)) > 0");
        Check(contact,"CK_CustomerContact_PrimaryActive","IsPrimary = 0 OR IsActive = 1");
        Check(contact,"CK_CustomerContact_Flags","IsActive IN (0,1) AND IsPrimary IN (0,1)");
        var bank=Child<CustomerBankDetail>(m,"CustomerBankDetails");
        bank.HasIndex(x=>new{x.CompanyId,x.CustomerId}).IsUnique();
        Check(bank,"CK_CustomerBank_Active","IsActive IN (0,1)");
        var opening=Child<CustomerOpeningBalanceDraft>(m,"CustomerOpeningBalanceDrafts");
        opening.HasIndex(x=>new{x.CompanyId,x.CustomerId}).IsUnique();
        opening.Property(x=>x.Amount).HasConversion(v=>CustomerMoneyStorage.Encode(v),v=>CustomerMoneyStorage.Decode(v))
            .HasColumnType("INTEGER").HasPrecision(19,4);
        Check(opening,"CK_CustomerOpening_Money","typeof(Amount) = 'integer' AND Amount BETWEEN 0 AND 999999999999999999");
        Check(opening,"CK_CustomerOpening_Nature","(Amount = 0 AND Nature = 'Zero') OR (Amount > 0 AND Nature IN ('Debit','Credit') AND AsOfDate IS NOT NULL)");
    }
}

