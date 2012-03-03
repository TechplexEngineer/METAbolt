//  Copyright (c) 2008 - 2011, www.metabolt.net (METAbolt)
//  Copyright (c) 2006-2008, Paul Clement (a.k.a. Delta)
//  All rights reserved.

//  Redistribution and use in source and binary forms, with or without modification, 
//  are permitted provided that the following conditions are met:

//  * Redistributions of source code must retain the above copyright notice, 
//    this list of conditions and the following disclaimer. 
//  * Redistributions in binary form must reproduce the above copyright notice, 
//    this list of conditions and the following disclaimer in the documentation 
//    and/or other materials provided with the distribution. 

//  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND 
//  ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED 
//  WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
//  IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, 
//  INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT 
//  NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR 
//  PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, 
//  WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
//  ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
//  POSSIBILITY OF SUCH DAMAGE.


using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.IO;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using SLNetworkComm;
using OpenMetaverse;
using OpenMetaverse.Packets;
using System.Security.Cryptography;
using MD5library;
using System.Diagnostics;
using System.Timers;
using ExceptionReporting;

namespace METAbolt
{
    public class ChatTextManager
    {
        private METAboltInstance instance;
        private SLNetCom netcom;
        private GridClient client;
        private ITextPrinter textPrinter;
        //private frmMain mainForm;

        private List<ChatBufferItem> textBuffer;

        private bool showTimestamps;

        //added by GM on 3-JUL-2009 for chair group IM calling
        private string gmu = string.Empty;
        private string imu = string.Empty;  
        private UUID cau = UUID.Zero;
        private int chairAnnouncerInterval;
        private int chairAnnouncerActives;
        private UUID[] chairAnnouncerGroups;
        private string[] chairAnnouncerGroupNames;
        private int indexGroup;
        private DateTime nextCallTime;
        private int targetIndex;
        private ManualResetEvent waitGroupIMSession = new ManualResetEvent(false);
        private ManualResetEvent waitGroupIMLeaveSession = new ManualResetEvent(false);
        private bool chairAnnEnabled = false;
        private bool chairAnnChat = true;
        //added by GM on 1-APR-2010
        private string chairAnnAdvert;

        private UUID GroupRequestID;
        //private ManualResetEvent GroupsEvent = new ManualResetEvent(false);
        private UUID irole = UUID.Zero;
        private List<UUID> roles = null;
        UUID igroup = UUID.Zero;
        UUID iperson = UUID.Zero;
        private string gavname = string.Empty;
        private string gmanlocation = string.Empty;
        //private GroupManager.GroupMembersCallback callback;
        private bool ChatLogin = true;

        private string commandin = string.Empty;
        //private bool TEnabled = false;
        //private string tName = string.Empty;
        //private string tPwd = string.Empty;
        //private bool tweet = true;
        //private string tweetname = string.Empty;
        private string lastspeaker = string.Empty;  
        private System.Timers.Timer aTimer;
        private bool ismember = false;
        private int invitecounter = 0;
        private bool classiclayout = false;
        private ExceptionReporter reporter = new ExceptionReporter();
        private bool reprinting = false;

        internal class ThreadExceptionHandler
        {
            public void ApplicationThreadException(object sender, ThreadExceptionEventArgs e)
            {
                ExceptionReporter reporter = new ExceptionReporter();
                reporter.Show(e.Exception);
            }
        }

        public ChatTextManager(METAboltInstance instance, ITextPrinter textPrinter)
        {
            SetExceptionReporter();
            Application.ThreadException += new ThreadExceptionHandler().ApplicationThreadException;

            this.textPrinter = textPrinter;
            this.textBuffer = new List<ChatBufferItem>();

            this.instance = instance;
            netcom = this.instance.Netcom;
            client = this.instance.Client;
            AddNetcomEvents();

            //TEnabled = this.instance.Config.CurrentConfig.EnableTweeter;
            //tName = this.instance.Config.CurrentConfig.TweeterName;
            //tPwd = this.instance.Config.CurrentConfig.TweeterPwd;
            //tweet = this.instance.Config.CurrentConfig.Tweet;
            //tweetname = this.instance.Config.CurrentConfig.TweeterUser;
            classiclayout = this.instance.Config.CurrentConfig.ClassicChatLayout;    

            //added by GM on 2-JUL-2009
            gmu = this.instance.Config.CurrentConfig.GroupManagerUID;
            imu = this.instance.Config.CurrentConfig.IgnoreUID;    
            cau = this.instance.Config.CurrentConfig.ChairAnnouncerUUID;
            chairAnnouncerInterval = this.instance.Config.CurrentConfig.ChairAnnouncerInterval;
            chairAnnEnabled = this.instance.Config.CurrentConfig.ChairAnnouncerEnabled;
            chairAnnChat = this.instance.Config.CurrentConfig.ChairAnnouncerChat;
            chairAnnouncerGroups = new UUID[6];
            chairAnnouncerGroupNames = new string[6]; //filled as joined
            chairAnnouncerGroups[0] = this.instance.Config.CurrentConfig.ChairAnnouncerGroup1;
            chairAnnouncerGroups[1] = this.instance.Config.CurrentConfig.ChairAnnouncerGroup2;
            chairAnnouncerGroups[2] = this.instance.Config.CurrentConfig.ChairAnnouncerGroup3;
            chairAnnouncerGroups[3] = this.instance.Config.CurrentConfig.ChairAnnouncerGroup4;
            chairAnnouncerGroups[4] = this.instance.Config.CurrentConfig.ChairAnnouncerGroup5;
            chairAnnouncerGroups[5] = this.instance.Config.CurrentConfig.ChairAnnouncerGroup6;
            CountActives();
            indexGroup = 0;
            nextCallTime = DateTime.Now;
            //added by GM on 1-APR-2010
            chairAnnAdvert = this.instance.Config.CurrentConfig.ChairAnnouncerAdvert;

            commandin = this.instance.Config.CurrentConfig.CommandInID;

            client.Groups.GroupMembersReply += new EventHandler<GroupMembersReplyEventArgs>(GroupMembersHandler);


            showTimestamps = this.instance.Config.CurrentConfig.ChatTimestamps;
            this.instance.Config.ConfigApplied += new EventHandler<ConfigAppliedEventArgs>(Config_ConfigApplied);
        }

