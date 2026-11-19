using System.Collections.ObjectModel;
using System.Windows;
using IEML.Core.Models;
namespace IEML.App;

public partial class InstanceSettingsWindow : Window
{
    public InstanceConfig Config { get; }
    public IReadOnlyList<Account> Accounts { get; }
    public ObservableCollection<QuickLaunchCard> Cards { get; }

    public InstanceSettingsWindow(InstanceConfig cfg, IReadOnlyList<Account> accounts)
    {
        Config = cfg; Accounts = accounts;
        Cards = new(cfg.QuickCards.Select(c => new QuickLaunchCard { Id = c.Id, AccountId = c.AccountId, X = c.X, Y = c.Y }));
        InitializeComponent();
        DataContext = this;
        LoaderBox.ItemsSource = Enum.GetValues<LoaderType>();
        LoaderBox.SelectedItem = cfg.Loader;
    }

    void PickJava_Click(object s, RoutedEventArgs e)
    {
        var w = new JavaPickerWindow(Config.McVersion) { Owner = this };
        if (w.ShowDialog() != true) return;
        Config.JavaPath = w.SelectedPath;
        JavaBox.Text = w.SelectedPath ?? "";
    }

    void AddCard_Click(object s, RoutedEventArgs e) =>
        Cards.Add(new QuickLaunchCard { AccountId = Accounts.FirstOrDefault()?.Id ?? Guid.Empty });

    void RemoveCard_Click(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is QuickLaunchCard c) Cards.Remove(c);
    }

    void Save_Click(object s, RoutedEventArgs e)
    {
        if (LoaderBox.SelectedItem is LoaderType l && l != Config.Loader) { Config.Loader = l; Config.LoaderVersion = null; }
        Config.QuickCards = Cards.Where(c => c.AccountId != Guid.Empty).ToList();
        DialogResult = true;
    }

    void Cancel_Click(object s, RoutedEventArgs e) => DialogResult = false;
}
