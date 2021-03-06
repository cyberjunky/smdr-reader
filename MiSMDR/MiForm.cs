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
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Security;
using System.Data;
using System.Drawing;
using System.Text;
using System.Globalization;
using System.Windows.Forms;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.IO;
using System.Diagnostics;
using System.Timers;
using Microsoft.VisualBasic;
using MiSMDR.BusinessLogicLayer;
using MiSMDR.MitelManager;
using MiSMDR.Logger;
using MiSMDR.Security;
using MiSMDR.SessionTracker;
using MiSMDR.DBIntegrity;
using MiSMDR.DataAccessLayer;
using MiSMDR.Properties;

namespace MiSMDR
{
    public partial class MiForm : Form
    {
        #region Global Variables

        private System.Timers.Timer mitelConnectionTimer = new System.Timers.Timer();
        private System.Timers.Timer callCountTimer = new System.Timers.Timer();
        private MiManager mitelManager = null;
        private DataSet exportData = null;
        delegate void SetTextCallback(Label lb, string text);

        private DataProvider _provider = DataProvider.None;
        private string _connectionString = String.Empty;
        private string _server = String.Empty;
        private string _exportPath = String.Empty;
        private int _port = -1;
        private int callCountTimerInterval = 60; // in seconds
        private bool _connected = false;
        private bool _demo = false;
        private bool _regPopup = false;
        private bool _trialStatus = false;
        private bool _initialised = false;
        private bool _refreshSearches = false;

        private bool _showDebug = false;
        private TabPage _debugPointer; //pointer used to bring back the debug tab

        private bool flagUnansweredWarning = false;

        private bool connCostValid = true;
        private bool blockSizeValid = true;
        private bool rateValid = true;
        private bool contactNumValid = true;

        private string ps_selectedPeriod = "day";

        private delegate void SetConnectionStatusHandler(bool connected);

        #endregion

        #region Initialization
        public MiForm(bool regPopup, bool trialStatus)
        {
            _provider = MiConfig.GetProvider();
            _demo = MiConfig.GetDemoMode(); // this is set during startup (and overwritten if the app is Demo Only)

            CheckDemo(); //get connection string
            LogManager.SetConnectionString(_connectionString, _provider);

            InitializeComponent(); //load all the GUI items

            SetupMitel(); // Create the MitelManager

            SetupDataGridKeys(); //Show the Search keys
            _initialised = true; // Set initialised as true so the CheckDemo() function does it's other responsibilities
            _regPopup = false; //regPopup; // disabled for opensource version
            _trialStatus = trialStatus;
        }

        private void MiForm_Load(object sender, EventArgs e)
        {
            RestoreWindow();

            //Get server and port settings
            _server = MiConfig.GetServer();
            _port = MiConfig.GetPort();
            tbServer.Text = _server;
            tbPort.Text = _port.ToString();

            //Load the Text for all tabs
            LoadSplashContent();
            GetLicenseInformation();
            LoadToolTips();
            LoadCallCostCopy();
            LoadAddressBookCopy();

            CheckDemo(); //re-check connection strings and set the status

            RefreshTabInfo(true); //get all the data for the tabs

            CheckDebug(); // Show or Hide the debug tab based on the settings

            this.MinimumSize = new Size(1020, 657);
            //If the config says to connect we connect
            if ((!_demo) && (MiConfig.GetConnectOnStartup()))
            {
                mitelManager.Connect(_server, _port);
                lbStatus.Text = "Connecting";
                lbStatus.ForeColor = Color.Orange;
                bnConnect.Enabled = false;
                bnDisconnect.Enabled = true;
            }
        }

        private void RestoreWindow()
        {
            // Set window location and size from the settings file provided it is in the current screen parameters
            if (Settings.Default.WindowLocation != null)
            {
                if ((Settings.Default.WindowLocation.Y < Screen.PrimaryScreen.Bounds.Height) && (Settings.Default.WindowLocation.X < Screen.PrimaryScreen.Bounds.Width))
                {
                    if ((Settings.Default.WindowLocation.Y > 0) && (Settings.Default.WindowLocation.X > 0))
                    {
                        this.Location = Settings.Default.WindowLocation;

                        //Now we get the window state
                        if (Settings.Default.WindowState == "Minimized")
                        {
                            this.WindowState = FormWindowState.Minimized;
                        }
                        else if (Settings.Default.WindowState == "Maximized")
                        {
                            this.WindowState = FormWindowState.Maximized;
                        }
                        else
                        {
                            //Default state is Normal
                            this.WindowState = FormWindowState.Normal;
                        }
                    }
                    else
                    {
                        //this.Location = new Point(0, 0);

                    }
                }
                else
                {
                    //this.Location = new Point(0, 0);
                }
            }
            if (Settings.Default.WindowSize != null)
            {
                this.Size = Settings.Default.WindowSize;
            }
        }

        private void SaveWindowState()
        {
            // Copy window size and location to app settings
            if (this.WindowState == FormWindowState.Normal)
            {
                Settings.Default.WindowSize = this.Size;
                Settings.Default.WindowLocation = this.Location;
            }
            else
            {
                Settings.Default.WindowSize = this.RestoreBounds.Size;
                Settings.Default.WindowLocation = this.RestoreBounds.Location;
            }
            // Save the Minimized or Normal state
            MiConfig.SetState(this.WindowState);

            // Save settings
            Settings.Default.Save();
        }

        private void RestoreWindow(int x, int y)
        {
            this.Location = new Point(x, y);
            this.Size = new Size(1020, 655);
            this.Show();
        }

        public static void customStart(bool regPopup, bool demoStatus)
        {
            MiForm mainForm = new MiForm(regPopup, demoStatus);
            mainForm.ShowDialog();
        }

        #endregion

        #region Mitel Connection

        private void SetupMitel()
        {
            string path = MiConfig.GetLogPath();
            mitelManager = new MiManager(_connectionString, _provider, path);
            mitelManager.MitelConnected += new EventHandler(mitelManager_MitelConnected);
        }

        public void Connect()
        {
            //only connect if not in demo mode
            if (!_demo)
            {
                _server = MiConfig.GetServer();
                _port = MiConfig.GetPort();

                if (mitelManager != null)
                {
                    List<string> connectErrors = new List<string>();
                    int port = -1;
                    string server = tbServer.Text;

                    if (tbServer.Text != string.Empty)
                        server = tbServer.Text;
                    else
                        connectErrors.Add("Server field cannot be blank.");

                    try
                    {
                        port = Convert.ToInt32(tbPort.Text);

                        if (port < 0)
                        {
                            connectErrors.Add("Port value must be a number greater than or equal to zero.");
                        }
                    }
                    catch (Exception)
                    {
                        //WriteDiagnostic(ex, DiagType.Error);
                        connectErrors.Add("Port value must be a number greater than or equal to zero.");
                    }

                    if (connectErrors.Count == 0)
                    {
                        SetStatusText("Connecting to server: " + server + " on port: " + port, "", "");
                        WriteToApplicationLog("[CONNECTION]: Connecting to server: " + server + " on port: " + port);

                        this.Refresh();

                        if (mitelManager != null)
                        {
                            string connectResult = mitelManager.Connect(server, port);
                            SetupMitelStatusTimer(); //start the connection tester
                            lbStatus.Text = connectResult;
                            lbStatus.ForeColor = Color.Orange;
                            bnDisconnect.Enabled = true;
                            bnConnect.Enabled = false;
                            //ConnectionSuccessful(mitelManager.Connected);
                            //SetStatusText(connectResult, "", "");

                            /*if (mitelManager.IsConnected())
                            {
                                mitelManager.SetLastConnectedDate();
                            }*/
                        }
                        else
                        {
                            LogManager.Log(LogEntryType.Error, SourceType.MiSMDR, "MitelManager object could not be created.");
                        }
                    }
                    else
                    {
                        string errorList = "Incorrect server and port information:" + Environment.NewLine;
                        foreach (string error in connectErrors)
                        {
                            //WriteDiagnostic(DiagType.Error, this.Name, "Connection error", error);
                            errorList += "- " + error + Environment.NewLine;
                        }
                        EntryError(errorList);
                    }
                }
            }
        }

        public void Disconnect()
        {
            if (mitelManager != null)
            {
                string result = mitelManager.Disconnect();
                _connected = false;
                callCountTimer.Stop();
                WriteToApplicationLog("[CONNECTION]: " + result);
                SetStatusText(result, "", "");

                ConnectionSuccessful(mitelManager.Connected); // status labels + images

            }
            else
            {
                ConnectionSuccessful(false);
                CheckDashboard();
            }
        }

        private void SetupCallCountTimer()
        {
            SetStatusText("Mitel Connected: Retrieving Call Records...", "", "");
            if (MiConfig.GetShowNotifications()) notifyIcon1.ShowBalloonTip(500, "MiSMDR Connected", "Connected to the Mitel Server", ToolTipIcon.Info);
            callCountTimer.Stop();
            callCountTimer.Interval = callCountTimerInterval * 1000;
            callCountTimer.Elapsed += new ElapsedEventHandler(callCountTimer_Tick);
            callCountTimer.Start();
        }

        private void SetupMitelStatusTimer()
        {
            mitelConnectionTimer.Stop();
            mitelConnectionTimer.Interval = 20 * 1000; //20 secs
            mitelConnectionTimer.Elapsed += new ElapsedEventHandler(mitelConnectionTimer_Tick);
            mitelConnectionTimer.Start();
        }

        private void ConnectToMitel()
        {
            Connect();
        }

        protected void mitelManager_MitelConnected(object sender, EventArgs e)
        {
            SetConnectionStatus(((MitelManager.MiManager)sender).Connected);
            SetupCallCountTimer();
        }

        private void SetConnectionStatus(bool connected)
        {
            if (lbStatus.InvokeRequired)
            {
                lbStatus.Invoke(new SetConnectionStatusHandler(SetConnectionStatus), new object[] { connected });
            }
            else
            {
                ConnectionSuccessful(connected);
            }
        }

        private void ConnectionSuccessful(bool connected)
        {
            _connected = connected;
            if (connected)
            {
                SetStatusText("Connected to server " + _server + " on port " + _port, "", "");
                miLog(LogEntryType.Information, SourceType.MitelDataCollection, "Connected to " + _server + " on port " + _port);

                // Set the status labels text/image
                connectionStatusLabel.Text = "Connected";
                connectionStatusLabel.Image = imageList.Images[0];

                lbStatus.Text = "Connected";
                lbStatus.ForeColor = Color.Green;
            }
            else
            {
                //SetStatusText("Failed to connect to " + _server + " on port " + _port, "", "");
                //miLog(LogEntryType.Error, SourceType.MitelDataCollection, "Failed to connect to " + _server + " on port " + _port);

                // Set the status labels text/image
                connectionStatusLabel.Text = "Not Connected";
                connectionStatusLabel.Image = imageList.Images[1];

                lbStatus.Text = "Not Connected";
                lbStatus.ForeColor = Color.Red;

            }
            CheckDashboard();
        }

        #endregion

        #region Checking Functions

        private void CheckDemo()
        {
            string tmpConnString = _connectionString; // to check if there has been a change
            _demo = MiConfig.GetDemoMode();
            if (_demo)
            {
                _connectionString = MiConfig.GetConnectionString("MiDemoString");
                //Only get the connection string if this is during the load
                if (_initialised == true)
                {
                    if (mitelManager.Connected) mitelManager.Disconnect();

                    ConnectionSuccessful(mitelManager.Connected);

                    //overwrite some status messages with Demo specific messages
                    SetStatusText("Demo Mode Activated - Connection to Mitel Disabled", "", "");
                    if (_trialStatus) this.Text = "Trial License - PLEASE REGISTER";
                    else this.Text = "MiSMDR Call Accounting - DEMO MODE - USING DEMO DATA ONLY";
                    lbStatus.Text = "Demo Mode - Not Connected";
                    lbStatus.ForeColor = Color.Red;
                    rbMMDemo.Checked = true;
                }
            }
            else
            {
                _connectionString = MiConfig.GetConnectionString("MiDatabaseString");
                //Only get the connection string if this is during the load
                if (_initialised == true)
                {
                    if (_trialStatus) this.Text = "Trial License - PLEASE REGISTER";
                    else this.Text = "MiSMDR Call Accounting";
                    mitelManager.Setup(_connectionString, _provider, MiConfig.GetLogPath());
                    if (tmpConnString != _connectionString) ConnectionSuccessful(mitelManager.Connected);

                    rbMMLive.Checked = true;
                }
            }

            CheckDashboard();

            if (_initialised)
            {
                //if we are changing DBs we need to update all the Tabs
                if (tmpConnString != _connectionString) RefreshTabInfo(true);
            }
        }

        private void CheckDashboard()
        {
            try
            {
                if (_demo)
                {
                    bnConnect.Enabled = false;
                    bnDisconnect.Enabled = false;
                    tbServer.Enabled = false;
                    tbPort.Enabled = false;
                }
                else
                {
                    if (mitelManager.Connected)
                    {
                        bnDisconnect.Enabled = true;
                        bnConnect.Enabled = false;
                        tbPort.Enabled = false;
                        tbServer.Enabled = false;
                    }
                    else
                    {
                        bnConnect.Enabled = true;
                        bnDisconnect.Enabled = false;
                        tbPort.Enabled = true;
                        tbServer.Enabled = true;
                    }
                }
            }
            catch (Exception ex)
            {

            }
        }

