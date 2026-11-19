using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTS.Billing.Application;
using static GTS.Billing.Presentation.Wpf.CustomerEditorFields;

namespace GTS.Billing.Presentation.Wpf;

public sealed record SecurityPage(string Label,SecurityAction ListAction);
public sealed record SecurityStatusFilter(string Label,bool? Value);
public sealed record PermissionGroup(string Module,IReadOnlyList<SecurityCheck> Items);
public sealed partial class SecurityCheck(string label,Guid id,bool selected,bool enabled=true) : ObservableObject
{
    public string Label { get; }=label;
    public Guid Id { get; }=id;
    public bool Enabled { get; }=enabled;
    [ObservableProperty] private bool _selected=selected;
}
public sealed record RolePermissionCheck(PermissionDefinition Definition,SecurityCheck Check);
public sealed record SecurityAssignmentPage(Guid CompanyId,string Label,IReadOnlyList<SecurityCheck> Roles);

public sealed partial class SecurityAdministrationViewModel(SecurityAdministrationService service) : ObservableObject
{
    private readonly MasterSearch liveSearch=new();
    private CancellationTokenSource viewLifetime=new();
    private AccessSnapshot access=AccessSnapshot.Denied;
    private SecurityOptions options=new([],[]);
    private UserDetails? user;
    private RoleDetails? role;
    private IReadOnlyList<RolePermissionCheck> permissionChecks=[];
    private IReadOnlyList<SecurityAssignmentPage> assignments=[];
    private bool loading;
    private bool more;
    private int page;

    public Action? SessionExpired { get; set; }
    public Func<bool>? ConfirmDiscard { get; set; }
    public Func<string,bool>? ConfirmChange { get; set; }
    public bool IsUsers=>Section?.ListAction==SecurityAction.Users;
    public bool IsRoles=>Section?.ListAction==SecurityAction.Roles;
    public bool IsAudit=>Section?.ListAction==SecurityAction.Audit;
    public bool IsAdmin=>access.IsSuperAdmin;
    public bool CanBrowse=>!Busy && !EditorOpen && Section is not null;
    public bool CanCreate=>CanBrowse && IsAdmin && (IsUsers || IsRoles);
    public bool CanWrite=>EditorOpen && Editing && IsAdmin && !Busy;
    public bool CanEditViewed=>EditorOpen && !Editing && IsAdmin && !Busy &&
        (user is { Summary.IsSuperAdmin:false } || role is { Summary.IsSystemRole:false });
    public bool CanReset=>IsUsers && CanEditViewed && user is not null;
    public bool NeedsPassword=>EditorOpen && Editing && (Resetting || IsUsers && user is null);
    public bool ShowUserControls=>IsUsers && EditorOpen && !Resetting;
    public bool ShowRoleControls=>IsRoles && EditorOpen;
    public bool CanRequireChange=>CanWrite && user?.Profile.RequireChange!=true;
    public bool CanChooseCompanyFilter=>IsAdmin && IsUsers;
    public bool ShowStatusFilter=>IsUsers || IsRoles;
    public string PageCaption=>$"Page {page+1}";
    public IReadOnlyList<SecurityStatusFilter> StatusChoices { get; }=[new("All statuses",null),new("Active",true),new("Inactive",false)];

