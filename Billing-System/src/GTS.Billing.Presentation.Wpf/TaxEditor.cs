using System.Globalization;
using System.Windows.Controls;
using GTS.Billing.Application;
using GTS.Billing.Domain;

namespace GTS.Billing.Presentation.Wpf;

internal static class TaxEditor
{
    internal static string Label(string key,string fallback)=>SettingsDisplay.Text("Tax."+key,fallback);
    internal static string Treatment(TaxTreatment value)=>Label(value.ToString(),value switch
    {
        TaxTreatment.Taxable=>"Taxable",TaxTreatment.Exempt=>"Exempt",TaxTreatment.NilRated=>"Nil-rated",
        TaxTreatment.ZeroRated=>"Zero-rated",_=>"Non-GST"
    });
    private static DateOnly? Read(DatePicker picker)=>picker.SelectedDate is DateTime date?DateOnly.FromDateTime(date):null;

    internal static void Configure(MeasureEditorDialog dialog,TaxMaster master,TaxEntry? saved,TaxResult? options,
        Func<TaxEntry,Task<string?>> save)
    {
        var row=saved??new();
        if(master==TaxMaster.Classifications)
        {
            var type=dialog.Choice(Label("Classification","Tax classification"),new[]{"HSN","SAC"},saved?.Type??"HSN",locked:saved is not null);
            var code=dialog.Text(Label("Hsn","HSN code")+" / "+Label("Sac","SAC code")+" *",row.Code,8,saved is not null);
            var description=dialog.Text("Description *",row.Description,500);
            var name=dialog.Text("Short description",row.Name,120);
            var notes=dialog.Text("Notes",row.Notes,1000);
            dialog.Text("Status",row.Active?"Active":"Inactive",20,true);
            if(saved?.Summary is { } summary) dialog.Text("Associated products",summary,800,true);
            dialog.Save(()=>save(row with {Type=type.SelectedItem as string??"",Code=code.Text,Name=name.Text,Description=description.Text,Notes=notes.Text}));
            return;
        }
        if(master==TaxMaster.Rates)
        {
            var code=dialog.Text("Rate code *",row.Code,30,saved is not null);
            var name=dialog.Text("Rate name *",row.Name,150);
            var treatmentChoices=Enum.GetValues<TaxTreatment>().Select(value=>new TreatmentChoice(value,Treatment(value))).ToArray();
            var treatment=dialog.Choice(Label("Treatment","Tax treatment"),treatmentChoices,treatmentChoices.Single(x=>x.Value==row.Treatment),
                nameof(TreatmentChoice.Label),locked:saved is not null);
            var total=Percent(dialog,Label("Rate","GST rate"),row.Total,saved);
            var central=Percent(dialog,Label("Cgst","CGST"),row.Cgst,saved);
            var state=Percent(dialog,Label("Sgst","SGST"),row.Sgst,saved);
            var integrated=Percent(dialog,Label("Igst","IGST"),row.Igst,saved);
            var territory=Percent(dialog,Label("Utgst","UTGST"),row.Utgst,saved);
            var cess=Percent(dialog,Label("Cess","Cess"),row.Cess,saved);
            var from=dialog.Date(Label("From","Effective from")+" *",row.From,saved is not null);
            var to=dialog.Date(Label("To","Effective to")+" (excluded)",row.To);
            var description=dialog.Text("Description",row.Description,500);
            dialog.Text("Status",row.Active?"Active":"Inactive",20,true);
            dialog.Save(()=>
            {
                if(!Decimal(total,out var tr) || !Decimal(central,out var c) || !Decimal(state,out var s) ||
                    !Decimal(integrated,out var i) || !Decimal(cess,out var ce) ||
                    !string.IsNullOrWhiteSpace(territory.Text) && !Decimal(territory,out _))
                    return Task.FromResult<string?>("Enter valid decimal percentages.");
                return save(row with {Code=code.Text,Name=name.Text,Description=description.Text,
                    Treatment=((TreatmentChoice)treatment.SelectedItem).Value,Total=tr,Cgst=c,Sgst=s,Igst=i,Cess=ce,
                    Utgst=string.IsNullOrWhiteSpace(territory.Text)?null:decimal.Parse(territory.Text,CultureInfo.CurrentCulture),
                    From=Read(from),To=Read(to)});
            });
            return;
        }

        var classes=options?.Classifications??[];
        var rates=options?.Rates??[];
        var classification=dialog.Choice(Label("Classification","Tax classification")+" *",classes,
            classes.FirstOrDefault(x=>x.Id==row.ClassificationId),nameof(TaxChoice.Label),locked:saved is not null,searchable:true);
        var version=dialog.Choice(Label("Rate","GST rate")+" *",rates,rates.FirstOrDefault(x=>x.Id==row.RateId),
            nameof(TaxChoice.Label),locked:saved is not null,searchable:true);
        var context=dialog.Text("Context code *",row.Context,40,saved is not null);
        var effectiveFrom=dialog.Date(Label("From","Effective from")+" *",row.From,saved is not null);
        var effectiveTo=dialog.Date(Label("To","Effective to")+" (excluded)",row.To);
        dialog.Save(()=>save(row with {ClassificationId=(classification.SelectedItem as TaxChoice)?.Id,
            RateId=(version.SelectedItem as TaxChoice)?.Id,Context=context.Text,From=Read(effectiveFrom),To=Read(effectiveTo)}));
    }

    internal static string Format(TaxSnapshot x) =>
        $"{x.Type} {x.Code} · {Treatment(x.Treatment)} · {x.RateCode} ({x.RateFrom:yyyy-MM-dd}){Environment.NewLine}"+
        $"{Label("Rate","GST rate")} {x.Total:G29}% · {Label("Cgst","CGST")} {x.Cgst:G29}% · {Label("Sgst","SGST")} {x.Sgst:G29}% · "+
        $"{Label("Igst","IGST")} {x.Igst:G29}% · {Label("Utgst","UTGST")} {(x.Utgst is decimal ut?$"{ut:G29}%":"—")} · {Label("Cess","Cess")} {x.Cess:G29}%";
    private static TextBox Percent(MeasureEditorDialog dialog,string label,decimal? value,TaxEntry? saved) =>
        dialog.Text(label+" (%)",value?.ToString("G29",CultureInfo.CurrentCulture),16,saved is not null);
    private static bool Decimal(TextBox box,out decimal value)=>decimal.TryParse(box.Text,NumberStyles.Number,CultureInfo.CurrentCulture,out value);
    private sealed record TreatmentChoice(TaxTreatment Value,string Label);
}