        private void SetExceptionReporter()
        {
            reporter.Config.ShowSysInfoTab = false;   // alternatively, set properties programmatically
            reporter.Config.ShowFlatButtons = true;   // this particular config is code-only
            reporter.Config.CompanyName = "METAbolt";
            reporter.Config.ContactEmail = "metabolt@vistalogic.co.uk";
            reporter.Config.EmailReportAddress = "metabolt@vistalogic.co.uk";
            reporter.Config.WebUrl = "http://www.metabolt.net/metaforums/";
            reporter.Config.AppName = "METAbolt";
            reporter.Config.MailMethod = ExceptionReporting.Core.ExceptionReportInfo.EmailMethod.SimpleMAPI;
            reporter.Config.BackgroundColor = Color.White;
            reporter.Config.ShowButtonIcons = false;
            reporter.Config.ShowLessMoreDetailButton = true;
            reporter.Config.TakeScreenshot = true;
            reporter.Config.ShowContactTab = true;
            reporter.Config.ShowExceptionsTab = true;
            reporter.Config.ShowFullDetail = true;
            reporter.Config.ShowGeneralTab = true;
            reporter.Config.ShowSysInfoTab = true;
            reporter.Config.TitleText = "METAbolt Exception Reporter";
        }

        private void CountActives()
        {
            chairAnnouncerActives = 0;
            foreach (UUID cag in chairAnnouncerGroups)
            {
                if (cag != UUID.Zero) chairAnnouncerActives++;
            }
        }

        private void Config_ConfigApplied(object sender, ConfigAppliedEventArgs e)
        {
            showTimestamps = e.AppliedConfig.ChatTimestamps;

            //TEnabled = e.AppliedConfig.EnableTweeter;
            //tName = e.AppliedConfig.TweeterName;
            //tPwd = e.AppliedConfig.TweeterPwd;
            //tweet = this.instance.Config.CurrentConfig.Tweet;
            //tweetname = this.instance.Config.CurrentConfig.TweeterUser;

            //update with new config
            gmu = e.AppliedConfig.GroupManagerUID;
            cau = e.AppliedConfig.ChairAnnouncerUUID;
            chairAnnouncerInterval = e.AppliedConfig.ChairAnnouncerInterval;
            chairAnnEnabled = e.AppliedConfig.ChairAnnouncerEnabled;
            chairAnnChat = e.AppliedConfig.ChairAnnouncerChat;
            chairAnnouncerGroups[0] = e.AppliedConfig.ChairAnnouncerGroup1;
            chairAnnouncerGroups[1] = e.AppliedConfig.ChairAnnouncerGroup2;
            chairAnnouncerGroups[2] = e.AppliedConfig.ChairAnnouncerGroup3;
            chairAnnouncerGroups[3] = e.AppliedConfig.ChairAnnouncerGroup4;
            chairAnnouncerGroups[4] = e.AppliedConfig.ChairAnnouncerGroup5;
            chairAnnouncerGroups[5] = e.AppliedConfig.ChairAnnouncerGroup6;
            CountActives();

            if (instance.Config.CurrentConfig.BufferApplied)
            {
                //ReprintAllText();
                CheckBufferSize();
                instance.Config.CurrentConfig.BufferApplied = false; 
            }
        }

        private void AddNetcomEvents()
        {
            netcom.ClientLoginStatus += new EventHandler<LoginProgressEventArgs>(netcom_ClientLoginStatus);
            netcom.ClientLoggedOut += new EventHandler(netcom_ClientLoggedOut);
            netcom.ClientDisconnected += new EventHandler<DisconnectedEventArgs>(netcom_ClientDisconnected);
            netcom.ChatReceived += new EventHandler<ChatEventArgs>(netcom_ChatReceived);
            netcom.ScriptDialogReceived += new EventHandler<ScriptDialogEventArgs>(netcom_ScriptDialogReceived);
            netcom.LoadURLReceived += new EventHandler<LoadUrlEventArgs>(netcom_LoadURLReceived);
            netcom.ScriptQuestionReceived += new EventHandler<ScriptQuestionEventArgs>(netcom_ScriptQuestionReceived);
            netcom.ChatSent += new EventHandler<ChatSentEventArgs>(netcom_ChatSent);
            netcom.AlertMessageReceived += new EventHandler<AlertMessageEventArgs>(netcom_AlertMessageReceived);
        }

        private void RemoveNetcomEvents()
        {
            netcom.ClientLoginStatus -= new EventHandler<LoginProgressEventArgs>(netcom_ClientLoginStatus);
            netcom.ClientLoggedOut -= new EventHandler(netcom_ClientLoggedOut);
            netcom.ClientDisconnected -= new EventHandler<DisconnectedEventArgs>(netcom_ClientDisconnected);
            netcom.ChatReceived -= new EventHandler<ChatEventArgs>(netcom_ChatReceived);
            netcom.ScriptDialogReceived -= new EventHandler<ScriptDialogEventArgs>(netcom_ScriptDialogReceived);
            netcom.LoadURLReceived -= new EventHandler<LoadUrlEventArgs>(netcom_LoadURLReceived);
            netcom.ScriptQuestionReceived -= new EventHandler<ScriptQuestionEventArgs>(netcom_ScriptQuestionReceived);
            netcom.ChatSent -= new EventHandler<ChatSentEventArgs>(netcom_ChatSent);
            netcom.AlertMessageReceived -= new EventHandler<AlertMessageEventArgs>(netcom_AlertMessageReceived);

            client.Groups.GroupMembersReply -= new EventHandler<GroupMembersReplyEventArgs>(GroupMembersHandler);
            this.instance.Config.ConfigApplied -= new EventHandler<ConfigAppliedEventArgs>(Config_ConfigApplied);
        }

        private void netcom_ChatSent(object sender, ChatSentEventArgs e)
        {
            if (e.Channel == 0) return;

            ProcessOutgoingChat(e);
        }

        private void netcom_ClientLoginStatus(object sender, LoginProgressEventArgs e)
        {
            if (e.Status == LoginStatus.Success)
            {
                ChatBufferItem loggedIn = new ChatBufferItem(
                    DateTime.Now,
                    " Logged into Second Life as " + netcom.LoginOptions.FullName + ".",
                    ChatBufferTextStyle.StatusBlue);

                ChatBufferItem loginReply = new ChatBufferItem(
                    DateTime.Now, " Login reply: " + e.Message, ChatBufferTextStyle.StatusDarkBlue);

                string avid = client.Self.AgentID.ToString();

                ChatBufferItem avuuid = new ChatBufferItem(
                    DateTime.Now, " " + netcom.LoginOptions.FullName + "'s UUID is " + avid + " ", ChatBufferTextStyle.Alert);

                //ChatBufferItem avuuid1 = new ChatBufferItem(
                //    DateTime.Now, " Waiting for avatar to rezz... ", ChatBufferTextStyle.Alert);

                ProcessBufferItem(loggedIn, true);
                ProcessBufferItem(loginReply, true);
                ProcessBufferItem(avuuid, true);
                //ProcessBufferItem(avuuid1, true);
            }
            else if (e.Status == LoginStatus.Failed)
            {
                ChatBufferItem loginError = new ChatBufferItem(
                    DateTime.Now, " Login error: " + e.Message, ChatBufferTextStyle.Error);

                ProcessBufferItem(loginError, true);
            }
        }

