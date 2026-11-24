using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using GTS.Billing.Application;
using Microsoft.Win32;

namespace GTS.Billing.Presentation.Wpf;

internal sealed class SalesFormConfigurationDialog : Window
{
    private readonly IReadOnlyList<SettingEntry> entries;
    private readonly Func<SettingEdit[],Task<SettingsResult>> save;
    private readonly Func<Task<SettingsResult>> history;
    private readonly List<Row> rows;
    private readonly Dictionary<string,SalesFormRule> original;
    private readonly SalesFormConfiguration baseline;
    private readonly NoticeBanner notice=new();
    private readonly TextBox search=new() {Width=185};
    private readonly ComboBox section=new() {MinWidth=140};
    private readonly DataGrid grid=new() {AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,AllowDrop=true,
        SelectionMode=DataGridSelectionMode.Single};
    private readonly ICollectionView view;
    private readonly bool editable;
    private bool busy,saved;
    private Point dragStart;

    internal SalesFormConfigurationDialog(Window owner,string company,IReadOnlyList<SettingEntry> entries,
        Func<SettingEdit[],Task<SettingsResult>> save,Func<Task<SettingsResult>> history,bool graftex=false)
    {
        this.entries=entries;this.save=save;this.history=history;
        Owner=owner;Title="Sales Invoice Configuration";
        Width=Math.Min(1460,SystemParameters.WorkArea.Width);Height=Math.Min(790,SystemParameters.WorkArea.Height);
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"Palette.Surface");
        SetResourceReference(ForegroundProperty,"Palette.Text");
        var stored=entries.ToDictionary(x=>x.Key,StringComparer.Ordinal);
        baseline=graftex?SalesFormConfiguration.Empty.ApplyGraftexDefaults():SalesFormConfiguration.Empty;
        rows=SalesFormConfiguration.Fields.Select(field=>new Row(field,
            stored.TryGetValue(field.Setting.Key,out var item) && item.StoredText is { } saved &&
            field.Parse(saved) is {Valid:true} value?value.Value:baseline.Field(field.Key))).ToList();
        original=rows.ToDictionary(row=>row.Key,row=>row.Rule(),StringComparer.Ordinal);
        editable=entries.Any(x=>x.CanEdit);
        grid.IsReadOnly=true;
        var layout=new DockPanel {Margin=new Thickness(18)};
        var top=new StackPanel();
        DockPanel.SetDock(top,Dock.Top);
        layout.Children.Add(top);
        top.Children.Add(new TextBlock {Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});
        top.Children.Add(new TextBlock {Text=company,Margin=new Thickness(0,4,0,10)});
        var controls=new WrapPanel();
        top.Children.Add(controls);
        controls.Children.Add(new Label {Content="_Search",Target=search});controls.Children.Add(search);
        controls.Children.Add(new Label {Content="Section",Target=section});controls.Children.Add(section);
        section.ItemsSource=new[]{"All sections","Sections"}.Concat(rows.Select(r=>r.Section).Distinct().Where(s=>s!="Sections")).ToArray();
        section.SelectedIndex=0;
        top.Children.Add(notice);
        var actions=new WrapPanel {Margin=new Thickness(0,10,0,0)};
        DockPanel.SetDock(actions,Dock.Bottom);layout.Children.Add(actions);
        layout.Children.Add(grid);
        Content=layout;
        AddColumn("Section",nameof(Row.Section));
        AddColumn("Field",nameof(Row.Name));
        AddColumn("Display label",nameof(Row.Label));
        AddColumn("Visible",nameof(Row.Visible));
        AddColumn("Required",nameof(Row.Required));
        AddColumn("Read only",nameof(Row.ReadOnly));
        AddColumn("Preview",nameof(Row.Preview));
        AddColumn("Print / PDF",nameof(Row.Print));
        AddColumn("Order",nameof(Row.Order));
        view=CollectionViewSource.GetDefaultView(rows);
        view.Filter=Matches;
        view.SortDescriptions.Add(new(nameof(Row.Section),ListSortDirection.Ascending));
        view.SortDescriptions.Add(new(nameof(Row.Order),ListSortDirection.Ascending));
        grid.ItemsSource=view;
        search.TextChanged+=(_,_)=>view.Refresh();
        section.SelectionChanged+=(_,_)=>view.Refresh();
        grid.MouseDoubleClick+=(_,eventArgs)=>
        {
            if(eventArgs.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(grid,source) is DataGridRow {Item:Row row})
                Edit(row,true);
        };
        if(editable)
        {
            Button("Edit configuration",(_,_)=>{if(grid.SelectedItem is Row row) Edit(row,false);});
            Button("Reset to defaults",(_,_)=>{if(Confirm("Reset this layout to its built-in values?")) LoadRules(new Dictionary<string,SalesFormRule>());});
            Button("Restore version",async (_,_)=>await RestoreVersionAsync());
            Button("Import JSON",(_,_)=>Import());
            Button("Save configuration",async (_,_)=>await SaveAsync());
            grid.PreviewMouseLeftButtonDown+=(_,e)=>dragStart=e.GetPosition(grid);
            grid.PreviewMouseMove+=(_,e)=>
            {
                if(e.LeftButton!=MouseButtonState.Pressed || grid.SelectedItem is not Row row || busy) return;
                var point=e.GetPosition(grid);
                if(Math.Abs(point.Y-dragStart.Y)>SystemParameters.MinimumVerticalDragDistance)
                    DragDrop.DoDragDrop(grid,new DataObject(typeof(Row),row),DragDropEffects.Move);
            };
            grid.Drop+=(_,e)=>
            {
                if(e.Data.GetData(typeof(Row)) is not Row from || e.OriginalSource is not DependencyObject target ||
                    ItemsControl.ContainerFromElement(grid,target) is not DataGridRow {Item:Row to} || from==to || from.Section!=to.Section) return;
                var peers=rows.Where(r=>r.Section==from.Section).OrderBy(r=>r.Order).ToList();
                peers.Remove(from);
                peers.Insert(peers.IndexOf(to),from);
                for(var index=0;index<peers.Count;index++) peers[index].Value=peers[index].Value with {Order=(index+1)*10};
                Refresh();
            };
        }
        Button("Preview layout",(_,_)=>Preview());
        Button("Export / duplicate JSON",(_,_)=>Export());
        Button("Close",(_,_)=>Close());
        Closing+=(_,e)=>e.Cancel=busy || !saved && rows.Any(row=>row.Rule()!=original[row.Key]) && !Confirm("Discard unsaved layout changes?");
        void AddColumn(string title,string property)=>grid.Columns.Add(new DataGridTextColumn
        {Header=title,Binding=new Binding(property),Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=75});
        void Button(string text,RoutedEventHandler handler)
        {
            var button=new Button {Content=text,Margin=new Thickness(0,4,8,0),Padding=new Thickness(9,5,9,5)};
            button.Click+=(sender,args)=>{if(!busy) handler(sender,args);};
            actions.Children.Add(button);
        }
    }

    private bool Matches(object item)=>item is Row row &&
        (section.SelectedIndex<=0 || Equals(section.SelectedItem,row.Section)) &&
        (string.IsNullOrWhiteSpace(search.Text) || row.Name.Contains(search.Text.Trim(),StringComparison.OrdinalIgnoreCase)
            || row.Label.Contains(search.Text.Trim(),StringComparison.OrdinalIgnoreCase) ||
            row.Key.Contains(search.Text.Trim(),StringComparison.OrdinalIgnoreCase));
    private bool Confirm(string text)=>MessageBox.Show(this,text,Title,MessageBoxButton.YesNo,MessageBoxImage.Question,
        MessageBoxResult.No)==MessageBoxResult.Yes;

    private void Edit(Row row,bool readOnly)
    {
        var field=row.Field;
        var current=row.Value;
        var dialog=new MeasureEditorDialog(field.Title,readOnly){Owner=this};
        var label=dialog.Text("Display label *",current.Label,80);
        var placeholder=dialog.Text("Placeholder / hint",current.Placeholder,80);
        var help=dialog.Text("Help / tooltip",current.Help,130);
        var order=dialog.Text("Display order",current.Order.ToString(),4);
        var value=dialog.Text("Default value",current.Default,140,locked:!field.TextDefault);
        var visible=Choice("Show in entry",current.Visible,!field.CanHideInEntry);
        if(field.Key=="TaxDetails")
            dialog.ShowMessage("Required controls whether GST review must be complete to save a draft. Finalization uses reviewed GST details or matching company defaults.",NoticeKind.Information);
        else if(field.CanCollapseForReview)
            dialog.ShowMessage(readOnly?"View mode. Select Edit configuration to change visibility.":
                "Choose No to hide this control during normal entry. Use Review GST details to edit it. GST review remains required.",NoticeKind.Information);
        var required=Choice("Required",current.Required,!field.CanChooseRequired);
        var locked=Choice("Read-only",current.ReadOnly,!field.TextDefault || field.Key=="PurchaseOrder");
        var validate=Choice("Validate configured requirement",current.Validate,!field.TextDefault);
        var preview=Choice("Include in preview",current.Preview,!field.Output);
        var print=Choice("Include in print / PDF",current.Print,!field.Output);
        dialog.Save(()=>
        {
            if(!int.TryParse(order.Text,out var position)) return Task.FromResult<string?>("Use an order from 0 to 9,999.");
            var changed=new SalesFormRule(label.Text,placeholder.Text,value.Text,help.Text,position,Pick(visible),Pick(required),
                Pick(locked),Pick(preview),Pick(print),Pick(validate));
            var parsed=field.Parse(JsonSerializer.Serialize(changed));
            if(!parsed.Valid) return Task.FromResult<string?>("Review the label, order, field rules and required/default combination.");
            row.Value=parsed.Value;
            return Task.FromResult<string?>(null);
        },"Apply to layout");
        dialog.ShowDialog();Refresh();

        ComboBox Choice(string name,bool selected,bool lockedField)=>dialog.Choice(name,new[]{"Yes","No"},selected?"Yes":"No",locked:lockedField);
        static bool Pick(ComboBox combo)=>Equals(combo.SelectedItem,"Yes");
    }

    private void Refresh()
    {
        grid.ItemsSource=null;view.Refresh();grid.ItemsSource=view;
    }

    private void LoadRules(IReadOnlyDictionary<string,SalesFormRule> imported)
    {
        var known=rows.ToDictionary(row=>row.Key,StringComparer.Ordinal);
        foreach(var (key,rule) in imported)
            if(!known.TryGetValue(key,out var row) || !row.Field.Parse(JsonSerializer.Serialize(rule)).Valid)
            {Report("Layout contains an unsupported or invalid rule: "+key,NoticeKind.Error);return;}
        foreach(var row in rows) row.Value=imported.TryGetValue(row.Key,out var rule)
            ?row.Field.Parse(JsonSerializer.Serialize(rule)).Value:baseline.Field(row.Key);
        Refresh();
        Report("Layout loaded. Save configuration to apply it to this company.",NoticeKind.Information);
    }

    private void Import()
    {
        var file=new OpenFileDialog {Filter="Invoice layout (*.json)|*.json",CheckFileExists=true};
        if(file.ShowDialog(this)!=true) return;
        try
        {
            using var stream=File.OpenRead(file.FileName);
            if(stream.Length>128_000){Report("Choose a layout file smaller than 128 KB.",NoticeKind.Error);return;}
            var rules=JsonSerializer.Deserialize<Dictionary<string,SalesFormRule>>(stream);
            if(rules is null){Report("The JSON file does not contain layout rules.",NoticeKind.Error);return;}
            LoadRules(rules);
        }
        catch(Exception err) when(err is JsonException or IOException or UnauthorizedAccessException)
        {Report("The layout JSON could not be read.",NoticeKind.Error);}
    }

    private void Export()
    {
        var file=new SaveFileDialog {Filter="Invoice layout (*.json)|*.json",FileName="Sales Invoice layout.json",
            AddExtension=true,DefaultExt=".json"};
        if(file.ShowDialog(this)!=true) return;
        try
        {
            using var stream=File.Create(file.FileName);
            JsonSerializer.Serialize(stream,rows.ToDictionary(row=>row.Key,row=>row.Rule()),
                new JsonSerializerOptions {WriteIndented=true});
            Report("Layout exported. An authorized administrator can import it for another company.",NoticeKind.Success);
        }
        catch(Exception err) when(err is IOException or UnauthorizedAccessException)
        {Report("The layout file could not be saved.",NoticeKind.Error);}
    }

    private void Preview()
    {
        var dialog=new MeasureEditorDialog("Sales Invoice layout preview",true){Owner=this,Width=950};
        foreach(var group in rows.Where(r=>r.Value.Visible && r.Field.IsSection).OrderBy(r=>r.Order))
        {
            var panel=new WrapPanel();
            var items=rows.Where(r=>r.Value.Visible && !r.Field.IsSection &&
                r.Field.Section==(group.Field.Section=="Addresses"?"Addresses":group.Key)).OrderBy(r=>r.Order);
            foreach(var row in items)
                dialog.Text(row.Label+(row.Required?" *":""),row.Rule().Default.Length>0?row.Rule().Default:row.Rule().Placeholder,
                    180,true,panel:panel);
            if(panel.Children.Count==0) continue;
            dialog.AddCard(group.Label,panel);
        }
        dialog.ShowMessage("Field layout preview. Uses the current unsaved configuration.",NoticeKind.Information);
        dialog.ShowDialog();
    }

    private async Task RestoreVersionAsync()
    {
        busy=true;
        try
        {
            var result=await history();
            if(result.Outcome!=CompanyOutcome.Success || result.SalesFormVersions is not {Count:>0} versions)
            {Report("No saved layout versions are available.",NoticeKind.Information);return;}
            var dialog=new MeasureEditorDialog("Restore invoice layout",false){Owner=this};
            var choice=dialog.Choice("Saved version",versions,versions[0],nameof(SalesFormVersion.Label));
            dialog.Save(()=>
            {
                if(choice.SelectedItem is not SalesFormVersion version) return Task.FromResult<string?>("Select a version.");
                try
                {
                    var restored=JsonSerializer.Deserialize<Dictionary<string,SalesFormRule>>(version.Json);
                    if(restored is null) return Task.FromResult<string?>("This version could not be read.");
                    LoadRules(restored);
                    return Task.FromResult<string?>(null);
                }
                catch(JsonException) {return Task.FromResult<string?>("This version could not be read.");}
            },"Load version");
            dialog.ShowDialog();
        }
        finally {busy=false;}
    }

    private async Task SaveAsync()
    {
        var version=entries.ToDictionary(x=>x.Key,StringComparer.Ordinal);
        var changes=rows.Where(row=>row.Rule()!=original[row.Key]).Select(row=>
        {
            var key=row.Field.Setting.Key;
            return new SettingEdit(key,row.Rule()==baseline.Field(row.Key)?null:JsonSerializer.Serialize(row.Rule()),
                version.GetValueOrDefault(key)?.Version);
        }).ToArray();
        if(changes.Length==0){Report("No layout changes to save.",NoticeKind.Information);return;}
        busy=true;
        Report("Saving invoice layout…",NoticeKind.Information);
        try
        {
            var result=await save(changes);
            if(result.Outcome==CompanyOutcome.Success){saved=true;busy=false;DialogResult=true;return;}
            Report(result.Message??(result.Outcome==CompanyOutcome.Conflict?"The layout changed. Reopen the configuration and review it.":
                "The layout could not be saved. Check permissions and field rules."),NoticeKind.Error);
        }
        finally {busy=false;}
    }

    private void Report(string message,NoticeKind kind){notice.Kind=kind;notice.Text=message;}

    private sealed class Row(SalesFormField field,SalesFormRule value)
    {
        public SalesFormField Field {get;}=field;
        public SalesFormRule Value {get;set;}=value;
        public string Key=>Field.Key;
        public string Section=>Field.IsSection?"Sections":Field.Section;
        public string Name=>Field.Title;
        public string Label=>Value.Label;
        public bool Visible=>Value.Visible;
        public bool Required=>Value.Required;
        public bool ReadOnly=>Value.ReadOnly;
        public bool Preview=>Value.Preview;
        public bool Print=>Value.Print;
        public int Order=>Value.Order;
        public SalesFormRule Rule()=>Value;
    }
}
