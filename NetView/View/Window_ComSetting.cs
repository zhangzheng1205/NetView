﻿using NetView.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NetView.View
{
    public partial class Window_ComSetting : Form
    {
        public Window_ComSetting()
        {
            InitializeComponent();

            this.propertyGrid1.SelectedObject = new ComportSettingModel();
        }
    }
}