        private void netcom_ClientLoggedOut(object sender, EventArgs e)
        {
            ChatBufferItem item = new ChatBufferItem(
                DateTime.Now, " Logged out of Second Life.\n", ChatBufferTextStyle.StatusBlue);

            ProcessBufferItem(item, true);

            RemoveNetcomEvents();
        }

        private void netcom_AlertMessageReceived(object sender, AlertMessageEventArgs e)
        {
            if (e.Message.ToLower().Contains("autopilot canceled")) return; //workaround the stupid autopilot alerts

            string emsg = e.Message.Trim();

            if (emsg.Contains("RESTART_X_MINUTES"))
            {
                string[] mins = emsg.Split(new Char[] { ' ' });
                emsg = "Region is restarting in " + mins[1].Trim() + " minutes. If you remain in this region you will be logged out.";
            }

            ChatBufferItem item = new ChatBufferItem(
                DateTime.Now, " Alert message: " + emsg, ChatBufferTextStyle.Alert);

            ProcessBufferItem(item, true);
        }

        private void netcom_ClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            if (e.Reason == NetworkManager.DisconnectType.ClientInitiated) return;

            ChatBufferItem item = new ChatBufferItem(
                DateTime.Now, " Client disconnected. Message: " + e.Message, ChatBufferTextStyle.Error);

            ProcessBufferItem(item, true);

            RemoveNetcomEvents();
        }

        private void netcom_ChatReceived(object sender, ChatEventArgs e)
        {
            ProcessIncomingChat(e);
        }

        private void netcom_ScriptDialogReceived(object sender, ScriptDialogEventArgs e)
        {
            if (instance.IsAvatarMuted(e.ObjectID))
                return;

            if (string.IsNullOrEmpty(e.Message)) return;

            // Count the ones already on display
            // to avoid flood attacks

            if (this.instance.DialogCount < 9)
            {
                this.instance.DialogCount += 1;
            }

            if (this.instance.DialogCount < 9)
            {
                (new frmDialog(instance, e)).ShowDialog();

                if (this.instance.DialogCount == 8)
                {
                    UUID objID = e.ObjectID;
                    string objname = e.ObjectName;

                    string objinfo = "\nObject UUID: " + objID;
                    objinfo += "\nObject Name: " + objname;

                    PrintDialogWarning(e.FirstName + " " + e.LastName, objinfo);
                }
            }
        }

        public void PrintDialogWarning(string avname, string oinfo)
        {
            ChatBufferItem dalert = new ChatBufferItem(
                DateTime.Now, " There is a total of 8 dialogs open. All new dialogs are now being blocked for your security. Close currently open dialogs to resume normal conditions.", ChatBufferTextStyle.Alert);

            ChatBufferItem warn1 = new ChatBufferItem(
                DateTime.Now, " Last dialog was from " + avname + ". Object details below..." + oinfo, ChatBufferTextStyle.Alert);

            ProcessBufferItem(dalert, false);
            ProcessBufferItem(warn1, false);
        }

        private void netcom_ScriptQuestionReceived(object sender, ScriptQuestionEventArgs e)
        {
            if (instance.IsAvatarMuted(e.ItemID))
                return;

            //e.ObjectName.ToString();
            //e.ObjectOwner.ToString();
            //e.Questions.ToString();

            ScriptPermission sperm = ScriptPermission.None;
            string smsg = string.Empty;

            switch (e.Questions)
            {
                case ScriptPermission.Attach:
                    sperm = ScriptPermission.Attach;
                    smsg = "Wants permission to ATTACH.";
                    break;

                case ScriptPermission.Debit:
                    sperm = ScriptPermission.Debit;
                    smsg = "Wants permission to DEBIT.";
                    break;

                case ScriptPermission.TakeControls:
                    sperm = ScriptPermission.TakeControls;
                    smsg = "Wants permission to TAKE CONTROLS.";
                    break;

                case ScriptPermission.TriggerAnimation:
                    sperm = ScriptPermission.TriggerAnimation;
                    smsg = "Wants permission to TRIGGER ANIMATION.";
                    break;
            }

            DialogResult sret = MessageBox.Show(e.ObjectName.ToString() + " owned by " + e.ObjectOwnerName + ":\n\n" + smsg, "Script permission...", MessageBoxButtons.OKCancel);

            if (sret == DialogResult.OK)
            {
                // Grant permission
                client.Self.ScriptQuestionReply(client.Network.CurrentSim, e.ItemID, e.TaskID, sperm);
            }
        }

        private void netcom_LoadURLReceived(object sender, LoadUrlEventArgs e)
        {
            //e.Message;
            //e.ObjectName;
            //e.url;

            DialogResult sret = MessageBox.Show(e.ObjectName.ToString() + ":\n\n" + e.Message, "URL offer...", MessageBoxButtons.OKCancel);

            if (sret == DialogResult.OK)
            {
                //ShellExecute(this.Handle, "open", e.url.ToString(), null, null, 0);
                System.Diagnostics.Process.Start(@e.URL.ToString());
            }
        }

        public void PrintStartupMessage()
        {
            ChatBufferItem title = new ChatBufferItem(
                DateTime.Now, " " + Properties.Resources.METAboltTitle + " " + Properties.Resources.METAboltVersion, ChatBufferTextStyle.StartupTitle);

            ChatBufferItem ready = new ChatBufferItem(
                DateTime.Now, " Ready.\n", ChatBufferTextStyle.StatusBlue);

            ProcessBufferItem(title, true);
            ProcessBufferItem(ready, true);
        }

        public void PrintAlertMessage(string msg)
        {
            ChatBufferItem ready = new ChatBufferItem(
                DateTime.Now, msg, ChatBufferTextStyle.Alert);

            ProcessBufferItem(ready, true);
        }

        public void PrintUUID()
        {
            string avid = client.Self.AgentID.ToString();

            ChatBufferItem avuuid = new ChatBufferItem(
                DateTime.Now, " My UUID is " + avid, ChatBufferTextStyle.Alert);

            ProcessBufferItem(avuuid, true);
        }

        public void PrintMsg(string Msg)
        {
            textPrinter.SetSelectionForeColor(Color.Brown);
            textPrinter.PrintText(Msg);
        }

        private void CheckBufferSize()
        {
            int lines = textBuffer.Count;
            int maxlines = this.instance.Config.CurrentConfig.lineMax;

            if (maxlines == 0) return;

            //if (lines > maxlines)
            //{
            //    textBuffer.RemoveAt(0);

            //    ReprintAllText();
            //}

            if (lines > maxlines)
            {
                int lineno = maxlines / 2;

                for (int a = 0; a < lineno; a++)
                {
                    textBuffer.RemoveAt(a);
                }

                reprinting = true;

                ReprintAllText();
            }
        }