    [ObservableProperty] private IReadOnlyList<SecurityPage> _sections=[];
    [ObservableProperty] private SecurityPage? _section;
    [ObservableProperty] private IReadOnlyList<UserSummary> _users=[];
    [ObservableProperty] private IReadOnlyList<RoleSummary> _roles=[];
    [ObservableProperty] private IReadOnlyList<SecurityAuditRow> _audit=[];
    [ObservableProperty] private UserSummary? _selectedUser;
    [ObservableProperty] private RoleSummary? _selectedRole;
    [ObservableProperty] private SecurityStatusFilter? _statusFilter;
    [ObservableProperty] private IReadOnlyList<SecurityOption> _companyFilters=[];
    [ObservableProperty] private SecurityOption? _companyFilter;
    [ObservableProperty] private string _search="";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _editorOpen;
    [ObservableProperty] private bool _editing;
    [ObservableProperty] private bool _resetting;
    [ObservableProperty] private bool _dirty;
    [ObservableProperty] private string _message="";
    [ObservableProperty] private IReadOnlyList<CustomerField> _fields=[];
    [ObservableProperty] private IReadOnlyList<SecurityCheck> _companies=[];
    [ObservableProperty] private IReadOnlyList<SecurityAssignmentPage> _assignmentPages=[];
    [ObservableProperty] private SecurityAssignmentPage? _assignmentPage;
    [ObservableProperty] private IReadOnlyList<PermissionGroup> _permissionGroups=[];
    [ObservableProperty] private IReadOnlyList<RoleMember> _members=[];
    [ObservableProperty] private string _securityInformation="";
    [ObservableProperty] private string _auditInformation="";
    [ObservableProperty] private bool _active=true;
    [ObservableProperty] private bool _locked;
    [ObservableProperty] private bool _requireChange;
    [ObservableProperty] private DateTime? _auditFrom;
    [ObservableProperty] private DateTime? _auditTo;
    [ObservableProperty] private string _auditEvent="";
    [ObservableProperty] private string _auditActor="";
    [ObservableProperty] private string _auditTarget="";
    [ObservableProperty] private string _auditOutcome="";

    partial void OnBusyChanged(bool value)=>Notify();
    partial void OnSelectedUserChanged(UserSummary? value)=>Notify();
    partial void OnSelectedRoleChanged(RoleSummary? value)=>Notify();
    partial void OnEditorOpenChanged(bool value)=>Notify();
    partial void OnEditingChanged(bool value)=>Notify();
    partial void OnResettingChanged(bool value)=>Notify();
    partial void OnActiveChanged(bool value)=>MarkDirty();
    partial void OnLockedChanged(bool value)=>MarkDirty();
    partial void OnRequireChangeChanged(bool value)=>MarkDirty();
    partial void OnStatusFilterChanged(SecurityStatusFilter? value)=>QueueList(false);
    partial void OnCompanyFilterChanged(SecurityOption? value)=>QueueList(false);
    partial void OnSearchChanged(string? oldValue,string newValue)
    {
        var query=MasterSearch.Query(newValue);
        if (query!=MasterSearch.Query(oldValue)) QueueList(query.Length>0);
    }
    partial void OnSectionChanged(SecurityPage? oldValue,SecurityPage? newValue)
    {
        if (loading) return;
        if (Busy || EditorOpen && Dirty && ConfirmDiscard?.Invoke()!=true)
        {
            loading=true;
            Section=oldValue;
            loading=false;
            return;
        }
        ClearEditor();
        Users=[];Roles=[];Audit=[];
        Notify();
        QueueList(false);
    }

    public void SetAccess(AccessSnapshot value)
    {
        if (Equivalent(access,value)) return;
        Clear();
        access=value;
        loading=true;
        var pages=new List<SecurityPage>();
        if(value.Allows("User.View")) pages.Add(new("Users",SecurityAction.Users));
        if(value.Allows("Role.View")) pages.Add(new("Roles & Permissions",SecurityAction.Roles));
        if(value.Allows("SecurityAudit.View")) pages.Add(new("Security Audit",SecurityAction.Audit));
        Sections=pages;
        Section=Sections.FirstOrDefault();
        loading=false;
        Notify();
    }

    private static bool Equivalent(AccessSnapshot a,AccessSnapshot b) => a.IsSuperAdmin==b.IsSuperAdmin &&
        a.CurrentCompanyId==b.CurrentCompanyId && a.Companies.Count==b.Companies.Count &&
        a.Companies.All(old=>b.Companies.Any(next=>next.CompanyId==old.CompanyId && next.Permissions.SetEquals(old.Permissions)));

