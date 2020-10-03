
/* Required Assemblies
 * 
 * System.Windows.Forms
 * WindowsFormsIntegration
 * 
 */

using System.Windows;
using System.Windows.Forms.Integration;

using SuRGeoNix.Flyleaf.Controls;

namespace SuRGeoNix.FlyleafPlayerWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            WindowsFormsHost host = new WindowsFormsHost();

            // Create the MaskedTextBox control.
            FlyleafPlayer flyleaf = new FlyleafPlayer();
            flyleaf.isWPF = true;
            flyleaf.config.hookForm._Enabled = false;
            host.Child = flyleaf;
            this.grid1.Children.Add(host);
        }
    }
}