        public void ProcessBufferItem(ChatBufferItem item, bool addToBuffer)
        {
            try
            {
                if (instance.IsAvatarMuted(item.FromUUID))
                    return;
            }
            catch
            {
                ; 
            }

            string smsg = item.Text;
            string prefix = string.Empty;

            if (smsg.Contains(imu)) return; // Ignore the message for plugin use or whatever

            if (addToBuffer)
            {
                textBuffer.Add(item);

                int lines = textBuffer.Count;
                int maxlines = this.instance.Config.CurrentConfig.lineMax;

                if (lines > maxlines && maxlines > 0)
                {
                    CheckBufferSize();
                    return;
                }
            }

            DateTime dte = item.Timestamp;

            if (classiclayout)
            {
                if (showTimestamps)
                {
                    dte = this.instance.State.GetTimeStamp(dte);   
                    //if (instance.Config.CurrentConfig.UseSLT)
                    //{
                    //    string _timeZoneId = "Pacific Standard Time";
                    //    DateTime startTime = DateTime.UtcNow;
                    //    TimeZoneInfo tst = TimeZoneInfo.FindSystemTimeZoneById(_timeZoneId);
                    //    dte = TimeZoneInfo.ConvertTime(startTime, TimeZoneInfo.Utc, tst);
                    //}

                    textPrinter.SetSelectionForeColor(Color.Gray);

                    if (item.Style == ChatBufferTextStyle.StatusDarkBlue || item.Style == ChatBufferTextStyle.Alert)
                    {
                        //textPrinter.PrintText("\n" + dte.ToString("[HH:mm] "));
                        prefix = "\n" + dte.ToString("[HH:mm] ");
                    }
                    else
                    {
                        //textPrinter.PrintText(dte.ToString("[HH:mm] "));
                        prefix = dte.ToString("[HH:mm] ");
                    }
                }
                else
                {
                    prefix = string.Empty;
                }
            }
            else
            {

                try
                {
                    if (lastspeaker != item.FromName)
                    {
                        //textPrinter.SetFontStyle(FontStyle.Bold);
                        textPrinter.PrintHeader(item.FromName);   // + buff);

                        lastspeaker = item.FromName;
                    }
                }
                catch
                {
                    ;
                }

                textPrinter.SetFontStyle(FontStyle.Regular);
                textPrinter.SetSelectionBackColor(Color.White);
                //textPrinter.SetSelectionBackColor(instance.Config.CurrentConfig.BgColour);

                if (showTimestamps)
                {
                    dte = this.instance.State.GetTimeStamp(dte);

                    //if (instance.Config.CurrentConfig.UseSLT)
                    //{
                    //    string _timeZoneId = "Pacific Standard Time";
                    //    DateTime startTime = DateTime.UtcNow;
                    //    TimeZoneInfo tst = TimeZoneInfo.FindSystemTimeZoneById(_timeZoneId);
                    //    dte = TimeZoneInfo.ConvertTime(startTime, TimeZoneInfo.Utc, tst);
                    //}

                    //textPrinter.ForeColor = Color.Gray;

                    if (item.Style == ChatBufferTextStyle.StatusDarkBlue || item.Style == ChatBufferTextStyle.Alert)
                    {
                        prefix = "\n" + dte.ToString("[HH:mm] ");
                        //prefix = dte.ToString("[HH:mm] ");
                    }
                    else
                    {
                        if (item.FromName == client.Self.FirstName + " " + client.Self.LastName)
                        {
                            prefix = dte.ToString("   [HH:mm] ");
                        }
                        else
                        {
                            prefix = dte.ToString("[HH:mm] ");
                        }
                    }
                }
                else
                {
                    prefix = string.Empty;
                }
            }

            // Check to see if it is an in-world plugin first
            //current possibilities are GroupManager or ChairAnnouncer
            if (smsg.Contains(gmu))
            {
                roles = new List<UUID>();

                char[] deli = ",".ToCharArray();
                string[] sGrp = smsg.Split(deli);

                try //trap the cast to UUID bug if passed string is malformed
                {
                    igroup = (UUID)sGrp[2];
                    iperson = (UUID)sGrp[1];
                }
                catch (Exception ex)
                {
                    Logger.Log(ex.Message, Helpers.LogLevel.Error);
                    return;
                }

                int ielems = sGrp.Length;

                // This is a hack for backward GroupMan Pro compatibility
                if (ielems > 6)
                {
                    gavname = sGrp[5];
                    gmanlocation = sGrp[6];
                }
                else
                {
                    gavname = string.Empty;
                    gmanlocation = string.Empty;
                }

                string pwd = string.Empty;

                try
                {
                    pwd = sGrp[3];
                }
                catch
                {
                    pwd = "invalid";
                }

                // the MD5 stuff (as of V 0.9.3)
                string str = this.instance.Config.CurrentConfig.GroupManPro;
                METAMD5 md5 = new METAMD5();
                string metapwd = md5.MD5(str);

                if (pwd != metapwd)
                {
                    string gmsg = string.Empty;

                    if (pwd == "invalid")
                    {
                        gmsg = "IMPORTANT WARNING: A group invite could not be sent out. Your GroupMan Pro is out of date. For more information please visit: http://www.metabolt.net/metaforums/yaf_postsm1525_GroupMan-Pro-update.aspx#post1525 \nThe password used is: " + pwd + ".";
                    }
                    else
                    {
                        gmsg = "IMPORTANT WARNING: A group invite request with an invalid password has been received. Either the passwords in METAbolt & GroupMan Pro don't match or someone is trying to get unauthorised access. This is for information purposes only. DO NOT PANIC. The request has been discarded.  \n\nRequest received from: " + item.FromName + " (" + item.FromUUID + ")\nThe password used is: " + pwd + ".\nThe requested invite is for group: " + igroup;
                    }
                    
                    textPrinter.PrintTextLine(gmsg);
                    return;
                }

                //roles.Add("00000000-0000-0000-0000-000000000000");

                // Check if being invited to a specific role
                if (ielems > 4)
                {
                    try //trap the cast to UUID bug if passed string is malformed 
                    {
                        irole = (UUID)sGrp[4].Trim();
                    }
                    catch { ;}

                    roles.Add(irole);
                }
                else
                {
                    roles.Add(UUID.Zero);
                }

                try
                {
                    textPrinter.SetSelectionForeColor(Color.DarkCyan);

                    if (this.instance.State.Groups.ContainsKey(igroup))
                    {
                        // check if already member of the group
                        client.Groups.GroupMembersReply += new EventHandler<GroupMembersReplyEventArgs>(GroupMembersHandler);
                        GroupRequestID = client.Groups.RequestGroupMembers(igroup);
                    }

                    return;
                }
                catch (Exception excp)
                {
                    textPrinter.PrintTextLine(String.Format("\n(GroupMan Pro @ " + gmanlocation + ")\nGroupMan Pro has encountered an error and a group invite could not be sent to: " + gavname));
                    OpenMetaverse.Logger.Log(String.Format("GroupMan Pro Error: {0}", excp), Helpers.LogLevel.Error);
                    return;
                }
            }
            else if (smsg.Contains(commandin))
            {
                // this is now handled by the MB_LSLAPI
                return;
            }
            //added by GM on 2-JUL-2009
            else if (item.FromUUID == cau && cau != UUID.Zero)
            {
                if (!chairAnnEnabled) return;
                if (nextCallTime <= DateTime.Now)
                {
                    textPrinter.SetSelectionForeColor(Color.DarkOrange);
                    if (chairAnnChat) textPrinter.PrintText(prefix);

                    // the MD5 stuff (as of V 0.9.3)
                    //chop out the Password if there is one
                    string[] chops = smsg.Split(new char[] { '|' }, StringSplitOptions.None);
                    string pwd;
                    if (chops.Length == 3)
                    {
                        pwd = chops[1];
                    }
                    else
                    {
                        pwd = "invalid";
                    }

                    string str = this.instance.Config.CurrentConfig.GroupManPro; //GM: actually it is the METAbolt password
                    METAMD5 md5 = new METAMD5();
                    string metapwd = md5.MD5(str);

                    if (pwd != metapwd)
                    {
                        string gmsg = string.Empty;

                        if (pwd == "invalid")
                        {
                            gmsg = "IMPORTANT WARNING: A chair announcement could not be sent out. Your Chair Announcer is out of date. \nThe password used is: " + pwd + ".";
                        }
                        else
                        {
                            gmsg = "IMPORTANT WARNING: A chair announcement with an invalid password has been received. Either the passwords in METAbolt & Chair Announcer don't match or someone is trying to get unauthorised access. This is for information purposes only. DO NOT PANIC. The request has been discarded.  \nThe password used is: " + pwd;
                        }

                        textPrinter.PrintTextLine(gmsg); //always print even if chairAnnChat is turned off
                        return;
                    }

                    //fixup the text
                    //chop off before the ChairPrefix
                    string cp = METAbolt.Properties.Resources.ChairPrefix;
                    int pos = smsg.IndexOf(cp, 2);
                    pos = pos < 0 ? 0 : pos;
                    StringBuilder sb = new StringBuilder(smsg.Substring(pos));

                    //include the advert on a new line
                    // modified by GM on 1-APR-2010
                    //was string ca = METAbolt.Properties.Resources.ChairAdvert;
                    string ca = chairAnnAdvert;
                    sb.Append(" \n");
                    sb.Append(ca);

                    //work out which group to IM between 0 and 5
                    indexGroup++;
                    if (indexGroup == 6 || chairAnnouncerGroups[indexGroup] == UUID.Zero) indexGroup = 0;
                    nextCallTime = DateTime.Now.AddMinutes(1);
                    //callbacks
                    client.Self.GroupChatJoined += new EventHandler<GroupChatJoinedEventArgs>(Self_OnGroupChatJoin);

                    //calculate the interval
                    int perGroupInterval = (int)Math.Round(((decimal)(chairAnnouncerInterval / chairAnnouncerActives)));
                    perGroupInterval = perGroupInterval < 1 ? 1 : perGroupInterval;
                    //find if already in the group
                    UUID grp = chairAnnouncerGroups[indexGroup];
                    if (client.Self.GroupChatSessions.ContainsKey(grp))
                    {
                        client.Self.InstantMessageGroup(grp, sb.ToString());
                        if (chairAnnChat) textPrinter.PrintTextLine("Chair Announcer: IM to existing group " + chairAnnouncerGroupNames[indexGroup]);
                        nextCallTime = nextCallTime.AddMinutes(perGroupInterval - 1);
                    }
                    else
                    {
                        targetIndex = indexGroup;
                        client.Self.RequestJoinGroupChat(grp);
                        waitGroupIMSession.Reset();
                        if (waitGroupIMSession.WaitOne(30000, false)) //30 seconds
                        {
                            client.Self.InstantMessageGroup(grp, sb.ToString());
                            if (chairAnnChat) textPrinter.PrintTextLine("Chair Announcer: IM to new group " + chairAnnouncerGroupNames[indexGroup]);
                            nextCallTime = nextCallTime.AddMinutes(perGroupInterval - 1);
                        }
                        else
                        {
                            Logger.Log("Chair Announcer: timeout after 30 seconds on group " + indexGroup.ToString(), Helpers.LogLevel.Warning);
                        }
                    }

                    client.Self.GroupChatJoined -= new EventHandler<GroupChatJoinedEventArgs>(Self_OnGroupChatJoin);
                }
                else
                {
                    Logger.Log("Chair Announcer: skipped", Helpers.LogLevel.Info);
                }

                OpenMetaverse.Logger.Log(String.Format("AddIn: {0} called {1}", cau.ToString(), smsg), Helpers.LogLevel.Debug);
                return;
            }

            if (classiclayout)
            {
                textPrinter.SetSelectionForeColor(Color.Gray);
                textPrinter.PrintText(prefix);
            }
            else
            {
                //textPrinter.SetSelectionForeColor(Color.Gray);
                //textPrinter.PrintText(dte.ToString("[HH:mm] "));
                //textPrinter.SetOffset(6);
                //textPrinter.SetFontSize(6.5f);
                textPrinter.PrintDate(prefix);
                //textPrinter.SetFontSize(8.5f);
                //textPrinter.SetOffset(0);
            }

            switch (item.Style)
            {
                case ChatBufferTextStyle.Normal:
                    textPrinter.SetSelectionForeColor(Color.Black);
                    break;

                case ChatBufferTextStyle.StatusBlue:
                    textPrinter.SetSelectionForeColor(Color.Blue);
                    break;

                case ChatBufferTextStyle.StatusDarkBlue:
                    textPrinter.SetSelectionForeColor(Color.Gray);
                    //textPrinter.BackColor = Color.LightSeaGreen;
                    break;

                case ChatBufferTextStyle.LindenChat:
                    textPrinter.SetSelectionForeColor(Color.DarkGreen);
                    textPrinter.SetSelectionBackColor(Color.LightYellow);
                    break;

                case ChatBufferTextStyle.ObjectChat:
                    textPrinter.SetSelectionForeColor(Color.DarkCyan);
                    //item.Text = "\n" + item.Text;
                    break;

                case ChatBufferTextStyle.StartupTitle:
                    textPrinter.SetSelectionForeColor(Color.Black);
                    textPrinter.SetFontStyle(FontStyle.Bold);
                    break;

                case ChatBufferTextStyle.Alert:
                    textPrinter.SetSelectionForeColor(Color.White);
                    textPrinter.SetSelectionBackColor(Color.BlueViolet);
                    item.Text = item.Text;   // +"\n";
                    break;

                case ChatBufferTextStyle.Error:
                    textPrinter.SetSelectionForeColor(Color.Yellow);
                    textPrinter.SetSelectionBackColor(Color.Red);
                    break;
            }

            textPrinter.PrintTextLine(item.Text);

            //// Handle chat tweets
            //if (TEnabled)
            //{
            //    if (instance.Config.CurrentConfig.EnableChatTweets)
            //    {
            //        Yedda.Twitter twit = new Yedda.Twitter();
            //        string resp = string.Empty;

            //        if (tweet)
            //        {
            //            // if enabled print to Twitter
            //            resp = twit.UpdateAsJSON(tName, tPwd, item.Text);
            //        }
            //        else
            //        {
            //            // it's a direct message
            //            resp = twit.Send(tName, tPwd, tweetname, item.Text);
            //        }

            //        if (resp != "OK")
            //        {
            //            Logger.Log("Twitter error: " + resp, Helpers.LogLevel.Warning);
            //        }
            //    }
            //}

            if (reprinting)
            {
                reprinting = false;
                return;
            }

            if (!classiclayout)
            {
                string txt = item.FromName + ": " + item.Text;
                LogMessage(dte, item.FromUUID.ToString(), item.FromName, txt);
            }
            else
            {
                LogMessage(dte, item.FromUUID.ToString(), item.FromName, item.Text);
            }
        }

