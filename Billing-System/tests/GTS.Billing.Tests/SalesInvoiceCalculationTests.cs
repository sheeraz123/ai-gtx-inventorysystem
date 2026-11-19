using GTS.Billing.Application;
using GTS.Billing.Domain;
using Xunit;

namespace GTS.Billing.Tests;

public sealed class SalesInvoiceCalculationTests
{
    private static readonly DateOnly Day=new(2026,11,19);

    [Fact]
    public void ExclusiveLinesApplyDiscountBeforeTaxAndReconcileInvoiceTotals()
    {
        var item=new SalesLineInput(Guid.NewGuid(),null,2,100,false,10);
        var first=SalesCalculation.Calculate(item,Tax(9,9,0))!;
        var second=SalesCalculation.Calculate(item with {Quantity=1,Price=55,DiscountPercent=0,DiscountAmount=5},Tax(0,0,5))!;
        Assert.Equal(new SalesAmounts(200,20,180,16.20m,16.20m,0,0,0,0,212.40m),first);
        Assert.Equal(new SalesAmounts(55,5,50,0,0,2.50m,0,0,0,52.50m),second);
        Assert.Equal(new SalesAmounts(255,25,230,16.20m,16.20m,2.50m,0,0,0,264.90m),SalesCalculation.Sum([first,second]));
    }

    [Fact]
    public void InclusivePriceReconcilesTaxComponentsAndRoundOff()
    {
        var item=new SalesLineInput(Guid.NewGuid(),null,2,1,true);
        var line=SalesCalculation.Calculate(item,Tax(9,9,0))!;
        Assert.Equal(2m,line.Total);
        Assert.Equal(1.69m,line.Taxable);
        Assert.Equal(.30m,line.Tax);
        Assert.Equal(.01m,line.RoundOff);
        Assert.Equal(line.Total,line.Taxable+line.Tax+line.RoundOff);
    }

    [Fact]
    public void InvalidLineReportsEachAffectedFieldAndDoesNotCalculate()
    {
        var item=new SalesLineInput(Guid.NewGuid(),null,0,-1,false,10,3);
        var errors=SalesCalculation.ValidateLine(item,2);
        Assert.Equal(new[]{"Quantity","Price","DiscountPercent","DiscountAmount"},errors.Select(error=>error.Field));
        Assert.All(errors,error=>Assert.Equal(2,error.Line));
        Assert.Null(SalesCalculation.Calculate(item,Tax(9,9,0)));
    }

    [Fact]
    public void FixedDiscountCannotExceedGrossAndZeroPriceIsValid()
    {
        var item=new SalesLineInput(Guid.NewGuid(),null,2,5,false,DiscountAmount:10.01m);
        Assert.Equal(nameof(item.DiscountAmount),Assert.Single(SalesCalculation.ValidateLine(item)).Field);
        Assert.Null(SalesCalculation.Calculate(item,Tax(9,9,0)));
        Assert.Equal(0m,SalesCalculation.Calculate(item with {Price=0,DiscountAmount=0},Tax(9,9,0))!.Total);
    }

    [Fact]
    public void PreviewCanCalculateBeforeNumberingButSaveRequiresYearAndSeries()
    {
        var draft=new SalesDraft(Day,RegistrationId:Guid.NewGuid(),CustomerId:Guid.NewGuid(),PlaceOfSupply:"27",
            Jurisdiction:GstJurisdiction.IntraState,DomesticConfirmed:true,Lines:[new(Guid.NewGuid(),null,1,100,false)]);
        Assert.Empty(draft.RequiredFields(requireNumbering:false));
        Assert.Equal(new[]{"YearId","SeriesId"},draft.RequiredFields(requireNumbering:true).Select(error=>error.Field));
        Assert.Contains(nameof(SalesDraft.RegistrationId),new SalesDraft(Day).RequiredFields(false).Select(error=>error.Field));
    }

    private static AppliedTaxSnapshot Tax(decimal cgst,decimal sgst,decimal igst)
    {
        var snapshot=new TaxSnapshot(Guid.NewGuid(),"HSN","6404","Synthetic footwear",Guid.NewGuid(),Guid.NewGuid(),
            "SYNTHETIC",Day,TaxTreatment.Taxable,cgst+sgst+igst,cgst,sgst,igst,null,0,Day,"SYNTHETIC");
        return new(snapshot,igst==0?GstJurisdiction.IntraState:GstJurisdiction.Interstate,"Synthetic",0,cgst,sgst,igst,0,0);
    }
}
