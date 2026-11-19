using GTS.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

internal static class SchemaV3
{
    public static void Configure(ModelBuilder m)
    {
        SchemaV2.Configure(m);
        m.Entity<GstRegistration>().Metadata.RemoveCheckConstraint("CK_Gst_Format");
        m.Entity<CompanyBankAccount>().ToTable(t=>t.HasCheckConstraint("CK_Bank_Required",
            "length(trim(BankName)) > 0 AND length(trim(AccountHolder)) > 0 AND length(AccountNumber) > 0"));
    }
}