        void Self_OnGroupChatLeft(UUID groupchatSessionID)
        {
            Logger.Log(String.Format("Chair Announcer: Left GroupChat {0}", groupchatSessionID.ToString()), Helpers.LogLevel.Debug);
            waitGroupIMLeaveSession.Set();
        }

        void Self_OnGroupChatJoin(object sender, GroupChatJoinedEventArgs e)
        {
            if (e.Success)
            {
                Logger.Log(String.Format("Chair Announcer: Joined GroupChat {0} with UUID {1} named {2}", targetIndex, e.SessionID.ToString(), e.SessionName), Helpers.LogLevel.Debug);
                chairAnnouncerGroupNames[targetIndex] = e.SessionName;
                waitGroupIMSession.Set();
            }
            else
            {
                Logger.Log(String.Format("Chair Announcer: Failed GroupChat {0} with UUID {1} named {2}", targetIndex, e.SessionID.ToString(), e.SessionName), Helpers.LogLevel.Debug);
                chairAnnouncerGroupNames[targetIndex] = "N/A";
                TextPrinter.PrintTextLine("Chair Announcer: Failed to join GroupChat");
            }
        }

        // Seperate thread
        private void GroupMembersHandler(object sender, GroupMembersReplyEventArgs e)
        {
            if (e.RequestID == GroupRequestID)
            {
                client.Groups.GroupMembersReply -= new EventHandler<GroupMembersReplyEventArgs>(GroupMembersHandler);

                if (igroup != e.GroupID)
                {
                    return;
                }
               
                invitecounter += 1;

                if (e.Members.Count > 0)
                {
                    GroupMember gmember;

                    if (e.Members.TryGetValue(iperson, out gmember))
                    {
                        invitecounter = 0;

                        if (!ismember)
                        {
                            DateTime dte = DateTime.Now;

                            dte = this.instance.State.GetTimeStamp(dte);

                            if (instance.Config.CurrentConfig.UseSLT)
                            {
                                string _timeZoneId = "Pacific Standard Time";
                                DateTime startTime = DateTime.UtcNow;
                                TimeZoneInfo tst = TimeZoneInfo.FindSystemTimeZoneById(_timeZoneId);
                                dte = TimeZoneInfo.ConvertTime(startTime, TimeZoneInfo.Utc, tst);
                            }

                            string prefix = dte.ToString("[HH:mm] ");
                            string gname = instance.State.GroupStore[igroup];

                            textPrinter.SetSelectionForeColor(Color.Gray);
                            textPrinter.PrintTextLine(prefix + "\n\n[ GroupMan Pro ] @ " + gmanlocation + "\n   Invite request for group " + gname.ToUpper() + " has been ignored. " + gavname + " (" + iperson.ToString() + ") is already a member.");
                            return;
                        }
                        else
                        {
                            ismember = false;
                            GivePresent();
                            return;
                        }
                    }
                }

                if (invitecounter > 1)
                {
                    invitecounter = 0;
                    ismember = false;
                    aTimer.Stop();  
                    aTimer.Enabled = false;
                    return;
                }

                if (invitecounter == 1)
                {
                    WriteToChat();
                }
            }
        }

