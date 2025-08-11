using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml.Linq;
using AdsUtilities;
using TwinCAT.Ads;

namespace AdsUtilitiesUI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window 
{
    private readonly MainWindowViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();

        DataContext = _viewModel;

        _viewModel.LogMessages.CollectionChanged += (s, e) =>
        {
            if (LogListBox.Items.Count > 0)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    LogListBox.ScrollIntoView(LogListBox.Items[^1]);    // Auto scroll
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        };
    }

    private void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        PowerMenu.PlacementTarget = PowerButton;
        PowerMenu.IsOpen = true;
    }
}

