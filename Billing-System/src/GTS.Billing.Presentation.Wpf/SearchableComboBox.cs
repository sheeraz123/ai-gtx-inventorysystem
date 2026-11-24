using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace GTS.Billing.Presentation.Wpf;

[TemplatePart(Name=SearchPart,Type=typeof(TextBox))]
public sealed class SearchableComboBox : ComboBox
{
    private const string SearchPart="PART_Search";
    private TextBox? input;
    private ListCollectionView? view;
    private string query="";
    private bool resettingInput;
    private readonly DispatcherTimer debounce=new();

    public SearchableComboBox()
    {
        // Filtering a private view may move its current item. Only an explicit selection changes the field.
        IsSynchronizedWithCurrentItem=false;
        debounce.Tick+=(_,_) =>
        {
            debounce.Stop();
            if(IsDropDownOpen && input is not null) ApplySearch(input.Text);
        };
    }

    public static readonly DependencyProperty SearchItemsSourceProperty=DependencyProperty.Register(
        nameof(SearchItemsSource),typeof(IEnumerable),typeof(SearchableComboBox),
        new PropertyMetadata(null,(owner,_)=>((SearchableComboBox)owner).ReplaceSource()));

    public static readonly DependencyProperty RemoteSearchProperty=DependencyProperty.Register(
        nameof(RemoteSearch),typeof(bool),typeof(SearchableComboBox));
    public static readonly DependencyProperty SearchQueryProperty=DependencyProperty.Register(
        nameof(SearchQuery),typeof(string),typeof(SearchableComboBox),new PropertyMetadata(""));
    public bool RemoteSearch {get=>(bool)GetValue(RemoteSearchProperty);set=>SetValue(RemoteSearchProperty,value);}
    public string SearchQuery {get=>(string)GetValue(SearchQueryProperty);set=>SetValue(SearchQueryProperty,value);}

    private static readonly DependencyPropertyKey SearchMessagePropertyKey=DependencyProperty.RegisterReadOnly(
        nameof(SearchMessage),typeof(string),typeof(SearchableComboBox),new PropertyMetadata("Type at least 3 characters to search."));
    public static readonly DependencyProperty SearchMessageProperty=SearchMessagePropertyKey.DependencyProperty;

    public IEnumerable? SearchItemsSource
    {
        get=>(IEnumerable?)GetValue(SearchItemsSourceProperty);
        set=>SetValue(SearchItemsSourceProperty,value);
    }

    public string SearchMessage=>(string)GetValue(SearchMessageProperty);

    public static void SetSource(ComboBox combo,IEnumerable? items)
    {
        if(combo is SearchableComboBox searchable) searchable.SearchItemsSource=items;
        else combo.ItemsSource=items;
    }

    public override void OnApplyTemplate()
    {
        if(input is not null)
        {
            input.TextChanged-=SearchChanged;
            input.PreviewKeyDown-=SearchKeyDown;
        }
        base.OnApplyTemplate();
        input=GetTemplateChild(SearchPart) as TextBox;
        if(input is null) return;
        input.TextChanged+=SearchChanged;
        input.PreviewKeyDown+=SearchKeyDown;
    }

    protected override void OnDropDownOpened(EventArgs e)
    {
        base.OnDropDownOpened(e);
        Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(()=>
        {
            if(IsDropDownOpen && input is not null) Keyboard.Focus(input);
        }));
    }

    protected override void OnDropDownClosed(EventArgs e)
    {
        base.OnDropDownClosed(e);
        ResetSearch();
    }

    private void ReplaceSource()
    {
        var previous=view;
        var selected=SelectedItem;
        if(!RemoteSearch)
        {
            debounce.Stop();
            query="";
            input?.Clear();
        }
        // One view per dropdown. Searching Billing Address must never filter Shipping Address.
        var items=SearchItemsSource as IList ?? SearchItemsSource?.Cast<object>().ToArray();
        view=items is null?null:new ListCollectionView(items);
        SetCurrentValue(ItemsSourceProperty,view);
        if(RemoteSearch && selected is not null && view?.Contains(selected)==true)
            SetCurrentValue(SelectedItemProperty,selected);
        previous?.DetachFromSourceCollection();
        UpdateMessage();
    }

    private void SearchChanged(object sender,TextChangedEventArgs e)
    {
        if(input is null || resettingInput) return;
        e.Handled=true;
        debounce.Stop();
        if(MasterSearch.Query(input.Text).Length==0) ApplySearch("");
        else
        {
            debounce.Interval=TimeSpan.FromMilliseconds(SettingsDisplay.Current.Get(GTS.Billing.Application.SettingsRegistry.SearchDebounce));
            debounce.Start();
        }
    }

    private void ApplySearch(string raw)
    {
        var next=MasterSearch.Query(raw);
        if(next==query) return;
        query=next;
        if(RemoteSearch)
        {
            SetCurrentValue(SearchQueryProperty,next);
            UpdateMessage();
            return;
        }
        if(view is not null) view.Filter=query.Length==0?null:item=>Equals(item,SelectedItem) || Matches(item);
        if(GetTemplateChild("PART_Results") is ScrollViewer results) results.ScrollToTop();
        UpdateMessage();
    }

    private void ResetSearch()
    {
        if(RemoteSearch)
        {
            resettingInput=true;
            input?.Clear();
            resettingInput=false;
            debounce.Stop();
            query="";
            UpdateMessage();
            return;
        }
        input?.Clear();
        debounce.Stop();
        ApplySearch("");
    }

    private void UpdateMessage()
    {
        var text=query.Length==0?"Type at least 3 characters to search.":
            view is not null && view.Cast<object>().Any(Matches)?"":"No matching options.";
        SetValue(SearchMessagePropertyKey,text);
    }

    private bool Matches(object item)
    {
        var label=Label(item);
        return CultureInfo.CurrentCulture.CompareInfo.IndexOf(label,query,CompareOptions.IgnoreCase)>=0;
    }

    private string Label(object item)
    {
        if(string.IsNullOrEmpty(DisplayMemberPath)) return item.ToString()??"";
        return System.ComponentModel.TypeDescriptor.GetProperties(item).Find(DisplayMemberPath,false)
            ?.GetValue(item)?.ToString()??"";
    }

    private void SearchKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape)
        {
            e.Handled=true;
            IsDropDownOpen=false;
            Focus();
        }
        else if(e.Key==Key.Down)
        {
            e.Handled=true;
            if(debounce.IsEnabled) return;
            for(var i=0;i<Items.Count;i++)
            {
                if(query.Length!=0 && !Matches(Items[i])) continue;
                if(ItemContainerGenerator.ContainerFromIndex(i) is ComboBoxItem item) item.Focus();
                break;
            }
        }
        else if(e.Key==Key.Enter)
        {
            // Only an explicit keyboard command with one matching result commits a search selection.
            e.Handled=true;
            if(debounce.IsEnabled || query.Length==0 || view is null) return;
            var results=view.Cast<object>().Where(Matches).Take(2).ToArray();
            if(results.Length!=1) return;
            SetCurrentValue(SelectedItemProperty,results[0]);
            IsDropDownOpen=false;
            Focus();
        }
    }
}
