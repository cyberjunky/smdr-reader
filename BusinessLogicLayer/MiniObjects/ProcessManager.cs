﻿//Mitel SMDR Reader
//Copyright (C) 2013  Insight4 Pty. Ltd. and Nicholas Evan Roberts

//This program is free software; you can redistribute it and/or modify
//it under the terms of the GNU General Public License as published by
//the Free Software Foundation; either version 2 of the License, or
//(at your option) any later version.

//This program is distributed in the hope that it will be useful,
//but WITHOUT ANY WARRANTY; without even the implied warranty of
//MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//GNU General Public License for more details.

//You should have received a copy of the GNU General Public License along
//with this program; if not, write to the Free Software Foundation, Inc.,
//51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA.
using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Forms;

namespace MiSMDR.BusinessLogicLayer
{
    public class ProcessManager
    {
        public ProcessManager()
        {
        }

        /*
         * Start the process
         */ 
        public void StartProcess(string exportPath)
        {
            try
            {
                Process.Start(@"" + exportPath);
            }
            catch(Exception)
            {
                MessageBox.Show("There is no log currently. Please check the MiSMDR settings.","Log Missing",MessageBoxButtons.OK,MessageBoxIcon.Exclamation);
            }
        }
    }
}
