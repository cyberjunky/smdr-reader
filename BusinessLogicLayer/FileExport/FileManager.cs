﻿//Mitel SMDR Reader
//Copyright (C) 2013 Insight4 Pty. Ltd. and Nicholas Evan Roberts

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
using System.IO;
using System.Text;

namespace MiSMDR.BusinessLogicLayer
{
    public class FileManager
    {
        public FileManager()
        {
        }

        /*
         * Save the text to file at the specified path
         */ 
        public static string Save(string path, string text)
        {
            if (path != String.Empty)
            {
                try
                {
                    TextWriter writer = new StreamWriter(path);
                    writer.Write(text);
                    writer.Close();
                    return "";
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }
            }
            else
            {
                return "An error occurred saving the file. A file name and location must be specified to save the log file.";
            }
        }
    }
}