    public void Clear()
    {
        liveSearch.Cancel();
        var old=viewLifetime;
        viewLifetime=new();
        old.Cancel();
        old.Dispose();
        ClearEditor();
        Users=[];Roles=[];Audit=[];
        SelectedUser=null;SelectedRole=null;
        loading=true;
        Search="";StatusFilter=StatusChoices[0];CompanyFilters=[];CompanyFilter=null;
        loading=false;
        options=new([],[]);
        page=0;more=false;
        Message="";
        Notify();
    }

    internal bool CanLeave()
    {
        if (Busy) return false;
        if (Dirty && ConfirmDiscard?.Invoke()!=true) return false;
        ClearEditor();
        return true;
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(IsUsers));OnPropertyChanged(nameof(IsRoles));OnPropertyChanged(nameof(IsAudit));
        OnPropertyChanged(nameof(IsAdmin));OnPropertyChanged(nameof(CanBrowse));OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(CanWrite));OnPropertyChanged(nameof(CanEditViewed));OnPropertyChanged(nameof(CanReset));
        OnPropertyChanged(nameof(NeedsPassword));OnPropertyChanged(nameof(ShowUserControls));OnPropertyChanged(nameof(ShowRoleControls));
        OnPropertyChanged(nameof(CanRequireChange));OnPropertyChanged(nameof(PageCaption));
        OnPropertyChanged(nameof(CanChooseCompanyFilter));OnPropertyChanged(nameof(ShowStatusFilter));
        OpenRowCommand.NotifyCanExecuteChanged();NextCommand.NotifyCanExecuteChanged();PreviousCommand.NotifyCanExecuteChanged();
    }

    private void QueueList(bool debounce)
    {
        if (loading || EditorOpen || Section is null) return;
        _=liveSearch.ScheduleAsync(debounce,()=>Busy,ct=>Run(async token=>{page=0;await Load(token);},ct));
    }

    public Task ActivateAsync()=>RefreshAsync();

    [RelayCommand]
    private Task RefreshAsync()=>Run(async ct=>
    {
        if (Section is null || EditorOpen) return;
        if (IsAdmin)
        {
            var result=await Send(new(SecurityAction.Options),null,ct);
            if (!Accept(result)) return;
            options=result.Options!;
            loading=true;
            var previous=CompanyFilter?.Id;
            CompanyFilters=[new(Guid.Empty,"All companies",true), ..options.Companies];
            CompanyFilter=CompanyFilters.FirstOrDefault(x=>x.Id==previous) ?? CompanyFilters[0];
            loading=false;
        }
        page=0;
        await Load(ct);
    });

    private async Task Load(CancellationToken ct)
    {
        if (Section is not { } current) return;
        if (IsAudit && (AuditFrom is DateTime start && AuditTo is DateTime end && start.Date>end.Date || AuditTo?.Year==9999))
        { Message="Enter a valid audit date range.";return; }
        var text=MasterSearch.Query(Search);
        var status=StatusFilter?.Value;
        var company=CompanyFilter?.Id;
        var filter=IsAudit ? new AuditFilter(AuditFrom?.Date.ToUniversalTime(),AuditTo?.Date.AddDays(1).ToUniversalTime(),
            MasterSearch.Query(AuditEvent),MasterSearch.Query(AuditActor),MasterSearch.Query(AuditTarget),AuditOutcome) : null;
        var beforeUser=SelectedUser?.Id;
        var beforeRole=SelectedRole?.Id;
        var result=await Send(new(current.ListAction,Search:text,Active:status,CompanyFilter:company==Guid.Empty?null:company,
            Page:page,Audit:filter,PageSize:SettingsDisplay.Current.Get(SettingsRegistry.PageSize)),null,ct);
        if (current!=Section || text!=MasterSearch.Query(Search) || status!=StatusFilter?.Value || company!=CompanyFilter?.Id) return;
        if (!Accept(result)) {Users=[];Roles=[];Audit=[];more=false;Notify();return;}
        Users=result.Users ?? [];
        Roles=result.Roles ?? [];
        Audit=result.Audit ?? [];
        SelectedUser=Users.FirstOrDefault(x=>x.Id==beforeUser);
        SelectedRole=Roles.FirstOrDefault(x=>x.Id==beforeRole);
        more=result.More;
        Notify();
    }

    internal void AuditFilterChanged() { if (IsAudit) QueueList(true); }
    private bool CanNext()=>CanBrowse && more;
    private bool CanPrevious()=>CanBrowse && page>0;
    [RelayCommand(CanExecute=nameof(CanNext))] private Task NextAsync()=>Run(async ct=>{page++;await Load(ct);});
    [RelayCommand(CanExecute=nameof(CanPrevious))] private Task PreviousAsync()=>Run(async ct=>{page--;await Load(ct);});

    private async Task Run(Func<CancellationToken,Task> work,CancellationToken caller=default)
    {
        if (Busy) return;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(caller,viewLifetime.Token);
        Busy=true;
        try { await work(linked.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.IO.IOException)
        { Message="Security administration is currently unavailable."; }
        finally { Busy=false; }
    }
    private async Task<SecurityResult> Send(SecurityRequest request,PasswordInput? password,CancellationToken ct)
    {
        var result=await Task.Run(()=>service.ExecuteAsync(request,password,ct),ct);
        ct.ThrowIfCancellationRequested();
        if (result.Outcome==CompanyOutcome.SessionRequired) { Clear();SessionExpired?.Invoke(); }
        return result;
    }
    private bool Accept(SecurityResult result)
    {
        Message=result.Message ?? result.Outcome switch
        {
            CompanyOutcome.Success=>"",
            CompanyOutcome.Forbidden=>"Access denied for this operation or company.",
            CompanyOutcome.SessionRequired=>"Sign in again to continue.",
            CompanyOutcome.Conflict=>"The record changed or conflicts with another record. Refresh and try again.",
            CompanyOutcome.NotFound=>"This record is unavailable.",
            CompanyOutcome.InvalidInput=>"Review the values and try again.",
            _=>"Security administration is unavailable. Check the database update."
        };
        return result.Outcome==CompanyOutcome.Success;
    }

    private bool CanOpenRow()=>CanBrowse && (IsUsers && SelectedUser is not null || IsRoles && SelectedRole is not null);

    [RelayCommand(CanExecute=nameof(CanOpenRow))]
    private Task OpenRowAsync()=>OpenAsync(IsUsers?SelectedUser?.Id:SelectedRole?.Id,false);

    [RelayCommand]
    private Task EditAsync()=>CanEditViewed ? OpenAsync(IsUsers?user?.Summary.Id:role?.Summary.Id,true) : Task.CompletedTask;

    private Task OpenAsync(Guid? id,bool edit)=>Run(async ct=>
    {
        if (id is null) return;
        var result=await Send(new(IsUsers?SecurityAction.User:SecurityAction.Role,id),null,ct);
        if (!Accept(result)) return;
        Show(result.User,result.Role,edit);
    });

    [RelayCommand]
    private void Add()
    {
        if (CanCreate) Show(null,null,true);
    }

    [RelayCommand]
    private void ResetPassword()
    {
        if (!CanReset) return;
        Resetting=true;
        Editing=true;
        Message="Enter a new temporary password for "+user!.Summary.Username+".";
    }

    private void Show(UserDetails? detail,RoleDetails? roleDetail,bool edit)
    {
        liveSearch.Cancel();
        loading=true;
        Resetting=false;
        user=detail;role=roleDetail;
        EditorOpen=true;Editing=edit;
        Message="";
        if (IsUsers)
        {
            var draft=detail?.Profile ?? new("","");
            Fields=[
                Field("Username","Username *",draft.Username,detail is not null),
                Field("FullName","Full name *",draft.FullName),Field("DisplayName","Display name",draft.DisplayName),
                Field("Email","Email",draft.Email),Field("MobileNumber","Mobile",draft.MobileNumber),
                Field("EmployeeCode","Employee code",draft.EmployeeCode),Field("Designation","Designation",draft.Designation),
                Field("Department","Department",draft.Department)];
            Active=draft.IsActive;Locked=draft.IsLocked;RequireChange=draft.RequireChange || detail is null;
            Companies=options.Companies.Select(c=>new SecurityCheck(c.Label,c.Id,draft.Companies?.Contains(c.Id)==true,c.Active ||
                draft.Companies?.Contains(c.Id)==true)).ToArray();
            foreach(var company in Companies) company.PropertyChanged+=(_,e)=>
            {
                if (e.PropertyName==nameof(SecurityCheck.Selected)){UpdateAssignmentPages();MarkDirty();}
            };
            assignments=options.Companies.Select(c=>new SecurityAssignmentPage(c.Id,c.Label,
                options.Roles.Select(r=>new SecurityCheck(r.Label+(r.Active?"":" (inactive)"),r.Id,
                    draft.Roles?.Contains(new RoleGrant(r.Id,c.Id))==true,
                    r.Active || draft.Roles?.Contains(new RoleGrant(r.Id,c.Id))==true)).ToArray())).ToArray();
            foreach(var item in assignments.SelectMany(x=>x.Roles)) item.PropertyChanged+=(_,_)=>MarkDirty();
            UpdateAssignmentPages();
            SecurityInformation=detail is null ? "New users replace their temporary password on first sign-in." :
                (detail.Summary.IsSuperAdmin ? "Protected Super Admin.\n" : "")+
                $"Last login: {Stamp(detail.Summary.LastLogin)} · Password changed: {Stamp(detail.LastPasswordChange)}\n"+
                $"Password change required: {detail.Profile.RequireChange} · Login rate limit active: {detail.LoginThrottled}\n"+
                $"Roles: {detail.Summary.RoleNames}\nCompany access: {detail.Summary.CompanyNames}";
            AuditInformation=detail is null?"":Attribution(detail.CreatedUtc,detail.CreatedBy,detail.UpdatedUtc,detail.UpdatedBy);
        }
        else
        {
            var draft=roleDetail?.Draft ?? new("","");
            Fields=[Field("Code","Role code *",draft.Code,roleDetail is not null),Field("Name","Role name *",draft.Name),
                Field("Description","Description",draft.Description)];
            Active=draft.IsActive;
            permissionChecks=PermissionCatalog.All.Select(x=>new RolePermissionCheck(x,
                new(x.Label,Guid.Empty,draft.Permissions?.Contains(x.Code)==true))).ToArray();
            foreach(var check in permissionChecks) check.Check.PropertyChanged+=(_,_)=>MarkDirty();
            PermissionGroups=permissionChecks.GroupBy(x=>x.Definition.Module)
                .Select(group=>new PermissionGroup(group.Key,group.Select(x=>x.Check).ToArray())).ToArray();
            Members=roleDetail?.Members ?? [];
            AuditInformation=roleDetail is null?"":Attribution(roleDetail.CreatedUtc,roleDetail.CreatedBy,roleDetail.UpdatedUtc,roleDetail.UpdatedBy);
        }
        foreach(var field in Fields) field.Changed+=_=>MarkDirty();
        Dirty=false;loading=false;Notify();
    }

    private void UpdateAssignmentPages()
    {
        var selected=AssignmentPage?.CompanyId;
        AssignmentPages=assignments.Where(x=>Companies.Any(c=>c.Id==x.CompanyId && c.Selected)).ToArray();
        AssignmentPage=AssignmentPages.FirstOrDefault(x=>x.CompanyId==selected) ?? AssignmentPages.FirstOrDefault();
    }

    public Task SaveAsync(PasswordInput? password)=>Run(async ct=>
    {
        if (!Editing || !IsAdmin || !EditorOpen) return;
        if (!Resetting && !ValidateFields()) return;
        if (ConfirmChange?.Invoke(Resetting ? "Reset this user's password and require a new password at sign-in?" :
            IsUsers ? "Save this account and its company and role assignments?" : "Save the role and its effective permissions?")!=true) return;
        var request=Resetting ? new SecurityRequest(SecurityAction.ResetPassword,user!.Summary.Id,user.Summary.Version) :
            IsUsers ? new(SecurityAction.SaveUser,user?.Summary.Id,user?.Summary.Version,
                User:new(Value("Username"),Value("FullName"),Value("DisplayName"),Value("Email"),Value("MobileNumber"),
                    Value("EmployeeCode"),Value("Designation"),Value("Department"),Active,Locked,RequireChange,
                    Companies.Where(x=>x.Selected).Select(x=>x.Id).ToArray(),
                    AssignmentPages.SelectMany(x=>x.Roles.Where(r=>r.Selected).Select(r=>new RoleGrant(r.Id,x.CompanyId))).ToArray())) :
            new(SecurityAction.SaveRole,role?.Summary.Id,role?.Summary.Version,
                Role:new(Value("Code"),Value("Name"),Value("Description"),Active,
                    permissionChecks.Where(x=>x.Check.Selected).Select(x=>x.Definition.Code).ToArray()));
        if (!Accept(await Send(request,password,ct))) return;
        ClearEditor();
        if (IsAdmin && IsRoles)
        {
            var update=await Send(new(SecurityAction.Options),null,ct);
            if (Accept(update)) options=update.Options!;
        }
        page=0;
        await Load(ct);
        if (Message.Length==0) Message="Saved.";
    });

    private bool ValidateFields()
    {
        foreach(var field in Fields) field.Error="";
        var required=IsUsers?new[]{"Username","FullName"}:new[]{"Code","Name"};
        foreach(var field in Fields.Where(x=>required.Contains(x.Name) && string.IsNullOrWhiteSpace(x.Text)))
            field.Error="This field is required.";
        var username=Fields.FirstOrDefault(x=>x.Name=="Username");
        if (username is not null && username.Error=="" &&
            !System.Text.RegularExpressions.Regex.IsMatch(username.Text.Trim(),@"\A[A-Za-z0-9._-]{3,40}\z"))
            username.Error="Use 3–40 letters, digits, dots, underscores or hyphens.";
        return Fields.All(x=>x.Error=="");
    }

    [RelayCommand]
    private void CloseEditor()=>CanLeave();

    private void ClearEditor()
    {
        loading=true;
        EditorOpen=false;Editing=false;Resetting=false;Dirty=false;
        Fields=[];Companies=[];AssignmentPages=[];AssignmentPage=null;PermissionGroups=[];Members=[];
        permissionChecks=[];assignments=[];user=null;role=null;
        loading=false;
        Notify();
    }

    private void MarkDirty() { if (!loading && Editing) Dirty=true; }
    private string Value(string name)=>Fields.Single(x=>x.Name==name).Text;
    private static CustomerField Field(string name,string label,string? value,bool readOnly=false)=>
        new(name,label) { Text=value ?? "",ReadOnly=readOnly };
    private static string Stamp(DateTime? stamp)=>stamp is null?"Unavailable":DateTime.SpecifyKind(stamp.Value,DateTimeKind.Utc).ToLocalTime().ToString("g");
    private static string Attribution(DateTime? created,string? creator,DateTime? updated,string? updater)=>
        $"Created: {Stamp(created)} by {creator ?? "unrecorded"} · Updated: {Stamp(updated)} by {updater ?? "unrecorded"}";
}
