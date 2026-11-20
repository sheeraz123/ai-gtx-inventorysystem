using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using GTS.Billing.Application;

namespace GTS.Billing.Presentation.Wpf;

internal sealed class ProductVariantsDialog : Window
{
    private readonly ProductVariantService service;
    private readonly CatalogRow product;
    private readonly MasterSearch search=new();
    private readonly CancellationTokenSource lifetime=new();
    private readonly NoticeBanner notice=new();
    private readonly DataGrid grid=new() {IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false};
    private readonly ComboBox section=new() {ItemsSource=new[]{"Variants","Size Master","Color Master","Other Attributes"},SelectedIndex=0};
    private readonly ComboBox statusFilter=new() {ItemsSource=new[]{"All statuses","Active","Inactive"},SelectedIndex=0};
    private readonly ComboBox sizeFilter=new() {MinWidth=115,DisplayMemberPath=nameof(VariantValue.Name)};
    private readonly ComboBox colorFilter=new() {MinWidth=105,DisplayMemberPath=nameof(VariantValue.Name)};
    private readonly TextBox query=new() {Width=205,MaxLength=100,ToolTip="Search from three characters"};
    private readonly WrapPanel actions=new();
    private VariantResult? data;
    private Guid? company;
    private bool busy;

    internal ProductVariantsDialog(Window owner,CatalogRow product,ProductVariantService service)
    {
        Owner=owner;this.product=product;this.service=service;
        Title=product.Name+" — Product variants";
        Width=Math.Min(1170,SystemParameters.WorkArea.Width);
        Height=Math.Min(725,SystemParameters.WorkArea.Height);
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"Palette.Surface");
        SetResourceReference(ForegroundProperty,"Palette.Text");
        var layout=new DockPanel {Margin=new Thickness(15)};
        var header=new StackPanel();
        DockPanel.SetDock(header,Dock.Top);
        layout.Children.Add(header);
        header.Children.Add(new TextBlock {Text=product.Name,FontSize=24,FontWeight=FontWeights.SemiBold});
        var navigation=new WrapPanel {Margin=new Thickness(0,12,0,4)};
        header.Children.Add(navigation);
        foreach(var input in new Control[]{section,statusFilter,sizeFilter,colorFilter,query})
        {
            input.Margin=new Thickness(0,0,8,4);
            navigation.Children.Add(input);
        }
        header.Children.Add(actions);
        header.Children.Add(notice);
        layout.Children.Add(grid);
        Content=layout;
        section.SelectionChanged+=(_,_)=>{Render();Schedule();};
        statusFilter.SelectionChanged+=(_,_)=>Render();
        sizeFilter.SelectionChanged+=(_,_)=>Render();
        colorFilter.SelectionChanged+=(_,_)=>Render();
        query.TextChanged+=(_,_)=>Schedule();
        var viewCommand=new RelayCommand(()=>OpenSelected(true),()=>!busy && grid.SelectedItem is not null);
        MasterGridInteraction.SetViewRowCommand(grid,viewCommand);
        Closing+=(_,args)=>args.Cancel|=busy;
        Closed+=(_,_)=>{search.Cancel();lifetime.Cancel();lifetime.Dispose();};
        Loaded+=(_,_)=>Schedule();
    }

    private void Schedule()
    {
        var text=MasterSearch.Query(query.Text);
        _=search.ScheduleAsync(text.Length>0,()=>busy,ct=>LoadAsync(text,ct));
    }

    private async Task LoadAsync(string text,CancellationToken ct)
    {
        var response=await service.ExecuteAsync(new(product.Id,Search:section.SelectedIndex==0?text:null,ExpectedCompany:company),ct);
        if(ct.IsCancellationRequested) return;
        if(response.Outcome!=CompanyOutcome.Success){data=null;Error(response);Render();return;}
        data=response;company=response.Company;
        UpdateFilter(sizeFilter,"All sizes",response.Options!.Where(x=>x.Axis.StartsWith("SIZE-",StringComparison.Ordinal)));
        UpdateFilter(colorFilter,"All colors",response.Options!.Where(x=>x.Axis=="COLOR"));
        Render();
    }

    private static void UpdateFilter(ComboBox filter,string label,IEnumerable<VariantValue> values)
    {
        var id=(filter.SelectedItem as VariantValue)?.Id;
        var options=new[]{new VariantValue(Guid.Empty,0,"","",label)}.Concat(values).ToArray();
        filter.ItemsSource=options;
        filter.SelectedItem=options.FirstOrDefault(x=>x.Id==id)??options[0];
    }

    private void Render()
    {
        grid.ItemsSource=null;grid.Columns.Clear();actions.Children.Clear();
        sizeFilter.Visibility=colorFilter.Visibility=section.SelectedIndex==0?Visibility.Visible:Visibility.Collapsed;
        AddButton("Refresh",Schedule);
        AddButton("Close",Close);
        if(data is not { } result) return;
        AddButton("View",()=>OpenSelected(true));
        var variants=section.SelectedIndex==0;
        if(variants)
        {
            foreach(var (caption,property) in new (string,string)[] {("Code","Code"),("Size","Size"),("Color","Color"),
                ("Other attributes","Attributes"),("SKU","Sku"),("Barcode","Barcode"),("MRP","Mrp"),
                ("Selling rate","SalesRate"),("Purchase rate","PurchaseRate"),("Status","Status")}) Column(caption,property);
            grid.ItemsSource=result.Variants!.Where(x=>StatusMatches(x.Active) && MatchesFilter(x,sizeFilter) &&
                MatchesFilter(x,colorFilter)).Select(x=>new VariantGridRow(x,
                Describe(x,o=>o.Axis.StartsWith("SIZE-")),Describe(x,o=>o.Axis=="COLOR"),
                Describe(x,o=>o.Axis!="COLOR" && !o.Axis.StartsWith("SIZE-")))).ToArray();
            if(result.CanCreate) AddButton("Add variant",()=>EditVariant(null,false,[]));
            AddButton("Size / color matrix",OpenMatrix);
            if(result.CanEdit) AddButton("Edit variant",()=>OpenSelected(false));
        }
        else
        {
            foreach(var (caption,property) in new (string,string)[] {("Attribute","Axis"),("Code","Code"),
                ("Name","Name"),("Display color","Hex"),("Sort","SortOrder"),("Active","Active")}) Column(caption,property);
            var term=MasterSearch.Query(query.Text);
            grid.ItemsSource=result.Options!.Where(InSection).Where(x=>StatusMatches(x.Active) &&
                (term.Length==0 || x.Label.Contains(term,StringComparison.OrdinalIgnoreCase))).ToArray();
            if(result.CanManageOptions)
            {
                AddButton("Add option",()=>EditOption(null,false));
                AddButton("Edit option",()=>OpenSelected(false));
            }
        }
        if(result.CanStatus) AddButton("Activate / deactivate",()=>ToggleStatus());
        void Column(string title,string property)=>grid.Columns.Add(new DataGridTextColumn {Header=title,
            Binding=new System.Windows.Data.Binding(property),Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=75});
    }

    private bool InSection(VariantValue option)=>section.SelectedIndex switch
    {
        1=>option.Axis.StartsWith("SIZE-",StringComparison.Ordinal),
        2=>option.Axis=="COLOR",
        _=>option.Axis!="COLOR" && !option.Axis.StartsWith("SIZE-",StringComparison.Ordinal)
    };
    private bool StatusMatches(bool active)=>statusFilter.SelectedIndex==0 || active==(statusFilter.SelectedIndex==1);
    private static bool MatchesFilter(VariantEntry row,ComboBox filter)=>
        filter.SelectedItem is not VariantValue {Id:var id} || id==Guid.Empty || row.Options.Contains(id);

    private string Describe(VariantEntry entry,Func<VariantValue,bool> include)=>
        string.Join(" / ",data!.Options!.Where(o=>entry.Options.Contains(o.Id) && include(o)).Select(o=>o.Name));

    private void AddButton(string caption,Action action)
    {
        var button=new Button {Content=caption,Padding=new Thickness(9,5,9,5),Margin=new Thickness(0,3,7,8)};
        button.Click+=(_,_)=>{if(!busy) action();};
        actions.Children.Add(button);
    }

    private void OpenSelected(bool readOnly)
    {
        if(grid.SelectedItem is VariantGridRow selected && (readOnly || data?.CanEdit==true))
            EditVariant(selected.Entry,readOnly,[]);
        else if(grid.SelectedItem is VariantValue option && (readOnly || data?.CanManageOptions==true))
            EditOption(option,readOnly);
    }

    private void EditOption(VariantValue? option,bool readOnly)
    {
        var dialog=Editor("Variant attribute option",readOnly);
        var systems=new[]{"SIZE-UK","SIZE-US","SIZE-EU","SIZE-INDIA-CUSTOM","SIZE-APPAREL","SIZE-NUMERIC","SIZE-OTHER"};
        var size=section.SelectedIndex==1?dialog.Choice("Size system",systems,option?.Axis??systems[0],
            locked:option is not null):null;
        var axis=size is null?dialog.Text("Attribute code",option?.Axis??(section.SelectedIndex==2?"COLOR":""),50,
            locked:option is not null || section.SelectedIndex==2):null;
        var code=dialog.Text("Code *",option?.Code,50);
        var name=dialog.Text("Name *",option?.Name,100);
        var order=dialog.Text("Sort order",(option?.SortOrder??0).ToString(CultureInfo.CurrentCulture),4);
        var hex=section.SelectedIndex==2?dialog.Text("Display color (#RRGGBB)",option?.Hex,7):null;
        dialog.Save(async ()=>
        {
            if(!int.TryParse(order.Text,out var sort)) return "Enter a whole-number sort order.";
            var input=new VariantValue(option?.Id??Guid.Empty,option?.Version??0,
                size?.SelectedItem as string??axis?.Text??"",code.Text,name.Text,hex?.Text,sort,option?.Active??true);
            return await SaveAsync(new(product.Id,VariantAction.SaveOption,Option:input,ExpectedCompany:company));
        });
        dialog.ShowDialog();Schedule();
    }

    private void EditVariant(VariantEntry? entry,bool readOnly,IReadOnlyList<Guid> preset,Window? owner=null)
    {
        var dialog=Editor("Product variant",readOnly,owner);
        var codes=new WrapPanel();
        dialog.AddCard("Identification",codes);
        var code=dialog.Text("Variant code *",entry?.Code,50,panel:codes);
        var sku=dialog.Text("SKU",entry?.Sku,80,panel:codes);
        var barcode=dialog.Text("Barcode",entry?.Barcode,80,panel:codes);
        var price=new WrapPanel();
        dialog.AddCard("Price overrides · blank inherits parent",price);
        var mrp=dialog.Text("MRP",Format(entry?.Mrp),24,data?.CanPrice!=true,panel:price);
        var sell=dialog.Text("Selling rate",Format(entry?.SalesRate),24,data?.CanPrice!=true,panel:price);
        var buy=dialog.Text("Purchase rate",Format(entry?.PurchaseRate),24,data?.CanPrice!=true,panel:price);
        var attributes=new WrapPanel();
        dialog.AddCard("Attributes",attributes);
        var original=entry?.Options??preset;
        var selectors=new List<ComboBox>();
        foreach(var group in data!.Options!.Where(x=>x.Active || original.Contains(x.Id)).GroupBy(x=>x.Axis))
        {
            var blank=new VariantValue(Guid.Empty,0,group.Key,"","None");
            var selector=dialog.Choice(group.Key,new[]{blank}.Concat(group),group.FirstOrDefault(x=>original.Contains(x.Id))??blank,
                nameof(VariantValue.Name),attributes,searchable:true);
            selector.Width=175;
            selectors.Add(selector);
        }
        if(!readOnly) dialog.ActionButton("Suggest SKU").Click+=(_,_) =>
        {
            if(!string.IsNullOrWhiteSpace(sku.Text)){dialog.ShowMessage("Clear the SKU before accepting a suggestion.",NoticeKind.Warning);return;}
            var parts=Selected().OrderBy(x=>x.Axis,StringComparer.Ordinal).Select(x=>x.Code);
            sku.Text=string.Join("-",new[]{product.Code}.Concat(parts));
        };
        VariantValue[] Selected()=>selectors.Select(x=>x.SelectedItem).OfType<VariantValue>().Where(x=>x.Id!=Guid.Empty).ToArray();
        dialog.Save(async ()=>
        {
            if(!Parse(mrp.Text,out var m) || !Parse(sell.Text,out var s) || !Parse(buy.Text,out var p))
                return "Enter valid decimal prices, or leave an override blank.";
            var input=new VariantEntry(entry?.Id??Guid.Empty,entry?.Version??0,code.Text,sku.Text,barcode.Text,
                m,s,p,entry?.Active??true,Selected().Select(x=>x.Id).ToArray());
            return await SaveAsync(new(product.Id,VariantAction.SaveVariant,Variant:input,ExpectedCompany:company));
        });
        dialog.ShowDialog();
        if(owner is null) Schedule();
    }

    private void OpenMatrix()
    {
        if(data?.Options is not { } options) return;
        var sizes=options.Where(x=>x.Active && x.Axis.StartsWith("SIZE-")).ToArray();
        var colors=options.Where(x=>x.Active && x.Axis=="COLOR").ToArray();
        if(sizes.Length==0 || colors.Length==0){notice.Text="Add active sizes and colors first.";notice.Kind=NoticeKind.Warning;return;}
        var window=Editor("Footwear size / color matrix",false);
        window.Width=Math.Min(1000,SystemParameters.WorkArea.Width);
        var matrix=new DataGrid {SelectionMode=DataGridSelectionMode.Single,SelectionUnit=DataGridSelectionUnit.Cell,
            IsReadOnly=true,CanUserAddRows=false,AutoGenerateColumns=true,MaxHeight=460};
        var table=new DataTable();
        table.Columns.Add("Size");
        for(var i=0;i<colors.Length;i++) table.Columns.Add("Color"+i);
        foreach(var size in sizes)
        {
            var row=table.NewRow();row[0]=size.Axis+" · "+size.Name;
            for(var i=0;i<colors.Length;i++) row[i+1]="";
            table.Rows.Add(row);
        }
        matrix.AutoGeneratingColumn+=(_,e)=>
        {
            if(e.PropertyName.StartsWith("Color") && int.TryParse(e.PropertyName.AsSpan(5),out var column))
                e.Column.Header=colors[column].Name;
        };
        matrix.ItemsSource=table.DefaultView;
        window.AddCard("Select a size and color cell",matrix);
        async Task Refresh()
        {
            var response=await service.ExecuteAsync(new(product.Id,ExpectedCompany:company),lifetime.Token);
            if(response.Outcome!=CompanyOutcome.Success){window.ShowMessage(response.Message??"Matrix unavailable.");return;}
            data=response;
            for(var r=0;r<sizes.Length;r++)
                for(var c=0;c<colors.Length;c++)
                    table.Rows[r][c+1]=Cell(r,c) is {} item?item.Code+(item.Active?"":" (inactive)"):"";
        }
        VariantEntry? Cell(int r,int c)=>data!.Variants!.FirstOrDefault(x=>x.Options.Count==2 &&
            x.Options.Contains(sizes[r].Id) && x.Options.Contains(colors[c].Id));
        bool Coordinates(out int r,out int c)
        {
            r=matrix.CurrentItem is DataRowView row?table.Rows.IndexOf(row.Row):-1;
            c=(matrix.CurrentColumn?.DisplayIndex??0)-1;
            return r>=0 && c>=0 && c<colors.Length;
        }
        matrix.CanUserReorderColumns=false;
        matrix.MouseDoubleClick+=(_,e)=>
        {
            if(e.OriginalSource is DependencyObject origin &&
                ItemsControl.ContainerFromElement(matrix,origin) is DataGridRow &&
                Coordinates(out var r,out var c) && Cell(r,c) is {} item) EditVariant(item,true,[],window);
        };
        if(data.CanCreate || data.CanEdit) window.ActionButton("Add or edit selected cell").Click+=async (_,_) =>
        {
            if(!Coordinates(out var r,out var c)){window.ShowMessage("Select a size and color cell first.");return;}
            var item=Cell(r,c);
            if(item is null && data?.CanCreate!=true || item is not null && data?.CanEdit!=true)
            {window.ShowMessage("You do not have permission to change this cell.");return;}
            EditVariant(item,false,[sizes[r].Id,colors[c].Id],window);
            await Refresh();
        };
        if(data.CanStatus) window.ActionButton("Activate / deactivate selected variant").Click+=async (_,_) =>
        {
            if(!Coordinates(out var r,out var c) || Cell(r,c) is not { } item)
            {window.ShowMessage("Select a populated matrix cell.");return;}
            if(MessageBox.Show(window,"Change this variant’s status?","Footwear matrix",MessageBoxButton.YesNo,
                MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes) return;
            var error=await SaveAsync(new(product.Id,VariantAction.VariantStatus,Variant:item with {Active=!item.Active},
                ExpectedCompany:company));
            if(error is not null) window.ShowMessage(error,NoticeKind.Error);
            else await Refresh();
        };
        window.Loaded+=async (_,_)=>await Refresh();
        window.ShowDialog();
        Schedule();
    }

    private MeasureEditorDialog Editor(string title,bool readOnly,Window? owner=null)=>new(title,readOnly){Owner=owner??this};
    private async void ToggleStatus()
    {
        VariantRequest? request=grid.SelectedItem switch
        {
            VariantGridRow x=>new(product.Id,VariantAction.VariantStatus,Variant:x.Entry with {Active=!x.Entry.Active},ExpectedCompany:company),
            VariantValue x=>new(product.Id,VariantAction.OptionStatus,Option:x with {Active=!x.Active},ExpectedCompany:company),
            _=>null
        };
        if(request is null || MessageBox.Show(this,"Change the selected record’s status?","Product variants",
            MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes) return;
        var error=await SaveAsync(request);
        if(error is not null){notice.Text=error;notice.Kind=NoticeKind.Error;}
        Schedule();
    }

    private async Task<string?> SaveAsync(VariantRequest request)
    {
        if(busy) return "Wait for the current update.";
        busy=true;
        try
        {
            var result=await service.ExecuteAsync(request,lifetime.Token);
            return result.Outcome==CompanyOutcome.Success?null:result.Message??"The operation was denied or could not be completed.";
        }
        finally {busy=false;}
    }
    private void Error(VariantResult result)
    {
        notice.Kind=NoticeKind.Error;
        notice.Text=result.Message??"Unable to open product variants for the selected company.";
    }
    private static string Format(decimal? value)=>value?.ToString(CultureInfo.CurrentCulture)??"";
    private static bool Parse(string input,out decimal? value)
    {
        value=null;
        if(string.IsNullOrWhiteSpace(input)) return true;
        if(!decimal.TryParse(input,NumberStyles.Number,CultureInfo.CurrentCulture,out var amount)) return false;
        value=amount;return true;
    }

    private sealed record VariantGridRow(VariantEntry Entry,string Size,string Color,string Attributes)
    {
        public string Code=>Entry.Code;
        public string? Sku=>Entry.Sku;
        public string? Barcode=>Entry.Barcode;
        public decimal? Mrp=>Entry.Mrp;
        public decimal? SalesRate=>Entry.SalesRate;
        public decimal? PurchaseRate=>Entry.PurchaseRate;
        public string Status=>Entry.Active?"Active":"Inactive";
    }
}
