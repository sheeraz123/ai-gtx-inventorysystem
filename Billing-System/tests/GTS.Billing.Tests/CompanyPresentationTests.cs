using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using GTS.Billing.Application;
using GTS.Billing.Infrastructure;
using GTS.Billing.Presentation.Wpf;
using Xunit;

namespace GTS.Billing.Tests;

public sealed class CompanyPresentationTests
{
    [Fact]
    public async Task RealWpfBindingsCreateReloadAndSelectCompanyWhileOtherModulesStayPlanned()
    {
        using var diagnostics=new DiagnosticsFixture();
        var directory=Path.Combine(PersistenceTests.FindBillingRoot(),".local","stage4a-wpf",Guid.NewGuid().ToString("N"));
        var path=Path.Combine(directory,"synthetic.db");
        var database=new DevelopmentDatabase(new ConfiguredDatabaseLocation(path));
        await database.InitializeAsync();
        var auth=new AuthenticationService(new AuthenticationStore(database),new Argon2PasswordHasher(),TimeProvider.System);
        await auth.SetupAsync("Synthetic.Admin",AuthenticationTests.Original,AuthenticationTests.Original);
        var completed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            var dispatcher=Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.InvokeAsync(async ()=>
            {
                MainWindow? window=null;
                CompanyViewModel? companies=null;
                AuthenticationViewModel? authentication=null;
                try
                {
                    var lifetime=new ApplicationLifetime();
                    authentication=new AuthenticationViewModel(auth,lifetime);
                    companies=new CompanyViewModel(new CompanyService(auth,new CompanyStore(database)),lifetime);
                    var shell=new ShellViewModel();
                    await authentication.StartAsync(true);
                    window=new MainWindow(shell,authentication,new ConfiguredDatabaseLocation(path),companies)
                        {ShowActivated=false,ShowInTaskbar=false,Left=-10000,Top=-10000,WindowStartupLocation=WindowStartupLocation.Manual};
                    window.Show();
                    Assert.False(window.IsBillingShellVisible);
                    await authentication.SubmitAsync("Synthetic.Admin",AuthenticationTests.Original,"","");
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    await Wait(()=>!companies.Busy);
                    Assert.True(window.IsBillingShellVisible);
                    shell.NavigateCommand.Execute(shell.Modules.Single(x=>x.Code=="company"));
                    await Wait(()=>!companies.Busy);
                    var view=Assert.IsType<CompanyView>(window.FindName("CompanyPanel"));
                    Assert.Equal(Visibility.Visible,view.Visibility);
                    companies.AddCommand.Execute(null);
                    window.UpdateLayout();
                    SetText(view,"Company code * (immutable after creation)","WPF_SYNTHETIC");
                    SetText(view,"Legal company name *","Synthetic WPF Company");
                    var established=Descendants<DatePicker>(view).Single(x=>AutomationProperties.GetName(x)=="Established on");
                    Assert.Equal(Visibility.Visible,established.Visibility);
                    established.SetCurrentValue(DatePicker.SelectedDateProperty,new DateTime(2010,4,1));
                    established.GetBindingExpression(DatePicker.SelectedDateProperty)!.UpdateSource();
                    var tabs=Descendants<TabControl>(view).Single();
                    tabs.SelectedIndex=1;window.UpdateLayout();
                    SetText(view,"Address line 1 *","Synthetic WPF address");
                    SetText(view,"City *","Mumbai");
                    SetText(view,"PIN *","400001");
                    var state=Descendants<ComboBox>(view).Single(x=>AutomationProperties.GetName(x)=="State / UT *");
                    Assert.True(state.IsEditable);
                    var stateField=companies.Fields.Single(x=>x.Name=="StateCode");
                    Assert.Same(stateField.FilteredChoices,state.ItemsSource);
                    stateField.FilterChoices("maha");Assert.Single(state.Items);Assert.Equal("27",Assert.IsType<CompanyChoice>(state.Items[0]).Key);
                    state.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedValueProperty,"27");
                    view.UpdateStateSearch(state);
                    Assert.Equal("27",state.SelectedValue);Assert.Equal("27",stateField.Value);
                    Assert.Equal(GTS.Billing.Domain.CompanyRules.States.Count,state.Items.Count);
                    stateField.FilterChoices("09");Assert.Single(state.Items);Assert.Equal("09",Assert.IsType<CompanyChoice>(state.Items[0]).Key);
                    stateField.FilterChoices("no such state");Assert.Empty(state.Items);
                    stateField.FilterChoices("");
                    state.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedValueProperty,"27");
                    state.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedValueProperty)!.UpdateSource();
                    Assert.True(companies.Dirty);
                    Assert.False(companies.AddCommand.CanExecute(null));
                    await companies.SaveCommand.ExecuteAsync(null);
                    Assert.False(companies.EditorVisible);
                    var row=Assert.Single(companies.Companies);
                    Assert.Equal("Synthetic WPF Company",row.LegalName);
                    Assert.Equal("2010-04-01",row.Details.Draft.EstablishedOn);
                    companies.Selected=row;
                    await companies.SelectCommand.ExecuteAsync(null);
                    Assert.Equal("Current Company: Synthetic WPF Company",
                        Assert.IsType<TextBlock>(window.FindName("CurrentCompanyIndicator")).Text);
                    await companies.EditCommand.ExecuteAsync(null);
                    Assert.True(companies.Fields.Single(x=>x.Name=="Code").ReadOnly);
                    companies.CancelEditor();
                    Assert.All(shell.Modules.Where(x=>x.Code!="company"),x=>Assert.Equal("Planned",x.Status));
                    await authentication.LogoutAsync();
                    Assert.False(window.IsBillingShellVisible);
                    Assert.Empty(companies.Companies);
                    completed.TrySetResult();
                }
                catch(Exception e) {completed.TrySetException(e);}
                finally
                {
                    companies?.Clear();
                    if(authentication is not null) await authentication.Lifetime.ShutdownAsync();
                    window?.Close();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
                }
            });
            Dispatcher.Run();
        });
        thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();
        try {await completed.Task.WaitAsync(TimeSpan.FromSeconds(40));}
        finally {thread.Join(TimeSpan.FromSeconds(5));Directory.Delete(directory,true);}
    }
    private static async Task Wait(Func<bool> condition)
    {
        var until=DateTime.UtcNow.AddSeconds(10);
        while(!condition()) {if(DateTime.UtcNow>until) throw new TimeoutException("Synthetic UI operation did not complete.");await Task.Delay(10);}
    }
    private static void SetText(DependencyObject root,string label,string value)
    {
        var box=Descendants<TextBox>(root).Single(x=>AutomationProperties.GetName(x)==label);
        box.SetCurrentValue(TextBox.TextProperty,value);
        box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is T match) yield return match;
            foreach(var nested in Descendants<T>(child)) yield return nested;
        }
    }
}