        private void CheckCallCosts()
        {
            if (callCostsGrid.RowCount > 0)
            {
                try
                {

                    if (callCostsGrid.SelectedCells[0].Value.ToString() != String.Empty)
                    {
                        //we have an old one to update
                        bnCCUpdate.Enabled = true;
                        bnCCDelete.Enabled = true;
                    }
                    else
                    {
                        //it is the empty one for creating a new call cost
                        bnCCUpdate.Enabled = true;
                        bnCCDelete.Enabled = false;
                    }

                    //enable the fields
                    tbCCName.Enabled = true;
                    rbCCStartsWith.Enabled = true;
                    rbCCExactMatch.Enabled = true;
                    rbCCRegEx.Enabled = true;
                    DataGridViewRow row = callCostsGrid.Rows[callCostsGrid.SelectedCells[0].RowIndex];
                    if (row.Cells[7].Value.ToString() == "starts")
                    {
                        tbCCStartsWith.Text = tbCCRegEx.Text.Substring(1, tbCCRegEx.Text.Length - 4); // remove ^ from start and \d* from the end
                        rbCCStartsWith.Checked = true;
                    }
                    else if (row.Cells[7].Value.ToString() == "exact")
                    {
                        tbCCExactMatch.Text = tbCCRegEx.Text.Substring(1, tbCCRegEx.Text.Length - 2); //remove the ^ from start and $ from the end
                        rbCCExactMatch.Checked = true;
                    }
                    else if (row.Cells[7].Value.ToString() == "regex")
                    {
                        tbCCRegEx.Enabled = true;
                        rbCCRegEx.Checked = true;
                    }

                    tbCCConnCost.Enabled = true;
                    tbCCBlockSize.Enabled = true;
                    tbCCRateBlock.Enabled = true;
                    cbCCChargeUnfinished.Enabled = true;
                }
                catch (Exception ex) //ignore the error when the row count is 1 and yet there is no values in the row
                {
                    //disable the fields
                    tbCCName.Enabled = false;
                    rbCCRegEx.Checked = true;
                    rbCCStartsWith.Enabled = false;
                    tbCCStartsWith.Enabled = false;
                    rbCCExactMatch.Enabled = false;
                    tbCCExactMatch.Enabled = false;
                    rbCCRegEx.Enabled = false;
                    tbCCRegEx.Enabled = false;
                    tbCCConnCost.Enabled = false;
                    tbCCBlockSize.Enabled = false;
                    tbCCRateBlock.Enabled = false;
                    cbCCChargeUnfinished.Enabled = false;

                    //only enable the create button
                    bnCCCreate.Enabled = true;
                    bnCCDelete.Enabled = false;
                    bnCCUpdate.Enabled = false;
                }
            }
            else
            {
                //disable the fields
                tbCCName.Enabled = false;
                rbCCRegEx.Checked = true;
                rbCCStartsWith.Enabled = false;
                tbCCStartsWith.Enabled = false;
                rbCCExactMatch.Enabled = false;
                tbCCExactMatch.Enabled = false;
                rbCCRegEx.Enabled = false;
                tbCCRegEx.Enabled = false;
                tbCCConnCost.Enabled = false;
                tbCCBlockSize.Enabled = false;
                tbCCRateBlock.Enabled = false;
                cbCCChargeUnfinished.Enabled = false;

                //only enable the create button
                bnCCCreate.Enabled = true;
                bnCCDelete.Enabled = false;
                bnCCUpdate.Enabled = false;

            }
        }

        private void CheckAddressBook()
        {

            if (addressDataGrid.RowCount > 0)
            {
                try
                {
                    if (addressDataGrid.SelectedCells[0].Value.ToString() != String.Empty)
                    {
                        //we have an old one to update
                        bnUpdateCont.Enabled = true;
                        bnContactDelete.Enabled = true;
                    }
                    else
                    {
                        //it is a new field
                        bnUpdateCont.Enabled = true;
                        bnContactDelete.Enabled = false;
                    }
                    //for both we need active input fields
                    tbContactName.Enabled = true;
                    tbContactNumber.Enabled = true;
                }
                catch (Exception ex)
                {
                    //if there is an error then the row count is faulty and there is none selected
                    tbContactName.Enabled = false;
                    tbContactNumber.Enabled = false;
                    bnCreateContact.Enabled = true;
                    bnContactDelete.Enabled = false;
                    bnUpdateCont.Enabled = false;
                }
            }
            else
            {
                //if there is none selected we only enable the Create button
                tbContactName.Enabled = false;
                tbContactNumber.Enabled = false;
                bnCreateContact.Enabled = true;
                bnContactDelete.Enabled = false;
                bnUpdateCont.Enabled = false;
            }

        }

        private void CheckDebug()
        {
            int DebugTab = 6;
            _showDebug = MiConfig.GetShowDebug();
            if (_showDebug)
            {
                //tabContainer.TabPages[5].Show();
                if (_debugPointer != null)
                {
                    tabContainer.TabPages.Insert(DebugTab, _debugPointer);
                    tabContainer.Refresh();
                    _debugPointer = null;
                }
            }
            else
            {
                //tabContainer.TabPages[5].Hide();
                if (_debugPointer == null)
                {
                    _debugPointer = tabContainer.TabPages[DebugTab];
                    tabContainer.TabPages.RemoveAt(DebugTab);
                    tabContainer.Refresh();
                }
            }
        }

        #endregion

        #region General Application Functions
        private void SetStatusText(string text1, string calls, string text2)
        {
            lbStatusText1.Text = text1;
            lbStatusCalls.Text = calls;
            lbStatusText2.Text = text2;
        }

        private void WriteToApplicationLog(string message)
        {
            tbLog.Text = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + " " + message + Environment.NewLine + tbLog.Text;
        }

        private void LoadToolTips()
        {
            System.Windows.Forms.ToolTip AnswerTimeToolTip = new System.Windows.Forms.ToolTip();
            AnswerTimeToolTip.SetToolTip(tbAnswerTime, "Format: 'hh:mm:ss' or '5 minutes'");

            System.Windows.Forms.ToolTip DurationToolTip = new System.Windows.Forms.ToolTip();
            DurationToolTip.SetToolTip(tbDuration, "Format: 'hh:mm:ss' or '5 minutes'");
        }

        private void RefreshTabInfo(bool search)
        {
            if (_refreshSearches || search)
            {
                //searchCalls(true);
                //GetCallSummaryData();
                dataGridView.DataSource = null;
                dataGridView.Refresh();
                callSummaryReport.Clear();
                PopulateFields(new CallManager(_connectionString, _provider));
                ClearSearchFields();
                ClearCallSummaryFields();
            }
            this.Cursor = Cursors.WaitCursor;
            GetSummariesData();
            GetSavedSearches();
            GetAddressBookData("");
            GetCallCostData("");
            this.Cursor = Cursors.Default;
        }

        private bool IsNumber(string strTextEntry)
        {
            Regex objNotWholePattern = new Regex("[^0-9]");
            return !objNotWholePattern.IsMatch(strTextEntry.Replace(" ", "")) && (strTextEntry != ""); //if without spaces it matches the regex
        }

