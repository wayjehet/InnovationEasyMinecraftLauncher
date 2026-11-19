using System.Windows;
using IEML.Core.Models;
using IEML.Core.Services;
namespace IEML.App;

public partial class NewInstanceWindow : Window
{
    public string InstanceName => NameBox.Text.Trim();
    public LoaderType Loader => LoaderBox.SelectedItem is LoaderType l ? l : LoaderType.Vanilla;
    public string McVersion => VerBox.SelectedItem as string ?? "";

    public NewInstanceWindow(string defaultName)
    {
        InitializeComponent();
        NameBox.Text = defaultName;
        LoaderBox.ItemsSource = Enum.GetValues<LoaderType>();
        LoaderBox.SelectedIndex = 0;
        Loaded += async (_, _) => await LoadVersions();
    }

    async Task LoadVersions()
    {
        VerBox.ItemsSource = await VersionService.ListAsync(SnapBox.IsChecked == true);
        VerBox.SelectedIndex = 0;
    }

    async void Snap_Click(object s, RoutedEventArgs e) => await LoadVersions();

    void Ok_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(InstanceName) || string.IsNullOrEmpty(McVersion)) return;
        DialogResult = true;
    }
}
