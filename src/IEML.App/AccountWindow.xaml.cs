using System.Windows;
using System.Windows.Controls;
using IEML.Core.Contracts;
using IEML.Core.Models;
using IEML.Core.Services;
namespace IEML.App;

public partial class AccountWindow : Window
{
    readonly IAccountService _svc;
    public AccountWindow(IAccountService svc)
    {
        _svc = svc; InitializeComponent();
        Loaded += async (_, _) => await ReloadAsync();
    }

    Account? Selected => AccList.SelectedItem as Account;

    async Task ReloadAsync()
    {
        var keep = Selected?.Id;
        var list = await _svc.ListAsync();
        AccList.ItemsSource = list;
        AccList.SelectedItem = list.FirstOrDefault(a => a.Id == keep);
    }

    void AccList_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (Selected is { Type: AccountType.Offline } a) NameBox.Text = a.Name;
    }

    async void AddOffline_Click(object s, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var err = AccountNames.Validate(name);
        if (err != null) { StatusText.Text = err; return; }
        var existing = (await _svc.ListAsync()).Any(a => a.Type == AccountType.Offline && a.Name == name);
        if (existing) { StatusText.Text = "已有同名的離線帳號"; return; }
        try { await _svc.AddOfflineAsync(name); StatusText.Text = $"已新增離線帳號：{name}"; await ReloadAsync(); }
        catch (Exception ex) { StatusText.Text = "新增失敗：" + ex.Message; }
    }

    async void AddMs_Click(object s, RoutedEventArgs e)
    {
        StatusText.Text = "請在彈出的視窗登入 Microsoft 帳號…";
        try { var a = await _svc.AddMicrosoftAsync(); StatusText.Text = $"已新增正版帳號：{a.Name}"; await ReloadAsync(); }
        catch (Exception ex) { StatusText.Text = "登入失敗：" + ex.Message; }
    }

    async void Rename_Click(object s, RoutedEventArgs e)
    {
        if (Selected is null) { StatusText.Text = "請先選一個帳號"; return; }
        try { await AccountNames.RenameOfflineAsync(Selected.Id, NameBox.Text.Trim()); StatusText.Text = "已重新命名"; await ReloadAsync(); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    async void Delete_Click(object s, RoutedEventArgs e)
    {
        if (Selected is null) { StatusText.Text = "請先選一個帳號"; return; }
        if (MessageBox.Show(this, $"確定刪除帳號「{Selected.Name}」？", "刪除帳號", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { await _svc.RemoveAsync(Selected.Id); StatusText.Text = "已刪除"; await ReloadAsync(); }
        catch (Exception ex) { StatusText.Text = "刪除失敗：" + ex.Message; }
    }
}
