using System.Windows;

namespace AlignerSimulator;

public partial class MainWindow : Window
{
	public MainWindow()
	{
		InitializeComponent();
		DataContext = new SimulatorViewModel();
	}
}