        private void WriteToChat()
        {
            DateTime dte = DateTime.Now;

            dte = this.instance.State.GetTimeStamp(dte);

            if (instance.Config.CurrentConfig.UseSLT)
            {
                string _timeZoneId = "Pacific Standard Time";
                DateTime startTime = DateTime.UtcNow;
                TimeZoneInfo tst = TimeZoneInfo.FindSystemTimeZoneById(_timeZoneId);
                dte = TimeZoneInfo.ConvertTime(startTime, TimeZoneInfo.Utc, tst);
            }

            //textPrinter.ForeColor = Color.Gray;
            string prefix = dte.ToString("[HH:mm] ");

            try
            {
                client.Groups.Invite(igroup, roles, iperson);

                // start timer to check if invite has been accepted
                aTimer = new System.Timers.Timer();
                aTimer.Elapsed += new ElapsedEventHandler(OnTimedEvent);
                // Set the Interval to 10 seconds.
                aTimer.Interval = 10000;
                aTimer.Enabled = true;
                aTimer.Start();  
            }
            catch (Exception excp)
            {
                //string eex = excp.ToString();
                //PrintIM(DateTime.Now, e.IM.FromAgentName, "GroupMan Pro has encountered an error and a group invite could not be sent to: " + sGrp[2].ToString());
                textPrinter.PrintTextLine(String.Format(prefix + "(\nGroupMan Pro @ " + gmanlocation + ")\nGroupMan Pro has encountered an error and a group invite could not be sent to: " + gavname));
                OpenMetaverse.Logger.Log(String.Format(prefix + "GroupMan Pro Error: {0}", excp), Helpers.LogLevel.Error);
                return;
            }

            string gname = string.Empty;
 
            if (instance.State.GroupStore.ContainsKey(igroup))
            {
                gname = "for group " + instance.State.GroupStore[igroup];
            }

            textPrinter.SetFontStyle(FontStyle.Bold);
            textPrinter.PrintTextLine(prefix + "\n\n[ GroupMan Pro ] @ " + gmanlocation + "\n   An invite has been sent to: " + gavname + " (" + iperson.ToString() + ")" + gname);
        }

