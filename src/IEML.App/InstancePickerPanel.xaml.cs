using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace IEML.App;

/// 實例選擇面板：DataContext 直接用 MainViewModel（清單與選取項目都綁在上面）。
public partial class InstancePickerPanel : UserControl
{
    readonly Action<bool> _close;
    public InstancePickerPanel(MainViewModel vm, Action<bool> close) { InitializeComponent(); DataContext = vm; _close = close; }
    void Ok_Click(object s, RoutedEventArgs e) => _close(true);
    void Add_Click(object s, RoutedEventArgs e) { _close(false); ((MainViewModel)DataContext).AddInstanceCommand.Execute(null); }   // 先關掉選擇面板，再開新增面板
    void List_DoubleClick(object s, MouseButtonEventArgs e) { if (((MainViewModel)DataContext).SelectedInstance is not null) _close(true); }
}
