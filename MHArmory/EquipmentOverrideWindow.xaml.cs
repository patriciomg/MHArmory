using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MHArmory.ViewModels;

namespace MHArmory
{
    /// <summary>
    /// Interaction logic for EquipmentOverrideWindow.xaml
    /// </summary>
    public partial class EquipmentOverrideWindow : Window
    {
        private readonly RootViewModel rootViewModel;

        public EquipmentOverrideWindow(RootViewModel rootViewModel)
        {
            InitializeComponent();

            WindowManager.FitInScreen(this);

            this.rootViewModel = rootViewModel;

            InputBindings.Add(new InputBinding(new AnonymousCommand(OnCancel), new KeyGesture(Key.Escape, ModifierKeys.None)));

            DataContext = rootViewModel.EquipmentOverride;
        }

        private void OnCancel(object parameter)
        {
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            e.Cancel = true;
            Hide();
        }
    }
}