        private void OnTimedEvent(object sender, ElapsedEventArgs e)
        {
            client.Groups.GroupMembersReply += new EventHandler<GroupMembersReplyEventArgs>(GroupMembersHandler);
            GroupRequestID = client.Groups.RequestGroupMembers(igroup);
            ismember = true;

            aTimer.Stop();
            aTimer.Enabled = false;
        }

        private void GivePresent()
        {
            if (!instance.Config.CurrentConfig.GivePresent) return;
 
            InventoryFolder presies = client.Inventory.Store.RootFolder;
            List<InventoryBase> foundfolders = client.Inventory.Store.GetContents(presies);

            foreach (InventoryBase o in foundfolders)
            {
                // for this to work the user needs to have a folder called "GroupMan Items"
                if (o.Name.ToLower() == "groupman items")
                {
                    if (o is InventoryFolder)
                    {
                        List<InventoryBase> founditems = client.Inventory.FolderContents(o.UUID, client.Self.AgentID, false, true, InventorySortOrder.ByName, 3000);
                        int icount = founditems.Count;
                        Random random = new Random();
                        int num = random.Next(icount);

                        InventoryItem item = (InventoryItem)founditems[num]; 

                        if ((item.Permissions.OwnerMask & PermissionMask.Transfer) == PermissionMask.Transfer)
                        {
                            DateTime dte = DateTime.Now;

                            dte = this.instance.State.GetTimeStamp(dte);

                            if (instance.Config.CurrentConfig.UseSLT)
                            {
                                string _timeZoneId = "Pacific Standard Time";
                                DateTime startTime = DateTime.UtcNow;
                                TimeZoneInfo tst = TimeZoneInfo.FindSystemTimeZoneById(_timeZoneId);
                                dte = TimeZoneInfo.ConvertTime(startTime, TimeZoneInfo.Utc, tst);
                            }

                            string prefix = dte.ToString("[HH:mm] ");

                            //Give the item
                            client.Self.InstantMessage(iperson, "I am giving you a present for joining our group. Thank you.");   
                            client.Inventory.GiveItem(item.UUID, item.Name, item.AssetType, iperson, false);
                            textPrinter.PrintTextLine(prefix + "\nGroupMan Pro: Gave '" + item.Name + "' as a joining present to " + gavname);
                        }
                    }
                }
            }
        }

        private void ProcessBufferItemResp(ChatBufferItem item, bool addToBuffer)
        {
            if (addToBuffer)
            {
                textBuffer.Add(item);

                int lines = textBuffer.Count;
                int maxlines = this.instance.Config.CurrentConfig.lineMax;

                if (lines > maxlines && maxlines > 0)
                {
                    CheckBufferSize();
                    return;
                }
            }

            textPrinter.SetSelectionForeColor(Color.Gray);
            DateTime dte = item.Timestamp;

            if (classiclayout)
            {
                if (showTimestamps)
                {
                    dte = this.instance.State.GetTimeStamp(dte);

                    //if (instance.Config.CurrentConfig.UseSLT)
                    //{
                    //    string _timeZoneId = "Pacific Standard Time";
                    //    DateTime startTime = DateTime.UtcNow;
                    //    TimeZoneInfo tst = TimeZoneInfo.FindSystemTimeZoneById(_timeZoneId);
                    //    dte = TimeZoneInfo.ConvertTime(startTime, TimeZoneInfo.Utc, tst);
                    //}

                    textPrinter.PrintText(dte.ToString("[HH:mm] "));
                }

                try
                {
                    if (item.Style != ChatBufferTextStyle.ObjectChat)
                    {
                        if (!string.IsNullOrEmpty(item.FromName))
                        {
                            textPrinter.PrintLink(item.FromName, item.Link + "&" + item.FromName);
                        }
                    }
                    else
                    {
                        textPrinter.PrintText(item.FromName);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("Chat Manager: " + ex.Message, Helpers.LogLevel.Error);
                }
            }
            else
            {
                try
                {
                    if (lastspeaker != item.FromName)
                    {
                        textPrinter.SetFontStyle(FontStyle.Bold);

                        if (item.Style != ChatBufferTextStyle.ObjectChat)
                        {
                            if (!string.IsNullOrEmpty(item.FromName))
                            {
                                textPrinter.PrintLinkHeader(item.FromName, item.FromUUID.ToString(), item.Link + "&" + item.FromName);
                            }
                        }
                        else
                        {
                            textPrinter.PrintHeader(item.FromName);
                        }

                        lastspeaker = item.FromName;
                    }
                }
                catch
                {
                    ;
                }

                textPrinter.SetFontStyle(FontStyle.Regular);
                textPrinter.SetSelectionBackColor(Color.White);
                //textPrinter.SetSelectionBackColor(instance.Config.CurrentConfig.BgColour);

                if (showTimestamps)
                {
                    dte = this.instance.State.GetTimeStamp(dte);

                    //if (instance.Config.CurrentConfig.UseSLT)
                    //{
                    //    string _timeZoneId = "Pacific Standard Time";
                    //    DateTime startTime = DateTime.UtcNow;
                    //    TimeZoneInfo tst = TimeZoneInfo.FindSystemTimeZoneById(_timeZoneId);
                    //    dte = TimeZoneInfo.ConvertTime(startTime, TimeZoneInfo.Utc, tst);
                    //}

                    string header = string.Empty;

                    header = "   [HH:mm] ";
                    //textPrinter.SetSelectionForeColor(Color.Gray);
                    //textPrinter.SetOffset(6);
                    //textPrinter.SetFontSize(6.5f);
                    textPrinter.PrintDate(dte.ToString(header));
                    //textPrinter.SetFontSize(8.5f);
                    //textPrinter.SetOffset(0);
                }
            }

            switch (item.Style)
            {
                case ChatBufferTextStyle.Normal:
                    textPrinter.SetSelectionForeColor(Color.Blue);
                    break;

                case ChatBufferTextStyle.StatusBlue:
                    textPrinter.SetSelectionForeColor(Color.BlueViolet);
                    break;

                case ChatBufferTextStyle.StatusDarkBlue:
                    textPrinter.SetSelectionForeColor(Color.White);
                    textPrinter.SetSelectionBackColor(Color.LightSeaGreen);
                    break;

                case ChatBufferTextStyle.LindenChat:
                    textPrinter.SetSelectionForeColor(Color.DarkGreen);
                    textPrinter.SetSelectionBackColor(Color.LightYellow);
                    break;

                case ChatBufferTextStyle.ObjectChat:
                    textPrinter.SetSelectionForeColor(Color.DarkCyan);
                    break;

                case ChatBufferTextStyle.StartupTitle:
                    textPrinter.SetSelectionForeColor(Color.Black);
                    textPrinter.SetFontStyle(FontStyle.Bold);
                    break;

                case ChatBufferTextStyle.Alert:
                    textPrinter.SetSelectionForeColor(Color.White);
                    textPrinter.SetSelectionBackColor(Color.BlueViolet);
                    break;

                case ChatBufferTextStyle.Error:
                    textPrinter.SetSelectionForeColor(Color.Yellow);
                    textPrinter.SetSelectionBackColor(Color.Red);
                    break;
            }

            textPrinter.PrintTextLine(item.Text);

            if (reprinting)
            {
                reprinting = false;
                return;
            }

            if (!classiclayout)
            {
                string txt = item.FromName + ": " + item.Text;
                LogMessage(dte, item.FromUUID.ToString(), item.FromName, txt);
            }
            else
            {
                LogMessage(dte, item.FromUUID.ToString(), item.FromName, item.Text);
            }
        }

        //Used only for non-public chat
        private void ProcessOutgoingChat(ChatSentEventArgs e)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("(channel ");
            sb.Append(e.Channel);

            if (classiclayout)
            {
                sb.Append(") You: ");
            }
            else
            {
                sb.Append(") ");
            }

            switch (e.Type)
            {
                case ChatType.Normal:
                    if (classiclayout)
                    {
                        sb.Append(": ");
                    }
                    break;

                case ChatType.Whisper:
                    sb.Append(" [whispers] ");
                    break;

                case ChatType.Shout:
                    sb.Append(" [shouts] ");
                    break;
            }

            sb.Append(e.Message);

            ChatBufferItem item = new ChatBufferItem(
                DateTime.Now, sb.ToString(), ChatBufferTextStyle.StatusDarkBlue, client.Self.Name);

            ProcessBufferItem(item, true);

            sb = null;
        }