        private void miLog(LogEntryType entry, SourceType source, string message)
        {
            try
            {
                string entrystring;
                if (entry == LogEntryType.Error) entrystring = "[ERROR]: ";
                else if (entry == LogEntryType.Debug) entrystring = "[DEBUG]: ";
                else entrystring = ""; // No type needed for general information
                WriteToApplicationLog(entrystring + message);
                bool failed = false;
                try
                {
                    LogManager.Log(entry, source, message.Replace('\'', '"'));
                }
                catch (Exception ex)
                {
                    failed = true;
                    DBControl control = new DBControl(_connectionString, _provider);
                    if (!control.CheckDataAccess()) control.CreateTables();
                }
                //if previously failed, retry
                if (failed) LogManager.Log(entry, source, message.Replace('\'', '"'));

            }
            catch (Exception ex)
            {
                MessageBox.Show("Serious error occured. Log access failed. Please report this bug to http://www.MiSMDR.info \n Details: " + ex.ToString(), "Serious Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }

        private void EntryError(string error)
        {
            MessageBox.Show(error, "Entry Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
        }

        private void UnexpectedError(string error)
        {
            MessageBox.Show(error + " Please check the Log for details.", "Unexpected Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        #endregion

        #region Search Form Functions

        private void SetupDataGridKeys()
        {
            keyIncoming.KeyName = "Incoming";
            keyIncoming.KeyColour = Color.FromArgb(204, 255, 204); //green

            keyInternal.KeyName = "Internal";
            keyInternal.KeyColour = Color.FromArgb(204, 255, 255); //blue

            keyOutgoing.KeyName = "Outgoing";
            keyOutgoing.KeyColour = Color.FromArgb(255, 255, 204); //yellow

            keyUnknown.KeyName = "Unknown";
            keyUnknown.KeyColour = Color.FromArgb(255, 204, 204); //red
        }

        private void FilterDataGridView()
        {
            FilterDataGridView(dataGridView); //default is to use the search data grid
        }

        private void FilterDataGridView(DataGridView dgv)
        {
            DataGridViewRowCollection rows = dgv.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                //Duration
                if (rows[i].Cells[1].FormattedValue.ToString().Length > 8)
                {
                    rows[i].Cells[1].Value = rows[i].Cells[1].FormattedValue.ToString().Substring(rows[i].Cells[1].FormattedValue.ToString().Length - 8);
                }
                //Direction
                switch (rows[i].Cells[4].FormattedValue.ToString())
                {
                    case "Incoming":
                        //Incoming (Green)
                        dgv.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(204, 255, 204);
                        break;
                    case "Outgoing":
                        //Outgoing (Yellow)
                        dgv.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(255, 255, 204);
                        break;
                    case "Internal":
                        //Internal (Blue)
                        dgv.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(204, 255, 255);
                        break;
                    case "Unknown":
                        //UNKNOWN (Red)
                        dgv.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(255, 204, 204);
                        keyUnknown.Visible = true;
                        break;
                    default:
                        break;
                }
                //Caller Name
                if (rows[i].Cells[5].FormattedValue.ToString() == String.Empty)
                {
                    rows[i].Cells[5].Value = "Unknown";
                }
                //Caller Number
                if (rows[i].Cells[6].FormattedValue.ToString().StartsWith("T"))
                {
                    rows[i].Cells[6].Value = "Private";
                }
                //Receiver Name
                if (rows[i].Cells[9].FormattedValue.ToString() == String.Empty)
                {
                    rows[i].Cells[9].Value = "Unknown";
                }
                //Filter Name
                if (rows[i].Cells[12].FormattedValue.ToString() == String.Empty)
                {
                    rows[i].Cells[12].Value = "-";
                }
                //Cost
                if (rows[i].Cells[13].FormattedValue.ToString() == String.Empty)
                {
                    rows[i].Cells[13].Value = (0.0).ToString(CultureInfo.CurrentCulture); //double value in current culture format
                }


            }
        }

        public void ColourGridView()
        {
            DataGridViewRowCollection rows = dataGridView.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                //Direction
                switch (rows[i].Cells[4].FormattedValue.ToString())
                {
                    case "Incoming":
                        //Incoming (Green)
                        this.dataGridView.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(204, 255, 204);
                        break;
                    case "Outgoing":
                        //Outgoing (Yellow)
                        this.dataGridView.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(255, 255, 204);
                        break;
                    case "Internal":
                        //Internal (Blue)
                        this.dataGridView.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(204, 255, 255);
                        break;
                    case "Unknown":
                        //UNKNOWN (Red)
                        this.dataGridView.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(255, 204, 204);
                        break;
                    default:
                        break;
                }
            }
        }

        private void PopulateFields(CallManager manager)
        {
            lbDurationTotal.Text = manager.TotalDuration.ToString();
            lbTotalCost.Text = CultureInfo.CurrentCulture.NumberFormat.CurrencySymbol + Math.Round(manager.TotalCost, 2).ToString();
            lbIncomingCount.Text = manager.Incomings.ToString();
            lbInternalCount.Text = manager.Internals.ToString();
            lbOutgoingCount.Text = manager.Outgoings.ToString();
        }

        private void ShowContextMenu(Point point)
        {
            ContextMenuStrip mnu = new ContextMenuStrip();
            mnu.Left = point.X;
            mnu.Top = point.Y;
            ToolStripMenuItem mnuCopy = new ToolStripMenuItem("Copy");
            mnuCopy.Click += new EventHandler(mnuCopy_Click);
            mnu.Items.Add(mnuCopy);
            dataGridView.ContextMenuStrip = mnu;
            //mnu.Show();
        }

        #endregion

        #region Data Retrieval Functions

        private void GetCallSearchData(bool searching)
        {
            this.Cursor = Cursors.WaitCursor;
            List<ValidationError> errors = new List<ValidationError>();
            CallManager manager = new CallManager(_connectionString, _provider);

            if (!String.IsNullOrEmpty(_connectionString))
            {
                try
                {


                    //string[] searchParamNames = new string[] { "Start Date: ", "End Date: ", "Duration: ", "Time to Answer: ", "Type: ", "Direction: ", "Caller Name: ", "Caller Number", "Caller Extension: ", "Dialled Extension: ", "Receiver Name: ", "Receiver Number: ", "Receiver Extension: " };
                    if (searching)
                    {
                        //clear Call Details field
                        callInfo.ClearFields();

                        /* ###### Init the different variables we need to search ##### */
                        string startDateValue = DateTime.Now.ToString("yyyy-MM-dd");
                        string endDateValue = "";

                        string callDirection = "";

                        string duration = "";
                        string durationCombo = "";
                        string answerTime = "";
                        string answerTimeCombo = "";

                        string callerIdentifier = "";
                        string callerExact = "";
                        string receiverIdentifier = "";
                        string receiverExact = "";

                        string dialledNumber = ""; // left out for this version (can add back in for later versions)

                        /* ###### New search value retrieval ##### */

                        if (rbSingleDate.Checked)
                        {
                            //startDateValue = DayFilter.Text.ToString("yyyy-MM-dd");
                            if (IsNumber(DayFilter.Text))
                            {
                                startDateValue = DateAndTime.Now.AddDays(-(Convert.ToInt32(DayFilter.Text)) + 1).ToString("yyyy-MM-dd");
                                endDateValue = DateAndTime.Now.ToString("yyyy-MM-dd");
                            }
                            else if (DayFilter.Text == String.Empty) startDateValue = DateAndTime.Now.ToString("yyyy-MM-dd");
                            else
                            {
                                EntryError("Last days value should be a number (searching with default 7 days)");
                                startDateValue = DateAndTime.Now.AddDays(-7 + 1).ToString("yyyy-MM-dd");
                                endDateValue = DateAndTime.Now.ToString("yyyy-MM-dd");
                            }
                        }
                        else if (rbDateRange.Checked)
                        {
                            startDateValue = StartDateFilter1.Value.ToString("yyyy-MM-dd");
                            endDateValue = EndDateFilter1.Value.ToString("yyyy-MM-dd");
                        }
                        else if (rbAllDates.Checked) startDateValue = "";

                        if (cbCallCategory.SelectedItem != null) callDirection = cbCallCategory.SelectedItem.ToString();

                        if (cbUnanswered.Checked)
                        {
                            answerTime = "****";
                            answerTimeCombo = "less than";
                        }
                        else
                        {
                            if (cbDuration.SelectedItem != null)
                            {
                                if (tbDuration.Text != String.Empty)
                                {
                                    //Remove all other possible entries and replace with proper format
                                    duration = tbDuration.Text.Replace("hours", "::").Replace("minutes", ":").Replace("hour", "::").Replace("minute", ":").Replace("hrs", "::").Replace("mins", ":").Replace("hr", "::").Replace("min", ":").Replace("h", "::").Replace("m", ":").Replace("seconds", "").Replace("second", "").Replace("secs", "").Replace("s", "").Replace("and", "").Replace("+", "").Replace("-", ":").Replace(",", "").Replace(" ", "");

                                    string[] splitTimes = duration.Split(':');
                                    string[] durTimes = { "00", "00", "00" };

                                    for (int i = 0; i < splitTimes.Length; i++)
                                    {
                                        //add in reverse so we get Seconds first
                                        if (i < 3) durTimes[i] = splitTimes[splitTimes.Length - 1 - i].PadLeft(2, '0'); ;
                                    }

                                    try
                                    {
                                        int secs = Convert.ToInt32(durTimes[0]);
                                        int mins = Convert.ToInt32(durTimes[1]);
                                        int hours = Convert.ToInt32(durTimes[2]);

                                        if (secs >= 60)
                                        {
                                            int temp = Convert.ToInt32(secs / 60);
                                            secs = secs - Convert.ToInt32(secs / 60) * 60;
                                            mins = mins + temp;
                                        }
                                        if (mins >= 60)
                                        {
                                            int temp = Convert.ToInt32(mins / 60);
                                            mins = mins - Convert.ToInt32(mins / 60) * 60;
                                            hours = hours + temp;
                                        }

                                        if (hours > 99)
                                        {
                                            EntryError("You cannot search for a call more than 99 hours long.");
                                            duration = "";
                                        }
                                        else
                                        {
                                            duration = hours.ToString() + ":" + mins.ToString() + ":" + secs.ToString();
                                            //check to make sure it is an accurate convert into an appropriate format
                                            Regex reg = new Regex("^\\d{0,2}\\:[0-5]{0,1}[0-9]{0,1}\\:[0-5]{0,1}[0-9]{0,1}$");
                                            if (!reg.IsMatch(duration))
                                            {
                                                EntryError("The duration format is not recognised and will be ignored in this search.");
                                                duration = "";
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        EntryError("The duration format is not recognised and will be ignored in this search.");
                                        duration = "";
                                    }
                                }
                                else duration = "";
                                durationCombo = cbDuration.SelectedItem.ToString();
                            }
                            if (cbAnswerTime.SelectedItem != null)
                            {
                                if (tbAnswerTime.Text != String.Empty)
                                {
                                    answerTime = tbAnswerTime.Text.Replace("hours", "::").Replace("minutes", ":").Replace("hour", "::").Replace("minute", ":").Replace("hrs", "::").Replace("mins", ":").Replace("hr", "::").Replace("min", ":").Replace("h", "::").Replace("m", ":").Replace("seconds", "").Replace("second", "").Replace("secs", "").Replace("s", "").Replace("and", "").Replace("+", "").Replace("-", ":").Replace(",", "").Replace(" ", "");

                                    string[] splitTimes = answerTime.Split(':');
                                    string[] ansTimes = { "00", "00", "00" };

                                    for (int i = 0; i < splitTimes.Length; i++)
                                    {
                                        //add in reverse so we get Seconds first
                                        if (i < 3) ansTimes[i] = splitTimes[splitTimes.Length - 1 - i].PadLeft(2, '0'); ;
                                    }

                                    try
                                    {
                                        int secs = Convert.ToInt32(ansTimes[0]);
                                        int mins = Convert.ToInt32(ansTimes[1]);
                                        int hours = Convert.ToInt32(ansTimes[2]);

                                        if (secs >= 60)
                                        {
                                            int temp = Convert.ToInt32(secs / 60);
                                            secs = secs - Convert.ToInt32(secs / 60) * 60;
                                            mins = mins + temp;
                                        }
                                        if (mins >= 60)
                                        {
                                            int temp = Convert.ToInt32(mins / 60);
                                            mins = mins - Convert.ToInt32(mins / 60) * 60;
                                            hours = hours + temp;
                                        }

                                        if (hours > 99)
                                        {
                                            EntryError("The Time to Answer cannot be more than 99 hours long.");
                                            answerTime = "";
                                        }
                                        else
                                        {
                                            answerTime = hours.ToString() + ":" + mins.ToString() + ":" + secs.ToString();
                                            //check to make sure it is an accurate convert into an appropriate format
                                            Regex reg = new Regex("^\\d{0,2}\\:[0-5]{0,1}[0-9]{0,1}\\:[0-5]{0,1}[0-9]{0,1}$");
                                            if (!reg.IsMatch(answerTime))
                                            {
                                                EntryError("The Time to Answer value is not recognised and will be ignored for this search.");
                                                answerTime = "";
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        EntryError("The Time to Answer value is not recognised and will be ignored for this search.");
                                        answerTime = "";
                                    }
                                }
                                else answerTime = "";

                                answerTimeCombo = cbAnswerTime.SelectedItem.ToString();
                            }
                        }
                        if (IsNumber(tbFrom.Text)) callerIdentifier = tbFrom.Text.Replace(" ", ""); //should be a number for extension or number and text for name
                        else callerIdentifier = tbFrom.Text;
                        if (!fromExactMatch.Checked) callerExact = "%";
                        if (IsNumber(tbTo.Text)) receiverIdentifier = tbTo.Text.Replace(" ", "");
                        else receiverIdentifier = tbTo.Text;
                        if (!toExactMatch.Checked) receiverExact = "%";

                        /* #### Run the search #### */
                        errors = manager.CreateConnection(startDateValue, endDateValue, callDirection, duration, durationCombo, answerTime, answerTimeCombo, callerIdentifier, callerExact, receiverIdentifier, receiverExact, dialledNumber, MiConfig.GetRecordLimit(), _connectionString, _provider);
                    }
                    else
                    {
                        //get everything if not doing a search (today's date)
                        errors = manager.CreateConnection(DateAndTime.Now.ToString("yyyy-MM-dd"), "", "", "", "", "", "", "", "", "", "", "", MiConfig.GetRecordLimit(), _connectionString, _provider);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(new ValidationError("Unexpected Error: " + ex.ToString()));
                }
            }
            else
            {
                errors.Add(new ValidationError("No connection string provided. Please check the Application configuration."));
            }

            if (errors.Count == 0)
            {
                exportData = null;
                DataSet ds = manager.Calls;

                if (ds.Tables[0].Rows.Count >= MiConfig.GetRecordLimit())
                {
                    MessageBox.Show("Your search has been limited to " + MiConfig.GetRecordLimit().ToString() + " records. \nPlease refine your search parameters or change the maximum call record setting in the application settings.", "Maximum Records Reached", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                if (ds.Tables[0].Rows.Count > 0)
                {
                    ds.Tables[0].Columns[2].ColumnName = "Answer Time"; // rename the Time to Answer column
                    ds.Tables[0].Columns[4].ColumnName = "Call Direction"; // rename the Direction column
                    ds.Tables[0].Columns[5].ColumnName = "Caller Name"; // rename the Caller Name column -- dont know why it is changing
                    ds.Tables[0].Columns[8].ColumnName = "Dialled Number"; // rename the Dialled Extension column

                    DataSet exportDs = ds.Copy();

                    exportDs.Tables[0].Columns.RemoveAt(11); // Remove Receiver Ext column ####Reverse order is important
                    exportDs.Tables[0].Columns.RemoveAt(8); // hide the Dialled Number column
                    exportDs.Tables[0].Columns.RemoveAt(7); // Remove Caller Ext column
                    exportDs.Tables[0].Columns.RemoveAt(3); // Remove Type column

                    exportData = exportDs;// set the global export data var

                    dataGridView.DataSource = ds.Tables[0];

                    dataGridView.Columns[3].Visible = false; // hide the Type column
                    dataGridView.Columns[7].Visible = false; // hide the Caller Ext column
                    dataGridView.Columns[8].Visible = false; // hide the Dialled Number column
                    dataGridView.Columns[11].Visible = false; // hide the Receiver Ext column

                    dataGridView.Columns[13].DefaultCellStyle.Format = "C";

                    FilterDataGridView(); // colour the rows

                    bnExport.Enabled = true;
                }
                else
                {
                    DataTable dt = new DataTable();
                    dt.Columns.Add("No Results");
                    dataGridView.DataSource = dt;
                }

                PopulateFields(manager); //calculate the call costs
            }
            else
            {
                miLog(LogEntryType.Information, SourceType.Search, "---------"); // empty line to seperate these errors from other
                UnexpectedError("Errors have occured with the Search.");
                foreach (ValidationError error in errors)
                {
                    miLog(LogEntryType.Error, SourceType.Search, "Error with Search: " + error.GetMesssage());
                }
            }
            this.Cursor = Cursors.Default;
        }

        private void FilldgvLatest4()
        {
            this.Cursor = Cursors.WaitCursor;
            List<ValidationError> errors = new List<ValidationError>();

            CallManager manager1 = new CallManager(_connectionString, _provider);
            string duration;
            //Get the Long Call Definition
            if (tb_LongCallDefinition.Text != String.Empty)
            {
                //Remove all other possible entries and replace with proper format
                duration = tb_LongCallDefinition.Text.Replace("hours", "::").Replace("minutes", ":").Replace("hour", "::").Replace("minute", ":").Replace("hrs", "::").Replace("mins", ":").Replace("hr", "::").Replace("min", ":").Replace("h", "::").Replace("m", ":").Replace("seconds", "").Replace("second", "").Replace("secs", "").Replace("s", "").Replace("and", "").Replace("+", "").Replace("-", ":").Replace(",", "").Replace(" ", "");

                string[] splitTimes = duration.Split(':');
                string[] durTimes = { "00", "00", "00" };

                for (int i = 0; i < splitTimes.Length; i++)
                {
                    //add in reverse so we get Seconds first
                    if (i < 3) durTimes[i] = splitTimes[splitTimes.Length - 1 - i].PadLeft(2, '0'); ;
                }

                try
                {
                    int secs = Convert.ToInt32(durTimes[0]);
                    int mins = Convert.ToInt32(durTimes[1]);
                    int hours = Convert.ToInt32(durTimes[2]);

                    if (secs >= 60)
                    {
                        int temp = Convert.ToInt32(secs / 60);
                        secs = secs - Convert.ToInt32(secs / 60) * 60;
                        mins = mins + temp;
                    }
                    if (mins >= 60)
                    {
                        int temp = Convert.ToInt32(mins / 60);
                        mins = mins - Convert.ToInt32(mins / 60) * 60;
                        hours = hours + temp;
                    }

                    if (hours > 99)
                    {
                        EntryError("You cannot search for a call more than 99 hours long.");
                        duration = "";
                    }
                    else
                    {
                        duration = hours.ToString() + ":" + mins.ToString() + ":" + secs.ToString();
                        //check to make sure it is an accurate convert into an appropriate format
                        Regex reg = new Regex("^\\d{0,2}\\:[0-5]{0,1}[0-9]{0,1}\\:[0-5]{0,1}[0-9]{0,1}$");
                        if (!reg.IsMatch(duration))
                        {
                            EntryError("The duration format is not recognised and the default of 5 minutes will be used.");
                            duration = "00:05:00";
                        }
                    }
                }
                catch (Exception ex)
                {
                    EntryError("The duration format is not recognised and the default of 5 minutes will be used.");
                    duration = "00:05:00";
                }
            }
            else duration = "00:05:00";
            errors = manager1.CreateConnection("", "", "Outgoing", duration, "greater than", "", "", "", "", "", "", "", 5, _connectionString, _provider);
            if (errors.Count == 0)
            {
                DataSet ds = manager1.Calls;
                if (ds.Tables[0].Rows.Count > 0)
                {
                    dgvLatest4.DataSource = ds.Tables[0];

                    /*
                    0 - Call Date
                    1 - Duration
                    2 - Answer Time
                    3 - Type
                    4 - Direction
                    5 - Caller Name
                    6 - Caller Number
                    7 - Caller Extension
                    8 - Dialled Digits
                    9 - Receiver Name
                    10 - Receiver Number
                    11 - Receiver Extension
                    12 - Filter
                    13 - Cost
                    */
                    dgvLatest4.Columns[0].Visible = true; // show the Call Date column
                    dgvLatest4.Columns[1].Visible = true; // show the Duration column
                    dgvLatest4.Columns[2].Visible = true; // hide the Answer Time column
                    dgvLatest4.Columns[3].Visible = false; // hide the Type column
                    dgvLatest4.Columns[4].Visible = false; // hide the Direction column
                    dgvLatest4.Columns[5].Visible = false; // hide the Caller Name column
                    dgvLatest4.Columns[6].Visible = true; // show the Caller Number column
                    dgvLatest4.Columns[7].Visible = false; // hide the Caller Ext column
                    dgvLatest4.Columns[8].Visible = false; // hide the Dialled Digits column
                    dgvLatest4.Columns[9].Visible = false; // hide the Receiver Name column
                    dgvLatest4.Columns[10].Visible = true; // show the Receiver Number column
                    dgvLatest4.Columns[11].Visible = false; // hide the Receiver Ext column
                    dgvLatest4.Columns[12].Visible = false; // hide the Filter column
                    dgvLatest4.Columns[13].Visible = true; // show the Cost column

                    dgvLatest4.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCellsExceptHeader;

                    //ADD COMMAND TO TIDY UP AND ADD PRIVATE / UNKNOWN

                    dgvLatest4.Columns[13].DefaultCellStyle.Format = "C";
                    FilterDataGridView(dgvLatest4); // colour the rows
                }
                else
                {
                    DataTable dt = new DataTable();
                    dt.Columns.Add("No Results");
                    dgvLatest4.DataSource = dt;
                }
            }
            else
            {
                miLog(LogEntryType.Information, SourceType.Search, "---------"); // empty line to seperate these errors from other
                UnexpectedError("Errors have occured with the Search.");
                foreach (ValidationError error in errors)
                {
                    miLog(LogEntryType.Error, SourceType.Search, "Error with Search: " + error.GetMesssage());
                }
            }
            this.Cursor = Cursors.Default;
        }

        private void GetSummariesData()
        {

            summaries_PeriodGroup.Enabled = false;
            BackgroundWorker summariesWorker;
            summariesWorker = new BackgroundWorker();
            summariesWorker.DoWork += new DoWorkEventHandler(summariesWorker_DoWork);
            summariesWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(summariesWorker_RunWorkerCompleted);
            summariesWorker.RunWorkerAsync();

            FilldgvLatest4();

            this.Cursor = Cursors.WaitCursor;
            List<ValidationError> errors = new List<ValidationError>();
            CallManager manager2 = new CallManager(_connectionString, _provider);
            errors = manager2.CreateConnection("", "", "Outgoing", "", "", "", "", "", "", "", "", "", 5, _connectionString, _provider);
            if (errors.Count == 0)
            {
                DataSet ds = manager2.Calls;
                if (ds.Tables[0].Rows.Count > 0)
                {
                    dgvLatest2.DataSource = ds.Tables[0];

                    /*
                    0 - Call Date
                    1 - Duration
                    2 - Answer Time
                    3 - Type
                    4 - Direction
                    5 - Caller Name
                    6 - Caller Number
                    7 - Caller Extension
                    8 - Dialled Digits
                    9 - Receiver Name
                    10 - Receiver Number
                    11 - Receiver Extension
                    12 - Filter
                    13 - Cost
                    */
                    dgvLatest2.Columns[0].Visible = true; // show the Call Date column
                    dgvLatest2.Columns[1].Visible = false; // hide the Duration column
                    dgvLatest2.Columns[2].Visible = false; // hide the Answer Time column
                    dgvLatest2.Columns[3].Visible = false; // hide the Type column
                    dgvLatest2.Columns[4].Visible = false; // hide the Direction column
                    dgvLatest2.Columns[5].Visible = false; // hide the Caller Name column
                    dgvLatest2.Columns[6].Visible = true; // show the Caller Number column
                    dgvLatest2.Columns[7].Visible = false; // hide the Caller Ext column
                    dgvLatest2.Columns[8].Visible = false; // hide the Dialled Digits column
                    dgvLatest2.Columns[9].Visible = false; // hide the Receiver Name column
                    dgvLatest2.Columns[10].Visible = true; // show the Receiver Number column
                    dgvLatest2.Columns[11].Visible = false; // hide the Receiver Ext column
                    dgvLatest2.Columns[12].Visible = false; // hide the Filter column
                    dgvLatest2.Columns[13].Visible = true; // show the Cost column

                    //ADD COMMAND TO TIDY UP AND ADD PRIVATE / UNKNOWN

                    dgvLatest2.Columns[13].DefaultCellStyle.Format = "C";
                    FilterDataGridView(dgvLatest2); // colour the rows
                }
                else
                {
                    DataTable dt = new DataTable();
                    dt.Columns.Add("No Results");
                    dgvLatest2.DataSource = dt;
                }
            }
            else
            {
                miLog(LogEntryType.Information, SourceType.Search, "---------"); // empty line to seperate these errors from other
                UnexpectedError("Errors have occured with the Search.");
                foreach (ValidationError error in errors)
                {
                    miLog(LogEntryType.Error, SourceType.Search, "Error with Search: " + error.GetMesssage());
                }
            }

            CallManager manager3 = new CallManager(_connectionString, _provider);
            errors = manager3.CreateConnection("", "", "Incoming", "", "", "****", "<=", "", "", "", "", "", 5, _connectionString, _provider);
            if (errors.Count == 0)
            {
                DataSet ds = manager3.Calls;
                if (ds.Tables[0].Rows.Count > 0)
                {
                    dgvLatest1.DataSource = ds.Tables[0];

                    /*
                    0 - Call Date
                    1 - Duration
                    2 - Answer Time
                    3 - Type
                    4 - Direction
                    5 - Caller Name
                    6 - Caller Number
                    7 - Caller Extension
                    8 - Dialled Digits
                    9 - Receiver Name
                    10 - Receiver Number
                    11 - Receiver Extension
                    12 - Filter
                    13 - Cost
                    */
                    dgvLatest1.Columns[0].Visible = true; // show the Call Date column
                    dgvLatest1.Columns[1].Visible = false; // hide the Duration column
                    dgvLatest1.Columns[2].Visible = false; // hide the Answer Time column
                    dgvLatest1.Columns[3].Visible = false; // hide the Type column
                    dgvLatest1.Columns[4].Visible = false; // hide the Direction column
                    dgvLatest1.Columns[5].Visible = false; // hide the Caller Name column
                    dgvLatest1.Columns[6].Visible = true; // show the Caller Number column
                    dgvLatest1.Columns[7].Visible = false; // hide the Caller Ext column
                    dgvLatest1.Columns[8].Visible = false; // hide the Dialled Digits column
                    dgvLatest1.Columns[9].Visible = false; // hide the Receiver Name column
                    dgvLatest1.Columns[10].Visible = true; // show the Receiver Number column
                    dgvLatest1.Columns[11].Visible = false; // hide the Receiver Ext column
                    dgvLatest1.Columns[12].Visible = false; // hide the Filter column
                    dgvLatest1.Columns[13].Visible = false; // hide the Cost column

                    FilterDataGridView(dgvLatest1); // colour the rows
                    //ADD COMMAND TO TIDY UP AND ADD PRIVATE / UNKNOWN
                }
                else
                {
                    DataTable dt = new DataTable();
                    dt.Columns.Add("No Results");
                    dgvLatest1.DataSource = dt;
                }
            }
            else
            {
                miLog(LogEntryType.Information, SourceType.Search, "---------"); // empty line to seperate these errors from other
                UnexpectedError("Errors have occured with the Search.");
                foreach (ValidationError error in errors)
                {
                    miLog(LogEntryType.Error, SourceType.Search, "Error with Search: " + error.GetMesssage());
                }
            }
            this.Cursor = Cursors.Default;
        }

        private void GetCallSummaryData()
        {
            this.Cursor = Cursors.WaitCursor;
            List<ValidationError> errors = new List<ValidationError>();
            CallManager manager = new CallManager(_connectionString, _provider);

            if (!String.IsNullOrEmpty(_connectionString))
            {
                try
                {
                    //clear Call Details field
                    callInfo.ClearFields();

                    /* ###### Init the different variables we need to search ##### */
                    string startDateValue = DateTime.Now.ToString("yyyy-MM-dd"); //default
                    string endDateValue = "";

                    string callDirection = "Outgoing";

                    string duration = "00:00:00";
                    string durationCombo = "greater than";
                    string answerTime = "";
                    string answerTimeCombo = "";

                    string callerIdentifier = "";
                    string callerExact = "";
                    string receiverIdentifier = "";
                    string receiverExact = "";

                    string dialledNumber = ""; // left out for this version (can add back in for later versions)

                    if (csSingleDate.Checked)
                    {
                        //startDateValue = csDay.Value.ToString("yyyy-MM-dd");
                        if (IsNumber(csDay.Text))
                        {
                            startDateValue = DateAndTime.Now.AddDays(-(Convert.ToInt32(csDay.Text)) + 1).ToString("yyyy-MM-dd");
                            endDateValue = DateAndTime.Now.ToString("yyyy-MM-dd");
                        }
                        else if (csDay.Text == String.Empty) startDateValue = DateAndTime.Now.ToString("yyyy-MM-dd");
                        else
                        {
                            EntryError("Last days value should be a number (searching with default 7 days)");
                            startDateValue = DateAndTime.Now.AddDays(-7 + 1).ToString("yyyy-MM-dd");
                            endDateValue = DateAndTime.Now.ToString("yyyy-MM-dd");
                        }
                    }
                    else if (csDateRange.Checked)
                    {
                        startDateValue = csStartDate.Value.ToString("yyyy-MM-dd");
                        endDateValue = csEndDate.Value.ToString("yyyy-MM-dd");
                    }
                    else if (csAllDates.Checked) startDateValue = "";

                    /* #### Run the search #### */
                    // currently with a max of 100,000 records - ignoring the config limit
                    errors = manager.CreateConnection(startDateValue, endDateValue, callDirection, duration, durationCombo, answerTime, answerTimeCombo, callerIdentifier, callerExact, receiverIdentifier, receiverExact, dialledNumber, 100000, _connectionString, _provider);
                }
                catch (Exception ex)
                {
                    errors.Add(new ValidationError("Unexpected Error: " + ex.ToString()));
                }
            }
            else
            {
                errors.Add(new ValidationError("No connection string provided. Please check the Application configuration."));
            }

            if (errors.Count == 0)
            {
                callSummaryReport.LocalReport.DataSources.Clear();

                DataTable callDataTable = manager.Calls.Tables[0];
                /*
                if (callDataTable.Rows.Count >= MiConfig.GetRecordLimit())
                {
                    MessageBox.Show("Your search has been limited to " + MiConfig.GetRecordLimit().ToString() + " records. \nPlease refine your search parameters or change the maximum call record setting in the application settings.", "Maximum Records Reached", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }*/

                //clean up table for the reportviewer

                callDataTable.Columns[0].ColumnName = "Call_Date";
                callDataTable.Columns[1].ColumnName = "Duration";
                callDataTable.Columns[2].ColumnName = "Answer_Time";
                callDataTable.Columns[3].ColumnName = "Type";
                callDataTable.Columns[4].ColumnName = "Call_Direction";
                callDataTable.Columns[5].ColumnName = "Caller_Name";
                callDataTable.Columns[6].ColumnName = "Caller_Number"; //after 4,3 and 2 are removed then this becomes Column #3
                callDataTable.Columns[7].ColumnName = "Caller_Extension";
                callDataTable.Columns[8].ColumnName = "Dialled_Number";
                callDataTable.Columns[9].ColumnName = "Receiver_Name";
                callDataTable.Columns[10].ColumnName = "Receiver_Number";
                callDataTable.Columns[11].ColumnName = "Receiver_Extension";
                callDataTable.Columns[12].ColumnName = "Filter_Name";
                callDataTable.Columns[13].ColumnName = "Cost";


                //#### Removing in REVERSE order is important
                callDataTable.Columns.RemoveAt(12);
                callDataTable.Columns.RemoveAt(11);
                callDataTable.Columns.RemoveAt(8);
                callDataTable.Columns.RemoveAt(7);
                callDataTable.Columns.RemoveAt(4);
                callDataTable.Columns.RemoveAt(3);
                callDataTable.Columns.RemoveAt(2);


                callDataTable.TableName = "CallDataTable";

                DataSet callData = callDataTable.DataSet;

                callData.DataSetName = "CallData";

                Rdlc report = new Rdlc(callData);

                callSummaryReport.LocalReport.DataSources.Add(new Microsoft.Reporting.WinForms.ReportDataSource(callData.DataSetName, callDataTable));
                callSummaryReport.LocalReport.LoadReportDefinition(report.GetRdlcStream(3)); // grouped by Caller Number

                //callSummaryReport.LocalReport.ReportPath = "../../CallSummary.rdlc";

                callSummaryReport.RefreshReport();


                //PopulateFields(false); //calculate the call costs
                //callSummaryReport.LocalReport.DataSources.Clear();
                //callSummaryReport.LocalReport.DataSources.Add(new Microsoft.Reporting.WinForms.ReportDataSource("Calls_callSummary",manager2.Calls.Tables[0]));

                //ReportDataSource re = new ReportDataSource();
                //callSummaryReport.RefreshReport();
            }
            else
            {
                miLog(LogEntryType.Information, SourceType.Search, "---------"); // empty line to seperate these errors from other
                UnexpectedError("Errors have occured with the Search.");
                foreach (ValidationError error in errors)
                {
                    miLog(LogEntryType.Error, SourceType.Search, "Error with Search: " + error.GetMesssage());
                }
            }
            this.Cursor = Cursors.Default;
        }

        private void GetAddressBookData(string selectedID)
        {
            try
            {
                PhoneNumberManager numberManager = new PhoneNumberManager(_connectionString, _provider);
                DataSet numberData;


                // get all
                numberData = numberManager.GetPhoneNumberData();


                DataTable newTable = numberData.Tables[0];
                addressDataGrid.DataSource = null;
                addressDataGrid.DataSource = newTable;

                addressDataGrid.Columns[2].Visible = false; // hide the NumberDesc column
                addressDataGrid.Columns[3].Visible = false; // hide the Type column
                addressDataGrid.Columns[4].Visible = false; // hide the PersonID column
                addressDataGrid.Columns[5].Visible = false; // hide the Email column
                addressDataGrid.Columns[6].Visible = false; // hide the PersonDesc column
                addressDataGrid.Columns[7].Visible = false; // hide the NumberID column

                if (selectedID != String.Empty)
                {
                    for (int i = 0; i < addressDataGrid.Rows.Count; i++)
                    {
                        //MessageBox.Show(numbersGrid.Rows[i].Cells[1].Value.ToString() +" == "+ selectID);
                        if (addressDataGrid.Rows[i].Cells[4].Value.ToString() == selectedID) //compare the phonenumber column with prev selected phonenumber
                        {
                            //MessageBox.Show("WORKING");
                            addressDataGrid.Rows[i].Selected = true;
                            break; // drop out so we dont still loop
                        }
                    }
                }
                //gets here if unable to select a row or it was empty
                //numbersGrid.Rows[0].Selected = true; //select the first row
            }
            catch (Exception ex)
            {
                miLog(LogEntryType.Information, SourceType.MiSMDR, "---------"); // empty line to seperate these errors from other
                UnexpectedError("Errors have occured retrieving the Phone Numbers Information.");
                miLog(LogEntryType.Error, SourceType.MiSMDR, "Unexpected Error: " + ex.ToString());
            }
        }

        private void GetCallCostData(string selectID)
        {
            try
            {
                CallCategoryManager callCostManager = new CallCategoryManager(_connectionString, _provider);
                DataSet callCostData;

                // get all
                callCostData = callCostManager.GetCallCostData();


                // now assign the retrieved DataSet to the grid
                DataTable newTable = callCostData.Tables[0];
                callCostsGrid.DataSource = null;
                callCostsGrid.DataSource = newTable;
                callCostsGrid.Columns[0].Visible = false; // hide the ID column
                callCostsGrid.Columns[3].Visible = false; // hide the original regex column
                callCostsGrid.Columns[7].Visible = false; // hide the Type column
                callCostsGrid.Columns[8].Visible = false; // hide the Priority column

                try
                {
                    callCostsGrid.Columns[2].Width = 200; // change the Filter column width
                }
                catch (Exception ex)
                {
                    //ignore failure as it doesnt matter
                }

                for (int i = 0; i < callCostsGrid.Rows.Count; i++)
                {
                    //select the row if needed
                    if (selectID != String.Empty)
                    {
                        if (callCostsGrid.Rows[i].Cells[0].Value.ToString() == selectID)
                        {
                            callCostsGrid.Rows[i].Selected = true;
                        }
                    }
                    //compile the value of the new row
                    if (callCostsGrid.Rows[i].Cells[7].Value.ToString() == "starts")
                    {
                        callCostsGrid.Rows[i].Cells[2].Value = "Starts With: " + callCostsGrid.Rows[i].Cells[3].Value.ToString().Substring(1, callCostsGrid.Rows[i].Cells[3].Value.ToString().Length - 4);
                    }
                    else if (callCostsGrid.Rows[i].Cells[7].Value.ToString() == "exact")
                    {
                        callCostsGrid.Rows[i].Cells[2].Value = "Matches: " + callCostsGrid.Rows[i].Cells[3].Value.ToString().Substring(1, callCostsGrid.Rows[i].Cells[3].Value.ToString().Length - 2);
                    }
                    else
                    {
                        callCostsGrid.Rows[i].Cells[2].Value = "Reg. Expression: " + callCostsGrid.Rows[i].Cells[3].Value;
                    }
                    if (callCostsGrid.Rows[i].Cells[9].Value.ToString() == "1") callCostsGrid.Rows[i].Cells[9].Value = "true";
                    else callCostsGrid.Rows[i].Cells[9].Value = "false";
                }

                //gets here if unable to select a row or it was empty
                //callCostsGrid.Rows[0].Selected = true; //select the entire first row
            }
            catch (Exception ex)
            {
                miLog(LogEntryType.Information, SourceType.MiSMDR, "---------"); // empty line to seperate these errors from other
                miLog(LogEntryType.Error, SourceType.MiSMDR, "Unexpected Error: " + ex.ToString());
                UnexpectedError("Errors have occured retrieving the Call Cost Information.");
            }
        }

        private void GetSavedSearches()
        {
            //Refresh the list displayed in the Saved Searches area
            try
            {
                SavedSearchManager manager = new SavedSearchManager(_connectionString, _provider);
                lbSavedSearches.DataSource = manager.GetSavedSearchData().Tables[0];
                lbSavedSearches.DisplayMember = "Name";
                lbSavedSearches.ValueMember = "ID";
            }
            catch (Exception ex)
            {
                UnexpectedError("Unexpected error refreshing the saved searches.");
                miLog(LogEntryType.Information, SourceType.MiSMDR, "---------"); // empty line to seperate these errors from other
                miLog(LogEntryType.Error, SourceType.MiSMDR, "Unexpected Error refreshing saved searches: " + ex.ToString());
            }
        }

        #endregion

        #region Form Clearing Functions
        private void ClearCallCostFields()
        {
            tbCCName.Text = "";
            tbCCBlockSize.Text = "";
            tbCCConnCost.Text = "";
            tbCCRateBlock.Text = "";
            tbCallCostID.Text = "";
            tbCCStartsWith.Text = "";
            tbCCExactMatch.Text = "";
            tbCCRegEx.Text = "";
            rbCCStartsWith.Checked = true;
        }
        private void ClearSearchFields()
        {
            rbSingleDate.Checked = true;
            DayFilter.Text = "7";
            StartDateFilter1.ResetText();
            EndDateFilter1.ResetText();
            tbAnswerTime.Text = "";
            cbUnanswered.Checked = false;
            tbDuration.Text = "";
            cbAnswerTime.SelectedIndex = 0;
            cbCallCategory.SelectedIndex = 0;
            cbDuration.SelectedIndex = 0;
            tbFrom.Text = "";
            fromExactMatch.Checked = false;
            tbTo.Text = "";
            toExactMatch.Checked = false;
        }
        private void ClearCallSummaryFields()
        {
            csDay.Text = "7";
            csStartDate.ResetText();
            csEndDate.ResetText();
            csSingleDate.Checked = true;
        }
        private void ClearAddressBookFields()
        {
            tbContactName.Text = "";
            tbContactNumber.Text = "";
        }
        #endregion

        #region Event Handlers

        #region General Application Events
        private void MiForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.None)
            {
                e.Cancel = true; // this cancels the close event.
            }
            else
            {
                notifyIcon1.Visible = false;
                notifyIcon1.Dispose();
                SaveWindowState(); // save window size and location
                Disconnect();
            }
        }

        #endregion

        #region Mitel Connection Events

        private void callCountTimer_Tick(object sender, EventArgs e)
        {
            if (mitelManager != null)
            {
                if (_connected)
                {
                    int callCount = mitelManager.CallCount;
                    SetStatusText("Mitel Update: There were", callCount.ToString(), "calls retrieved from the Mitel database in the last " + callCountTimerInterval.ToString() + " seconds.");
                    WriteToApplicationLog("Mitel Update: There were " + callCount.ToString() + " calls retrieved from the Mitel database in the last " + callCountTimerInterval.ToString() + " seconds.");

                    if (MiConfig.GetShowNotifications()) notifyIcon1.ShowBalloonTip(500, "MiSMDR Update", callCount.ToString() + " calls retrieved", ToolTipIcon.Info);
                }
            }
        }

        private void mitelConnectionTimer_Tick(object sender, EventArgs e)
        {
            if (mitelManager != null)
            {
                if (!_connected)
                {
                    lbStatus.Text = "Unable to Connect";
                    lbStatus.ForeColor = Color.OrangeRed;
                    SetStatusText("Unable to connect to the Mitel server specified", "", "");
                }
                mitelConnectionTimer.Stop();
            }
        }

        #endregion

        #region Debug Log Form Events
        private void bnExport_Click(object sender, EventArgs e)
        {
            exportSaveDialog.FileName = "[" + DateTime.Now.ToString("ddMMyyyy") + "] MiSMDR_Search_Data.csv";
            exportSaveDialog.Filter = "Comma Seperated Value Files (*.csv)|*.csv|All Files (*.*)|*.*";
            DialogResult exportDialog = exportSaveDialog.ShowDialog();

            if (exportDialog == DialogResult.OK)
            {
                _exportPath = exportSaveDialog.FileName;

                Exporter exporter = new Exporter();

                DataTable dt = exportData.Tables[0];

                string result = exporter.Export(dt, _exportPath);
                if (result != null)
                {
                    string errorMessage = "Failed to export data: " + result;
                    WriteToApplicationLog("[EXPORT]: " + errorMessage);
                    LogManager.Log(LogEntryType.Error, SourceType.MiSMDR, errorMessage);
                    MessageBox.Show(errorMessage, "Export Failure", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    WriteToApplicationLog("[EXPORT]: Successfully exported MiSMDR call data to " + _exportPath);
                }
            }
        }

        private void bnClearLog_Click(object sender, EventArgs e)
        {
            tbLog.Text = "";
        }

        private void bnSaveLog_Click(object sender, EventArgs e)
        {
            DialogResult save = saveFileDialog.ShowDialog();

            if (save == DialogResult.OK)
            {
                string result = FileManager.Save(saveFileDialog.FileName, tbLog.Text);

                if (result == String.Empty)
                {
                    MessageBox.Show("File saved.", "Save", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    LogManager.Log(LogEntryType.Information, SourceType.MiSMDR, "MiSMDR internal application log file saved by user.");
                }
                else
                {
                    MessageBox.Show("Error: Could not save the file. " + result, "Save", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    LogManager.Log(LogEntryType.Error, SourceType.MiSMDR, "MiSMDR could not save the internal log file as requested by the user. " + result);
                }
            }
        }
        #endregion

        #region Data Grid Events

        private void dataGridView_SelectionChanged(object sender, EventArgs e)
        {
            // Display the current selected call data in the CallInfo field
            try
            {
                // Select the whole row
                if (dataGridView.SelectedCells.Count > 0)
                {
                    dataGridView.Rows[dataGridView.SelectedCells[0].RowIndex].Selected = true;

                    DataGridViewRow row = dataGridView.Rows[dataGridView.SelectedCells[0].RowIndex];
                    callInfo.CallDate = row.Cells[0].Value.ToString();
                    callInfo.Duration = row.Cells[1].Value.ToString();
                    callInfo.TimeToAnswer = row.Cells[2].Value.ToString();
                    callInfo.Direction = row.Cells[4].Value.ToString();

                    callInfo.CallerName = row.Cells[5].Value.ToString();
                    callInfo.CallerNumber = row.Cells[6].Value.ToString();

                    callInfo.ReceiverName = row.Cells[9].Value.ToString();
                    callInfo.ReceiverNumber = row.Cells[10].Value.ToString();
                }
            }
            catch (Exception ex)
            {
                UnexpectedError("Unexpected error occurred when selecting the call record.");
                miLog(LogEntryType.Information, SourceType.MiSMDR, "---------"); // empty line to seperate these errors from other
                miLog(LogEntryType.Error, SourceType.MiSMDR, "Unexpected error occurred when selecting the call record: " + ex.ToString());
            }
        }
        private void dataGridView_Sorted(object sender, EventArgs e)
        {
            FilterDataGridView();
        }
        private void dataGridView_RowsAdded(object sender, DataGridViewRowsAddedEventArgs e)
        {
            ColourGridView();
        }
        private void addressDataGrid_SelectionChanged(object sender, EventArgs e)
        {
            try
            {
                if (addressDataGrid.SelectedCells.Count > 0)
                {
                    // Select the whole row
                    addressDataGrid.Rows[addressDataGrid.SelectedCells[0].RowIndex].Selected = true;
                    tbSelectedContactID.Text = "";
                    tbOldName.Text = "";
                    tbOldNumber.Text = "";

                    //only copy the data into the fields if it is not a blank row
                    if (addressDataGrid.SelectedCells[0].Value.ToString() != String.Empty)
                    {
                        // Get the current selected row (based on the selected cell) and 
                        // set the text box values to the specified cell values.
                        DataGridViewRow row = addressDataGrid.Rows[addressDataGrid.SelectedCells[0].RowIndex];
                        tbContactName.Text = row.Cells[0].Value.ToString(); // personname
                        tbContactNumber.Text = row.Cells[1].Value.ToString(); //number
                        string type = row.Cells[3].Value.ToString(); //type
                        tbSelectedContactID.Text = row.Cells[4].Value.ToString(); // personid for updating
                        tbNumberID.Text = row.Cells[7].Value.ToString(); // personid for updating
                        tbOldNumber.Text = row.Cells[1].Value.ToString(); //number for updating
                        tbOldName.Text = row.Cells[0].Value.ToString(); // personname for updating
                        tbOldType.Text = row.Cells[3].Value.ToString(); //type for updating
                        if (type == "Internal") cbContactType.SelectedIndex = 0;
                        else cbContactType.SelectedIndex = 1;
                    }
                }
                CheckAddressBook();
            }
            catch (Exception ex)
            {
                UnexpectedError("Unexpected error occured when selecting the Contact.");
                miLog(LogEntryType.Information, SourceType.MiSMDR, "---------"); // empty line to seperate these errors from other
                miLog(LogEntryType.Error, SourceType.MiSMDR, "Unexpected error occured when selecting the Contact: " + ex.ToString());
            }
        }
        private void callCostsGrid_SelectionChanged(object sender, EventArgs e)
        {
            try
            {
                if (callCostsGrid.SelectedCells.Count > 0)
                {
                    // Select the whole row
                    callCostsGrid.Rows[callCostsGrid.SelectedCells[0].RowIndex].Selected = true;
                    tbCallCostID.Text = "";

                    if (callCostsGrid.SelectedCells[0].Value.ToString() != String.Empty)
                    {
                        ClearCallCostFields();
                        // Get the current selected row (based on the selected cell) and 
                        // set the text box values to the specified cell values.
                        DataGridViewRow row = callCostsGrid.Rows[callCostsGrid.SelectedCells[0].RowIndex];
                        tbCallCostID.Text = row.Cells[0].Value.ToString(); // used for updating
                        tbCCRegEx.Text = row.Cells[3].Value.ToString();
                        tbCCName.Text = row.Cells[1].Value.ToString();
                        tbCCBlockSize.Text = row.Cells[4].Value.ToString();
                        tbCCRateBlock.Text = row.Cells[5].Value.ToString();
                        tbCCConnCost.Text = row.Cells[6].Value.ToString();
                        if ((row.Cells[9].Value.ToString() == "1") || (row.Cells[9].Value.ToString() == "true")) cbCCChargeUnfinished.Checked = true;
                        else cbCCChargeUnfinished.Checked = false;
                        if (row.Cells[7].Value.ToString() == "starts")
                        {
                            tbCCStartsWith.Text = tbCCRegEx.Text.Substring(1, tbCCRegEx.Text.Length - 4); // remove ^ from start and \d* from the end
                            rbCCStartsWith.Checked = true;
                        }
                        else if (row.Cells[7].Value.ToString() == "exact")
                        {
                            tbCCExactMatch.Text = tbCCRegEx.Text.Substring(1, tbCCRegEx.Text.Length - 2); //remove the ^ from start and $ from the end
                            rbCCExactMatch.Checked = true;
                        }
                        else rbCCRegEx.Checked = true;
                    }
                }
                CheckCallCosts();
            }
            catch (Exception ex)
            {
                UnexpectedError("Unexpected error occured when selecting the Call Cost.");
                miLog(LogEntryType.Information, SourceType.MiSMDR, "---------"); // empty line to seperate these errors from other
                miLog(LogEntryType.Error, SourceType.MiSMDR, "Unexpected error occured when selecting the Call Cost: " + ex.ToString());
            }
        }
        #endregion

        #region Database Connection Events
        private void bnConnect_Click(object sender, EventArgs e)
        {
            try
            {
                //Check the field are not empty
                if (tbServer.Text != String.Empty)
                {
                    if (tbPort.Text != String.Empty)
                    {
                        //If there are no problems then change the App settings
                        MiConfig.SetServer(tbServer.Text);
                        MiConfig.SetPort(Convert.ToInt32(tbPort.Text));

                        Connect(); //attempt to connect to the server
                        //SetupCallCountTimer(); //this should be automatic...
                        //callCountTimer.Start();
                        CheckDashboard(); //check the button statuses on the Dashboard
                    }
                    else EntryError("Port field cannot be blank");
                }
                else EntryError("Server field cannot be blank");
            }
            catch (Exception ex)
            {
                UnexpectedError("Unexpected error occured when trying to connect to the specified server.");
                miLog(LogEntryType.Information, SourceType.MiSMDR, "---------"); // empty line to seperate these errors from other
                miLog(LogEntryType.Error, SourceType.MiSMDR, "Unexpected error occured when trying to connect to the specified server: " + ex.ToString());
            }
        }

        private void bnDisconnect_Click(object sender, EventArgs e)
        {
            try
            {
                Disconnect();

                ConnectionSuccessful(mitelManager.Connected);

                CheckDashboard();
            }
            catch (Exception ex)
            {
                UnexpectedError("Unexpected error occured when trying to disconnect from the server");
                miLog(LogEntryType.Information, SourceType.MiSMDR, "---------"); // empty line to seperate these errors from other
                miLog(LogEntryType.Error, SourceType.MiSMDR, "Unexpected error occured when trying to disconnect from the server: " + ex.ToString());
            }
        }
        #endregion

        #region Top Menu Events

        private void rawDataToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                ProcessManager processManager = new ProcessManager();
                //processManager.StartProcess(Properties.Settings.Default["LogFile"].ToString());
                processManager.StartProcess(MiConfig.GetLogPath());
            }
            catch (Exception ex)
            {
                UnexpectedError("Unexpected error occured when trying to open the Raw Data file.");
                miLog(LogEntryType.Information, SourceType.MiSMDR, "---------"); // empty line to seperate these errors from other
                miLog(LogEntryType.Error, SourceType.MiSMDR, "Unexpected error occured when trying to open the Raw Data file: " + ex.ToString());
            }
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SettingsForm settings = new SettingsForm(_connectionString, _provider, _demo);
            DialogResult settingsDialog = settings.ShowDialog();

            if (settingsDialog == DialogResult.OK)
            {
                // The user has saved the settings changes so disconnect from the Mitel database,
                // retrieve the server/port information from the Settings file and then re-connect
                // to the Mitel ICP
                CheckDebug();
                CheckDemo(); //re-check all the demo/live settings
                RefreshTabInfo(true); //reget everything
            }
        }

        private void viewLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            LogForm logForm = new LogForm(_connectionString, _provider);
            logForm.ShowDialog();
            logForm.Dispose();
        }

        private void quitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void aboutMiSMDRToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutMiSMDRForm aboutForm = new AboutMiSMDRForm();
            aboutForm.ShowDialog();
            aboutForm.Dispose();
        }

        private void regularExpressionsGuideToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RegexHelp regexForm = new RegexHelp();
            regexForm.ShowDialog();
            regexForm.Dispose();
        }

        private void importRawFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ImportForm importf = new ImportForm(_connectionString, _provider);
            importf.ShowDialog();
            importf.Dispose();
        }

        private void applyUpdateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PluginForm plugf = new PluginForm(_connectionString, _provider);
            plugf.ShowDialog();
            plugf.Dispose();
        }

        private void MiSMDRSupportToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProcessStartInfo sInfo = new ProcessStartInfo("http://www.MiSMDR.info/support/");
            Process.Start(sInfo);
        }

        #endregion

        #region Dashboard Tab Events

        private void rbDemoLive_CheckedChanged(object sender, EventArgs e)
        {
            if (rbMMDemo.Checked)
            {
                if (!_demo) // if current mode is Live then we need to swap to Demo
                {
                    DialogResult d = MessageBox.Show("Changing to demo mode will swap to using a Demo Call Records file and will prevent the application from accessing a Mitel server.", "Swap to demo mode?", MessageBoxButtons.OKCancel);
                    if (d == DialogResult.OK)
                    {
                        _demo = true;
                        MiConfig.SetDemoMode(true);

                        string[] pieces = MiConfig.GetConnectionString("MiDemoString").Split(new string[] { ";" }, StringSplitOptions.None);
                        string test = pieces[0].Remove(0, 12);

                        if (test == String.Empty)
                        {
                            string callrec = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                            if (!Directory.Exists(callrec + "\\MiSMDR")) Directory.CreateDirectory(callrec + "\\MiSMDR");
                            callrec += "\\MiSMDR\\MiSMDR_Demo_Call_Records.db";

                            ConnStringer stringer = new ConnStringer();
                            MiConfig.SetConnectionString("MiDemoString", stringer.buildLiteConnectionString(callrec, "3", "True", "False", "", "", false));

                            DBControl control = new DBControl(MiConfig.GetConnectionString("MiDemoString"), _provider);
                            if (!control.CheckDataAccess()) control.CreateTables();
                        }
                    }
                    else
                    {
                        rbMMLive.Checked = true;
                    }
                }
            }
            else
            {
                if (_demo) // if current mode is Demo we need to swap to Live
                {
                    _demo = false;
                    MiConfig.SetDemoMode(false);

                    string[] pieces = MiConfig.GetConnectionString("MiDatabaseString").Split(new string[] { ";" }, StringSplitOptions.None);
                    string test = pieces[0].Remove(0, 12);

                    if (test == String.Empty) //if there is no call record file
                    {
                        string callrec = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        if (!Directory.Exists(callrec + "\\MiSMDR")) Directory.CreateDirectory(callrec + "\\MiSMDR");
                        callrec += "\\MiSMDR\\MiSMDR_Call_Records.db";

                        ConnStringer stringer = new ConnStringer();
                        MiConfig.SetConnectionString("MiDatabaseString", stringer.buildLiteConnectionString(callrec, "3", "True", "False", "", "", false));

                        //Check the database exists
                        DBControl control = new DBControl(MiConfig.GetConnectionString("MiDatabaseString"), MiConfig.GetProvider());
                        if (!control.CheckDataAccess()) control.CreateTables();
                    }
                }
            }
            CheckDemo(); //re-check all the demo settings

            //Clear the Address Book and Call Cost fields and then make the buttons the right state
            ClearAddressBookFields();
            ClearCallCostFields();
            CheckAddressBook();
            CheckCallCosts();
        }

        private void tbWelcomeContent_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            ProcessStartInfo sInfo = new ProcessStartInfo(e.LinkText.ToString());
            Process.Start(sInfo);
        }

        #endregion

        #region Search Tab Events

        private void bnSearch_Click(object sender, EventArgs e)
        {
            GetCallSearchData(true);
        }

        private void searchTab_SizeChanged(object sender, EventArgs e)
        {
            // Force a refresh of the form because the resizing of the tab panel does not work properly
            this.Refresh();
        }

        private void bnSaveCurrentSearch_Click(object sender, EventArgs e)
        {
            try
            {
                string saveResponse = Interaction.InputBox("Please type a name for your Saved Search", "Saved Search Name", "Saved Search Name", 150, 150);
                if (saveResponse != String.Empty)
                {
                    SavedSearch newSearch = new SavedSearch(_connectionString, _provider);

                    // Add the Date fields
                    newSearch.searchObjects.Add(new SearchObject("rbAllDates", rbAllDates.Checked.ToString()));
                    newSearch.searchObjects.Add(new SearchObject("rbSingleDate", rbSingleDate.Checked.ToString()));
                    newSearch.searchObjects.Add(new SearchObject("rbDateRange", rbDateRange.Checked.ToString()));
                    newSearch.searchObjects.Add(new SearchObject("DayFilter", DayFilter.Text));
                    newSearch.searchObjects.Add(new SearchObject("StartDateFilter1", StartDateFilter1.Text));
                    newSearch.searchObjects.Add(new SearchObject("EndDateFilter1", EndDateFilter1.Text));
                    //Add the Call Timing fields
                    newSearch.searchObjects.Add(new SearchObject("cbUnanswered", cbUnanswered.Checked.ToString()));
                    newSearch.searchObjects.Add(new SearchObject("cbAnswerTime", cbAnswerTime.SelectedIndex.ToString()));
                    newSearch.searchObjects.Add(new SearchObject("cbDuration", cbDuration.SelectedIndex.ToString()));
                    newSearch.searchObjects.Add(new SearchObject("tbAnswerTime", tbAnswerTime.Text));
                    newSearch.searchObjects.Add(new SearchObject("tbDuration", tbDuration.Text));
                    // Add the Call Participant fields
                    newSearch.searchObjects.Add(new SearchObject("tbFrom", tbFrom.Text));
                    newSearch.searchObjects.Add(new SearchObject("tbTo", tbTo.Text));
                    newSearch.searchObjects.Add(new SearchObject("toExactMatch", toExactMatch.Checked.ToString()));
                    newSearch.searchObjects.Add(new SearchObject("fromExactMatch", fromExactMatch.Checked.ToString()));
                    //Call Category fields
                    newSearch.searchObjects.Add(new SearchObject("cbCallCategory", cbCallCategory.SelectedIndex.ToString()));


                    if (cbSavedSearchOverwrite.Checked)
                    {
                        // get the ID and name if a saved search has been loaded
                        newSearch.ID = Convert.ToInt32(hiddenSavedSearchID.Text);
                        newSearch.Name = hiddenSavedSearchName.Text;
                        newSearch.Description = ""; // currently no space for description
                        newSearch.Save(true); // Save(boolean updating)
                    }
                    else
                    {
                        // create fresh new saved search
                        //ID will get created automatically
                        newSearch.Name = saveResponse;
                        newSearch.Description = ""; // currently no space for description
                        newSearch.Save(false);
                    }
                    GetSavedSearches();
                }
            }
            catch (Exception ex)
            {
                UnexpectedError("Error has occured when trying to save the Search.");
                miLog(LogEntryType.Error, SourceType.MiSMDR, ex.ToString());
            }
        }

        private void bnLoadSavedSearch_Click(object sender, EventArgs e)
        {
            try
            {
                string searchID = lbSavedSearches.SelectedValue.ToString();
                SavedSearchManager searchManager = new SavedSearchManager(_connectionString, _provider);
                SavedSearch loadedSearch = searchManager.GetSavedSearch(Convert.ToInt32(searchID));
                hiddenSavedSearchID.Text = searchID;
                hiddenSavedSearchName.Text = loadedSearch.Name;
                foreach (SearchObject sObject in loadedSearch.searchObjects)
                {

                    // Add the Date fields
                    if (sObject.objectName == "rbAllDates") rbAllDates.Checked = Convert.ToBoolean(sObject.objectValue);
                    else if (sObject.objectName == "rbSingleDate") rbSingleDate.Checked = Convert.ToBoolean(sObject.objectValue);
                    else if (sObject.objectName == "rbDateRange") rbDateRange.Checked = Convert.ToBoolean(sObject.objectValue);
                    else if (sObject.objectName == "DayFilter") DayFilter.Text = sObject.objectValue.ToString();
                    else if (sObject.objectName == "StartDateFilter1") StartDateFilter1.Value = DateTime.Parse(sObject.objectValue.ToString());
                    else if (sObject.objectName == "EndDateFilter1") EndDateFilter1.Value = DateTime.Parse(sObject.objectValue.ToString());
                    //Add the Call Timing fields
                    else if (sObject.objectName == "cbUnanswered") cbUnanswered.Checked = Convert.ToBoolean(sObject.objectValue);
                    else if (sObject.objectName == "cbAnswerTime") cbAnswerTime.SelectedIndex = Convert.ToInt32(sObject.objectValue);
                    else if (sObject.objectName == "cbDuration") cbDuration.SelectedIndex = Convert.ToInt32(sObject.objectValue);
                    else if (sObject.objectName == "tbAnswerTime") tbAnswerTime.Text = sObject.objectValue.ToString();
                    else if (sObject.objectName == "tbDuration") tbDuration.Text = sObject.objectValue.ToString();
                    // Add the Call Participant fields
                    else if (sObject.objectName == "tbFrom") tbFrom.Text = sObject.objectValue.ToString();
                    else if (sObject.objectName == "tbTo") tbTo.Text = sObject.objectValue.ToString();
                    else if (sObject.objectName == "toExactMatch") toExactMatch.Checked = Convert.ToBoolean(sObject.objectValue);
                    else if (sObject.objectName == "fromExactMatch") fromExactMatch.Checked = Convert.ToBoolean(sObject.objectValue);
                    //Call Category fields
                    else if (sObject.objectName == "cbCallCategory") cbCallCategory.SelectedIndex = Convert.ToInt32(sObject.objectValue);
                }
                //FilterSavedSearchInput();
            }
            catch (Exception ex)
            {
                UnexpectedError("Error has occured when trying to Load the Saved Search.");
                miLog(LogEntryType.Error, SourceType.MiSMDR, ex.ToString());
            }
        }

        private void bnDeleteSavedSearch_Click(object sender, EventArgs e)
        {
            //Delete the selected Saved Search
            if (lbSavedSearches.SelectedIndex > -1)
            {
                DataRowView dbv = (DataRowView)lbSavedSearches.SelectedItem;
                if (MessageBox.Show("Are you sure you want to delete the Saved Search: '" + dbv.Row.ItemArray[1].ToString() + "' (" + lbSavedSearches.SelectedValue.ToString() + ")", "Delete?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    SavedSearchManager manager = new SavedSearchManager(_connectionString, _provider);
                    List<ValidationError> errors = new List<ValidationError>();

                    // Update the selected Phone Number in the database
                    errors = manager.DeleteSavedSearch(Convert.ToInt32(lbSavedSearches.SelectedValue)); //"" for description (later feature)

                    if (errors.Count > 0)
                    {
                        UnexpectedError("Some errors occured while attempting to delete the saved search");
                        foreach (ValidationError error in errors)
                        {
                            miLog(LogEntryType.Error, SourceType.MiSMDR, "Saved Search delete error: " + error.GetMesssage());
                        }
                    }
                    else
                    {
                        GetSavedSearches();
                    }
                }
            }
            else
            {
                EntryError("You must select a Saved Search before you can delete one.");
            }
        }

        private void rbSingleDate_CheckedChanged(object sender, EventArgs e)
        {
            if (rbSingleDate.Checked) DayFilter.Enabled = true;
            else DayFilter.Enabled = false;
        }

        private void rbDateRange_CheckedChanged(object sender, EventArgs e)
        {
            if (rbDateRange.Checked)
            {
                StartDateFilter1.Enabled = true;
                EndDateFilter1.Enabled = true;
            }
            else
            {
                StartDateFilter1.Enabled = false;
                EndDateFilter1.Enabled = false;
            }
        }

        private void bnSClearFields_Click(object sender, EventArgs e)
        {
            ClearSearchFields();
        }

        private void dataGridView_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dataGridView.SelectionMode = DataGridViewSelectionMode.RowHeaderSelect;
                dataGridView.Rows[dataGridView.CurrentCell.RowIndex].Selected = true;
                return;
            }
            else if (e.Button == MouseButtons.Right)
            {
                int row = e.RowIndex;
                int col = e.ColumnIndex;
                if ((row > 0) && (col > 0))
                {
                    dataGridView.SelectionMode = DataGridViewSelectionMode.CellSelect;
                    dataGridView.Rows[row].Cells[col].Selected = true;
                    ShowContextMenu(this.PointToScreen(dataGridView.PointToClient(e.Location)));
                }
            }
        }

        private void mnuCopy_Click(object sender, EventArgs e)
        {
            Clipboard.SetData(DataFormats.Text, dataGridView.CurrentCell.Value.ToString());
        }

        private void cbUnanswered_CheckedChanged(object sender, EventArgs e)
        {
            if (cbUnanswered.Checked)
            {
                if (flagUnansweredWarning == false)
                {
                    //only show this once per app run
                    MessageBox.Show("This option will search for unanswered calls, but does not include calls which are diverted to voice mail.\n To search for calls sent to voice mail, search for calls received by the voicemail extension.", "Unanswered Calls", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    flagUnansweredWarning = true;
                }
                cbAnswerTime.Enabled = false;
                tbAnswerTime.Enabled = false;
                cbDuration.Enabled = false;
                tbDuration.Enabled = false;
                cbCallCategory.SelectedIndex = 1;
                cbCallCategory.Enabled = false;
            }
            else
            {
                cbAnswerTime.Enabled = true;
                tbAnswerTime.Enabled = true;
                cbDuration.Enabled = true;
                tbDuration.Enabled = true;
                cbCallCategory.Enabled = true;
            }
        }

        private void DayFilter_Leave(object sender, EventArgs e)
        {
            if (DayFilter.Text == String.Empty) DayFilter.Text = "7";
        }

        #endregion

        #region Call Summary Tab Events

        private void bnCallSummarySearch_Click(object sender, EventArgs e)
        {
            GetCallSummaryData();
        }

        private void csSingleDay_CheckedChanged(object sender, EventArgs e)
        {
            if (csSingleDate.Checked) csDay.Enabled = true;
            else csDay.Enabled = false;
        }

        private void csDateRange_CheckedChanged(object sender, EventArgs e)
        {
            if (csDateRange.Checked)
            {
                csStartDate.Enabled = true;
                csEndDate.Enabled = true;
            }
            else
            {
                csStartDate.Enabled = false;
                csEndDate.Enabled = false;
            }
        }

        private void csDay_Leave(object sender, EventArgs e)
        {
            if (csDay.Text == String.Empty) csDay.Text = "7";
        }

        #endregion

        #region Address Book Tab Events

        private void bnABCreate_Click(object sender, EventArgs e)
        {
            try
            {
                DataGridViewRow row = new DataGridViewRow();
                if (addressDataGrid.RowCount > 0)
                {
                    //empty fields should be either at the top or the bottom
                    if (addressDataGrid.Rows[addressDataGrid.RowCount - 1].Cells[0].Value.ToString() == String.Empty)
                    {
                        //if last row is blank we select that one
                        addressDataGrid.Rows[addressDataGrid.RowCount - 1].Selected = true;
                    }
                    else if (addressDataGrid.Rows[0].Cells[0].Value.ToString() == String.Empty)
                    {
                        //if first row is blank we select that one
                        addressDataGrid.Rows[0].Selected = true;
                    }
                    //if the last row is blank already we dont need to add another one
                    row = addressDataGrid.Rows[addressDataGrid.SelectedCells[0].RowIndex];
                }
                if ((addressDataGrid.RowCount == 0) || (row.Cells[1].Value.ToString() != String.Empty))
                {
                    DataTable dt = ((DataTable)addressDataGrid.DataSource);
                    DataRow dr = dt.NewRow();
                    dt.Rows.Add(dr);

                    addressDataGrid.DataSource = null;
                    addressDataGrid.DataSource = dt;

                    addressDataGrid.Columns[2].Visible = false; // hide the NumberDesc column
                    addressDataGrid.Columns[3].Visible = false; // hide the Type column
                    addressDataGrid.Columns[4].Visible = false; // hide the PersonID column
                    addressDataGrid.Columns[5].Visible = false; // hide the Email column
                    addressDataGrid.Columns[6].Visible = false; // hide the PersonDesc column
                    addressDataGrid.Columns[7].Visible = false; // hide the NumberID column

                    //make sure it is selected
                    if (addressDataGrid.Rows[addressDataGrid.RowCount - 1].Cells[0].Value.ToString() == String.Empty)
                    {
                        //if last row is blank we select that one
                        addressDataGrid.Rows[addressDataGrid.RowCount - 1].Selected = true;
                    }
                    else if (addressDataGrid.Rows[0].Cells[0].Value.ToString() == String.Empty)
                    {
                        //if first row is blank we select that one
                        addressDataGrid.Rows[0].Selected = true;
                    }
                }
            }
            catch (Exception ex)
            {
                UnexpectedError("Unexpected error occurred when updating the address grid.");
                miLog(LogEntryType.Error, SourceType.MiSMDR, "Unexpected error occurred when updating the address grid: " + ex.ToString());
            }
            ClearAddressBookFields(); //Empty fields when creating a new record
            CheckAddressBook();
        }

        private void bnABUpdate_Click(object sender, EventArgs e)
        {
            PhoneNumberManager manager = new PhoneNumberManager(_connectionString, _provider);
            List<ValidationError> errors = new List<ValidationError>();

            try
            {
                if (tbContactName.Text != String.Empty)
                {
                    string thisNumber = tbContactNumber.Text.Replace("+", "").Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
                    if (IsNumber(thisNumber))
                    {
                        DataGridViewRow row = addressDataGrid.Rows[addressDataGrid.SelectedCells[0].RowIndex];
                        if (row.Cells[1].Value.ToString() != String.Empty) //if selected number field is not blank then we update
                        {
                            // Update the selected Phone Number in the database
                            errors = manager.UpdatePhoneNumber(Convert.ToInt32(tbSelectedContactID.Text), tbContactName.Text, tbOldNumber.Text, thisNumber, cbContactType.SelectedItem.ToString());
                        }
                        else //this is a newly created set of field we need to add the address instead of updating
                        {
                            DataGridViewRowCollection rows = addressDataGrid.Rows;
                            for (int i = 0; i < rows.Count; i++)
                            {
                                if (rows[i].Cells[1].Value.ToString() == tbContactNumber.Text)
                                {
                                    EntryError("This phone number already exists in the Address Book");
                                    return;
                                }
                            }
                            errors = manager.AddPhoneNumber(tbContactName.Text, thisNumber, "External");
                            tbSelectedContactID.Text = manager.GetLastContactID();

                        }
                        if (errors.Count > 0)
                        {
                            UnexpectedError("Some errors occured when attempting to save the contact.");
                            foreach (ValidationError error in errors)
                            {
                                miLog(LogEntryType.Error, SourceType.MiSMDR, "Some errors occured when attempting to save the contact.: " + error.GetMesssage());
                            }
                        }
                        GetAddressBookData(tbSelectedContactID.Text);
                    }
                    else EntryError("Please enter a valid phone number.");
                }
                else EntryError("Please enter a name.");
            }
            catch (Exception ex)
            {
                UnexpectedError("Unexpected error occurred when saving the contact.");
                miLog(LogEntryType.Error, SourceType.MiSMDR, "Unexpected error occurred when saving the contact: " + ex.ToString());
            }
            CheckAddressBook();
        }

        private void bnABDelete_Click(object sender, EventArgs e)
        {
            try
            {
                DialogResult d = MessageBox.Show("Are you sure you want to delete the contact: " + tbOldName.Text + "? This operation cannot be undone.", "Delete Operation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (d == DialogResult.Yes)
                {
                    PhoneNumberManager manager = new PhoneNumberManager(_connectionString, _provider);

                    // Ensure that the user has selected a valid cell/row
                    if (tbNumberID.Text != String.Empty)
                    {
                        List<ValidationError> errors = new List<ValidationError>();
                        // Delete the Call Cost from the database
                        errors = manager.DeletePhoneNumber(Convert.ToInt32(tbNumberID.Text));

                        if (errors.Count == 0)
                        {
                            // Inform the user that the Call Cost has been removed and reset the button text
                            MessageBox.Show("Contact '" + tbOldName.Text + "' has been deleted.", "Delete Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            UnexpectedError("Errors have occured with the Deletion of a Contact.");
                            // Display a list of errors on the error screen
                            foreach (ValidationError error in errors)
                            {
                                miLog(LogEntryType.Error, SourceType.MiSMDR, "Contact Delete Error: " + error.GetMesssage());
                            }
                        }
                        ClearAddressBookFields();
                        GetAddressBookData(""); //none selected because we deleted it
                    }
                }
            }
            catch (Exception ex)
            {
                UnexpectedError("Unexpected error occurred when deleting the contact.");
                miLog(LogEntryType.Error, SourceType.MiSMDR, "Unexpected error occurred when deleting the contact: " + ex.ToString());
            }
            CheckAddressBook();
        }

        private void tbContactNumber_TextChanged(object sender, EventArgs e)
        {
            if (tbContactNumber.Text != String.Empty)
            {
                try
                {
                    string thisNumber = tbContactNumber.Text.Replace("+", "").Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
                    long temp = Convert.ToInt64(thisNumber);
                    contactNumValid = true;
                }
                catch (Exception ex)
                {
                    if (contactNumValid)
                    {
                        EntryError("The phone number field is not valid.");
                        contactNumValid = false;
                    }
                }
            }
        }

        //private void bnImportContacts_Click(object sender, EventArgs e)
        //{
        //    //Process the contacts file depending on the type
        //    GoogleImporter gImport = new GoogleImporter(_connectionString, _provider);

        //    //gImport.importFile(tbImportFile.Text,cbImportOnlyNumbers.Checked);

        //    List<ValidationError> errors = new List<ValidationError>();
        //    errors = gImport.ReadCsv(tbImportFile.Text, cbImportOnlyNumbers.Checked);

        //    if (errors.Count > 0)
        //    {
        //        MessageBox.Show("Some errors occured.\nPlease check the Log (In the File menu) for more details");
        //        foreach (ValidationError error in errors)
        //        {
        //            miLog(LogEntryType.Error, SourceType.MiSMDR, "Add Contact Error: " + error.GetMesssage());
        //        }
        //    }
        //    //GetPhoneNumberData(false, "");
        //    GetContactData(false, "");
        //    //searchCalls(false);
        //}

        #endregion

        #region Call Cost Tab Events

        private void bnCCCreate_Click(object sender, EventArgs e)
        {
            try
            {
                DataGridViewRow row = new DataGridViewRow();
                if (callCostsGrid.RowCount > 0)
                {
                    //empty fields should be either at the top or the bottom
                    if (callCostsGrid.Rows[callCostsGrid.RowCount - 1].Cells[0].Value.ToString() == String.Empty)
                    {
                        //if last row is blank we select that one
                        callCostsGrid.Rows[callCostsGrid.RowCount - 1].Selected = true;
                    }
                    else if (callCostsGrid.Rows[0].Cells[0].Value.ToString() == String.Empty)
                    {
                        //if first row is blank we select that one
                        callCostsGrid.Rows[0].Selected = true;
                    }
                    //if the last row is blank already we dont need to add another one
                    row = callCostsGrid.Rows[callCostsGrid.SelectedCells[0].RowIndex];
                }
                if ((callCostsGrid.RowCount == 0) || (row.Cells[0].Value.ToString() != String.Empty))
                {
                    DataTable dt = ((DataTable)callCostsGrid.DataSource);
                    DataRow dr = dt.NewRow();
                    dt.Rows.Add(dr);

                    callCostsGrid.DataSource = null;
                    callCostsGrid.DataSource = dt;

                    callCostsGrid.Columns[0].Visible = false; // hide the ID column
                    callCostsGrid.Columns[3].Visible = false; // hide the original regex column
                    callCostsGrid.Columns[7].Visible = false; // hide the Type column
                    callCostsGrid.Columns[8].Visible = false; // hide the Priority column
                    callCostsGrid.Columns[2].Width = 200; // change the Filter column width

                    //make sure the new field is selected - it should be at the top or the bottom
                    if (callCostsGrid.Rows[callCostsGrid.RowCount - 1].Cells[0].Value.ToString() == String.Empty)
                    {
                        //if last row is blank we select that one
                        callCostsGrid.Rows[callCostsGrid.RowCount - 1].Selected = true;
                    }
                    else if (callCostsGrid.Rows[0].Cells[0].Value.ToString() == String.Empty)
                    {
                        //if first row is blank we select that one
                        callCostsGrid.Rows[0].Selected = true;
                    }
                }
            }
            catch (Exception ex)
            {
                UnexpectedError("An unexpected error has occured with the creation of a new Call Cost filter.");
                miLog(LogEntryType.Error, SourceType.MiSMDR, "An unexpected error has occured with the creation of a new Call Cost filter: " + ex.ToString());
            }
            ClearCallCostFields(); //Clear fields when creating new Call Cost
            CheckCallCosts();
        }

        private void bnCCUpdate_Click(object sender, EventArgs e)
        {
            CallCategoryManager manager = new CallCategoryManager(_connectionString, _provider);
            List<ValidationError> errors = new List<ValidationError>();

            if (tbCCName.Text != String.Empty)
            {
                if (tbCCRegEx.Text != String.Empty)
                {
                    if ((IsNumber(tbCCConnCost.Text)) && (Convert.ToInt32(tbCCConnCost.Text) < 1000000))
                    {
                        if ((IsNumber(tbCCBlockSize.Text)) && (Convert.ToInt32(tbCCBlockSize.Text) < 1000000))
                        {
                            if ((IsNumber(tbCCRateBlock.Text)) && (Convert.ToInt32(tbCCRateBlock.Text) < 1000000))
                            {
                                string type;
                                if (rbCCExactMatch.Checked) type = "exact";
                                else if (rbCCStartsWith.Checked) type = "starts";
                                else type = "regex";

                                string chargeUnfinished;
                                if (cbCCChargeUnfinished.Checked) chargeUnfinished = "1";
                                else chargeUnfinished = "0";

                                string priority = "0";

                                DataGridViewRow row = callCostsGrid.Rows[callCostsGrid.SelectedCells[0].RowIndex];
                                if (row.Cells[0].Value.ToString() != String.Empty) //if selected id field is not blank then we update
                                {
                                    errors = manager.UpdateCallCost(tbCallCostID.Text, tbCCBlockSize.Text, tbCCRateBlock.Text, tbCCConnCost.Text, tbCCRegEx.Text, type, tbCCName.Text, priority, chargeUnfinished, true);
                                }
                                else //if the ID is blank then we create a new
                                {
                                    errors = manager.UpdateCallCost(tbCallCostID.Text, tbCCBlockSize.Text, tbCCRateBlock.Text, tbCCConnCost.Text, tbCCRegEx.Text, type, tbCCName.Text, priority, chargeUnfinished, false);
                                    tbCallCostID.Text = manager.GetLastRowID();
                                }
                                GetCallCostData(tbCallCostID.Text);

                                if (errors.Count > 0)
                                {
                                    UnexpectedError("Errors occured when trying to update the Call Cost.");
                                    // Display a list of errors
                                    foreach (ValidationError error in errors)
                                    {
                                        miLog(LogEntryType.Error, SourceType.MiSMDR, "Call Cost Update Error: " + error.GetMesssage());
                                    }
                                }
                            }
                            else EntryError("Please enter a positive number for the Rate per Block");
                        }
                        else EntryError("Please enter a positive number for the Block Size");
                    }
                    else EntryError("Please enter a positive number for the Connection Cost");
                }
                else EntryError("Please enter something to filter the numbers in one of the fields on the left side");
            }
            else EntryError("Please enter a name for the Call Cost Filter");

            CheckCallCosts();
        }

        private void bnCCDelete_Click(object sender, EventArgs e)
        {
            DialogResult d = MessageBox.Show("Are you sure you want to delete this Call Cost? This operation cannot be undone.", "Delete Call Cost", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (d == DialogResult.Yes)
            {
                CallCategoryManager manager = new CallCategoryManager(_connectionString, _provider);

                // Ensure that the user has selected a valid cell/row
                if (tbCallCostID.Text != String.Empty)
                {
                    List<ValidationError> errors = new List<ValidationError>();
                    // Delete the Call Cost from the database
                    errors = manager.DeleteCallCost(Convert.ToInt32(tbCallCostID.Text));

                    if (errors.Count == 0)
                    {
                        // Inform the user that the Call Cost has been removed and reset the button text
                        MessageBox.Show("The Call Cost has been deleted.", "Delete Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    }
                    else
                    {
                        UnexpectedError("Errors have occured with the Deletion of a Call Cost.");
                        // Display a list of errors on the error screen
                        foreach (ValidationError error in errors)
                        {
                            miLog(LogEntryType.Error, SourceType.MiSMDR, "Errors have occured with the Deletion of a Call Cost: " + error.GetMesssage());
                        }
                    }
                }
                GetCallCostData("");
                if (callCostsGrid.Rows.Count > 0) callCostsGrid.Rows[0].Selected = true;
                ClearCallCostFields();
            }

            CheckCallCosts();
        }

        private void CallCostTextBoxes_TextChanged(object sender, EventArgs e)
        {
            //as text is entered in the regex boxes it is updated as a Regular Expression in the regex box at the bottom
            string compositeRegExp = "";

            if (rbCCStartsWith.Checked) //Starts With
            {
                compositeRegExp += @"^" + tbCCStartsWith.Text + @"\d*";
            }
            else if (rbCCExactMatch.Checked) //Exact Match
            {
                compositeRegExp += @"^" + tbCCExactMatch.Text + "$";
            }
            else if (rbCCRegEx.Checked) //International
            {
                //do basically nothing because this is the resultant field as well
                compositeRegExp = tbCCRegEx.Text;
            }
            tbCCRegEx.Text = compositeRegExp;
        }

        private void rbCC_CheckedChanged(object sender, EventArgs e)
        {
            if (rbCCStartsWith.Checked)
            {
                tbCCStartsWith.Enabled = true;
                tbCCExactMatch.Enabled = false;
                tbCCRegEx.Enabled = false;
            }
            else if (rbCCExactMatch.Checked)
            {
                tbCCStartsWith.Enabled = false;
                tbCCExactMatch.Enabled = true;
                tbCCRegEx.Enabled = false;
            }
            else if (rbCCRegEx.Checked)
            {
                tbCCStartsWith.Enabled = false;
                tbCCExactMatch.Enabled = false;
                tbCCRegEx.Enabled = true;
            }
        }

        private void bnCCTest_Click(object sender, EventArgs e)
        {
            CallCostTestForm testForm = new CallCostTestForm(_connectionString, _provider);
            testForm.ShowDialog();
        }

        private void tbCallCostNumbers_TextChanged(object sender, EventArgs e)
        {
            if (tbCCRateBlock.Text != String.Empty)
            {
                try
                {
                    int temp = Convert.ToInt32(tbCCRateBlock.Text);
                    if (temp > 999999) throw new Exception();
                    rateValid = true;
                }
                catch (Exception)
                {
                    if (rateValid)
                    {
                        //only show the error message once
                        EntryError("Rate per Block can only be a whole number with a value less than 1,000,000");
                        rateValid = false;
                    }
                }
            }
            if (tbCCConnCost.Text != String.Empty)
            {
                try
                {
                    int temp = Convert.ToInt32(tbCCConnCost.Text);
                    if (temp > 999999) throw new Exception();
                    connCostValid = true;
                }
                catch (Exception)
                {
                    if (connCostValid)
                    {
                        EntryError("Connection Cost can only be a whole number with a value less than 1,000,000");
                        connCostValid = false;
                    }
                }
            }
            if (tbCCBlockSize.Text != String.Empty)
            {
                try
                {
                    int temp = Convert.ToInt32(tbCCBlockSize.Text);
                    if (temp > 999999) throw new Exception();
                    blockSizeValid = true;
                }
                catch (Exception)
                {
                    if (blockSizeValid)
                    {
                        EntryError("Block Size can only be a whole number with a value less than 1,000,000");
                        blockSizeValid = false;
                    }
                }
            }
        }

        #endregion

        #region System Tray Events

        private void notifyIcon1_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                RestoreWindow();
                ShowInTaskbar = true;
                WindowState = FormWindowState.Normal;
            }
        }

        private void MiForm_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                SaveWindowState();
                if (MiConfig.GetMinimiseToTray())
                {
                    ShowInTaskbar = false;
                    notifyIcon1.Visible = true;
                }
                else
                {
                    ShowInTaskbar = true;
                    notifyIcon1.Visible = false;
                }
            }
            else
            {
                notifyIcon1.Visible = false;
            }
        }

        private void resetPositionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Show();
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            RestoreWindow(0, 0);
        }

        private void smRestore_Click(object sender, EventArgs e)
        {
            Show();
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            RestoreWindow();
        }

        private void smExit_Click(object sender, EventArgs e)
        {
            Disconnect();
            Close();
            Application.Exit();
        }

        #endregion

        private void ps_rb_period_CheckedChanged(object sender, EventArgs e)
        {
            summaries_UpdateLabel.Visible = true;
            GetSummariesData();
        }


        private void ps_startUpdate()
        {
            if (ps_rb_periodHour.Checked)
            {
                ps_updateSummary(DateAndTime.Now.AddHours(-1.0).ToString("yyyy-MM-dd HH:mm:ss"));
                ps_selectedPeriod = "hour";
            }
            else if (ps_rb_periodDay.Checked)
            {
                ps_updateSummary(DateAndTime.Now.ToString("yyyy-MM-dd"));
                ps_selectedPeriod = "day";
            }
            else if (ps_rb_periodWeek.Checked)
            {
                ps_updateSummary(DateAndTime.Now.AddDays(-7 + 1).ToString("yyyy-MM-dd"));
                ps_selectedPeriod = "week";
            }
            else if (ps_rb_periodMonth.Checked)
            {
                ps_updateSummary(DateAndTime.Now.AddMonths(-1).ToString("yyyy-MM-dd"));
                ps_selectedPeriod = "month";
            }
        }

        // This method does the work you want done in the background.
        private void summariesWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            ps_startUpdate();
        }

        private void summariesWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            //MessageBox.Show("Summaries Update Done", "Finished Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
            summaries_PeriodGroup.Enabled = true;
            summaries_UpdateLabel.Visible = false;
        }

        private void ps_updateSummary(string startDate)
        {
            List<ValidationError> errors = new List<ValidationError>();
            CallManager manager = new CallManager(_connectionString, _provider);
            //MessageBox.Show(startDate.ToString());
            errors = manager.CreateConnection(startDate, DateAndTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "", "", "", "", "", "", "", "", "", "", MiConfig.GetRecordLimit(), _connectionString, _provider);
            if (errors.Count == 0)
            {
                SummariesSetText(ps_callcount_out, manager.Outgoings.ToString() + " Outgoing calls in the last " + ps_selectedPeriod);
                SummariesSetText(ps_callcount_inc, manager.Incomings.ToString() + " Incoming calls in the last " + ps_selectedPeriod);
                SummariesSetText(ps_callcount_int, manager.Internals.ToString() + " Internal calls in the last " + ps_selectedPeriod);
                SummariesSetText(ps_callcount_una, manager.Unanswered.ToString() + " Unanswered calls in the last " + ps_selectedPeriod);

                SummariesSetText(ps_talktime_out, manager.GetTalkTime("Outgoing").ToString() + " talk time (outgoing calls)");
                SummariesSetText(ps_talktime_inc, manager.GetTalkTime("Incoming").ToString() + " talk time (incoming calls)");
                SummariesSetText(ps_talktime_int, manager.GetTalkTime("Internal").ToString() + " talk time (internal calls)");

                SummariesSetText(ps_talktime_tot, manager.TotalDuration.ToString() + " talk time total");
                SummariesSetText(ps_totalcallcost, manager.TotalCost.ToString("C", CultureInfo.CurrentCulture) + " call cost total");

            }
            else
            {
                miLog(LogEntryType.Information, SourceType.Search, "---------"); // empty line to seperate these errors from others
                UnexpectedError("Errors occured when trying to update the Period Summary.");
                foreach (ValidationError error in errors)
                {
                    miLog(LogEntryType.Error, SourceType.Search, "Data Retrieval Error: " + error.GetMesssage());
                }
            }
        }

        private void SummariesSetText(Label lb, string text)
        {
            // InvokeRequired required compares the thread ID of the
            // calling thread to the thread ID of the creating thread.
            // If these threads are different, it returns true.
            if (lb.InvokeRequired)
            {
                SetTextCallback d = new SetTextCallback(SummariesSetText);
                this.Invoke(d, new object[] { lb, text });
            }
            else
            {
                lb.Text = text;
            }
        }


        #endregion

        private void button2_Click(object sender, EventArgs e)
        {
            KeyGenerator gen = new KeyGenerator();
            gen.ShowDialog();
            gen.Dispose();
        }
        private void showLicense()
        {
            Licensing lic = new Licensing();
            lic.ShowDialog();
            lic.Dispose();
        }

        private void licenseInformationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            showLicense();
        }

