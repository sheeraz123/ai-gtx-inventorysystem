using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using GTS.Billing.Application;
using GTS.Billing.Domain;
using Microsoft.Win32;

namespace GTS.Billing.Presentation.Wpf;

internal sealed class MeasureEditorDialog : Window
{
    private readonly StackPanel fields = new() { Margin = new Thickness(16) };
    private readonly NoticeBanner message = new();
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(16,6,16,10) };
    private readonly bool readOnly;
    private bool saving;
    private bool dirty;
    private bool discard;
    private readonly Dictionary<Control,(TextBlock Label,Border Frame,NoticeBanner Warning)> fieldErrors=new();

    internal MeasureEditorDialog(string title, bool readOnly)
    {
        this.readOnly = readOnly;
        Title = title + (readOnly ? " — View" : " — Edit");
        Width = 720;
        Height = 650;
        MinWidth = 530;
        MinHeight = 400;
        Owner = System.Windows.Application.Current.MainWindow;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MaxHeight = SystemParameters.WorkArea.Height;
        MaxWidth = SystemParameters.WorkArea.Width;
        SetResourceReference(BackgroundProperty, "Palette.Surface");
        SetResourceReference(ForegroundProperty, "Palette.Text");
        var layout = new DockPanel();
        DockPanel.SetDock(actions, Dock.Bottom);
        layout.Children.Add(actions);
        DockPanel.SetDock(message, Dock.Bottom);
        message.Margin = new Thickness(16,0,16,4);
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
        layout.Children.Add(message);
        layout.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;
        var close = Button(readOnly ? "Close" : "Cancel", actions);
        close.Click += (_, _) => Close();
        Closing += (_, e) =>
        {
            e.Cancel = saving || dirty && !discard && MessageBox.Show(this, "Discard unsaved changes?", Title,
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes;
        };
    }

    internal void ConfigureMaster(bool brands, MeasureMaster? item,
        Func<MeasureMaster,byte[]?,bool,Task<string?>> save)
    {
        if (item is null && !readOnly) Title = brands ? "Add brand" : "Add unit";
        var code = Text("Code *", item?.Code, 30, item is not null, "UnitBrand.Code");
        var name = Text("Name *", item?.Name, 120, labelKey:"UnitBrand.Name");
        var description = Text("Description", item?.Description, 500);
        var sort = Text("Sort order", (item?.SortOrder ?? 0).ToString(CultureInfo.CurrentCulture), 9);
        Text("Status", item?.Active == false ? "Inactive" : "Active", 30, true);
        if (item is not null)
        {
            Text("Created (UTC)", item.CreatedUtc.ToString("u", CultureInfo.InvariantCulture), 30, true);
            Text("Updated (UTC)", item.UpdatedUtc.ToString("u", CultureInfo.InvariantCulture), 30, true);
        }
        TextBox? symbol = null, precision = null, uqc = null, manufacturer = null;
        ComboBox? type = null;
        byte[]? logo = null;
        var remove = false;
        if (brands)
        {
            manufacturer = Text("Manufacturer", item?.Manufacturer, 160);
            var preview = new Image { MaxWidth = 200, MaxHeight = 100, HorizontalAlignment=HorizontalAlignment.Left };
            fields.Children.Add(preview);
            SetImage(preview, item?.Logo);
            if (!readOnly)
            {
                var logoButtons = new WrapPanel();
                fields.Children.Add(logoButtons);
                Button("Choose logo", logoButtons).Click += (_, _) =>
                {
                    var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg" };
                    if (dialog.ShowDialog(this) != true) return;
                    try
                    {
                        using var input = File.OpenRead(dialog.FileName);
                        if (input.Length is < 45 or > CompanyLogo.MaximumBytes)
                        {
                            message.Text = "Choose a PNG or JPEG up to 1 MiB.";
                            return;
                        }
                        logo = new byte[(int)input.Length];
                        input.ReadExactly(logo);
                        remove = false;
                        dirty = true;
                        message.Text = "Logo selected. It will be validated when saved.";
                    }
                    catch (IOException) { message.Text = "The selected logo could not be read."; }
                    catch (UnauthorizedAccessException) { message.Text = "The selected logo could not be read."; }
                };
                Button("Remove logo", logoButtons).Click += (_, _) =>
                {
                    logo = null;
                    remove = true;
                    dirty = true;
                    preview.Source = null;
                };
            }
        }
        else
        {
            symbol = Text("Unit symbol", item?.Symbol, 20);
            type = Choice("Unit type", UnitBrandService.UnitTypes, item?.UnitType ?? "Count");
            precision = Text("Decimal precision (0–6)", (item?.Precision ?? 0).ToString(CultureInfo.CurrentCulture), 1);
            uqc = Text("GST UQC", item?.Uqc, 20);
        }
        Save(async () =>
        {
            if (!int.TryParse(sort.Text, out var order) || precision is not null && !int.TryParse(precision.Text, out _))
                return "Enter a whole number for sort order and decimal precision.";
            return await save(new(item?.Id ?? Guid.Empty, item?.Version ?? 0,code.Text,name.Text,item?.Active ?? true,
                description.Text,order,symbol?.Text,type?.SelectedItem as string ?? "Count",
                precision is null ? 0 : int.Parse(precision.Text,CultureInfo.CurrentCulture),uqc?.Text,manufacturer?.Text), logo, remove);
        });
    }

    internal void ConfigureProduct(MeasureResult data, Func<ProductMeasures,Task<string?>> save)
    {
        var product = data.Product!;
        Text("Existing values and mapping status", data.Legacy!.Count == 0 ? "No unresolved values." :
            string.Join(Environment.NewLine,data.Legacy), 6000, true);
        var noBrand = new MeasureChoice(Guid.Empty,"","Keep legacy / no master link",true);
        var noUnit = new MeasureChoice(Guid.Empty,"","Keep legacy / no base link",true);
        var units = data.Units!;
        var brands = data.Brands!;
        var brand = Choice("Primary brand",[noBrand, ..brands.Where(x=>x.Active || x.Id==product.BrandId)],
            brands.FirstOrDefault(x=>x.Id==product.BrandId) ?? noBrand,nameof(MeasureChoice.Label),searchable:true);
        var basis = Choice("Base unit",[noUnit, ..units.Where(x=>x.Active)],
            units.FirstOrDefault(x=>x.Id==product.BaseUnitId) ?? noUnit,nameof(MeasureChoice.Label),searchable:true);
        var classification = Choice("Material classification",["", ..UnitBrandService.Classifications],product.Classification ?? "");
        fields.Children.Add(new TextBlock { Text="Factor = base units per one selected unit. Base factor is 1. Check Sales or Purchase to choose the default.", TextWrapping = TextWrapping.Wrap });
        var rows = new StackPanel();
        fields.Children.Add(rows);
        var editors = new List<(ComboBox Unit, TextBox Factor, CheckBox Sales, CheckBox Purchase, CheckBox Active)>();
        void AddRow(ConversionInput? input)
        {
            var row = new WrapPanel { Margin = new Thickness(0,4,0,4) };
            rows.Children.Add(row);
            var unit = Choice("Unit", units.Where(x=>x.Active || x.Id==input?.UnitId).ToArray(),
                units.FirstOrDefault(x=>x.Id==input?.UnitId),nameof(MeasureChoice.Label),row,searchable:true);
            unit.Width = 185;
            var factor = Text("Factor", (input?.Factor ?? 1).ToString(CultureInfo.CurrentCulture), 24, panel:row);
            factor.Width = 95;
            var sales = Check("Sales", input?.Sales ?? false,row);
            var purchase = Check("Purchase", input?.Purchase ?? false,row);
            var active = Check("Active", input?.Active ?? true,row);
            editors.Add((unit,factor,sales,purchase,active));
            if (!readOnly && input is null) Button("Discard row",row).Click += (_,_) =>
            {
                editors.RemoveAll(x => x.Unit == unit);
                rows.Children.Remove(row);
            };
            else if (!readOnly) Button("Deactivate row",row).Click += (_,_) =>
            {
                active.IsChecked = false;
                sales.IsChecked = false;
                purchase.IsChecked = false;
                dirty = true;
            };
        }
        foreach (var entry in product.Conversions) AddRow(entry);
        if (!readOnly) Button("Add conversion",fields).Click += (_,_) => { AddRow(null); dirty = true; };
        Save(async () =>
        {
            var items = new List<ConversionInput>();
            foreach (var entry in editors)
            {
                if (entry.Unit.SelectedItem is not MeasureChoice unit ||
                    !decimal.TryParse(entry.Factor.Text,NumberStyles.Number,CultureInfo.CurrentCulture,out var factor))
                    return "Select a unit and enter a decimal factor for each row.";
                items.Add(new(unit.Id,factor,entry.Sales.IsChecked==true,entry.Purchase.IsChecked==true,entry.Active.IsChecked==true));
            }
            return await save(product with { BrandId=Id(brand), BaseUnitId=Id(basis),
                Classification=CatalogRules.Optional(classification.SelectedItem as string),Conversions=items });
        });
    }

    private static Guid? Id(ComboBox combo) => combo.SelectedItem is MeasureChoice choice && choice.Id != Guid.Empty ? choice.Id : null;

    internal TextBox Text(string label, string? value, int length, bool locked=false, string? labelKey=null, Panel? panel=null)
    {
        var box = new TextBox { Text=value ?? "", MaxLength=length, IsReadOnly=readOnly || locked,
            TextWrapping=TextWrapping.Wrap, AcceptsReturn=length>=500, MinWidth=75,
            MinHeight=32, Padding=new Thickness(7,4,7,4), VerticalContentAlignment=VerticalAlignment.Center };
        AddField(labelKey is null ? label : SettingsDisplay.Text(labelKey,label.TrimEnd(' ','*')) + (label.EndsWith('*') ? " *" : ""), box, panel);
        if(!box.IsReadOnly) box.TextChanged += (_,_) => dirty = true;
        return box;
    }

    internal ComboBox Choice<T>(string label, IEnumerable<T> items, T? selected, string path="", Panel? panel=null,
        bool locked=false,bool searchable=false)
    {
        ComboBox combo=searchable ? new SearchableComboBox() : new ComboBox();
        combo.DisplayMemberPath=path;
        combo.IsEnabled=!readOnly && !locked;
        combo.MinHeight=32;
        combo.Padding=new Thickness(7,4,7,4);
        SearchableComboBox.SetSource(combo,items);
        combo.SelectedItem=selected;
        AddField(label,combo,panel);
        combo.SelectionChanged += (_,_) => dirty = true;
        return combo;
    }

    private CheckBox Check(string label,bool selected,Panel panel)
    {
        var check = new CheckBox { Content=label, IsChecked=selected, IsEnabled=!readOnly, VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(6) };
        check.Click += (_,_) => dirty = true;
        panel.Children.Add(check);
        return check;
    }

    internal void AddField(string label, Control control, Panel? panel)
    {
        var group = new StackPanel { Margin=new Thickness(0,0,8,10) };
        if(!string.IsNullOrEmpty(label))
        {
            group.Children.Add(new Label { Content=label, Target=control, Padding=new Thickness(0,0,0,4) });
            AutomationProperties.SetName(control,label);
        }
        var frame=new Border {Child=control};
        group.Children.Add(frame);
        var error = new TextBlock {Foreground=Brushes.Firebrick,TextWrapping=TextWrapping.Wrap,
            Visibility=Visibility.Collapsed,Margin=new Thickness(0,4,0,0)};
        AutomationProperties.SetLiveSetting(error,AutomationLiveSetting.Polite);
        group.Children.Add(error);
        var warning=new NoticeBanner {Kind=NoticeKind.Warning,MaxWidth=210,Margin=new Thickness(0,4,0,0)};
        group.Children.Add(warning);
        fieldErrors[control]=(error,frame,warning);
        (panel ?? fields).Children.Add(group);
    }

    private static Button Button(string label, Panel panel)
    {
        var button = new Button { Content=label, Margin=new Thickness(4), Padding=new Thickness(9,4,9,4) };
        panel.Children.Add(button);
        return button;
    }

    private static void SetImage(Image preview, byte[]? content)
    {
        if (content is null) return;
        using var source = new MemoryStream(content);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = source;
        image.EndInit();
        preview.Source = image;
    }

    internal DatePicker Date(string label,DateOnly? value,bool locked=false,bool allowInView=false,Panel? panel=null)
    {
        var picker=new DatePicker {SelectedDate=value?.ToDateTime(TimeOnly.MinValue),IsEnabled=(!readOnly || allowInView) && !locked,MinHeight=32};
        AddField(label,picker,panel);
        if(!allowInView) picker.SelectedDateChanged+=(_,_)=>dirty=true;
        return picker;
    }

    internal Button ActionButton(string label) => Button(label,fields);
    internal Button FooterButton(string label) => Button(label,actions);
    internal bool IsSaving=>saving;
    internal void SetFieldError(Control field,string? error)
    {
        if(!fieldErrors.TryGetValue(field,out var entry)) return;
        entry.Label.Text=error??"";
        entry.Label.Visibility=error is null?Visibility.Collapsed:Visibility.Visible;
        entry.Frame.BorderBrush=error is not null?Brushes.Firebrick:
            string.IsNullOrEmpty(entry.Warning.Text)?null:Brushes.DarkGoldenrod;
        entry.Frame.BorderThickness=new Thickness(entry.Frame.BorderBrush is null?0:2);
        AutomationProperties.SetHelpText(field,error??"");
    }

    internal void SetFieldWarning(Control field,string? warning)
    {
        if(!fieldErrors.TryGetValue(field,out var entry)) return;
        entry.Warning.Text=warning??"";
        SetFieldError(field,entry.Label.Visibility==Visibility.Visible?entry.Label.Text:null);
    }

    internal void FocusInvalidField(Control field)
    {
        Dispatcher.BeginInvoke(new Action(()=>
        {
            if(!IsVisible) return;
            DependencyObject? ancestor=field;
            while(ancestor is not null)
            {
                if(ancestor is Expander expander) expander.IsExpanded=true;
                ancestor=LogicalTreeHelper.GetParent(ancestor)??VisualTreeHelper.GetParent(ancestor);
            }
            UpdateLayout();
            field.BringIntoView();
            field.Focus();
        }),DispatcherPriority.Input);
    }
    internal void DockSummary(FrameworkElement summary)
    {
        var layout=(DockPanel)Content;
        DockPanel.SetDock(summary,Dock.Bottom);
        layout.Children.Insert(layout.Children.Count-1,summary);
    }
    internal void AddSection(Panel panel)=>fields.Children.Add(panel);
    internal void AddCard(string title,FrameworkElement content)
    {
        var body=new StackPanel();
        body.Children.Add(new TextBlock {Text=title,FontSize=16,FontWeight=FontWeights.SemiBold,
            Margin=new Thickness(0,0,0,10)});
        body.Children.Add(content);
        var card=new Border {Child=body,CornerRadius=new CornerRadius(8),
            Padding=new Thickness(13),Margin=new Thickness(0,0,0,12),BorderThickness=new Thickness(1)};
        card.SetResourceReference(Border.BackgroundProperty,"Palette.Surface");
        card.SetResourceReference(Border.BorderBrushProperty,"Palette.GridLine");
        fields.Children.Add(card);
    }

    internal FrameworkElement AddConfiguredCard(SalesFormRule rule,FrameworkElement content)
    {
        AddCard(rule.Label,content);
        var card=(FrameworkElement)fields.Children[^1];
        card.Tag=rule.Order;
        card.Visibility=rule.Visible?Visibility.Visible:Visibility.Collapsed;
        return card;
    }

    internal void ConfigureField(Control control,SalesFormRule rule,bool customized=true)
    {
        if(control.Parent is not Border {Parent:StackPanel group}) return;
        group.Tag=rule.Order;
        if(!customized) return;
        group.Visibility=rule.Visible?Visibility.Visible:Visibility.Collapsed;
        if(group.Children.OfType<Label>().FirstOrDefault() is { } label)
            label.Content=rule.Label+(rule.Required && !rule.Label.EndsWith('*')?" *":"");
        AutomationProperties.SetName(control,rule.Label);
        var hint=string.Join(" · ",new[]{rule.Placeholder,rule.Help}.Where(x=>!string.IsNullOrWhiteSpace(x)));
        if(hint.Length>0) control.ToolTip=hint;
        if(rule.Placeholder.Length>0 && control is TextBox text && !rule.ReadOnly)
        {
            var placeholder=new TextBlock {Text=rule.Placeholder,TextWrapping=TextWrapping.Wrap,
                FontSize=11,Margin=new Thickness(2,3,2,0)};
            placeholder.SetResourceReference(TextBlock.ForegroundProperty,"Palette.Text");
            group.Children.Insert(group.Children.IndexOf((UIElement)control.Parent)+1,placeholder);
            void RefreshHint()=>placeholder.Visibility=string.IsNullOrEmpty(text.Text)?Visibility.Visible:Visibility.Collapsed;
            text.TextChanged+=(_,_)=>RefreshHint();
            RefreshHint();
        }
        if(rule.ReadOnly && control is TextBox box) box.IsReadOnly=true;
    }

    internal void ShowConfiguredField(Control control,bool visible)
    {
        if(control.Parent is Border {Parent:StackPanel group})
            group.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
    }

    internal void OrderConfiguredSections()=>OrderConfigured(fields);
    internal void ApplyInvoiceEntryLayout(string layout,FrameworkElement header,FrameworkElement products,
        FrameworkElement? customer,FrameworkElement addresses)
    {
        if(customer is not null)
        {
            fields.Children.Remove(customer);
            fields.Children.Insert(fields.Children.IndexOf(header)+1,customer);
            fields.Children.Remove(addresses);
            fields.Children.Insert(fields.Children.IndexOf(customer)+1,addresses);
        }
        if(layout=="Legacy Keyboard-Friendly")
        {
            fields.Children.Remove(products);
            fields.Children.Insert(fields.Children.IndexOf(customer is null?header:addresses)+1,products);
        }
        else if(layout=="Split Layout" && Width>=1100)
        {
            var stack=new StackPanel {Margin=new Thickness(0,0,12,0)};
            var grid=new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(2,GridUnitType.Star)});
            grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(3,GridUnitType.Star)});
            var sections=fields.Children.Cast<UIElement>().ToArray();
            fields.Children.Clear();
            foreach(var section in sections.Where(section=>section!=products &&
                (customer is null || section!=header && section!=customer && section!=addresses))) stack.Children.Add(section);
            grid.Children.Add(stack);
            Grid.SetColumn(products,1);
            grid.Children.Add(products);
            if(customer is not null)
            {
                fields.Children.Add(header);
                fields.Children.Add(customer);
                fields.Children.Add(addresses);
            }
            fields.Children.Add(grid);
        }
        else if(customer is not null && fields.Children.IndexOf(products)<fields.Children.IndexOf(addresses))
        {
            fields.Children.Remove(products);
            fields.Children.Insert(fields.Children.IndexOf(addresses)+1,products);
        }
    }
    internal static void OrderConfigured(Panel panel)
    {
        var items=panel.Children.Cast<UIElement>().ToArray();
        var ordered=items.Where(x=>x is FrameworkElement {Tag:int}).OrderBy(x=>(int)((FrameworkElement)x).Tag).ToArray();
        var index=0;
        var sequence=items.Select(x=>x is FrameworkElement {Tag:int}?ordered[index++]:x).ToArray();
        if(items.SequenceEqual(sequence)) return;
        panel.Children.Clear();
        foreach(var item in sequence) panel.Children.Add(item);
    }
    internal void MarkChanged()=>dirty=true;
    internal void ShowMessage(string text,NoticeKind kind=NoticeKind.Automatic)
    {
        message.Kind=kind;
        message.Text=text;
    }

    internal void RemoveEditorFields(IEnumerable<Control> controls)
    {
        foreach(var control in controls) fieldErrors.Remove(control);
    }

    internal void Save(Func<Task<string?>> operation,string label="Save",bool primary=false,Panel? panel=null,string? icon=null)
    {
        if (readOnly) return;
        var save = Button(label, panel??actions);
        if(icon is not null) BillingIcon.Set(save,label,icon);
        if(primary && !SystemParameters.HighContrast)
        {
            save.SetResourceReference(Control.BackgroundProperty,"Palette.InvoiceAccent");
            save.Foreground=Brushes.White;
        }
        save.Click += async (_,_) =>
        {
            if (saving) return;
            saving = true;
            fields.IsEnabled = actions.IsEnabled = false;
            try { ShowMessage(await operation() ?? "",NoticeKind.Error); }
            catch (OperationCanceledException) { ShowMessage("The operation was canceled.",NoticeKind.Warning); }
            finally
            {
                saving = false;
                fields.IsEnabled = actions.IsEnabled = true;
            }
            if (panel is not null || message.Text.Length != 0) return;
            discard = true;
            Close();
        };
    }
}