        private void ProcessIncomingChat(ChatEventArgs e)
        {
            if (instance.IsAvatarMuted(e.OwnerID))
                return;

            if (string.IsNullOrEmpty(e.Message)) return;

            if (e.Message.Substring(0, 1) == "@") return;   // Ignore RLV commands

            StringBuilder sb = new StringBuilder();  

            if (e.Message.StartsWith("/me "))
            {
                sb.Append(e.FromName);
                sb.Append(e.Message.Substring(3));
            }
            else if (e.FromName.ToLower() == client.Self.Name.ToLower() && e.SourceType == ChatSourceType.Agent)
            {
                if (classiclayout)
                {
                    sb.Append(e.FromName);
                }

                switch (e.Type)
                {
                    case ChatType.Normal:
                        if (classiclayout)
                        {
                            sb.Append(": ");
                        }
                        else
                        {
                            sb.Append(" ");
                        }
                        break;

                    case ChatType.Whisper:
                        sb.Append(" [whispers] ");
                        break;

                    case ChatType.Shout:
                        sb.Append(" [shouts] ");
                        break;
                }

                sb.Append(e.Message);
            }
            else
            {               
                switch (e.Type)
                {
                    case ChatType.Normal:
                        if (classiclayout)
                        {
                            sb.Append(": ");
                        }
                        else
                        {
                            sb.Append(" ");
                        }
                        break;

                    case ChatType.Whisper:
                        sb.Append(" [whispers] ");
                        break;

                    case ChatType.Shout:
                        sb.Append(" [shouts] ");
                        break;
                    case ChatType.Debug:
                        //sb.Append(": ");
                        return;
                }

                sb.Append(e.Message);
            }

            ChatBufferItem item = new ChatBufferItem();
            item.Timestamp = DateTime.Now;
            item.Text = sb.ToString();
            item.FromName = e.FromName;
            item.Link = "http://mbprofile:" + e.OwnerID.ToString();

            switch (e.SourceType)
            {
                case ChatSourceType.Agent:
                    item.Style =
                        (e.FromName.EndsWith("Linden") ?
                        ChatBufferTextStyle.LindenChat : ChatBufferTextStyle.Normal);
                    break;

                case ChatSourceType.Object:
                    if (instance.IsAvatarMuted(e.SourceID))
                           return;

                    // Ignore RLV commands from objects
                    if (item.Text.StartsWith("@")) return;

                    item.Style = ChatBufferTextStyle.ObjectChat;
                    break;
            }

            if (e.FromName.ToLower() == client.Self.Name.ToLower())
            {
                ProcessBufferItem(item, true);
            }
            else
            {
                ProcessBufferItemResp(item, true);
            }

            sb = null;
        }

        private void ProcessIncomingDialog(ScriptDialogEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Message)) return;

            if (instance.IsAvatarMuted(e.ObjectID))
                return;

            (new frmDialog(instance, e)).ShowDialog();
        }

        private void LogMessage(DateTime timestamp, string uuid, string fromName, string msg)
        {
            if (!instance.Config.CurrentConfig.SaveChat)
                return;

            if (string.IsNullOrEmpty(fromName))
                return;

            string newsess = string.Empty;

            if (ChatLogin)
            {
                newsess = "\r\n\r\n\nNew login session...\r\n";
                ChatLogin = false;
            }

            string folder = instance.Config.CurrentConfig.LogDir;

            if (!folder.EndsWith("\\"))
            {
                folder += "\\";
            }

            // Log the message
            string filename = "CHAT-" + timestamp.Date.ToString() + "-" + client.Self.FirstName + " " + client.Self.LastName + ".txt";

            filename = filename.Replace("/", "-");
            //filename = filename.Replace(" ", "_");
            filename = filename.Replace(":", "-");

            StreamWriter SW;
            string path = folder + filename;
            string line = newsess + "[" + timestamp.ToShortTimeString() + "] " +msg;

            bool exists = false;

            // Check if the file exists
            try
            {
                exists = File.Exists(path);
            }
            catch
            {
                ;
            }

            if (exists)
            {
                try
                {
                    SW = File.AppendText(path);
                    SW.WriteLine(line);
                    SW.Dispose();
                }
                catch
                {
                    ;
                }
            }
            else
            {
                try
                {
                    SW = File.CreateText(path);
                    SW.WriteLine(line);
                    SW.Dispose();
                }
                catch (Exception ex)
                {
                    string exp = ex.Message;
                }
            }
        }

        public void ReprintAllText()
        {
            textPrinter.ClearText();

            try
            {
                foreach (ChatBufferItem item in textBuffer)
                {
                    //ProcessBufferItem(item, false);
                    if (item.FromName == client.Self.Name)
                    {
                        ProcessBufferItem(item, false);
                    }
                    else
                    {
                        ProcessBufferItemResp(item, false);
                    }
                }
            }
            catch
            {
                ;
            }
        }

        public void ClearInternalBuffer()
        {
            textBuffer.Clear();
        }

        public ITextPrinter TextPrinter
        {
            get { return textPrinter; }
            set { textPrinter = value; }
        }
    }
}