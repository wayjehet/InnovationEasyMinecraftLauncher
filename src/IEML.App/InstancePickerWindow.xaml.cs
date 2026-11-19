using System.Windows;
using System.Windows.Input;
namespace IEML.App;

/// 實例選擇：DataContext 直接用 MainViewModel（清單與選取項目都綁在上面）。
public partial class InstancePickerWindow : Window
{
    public InstancePickerWindow(MainViewModel vm) { InitializeComponent(); DataContext = vm; }
    void Ok_Click(object s, RoutedEventArgs e) => DialogResult = true;
    void List_DoubleClick(object s, MouseButtonEventArgs e) { if (((MainViewModel)DataContext).SelectedInstance is not null) DialogResult = true; }
}
