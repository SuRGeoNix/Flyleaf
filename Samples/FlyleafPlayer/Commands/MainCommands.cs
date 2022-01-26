using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using FlyleafLib.Controls.WPF;

namespace FlyleafPlayer.Commands
{
    public class MainCommands
    {
        public ICommand ToggleVisibility { get; set; }
        public MainCommands()
        {
            ToggleVisibility = new RelayCommand(ToggleVisibilityAction);
        }

        private void ToggleVisibilityAction(object obj)
        {
            FrameworkElement fe = (FrameworkElement)obj;
            if (fe.Visibility == Visibility.Visible)
                fe.Visibility = Visibility.Collapsed;
            else
                fe.Visibility = Visibility.Visible;
        }
    }
}
