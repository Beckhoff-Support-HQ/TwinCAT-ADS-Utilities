﻿using System;
using System.Collections.Generic;
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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AdsUtilitiesUI.Views.Pages
{
    /// <summary>
    /// Interaction logic for DeviceInfoPage.xaml
    /// </summary>
    public partial class DeviceInfoPage : Page
    {
        public DeviceInfoPage()
        {
            InitializeComponent();
        }
        private void ApplyNetId_Click(object sender, RoutedEventArgs e)
        {
            NetIdChangeMenu.PlacementTarget = ApplyButton;
            NetIdChangeMenu.IsOpen = true;
        }

    }
}