        private void MiForm_Shown(object sender, EventArgs e)
        {
            if (_regPopup)
            {
                Register reg = new Register(true); //indicate that this is the call from startup
                reg.ShowDialog();
                reg.Dispose();
                _regPopup = false; //make sure this only happens once
                GetLicenseInformation();
            }
            if (_trialStatus)
            {
                this.Text = "Trial License - PLEASE REGISTER";
            }
        }

        #region Licensing
        private void GetLicenseInformation()
        {

            Microsoft.Win32.RegistryKey key;
            key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\MiSMDR");
            string existing_key = (string)key.GetValue("MiSMDRKey");
            key.Close();
            LicenseBreaker breaker = new LicenseBreaker(existing_key);
            RegisteredDetails details = breaker.BreakKey();

            tb_name.Text = details.c_name;
            tb_contactEmail.Text = details.c_email;
            tb_expiry.Text = details.expiry;
            tb_reseller.Text = details.reseller;
            if (details.reseller != String.Empty) lb_reseller.Visible = true;
            else lb_reseller.Visible = false;
            if (details.orig_licence_type != "invalid") tb_type.Text = char.ToUpper(details.orig_licence_type[0]) + details.orig_licence_type.Substring(1, details.orig_licence_type.Length - 1);

            if (details.licence_type == "expired")
            {
                //this should just be expired professional
                //expired-demo should go into the dialog

                //lb_LicenseMessage.Visible = true;
                //lb_LicenseMessage.Text = "Your license support period has expired.\n" + "To continue receiving support and updates, please renew your license at www.MiSMDR.info";
                rtb_license.Visible = true;
                rtb_license.Text = "Your license support period has expired.\n" + "To continue receiving support and updates, please renew your license at www.MiSMDR.info";
                //Support for your "+details.orig_licence_type+" license has expired.\n"+"You can continue using MiSMDR but you won't have access newer updates or features.\n"+"Please consider contacting your MiSMDR reseller or MiSMDR support (support@MiSMDR.info) to renew your license.";

                pb_alert.Visible = true;
            }
            else if (details.licence_type == "expired-trial")
            {
                //lb_LicenseMessage.Text = "";
                pb_alert.Visible = false;
            }
            else
            {
                int diff = (Convert.ToDateTime(details.orig_expiry) - DateTime.Now).Days + 1;
                string plural = "";
                if (diff > 1) plural = "s";
                if (diff <= 40 && (details.orig_licence_type == "trial"))
                {
                    //lb_LicenseMessage.Visible = true;
                    //lb_LicenseMessage.Text = "The current " + tb_type.Text + @" license will expire in " + diff + @" day" + plural + ".";
                    rtb_license.Visible = true;
                    rtb_license.Text = "The current " + tb_type.Text + @" license will expire in " + diff + @" day" + plural + ".";
                    pb_alert.Visible = true;
                }
                else if (diff <= 7 && (details.orig_licence_type != "invalid"))
                {
                    //lb_LicenseMessage.Visible = true;
                    //lb_LicenseMessage.Text = "The current " + tb_type.Text + @" license will expire in " + diff + @" day" + plural + ".";
                    rtb_license.Visible = true;
                    rtb_license.Text = "The current " + tb_type.Text + @" license will expire in " + diff + @" day" + plural + ".";
                    pb_alert.Visible = true;
                }
                else
                {
                    //lb_LicenseMessage.Visible = false;
                    rtb_license.Visible = false;
                    pb_alert.Visible = false;
                }
            }
        }

        private void bn_ChangeLicense_Click(object sender, EventArgs e)
        {
            Register reg = new Register(false);
            DialogResult res = reg.ShowDialog();
            reg.Dispose();
            GetLicenseInformation();
        }
        #endregion

        private void rtb_LicenseHeader_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            ProcessStartInfo sInfo = new ProcessStartInfo(e.LinkText.ToString());
            Process.Start(sInfo);
        }

        private void tb_LongCallDefinition_Leave(object sender, EventArgs e)
        {
            FilldgvLatest4();
        }

        private void tb_LongCallDefinition_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) FilldgvLatest4();
        }

        private void dgvLatest1_Paint(object sender, PaintEventArgs e)
        {
            FilterDataGridView(dgvLatest1);
        }

        private void dgvLatest2_Paint(object sender, PaintEventArgs e)
        {
            FilterDataGridView(dgvLatest2);
        }

        private void dgvLatest4_Paint(object sender, PaintEventArgs e)
        {
            FilterDataGridView(dgvLatest4);
        }
    }
}
