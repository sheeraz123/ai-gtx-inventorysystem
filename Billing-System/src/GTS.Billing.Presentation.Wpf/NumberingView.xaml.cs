using System.Windows.Controls;
using System.Windows.Data;
using GTS.Billing.Application;

namespace GTS.Billing.Presentation.Wpf;

public partial class NumberingView : UserControl
{
    public NumberingView()=>InitializeComponent();

    internal void ConfigureColumns(NumberingMaster master)
    {
        Records.Columns.Clear();
        void Text(string title,string path,int width,string? key=null,string? format=null)
        {
            var column=new DataGridTextColumn {Header=title,Binding=new Binding(path){StringFormat=format},MinWidth=72,
                Width=width==0?new DataGridLength(1,DataGridLengthUnitType.Star):new DataGridLength(width)};
            if(key is not null) SettingsDisplay.SetLabelKey(column,"Numbering."+key);
            Records.Columns.Add(column);
        }
        void Flag(string title,string path,string key,int width)
        {
            var column=new DataGridCheckBoxColumn {Header=title,Binding=new Binding(path){Mode=BindingMode.OneWay},
                Width=new DataGridLength(width)};
            SettingsDisplay.SetLabelKey(column,"Numbering."+key);
            Records.Columns.Add(column);
        }
        if(master==NumberingMaster.Years)
        {
            Text("Year code","Entry.Code",110,"YearCode");
            Text("Year name","Entry.Name",0,"YearName");
            Text("Starts","Entry.Start",125,format:"{0:d}");
            Text("Ends","Entry.End",125,format:"{0:d}");
            Flag("Current year","Entry.IsCurrent","Current",125);
        }
        else
        {
            Text("Series code","Entry.Code",110,"SeriesCode");
            Text("Series name","Entry.Name",0,"SeriesName");
            Text("Document type","Document",155,"DocumentType");
            Text("Financial year","Entry.YearLabel",105);
            Text("Prefix / format","Format",145,"Prefix");
            Text("Next number","Entry.Next",105,"Next");
            Flag("Default","Entry.IsDefault","Default",95);
        }
        Text("Status","Status",92);
    }
}

public sealed record NumberingRow(NumberingEntry Entry,NumberingMaster Master)
{
    public string Document=>DocumentTypes.Label(Entry.DocumentType);
    public string Format=>Entry.Prefix+(Entry.Prefix.Length>0?Entry.Separator:"")+"#"+
        (Entry.Suffix.Length>0?Entry.Separator+Entry.Suffix:"");
    public string Status=>Master==NumberingMaster.Years?(Entry.Open?"Open":"Closed"):(Entry.Active?"Active":"Inactive");
}
