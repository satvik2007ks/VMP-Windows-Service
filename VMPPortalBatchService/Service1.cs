﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;

namespace VMPPortalBatchService
{
    public partial class Service1 : ServiceBase
    {
        public Service1()
        {
            InitializeComponent();
        }

        public static string conStr = ConfigurationManager.ConnectionStrings["vmpconStr"].ConnectionString;
        protected override void OnStart(string[] args)
        {
            try
            {
                FetchPortalResponse();
                Timer aTimer;
                aTimer = new System.Timers.Timer();

                // Hook up the Elapsed event for the timer.
                aTimer.Elapsed += new ElapsedEventHandler(OnTimedEvent);

                // Set the Interval 
                aTimer.Interval = Convert.ToInt64(ConfigurationManager.AppSettings["Interval"]);
                aTimer.Enabled = true;
            }
            catch (Exception ex)
            {
                this.WriteToFile("Error While Starting : " + ex.Message.ToString().Substring(0, 500),"Error");
                Notifyfailure(ex.Message.ToString().Length >= 250 ? ex.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : ex.Message.ToString().Replace('\'', '\"').TrimEnd('.'));
            }
        }

        private static void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            Service1 objReadPortalResponse = new Service1();
            objReadPortalResponse.FetchPortalResponse();
        }
        private async void FetchPortalResponse()
        {
            this.WriteToFile("VMP Batch Process Started {0}","Log");
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                string response = string.Empty;
                try
                {
                    Uri geturi = new Uri(ConfigurationManager.AppSettings["PortalJsonURL"]); //Response from Portal in Json Format
                    System.Net.Http.HttpClient client = new System.Net.Http.HttpClient();
                    var byteArray = Encoding.ASCII.GetBytes(ConfigurationManager.AppSettings["ByteArray"]);
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                    System.Net.Http.HttpResponseMessage responseGet = await client.GetAsync(geturi);
                    response = await responseGet.Content.ReadAsStringAsync();
                }
                catch(Exception ex)
                {
                    this.WriteToFile("Error While Reading the API: " + ex.Message.ToString() + " {0}", "Error");
                    this.WriteToFile("Inner Exception:" + ex.InnerException.Message.ToString() + " {0}", "Error");
                    Notifyfailure(ex.Message.ToString().Length >= 250 ? ex.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : ex.Message.ToString().Replace('\'', '\"').TrimEnd('.'));

                }

                string path = ConfigurationManager.AppSettings["JsonFilePath"] + ConfigurationManager.AppSettings["JsonFileName"] + DateTime.Now.ToString("yyyyMdd") + ".json";
                File.WriteAllText(path, response);
                this.WriteToFile("JSON File Saved in : "+ path + " {0}", "Log");
                LoadApplicationsOnStageDB(LoadJson(path));
                DeleteOlderApp();
                purgeOlderFiles();
                this.WriteToFile("VMP Batch Process Completed {0}","Log");
            }
            catch (Exception ex)
            {
                this.WriteToFile("Error After reading API and While Saving Data: "+ ex.Message.ToString(),"Error");
                Notifyfailure(ex.Message.ToString().Length >= 250 ? ex.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : ex.Message.ToString().Replace('\'', '\"').TrimEnd('.'));
            }
        }

        private void DeleteOlderApp()
        {
            using (SqlConnection con = new SqlConnection(conStr))
            {
                try
                {
                    using (SqlCommand cmd = new SqlCommand("proc_DeleteApplication", con))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add("@DeleteDays", SqlDbType.Int).Value = int.Parse(ConfigurationManager.AppSettings["DeleteAppDuration"]);
                        con.Open();
                        cmd.ExecuteNonQuery();
                        this.WriteToFile("Older applications are deleted which are marked to delete {0} ", "Log");
                    }
                }
                catch (Exception ex)
                {
                    this.WriteToFile("Error while deleting Older application which are marked deleted: - " + ex.Message.ToString() + " {0}", "Error");
                    Notifyfailure(ex.Message.ToString().Length >= 250 ? ex.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : ex.Message.ToString().Replace('\'', '\"').TrimEnd('.'));
                }
            }
        }

        private void purgeOlderFiles()
        {
            try
            {
                string[] logfiles = Directory.GetFiles(ConfigurationManager.AppSettings["LogPath"]);
                string[] jsonfiles = Directory.GetFiles(ConfigurationManager.AppSettings["JsonFilePath"]);

                foreach (string file in logfiles)
                {
                    FileInfo fi = new FileInfo(file);
                    if (fi.LastAccessTime < DateTime.Now.AddDays(double.Parse(ConfigurationManager.AppSettings["PurgeFilesDuration"])))
                        fi.Delete();
                }
                foreach (string file in jsonfiles)
                {
                    FileInfo fi = new FileInfo(file);
                    fi.Delete();
                }
                this.WriteToFile("Log and Json Files Purged {0}", "Log");
            }
            catch (Exception ex)
            {
                this.WriteToFile("Error While Purging the files: " + ex.Message.ToString() + " {0}", "Error");
                Notifyfailure(ex.Message.ToString().Length >= 250 ? ex.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : ex.Message.ToString().Replace('\'', '\"').TrimEnd('.'));
            }

        }
        private void LoadApplicationsOnStageDB(JsonSchema.Applications applications)
        {
            int AppSkippedCount = 0;
            foreach (JsonSchema.Vendor vendor in applications.ApplicationList)
            {
                Int64 AppId = 0;
                if (!CheckAppExists(vendor.UUID))
                {
                    try
                    {
                        using (SqlConnection con = new SqlConnection(conStr))
                        {
                            SqlDataAdapter adapter;
                            using (SqlCommand cmd = new SqlCommand("proc_insertApplication", con))
                            {
                                cmd.CommandType = CommandType.StoredProcedure;
                                cmd.Parameters.Add("@uuid", SqlDbType.VarChar).Value = vendor.UUID;
                                cmd.Parameters.Add("@vendorName", SqlDbType.VarChar).Value = vendor.VendorName == null ? string.Empty : vendor.VendorName;
                                cmd.Parameters.Add("@businessName", SqlDbType.VarChar).Value = vendor.BusinessName == null ? string.Empty : vendor.BusinessName;
                                cmd.Parameters.Add("@productDescription", SqlDbType.VarChar).Value = vendor.ProductDescription == null ? string.Empty : vendor.ProductDescription;
                                cmd.Parameters.Add("@county", SqlDbType.VarChar).Value = vendor.County == null ? string.Empty : vendor.County;
                                cmd.Parameters.Add("@telephone", SqlDbType.VarChar).Value = vendor.Telephone == null ? string.Empty : vendor.Telephone;
                                cmd.Parameters.Add("@emailId", SqlDbType.VarChar).Value = vendor.EMail == null ? string.Empty : vendor.EMail;
                                cmd.Parameters.Add("@fax", SqlDbType.VarChar).Value = vendor.Fax == null ? string.Empty : vendor.Fax;
                                cmd.Parameters.Add("@webAddress", SqlDbType.VarChar).Value = vendor.WebAddress == null ? string.Empty : vendor.WebAddress;
                                cmd.Parameters.Add("@state", SqlDbType.VarChar).Value = vendor.State == null ? string.Empty : vendor.State;
                                cmd.Parameters.Add("@city", SqlDbType.VarChar).Value = vendor.City == null ? string.Empty : vendor.City;
                                cmd.Parameters.Add("@address", SqlDbType.VarChar).Value = vendor.Address == null ? string.Empty : vendor.Address;
                                cmd.Parameters.Add("@zipCode", SqlDbType.VarChar).Value = vendor.Zipcode == null ? string.Empty : vendor.Zipcode;
                                try
                                {
                                    DataSet ds = new DataSet();
                                    con.Open();
                                    adapter = new SqlDataAdapter(cmd);
                                    adapter.Fill(ds);
                                    if (ds.Tables[0].Rows.Count > 0)
                                    {
                                        AppId = Int64.Parse(ds.Tables[0].Rows[0][0].ToString());
                                        this.WriteToFile("Application Basic details saved sucessfully. App ID: " + AppId + " {0}", "Log");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    this.WriteToFile("Error While inserting in Vendor Details Table: " + ex.Message.ToString() + " {0}", "Error");
                                    SaveUUIDAppName(vendor);
                                }
                                finally
                                {
                                    con.Close();
                                }
                                if (AppId > 0)
                                {
                                    UpdateFirmBusinessStructure(AppId, vendor);
                                    UpdateOwnerInfo(AppId, vendor.owner);
                                    DownloadAttachment(vendor.AttachmentList, AppId);
                                }
                            }
                        }
                    }

                    catch (Exception ex)
                    {
                        this.WriteToFile("Error While inserting in Vendor Details Table: " + ex.Message.ToString() + " {0}", "Error");
                        Notifyfailure(ex.Message.ToString().Length >= 250 ? ex.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : ex.Message.ToString().Replace('\'', '\"').TrimEnd('.'));
                    }

                }
                else
                {
                    AppSkippedCount++;
                }
            }
            if((applications.ApplicationList.Count - AppSkippedCount) > 0)
            {
                NotifyApplicationOwner(applications.ApplicationList.Count - AppSkippedCount);
            }
        }

        private bool CheckAppExists(string UUID)
        {
            bool retBool = false;

            SqlConnection conn = new SqlConnection(conStr);
            SqlCommand command = new SqlCommand("select * from VendorDetails where uuid = '"+ UUID+"'", conn);
            try
            {
                conn.Open();
                SqlDataReader reader;
                reader = command.ExecuteReader();
                if (reader.HasRows)
                {
                    retBool = true;
                }
                conn.Close();
            }
            catch (Exception ex)
            {
                this.WriteToFile("Error While Checking Existing App: " + ex.Message.ToString() + " {0}", "Error");
                Notifyfailure(ex.Message.ToString().Length >= 250 ? ex.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : ex.Message.ToString().Replace('\'', '\"').TrimEnd('.'));
            }
            finally
            {
                conn.Close();
            }
            return retBool;
        }

        private void SaveUUIDAppName(JsonSchema.Vendor vendor)
        {
            Int64 AppId = 0;
            try
            {
                using (SqlConnection con = new SqlConnection(conStr))
                {
                    SqlDataAdapter adapter;
                    using (SqlCommand cmd = new SqlCommand("proc_insertApplication", con))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add("@uuid", SqlDbType.VarChar).Value = vendor.UUID;
                        cmd.Parameters.Add("@vendorName", SqlDbType.VarChar).Value = vendor.VendorName == null ? string.Empty : vendor.VendorName;
                        cmd.Parameters.Add("@businessName", SqlDbType.VarChar).Value = DBNull.Value;
                        cmd.Parameters.Add("@productDescription", SqlDbType.VarChar).Value = DBNull.Value;
                        cmd.Parameters.Add("@county", SqlDbType.VarChar).Value = DBNull.Value;
                        cmd.Parameters.Add("@telephone", SqlDbType.VarChar).Value = DBNull.Value;
                        cmd.Parameters.Add("@emailId", SqlDbType.VarChar).Value = DBNull.Value;
                        cmd.Parameters.Add("@fax", SqlDbType.VarChar).Value = DBNull.Value;
                        cmd.Parameters.Add("@webAddress", SqlDbType.VarChar).Value = DBNull.Value;
                        cmd.Parameters.Add("@state", SqlDbType.VarChar).Value = DBNull.Value;
                        cmd.Parameters.Add("@city", SqlDbType.VarChar).Value = DBNull.Value;
                        cmd.Parameters.Add("@address", SqlDbType.VarChar).Value = DBNull.Value;
                        cmd.Parameters.Add("@zipCode", SqlDbType.VarChar).Value = DBNull.Value;
                        try
                        {
                            DataSet ds = new DataSet();
                            con.Open();
                            adapter = new SqlDataAdapter(cmd);
                            adapter.Fill(ds);
                            if (ds.Tables[0].Rows.Count > 0)
                            {
                                AppId = Int64.Parse(ds.Tables[0].Rows[0][0].ToString());
                                this.WriteToFile("Application Basic (Only UUID and Application Name) details saved sucessfully. App ID: " + AppId + " {0}", "Log");
                            }
                        }
                        catch (Exception ex)
                        {
                            this.WriteToFile("Error While inserting in Vendor Details (Only UUID and Application Name) Table: " + ex.Message.ToString() + " {0}", "Error");
                       }
                        finally
                        {
                            con.Close();
                        }
                        if (AppId > 0)
                        {
                            UpdateFirmBusinessStructure(AppId, vendor);
                            UpdateOwnerInfo(AppId, vendor.owner);
                            DownloadAttachment(vendor.AttachmentList, AppId);
                        }
                    }
                }
            }

            catch (Exception ex)
            {
                this.WriteToFile("Error While inserting in Vendor Details(Only UUID and Application Name) Table: " + ex.Message.ToString() + " {0}", "Error");
                Notifyfailure(ex.Message.ToString().Length >= 250 ? ex.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : ex.Message.ToString().Replace('\'', '\"').TrimEnd('.'));
            }
        }

        private void UpdateOwnerInfo(long appId, List<JsonSchema.Owners> owner)
        {
            try
            {
                DataTable dtOwners = new DataTable();
                dtOwners.Columns.Add("appID");
                dtOwners.Columns.Add("ownerFirstName");
                dtOwners.Columns.Add("ownerLastName");
                dtOwners.Columns.Add("address");
                dtOwners.Columns.Add("city");
                dtOwners.Columns.Add("State");
                dtOwners.Columns.Add("zipCode");
                dtOwners.Columns.Add("Phone");
                dtOwners.Columns.Add("otherPhone");
                dtOwners.Columns.Add("percentageOwnership");
                dtOwners.Columns.Add("citizen");
                dtOwners.Columns.Add("Asian");
                dtOwners.Columns.Add("Black");
                dtOwners.Columns.Add("Hispanic");
                dtOwners.Columns.Add("AmericanIndian");
                dtOwners.Columns.Add("IndiginousAmerican");
                dtOwners.Columns.Add("TribalID");
                dtOwners.Columns.Add("Woman");
                dtOwners.Columns.Add("PhysicalDisability");
                dtOwners.Columns.Add("Veteran");
                dtOwners.Columns.Add("serviceDisabled");
                dtOwners.Columns.Add("NumberOfShares");
                dtOwners.Columns.Add("CurrentPosition ");
                dtOwners.Columns.Add("busPlanning");
                dtOwners.Columns.Add("salesMarket");
                dtOwners.Columns.Add("financial");
                dtOwners.Columns.Add("personnel");
                dtOwners.Columns.Add("projManagement");
                dtOwners.Columns.Add("assets");
                dtOwners.Columns.Add("liabilities");
                dtOwners.Columns.Add("personalNetWorth");

                DataTable dtSecFirms = new DataTable();
                dtSecFirms.Columns.Add("appID");
                dtSecFirms.Columns.Add("ownerFirstName");
                dtSecFirms.Columns.Add("ownerLastName");
                dtSecFirms.Columns.Add("FirmName");
                dtSecFirms.Columns.Add("FirmAddress");
                dtSecFirms.Columns.Add("FirmPercentageOwnership ");
                dtSecFirms.Columns.Add("FirmGrossRevenue");

                foreach (JsonSchema.Owners Owner in owner)
                {
                    dtOwners.Rows.Add(appId, Owner.FirstName, Owner.LastName, Owner.Address, Owner.City, Owner.State, Owner.ZipCode, Owner.Phone, Owner.OtherPhone, Owner.PercentageOwnership,
                        Owner.Citizen, Owner.Asian, Owner.Black, Owner.Hispanic, Owner.AmericanIndian, Owner.IndiginousAmerican, Owner.TribalID, Owner.Woman, Owner.PhysicallyDisabled, Owner.Veteran, Owner.ServiceDisabledVeteran, Owner.NoOfSharesOwned, Owner.CurrentPosition, Owner.BusinessPlanning, Owner.SalesMarketing, Owner.Financial, Owner.Personnel, Owner.ProjectManagement,Owner.CurrentAsset,Owner.TotalLiabilities,Owner.PersonalNetworth);
                    foreach (JsonSchema.SecondaryFirms secFirms in Owner.SecondaryFirm)
                    {
                        dtSecFirms.Rows.Add(appId, Owner.FirstName, Owner.LastName, secFirms.FirmName, secFirms.FirmAddress, secFirms.FirmPercentageOwnership, secFirms.FirmGrossRevenue);
                    }
                }
                if (dtOwners.Rows.Count != 0)
                {
                    UpdateOwners("[proc_UpdateOwners]", "@owners", dtOwners, appId);
                    this.WriteToFile("Application Owner details saved sucessfully. App ID: " + appId + " {0}", "Log");
                }
                if (dtSecFirms.Rows.Count != 0)
                {
                    UpdateSecFirms("[proc_UpdateSecFirms]", "@secFirms", dtSecFirms, appId);
                    UpdateOwnerId(appId);
                    this.WriteToFile("Application Secondary Firm details saved sucessfully. App ID: " + appId + " {0}", "Log");
                }

            }
            catch (Exception ex)
            {
                this.WriteToFile("Error While inserting Owner details - App ID: " + appId + " - " + ex.Message.ToString() + " {0}", "Error");
                Notifyfailure(ex.Message.ToString().Length >= 250 ? ex.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : ex.Message.ToString().Replace('\'', '\"').TrimEnd('.'));
            }

        }

        private void NotifyApplicationOwner(int count)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls;
                System.Net.Mail.MailMessage mail = new System.Net.Mail.MailMessage();
                SmtpClient SmtpServer = new SmtpClient(ConfigurationManager.AppSettings["SmtpClient"]);
                mail.From = new MailAddress(ConfigurationManager.AppSettings["FromEmailAddress"]);
                mail.To.Add(ConfigurationManager.AppSettings["ToEmailAddress"]);
                mail.Subject = ConfigurationManager.AppSettings["EmailSubject"] + " - " + ConfigurationManager.AppSettings["Environment"];
                mail.Body = count + ConfigurationManager.AppSettings["EmailBody"];
                SmtpServer.Port = int.Parse(ConfigurationManager.AppSettings["EmailPort"]);
                SmtpServer.EnableSsl = Boolean.Parse(ConfigurationManager.AppSettings["EnableSSL"]);
                SmtpServer.Send(mail);
            }
            catch (Exception ex)
            {
                this.WriteToFile("Error while sending email to notify the applicaiton owner: " + ex.Message.ToString() + " {0}", "Error");
                this.WriteToFile("Error while sending email to notify the applicaiton owner- Inner Exception: " + ex.InnerException.Message.ToString() + " {0}", "Error");
            }
        }
        private void Notifyfailure(string ErrorMessage)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls;
                System.Net.Mail.MailMessage mail = new System.Net.Mail.MailMessage();
                SmtpClient SmtpServer = new SmtpClient(ConfigurationManager.AppSettings["SmtpClient"]);
                mail.From = new MailAddress(ConfigurationManager.AppSettings["FromEmailAddress"]);
                mail.To.Add(ConfigurationManager.AppSettings["ToEmailAddressMnit"]);
                mail.Subject = ConfigurationManager.AppSettings["ErrorEmailSubject"] + " - " + ConfigurationManager.AppSettings["Environment"];
                mail.Body = ConfigurationManager.AppSettings["ErrorEmailSubject"] + ErrorMessage;
                SmtpServer.Port = int.Parse(ConfigurationManager.AppSettings["EmailPort"]);
                SmtpServer.EnableSsl = Boolean.Parse(ConfigurationManager.AppSettings["EnableSSL"]);
                SmtpServer.Send(mail);
            }
            catch (Exception ex)
            {
                this.WriteToFile("Error while sending email to notify the applicaiton owner: " + ex.Message.ToString() + " {0}", "Error");
            }
        }
        private void NotifyProcessStop()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls;
                System.Net.Mail.MailMessage mail = new System.Net.Mail.MailMessage();
                SmtpClient SmtpServer = new SmtpClient(ConfigurationManager.AppSettings["SmtpClient"]);
                mail.From = new MailAddress(ConfigurationManager.AppSettings["FromEmailAddress"]);
                mail.To.Add(ConfigurationManager.AppSettings["ToEmailAddressMnit"]);
                mail.Subject = ConfigurationManager.AppSettings["StopServiceMessage"] + " - " + ConfigurationManager.AppSettings["Environment"];
                mail.Body = ConfigurationManager.AppSettings["StopServiceMessage"];
                SmtpServer.Port = int.Parse(ConfigurationManager.AppSettings["EmailPort"]);
                SmtpServer.EnableSsl = Boolean.Parse(ConfigurationManager.AppSettings["EnableSSL"]);
                SmtpServer.Send(mail);
            }
            catch (Exception ex)
            {
                this.WriteToFile("Error while sending email to notify the applicaiton owner: " + ex.Message.ToString() + " {0}", "Error");
            }
        }

        private void DownloadAttachment(List<JsonSchema.Attachments> attachmentList, long AppId)
        {
            try
            {
                WebClient myWebClient = new WebClient();
                string path = ConfigurationManager.AppSettings["AttachmentPath"] + "\\" + AppId + "\\";
                DataTable dtAttachments = new DataTable();
                dtAttachments.Columns.Add("appID");
                dtAttachments.Columns.Add("AttachmentDescription");
                dtAttachments.Columns.Add("AttachmentType");
                dtAttachments.Columns.Add("AttachmentUrl");
                dtAttachments.Columns.Add("AttachmentPath");
                if (!(Directory.Exists(path)))
                {
                    Directory.CreateDirectory(path);
                }
                string fileName = string.Empty;
                int attachmentcount = 1;
                foreach (JsonSchema.Attachments attachment in attachmentList)
                {
                    if (attachment.AttachmentDescription != string.Empty)
                    {
                        fileName = attachment.AttachmentDescription.Length > 20 ? attachment.AttachmentDescription.Substring(0, 19) : attachment.AttachmentDescription;
                    }
                    else
                    {
                        if (attachment.AttachmentType != string.Empty)
                        {
                            fileName = attachment.AttachmentType.Length > 20 ? attachment.AttachmentType.Substring(0, 19) : attachment.AttachmentType;
                        }
                        else
                        {
                            fileName = "Attachment_" + AppId + "_" + attachmentcount;
                        }

                    }
                    fileName = fileName + "_"+ attachmentcount;
                    try
                    {
                        String[] splitAttachment = attachment.AttachmentPath.Split('?');
                        string ext = Path.GetExtension(splitAttachment[0]);
                        fileName = Regex.Replace(fileName, @"[^0-9a-zA-Z_\s]+", "");
                        fileName = path + fileName + ext;
                        myWebClient.DownloadFile(attachment.AttachmentPath, fileName);
                    }
                    catch (Exception ex)
                    {
                        this.WriteToFile("Error Downloading the attachment - App ID: " + AppId + " - " + ex.Message.ToString() + " {0}", "Error");
                    }
                    dtAttachments.Rows.Add(AppId, attachment.AttachmentDescription, attachment.AttachmentType, fileName);
                    ++attachmentcount;
                }
                SaveAttachmentLog(AppId, dtAttachments, "[proc_SaveAttachmentLog]", "@attachments");
                this.WriteToFile("Application Attachment & details saved sucessfully. App ID: " + AppId, "Log");
            }
            catch (Exception ex)
            {
                this.WriteToFile("Error Downloading the attachment - App ID: " + AppId + " - " + ex.Message.ToString() + " {0}", "Error");
                Notifyfailure(ex.Message.ToString().Length >= 250 ? ex.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : ex.Message.ToString().Replace('\'', '\"').TrimEnd('.'));
            }
        }

        private void SaveAttachmentLog(long appId, DataTable dtAttachments, string procedureName, string paramName)
        {
            SqlConnection conn = new SqlConnection(conStr);
            SqlCommand cmd = new SqlCommand(procedureName, conn);
            cmd.CommandType = CommandType.StoredProcedure;
            SqlParameter dtparam = cmd.Parameters.AddWithValue(paramName, dtAttachments);
            dtparam.SqlDbType = SqlDbType.Structured;
            try
            {
                conn.Open();
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                this.WriteToFile("Error While inserting Attchment log - App ID: " + appId + " - " + ex.Message.ToString() + " {0}", "Error");
                Notifyfailure(ex.Message.ToString().Length >= 250 ? ex.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : ex.Message.ToString().Replace('\'', '\"').TrimEnd('.'));
            }
            finally
            {
                conn.Close();
            }

        }


        private void UpdateOwners(string procedureName, string paramName, DataTable dtOwners, Int64 appId)
        {
            SqlConnection conn = new SqlConnection(conStr);
            SqlCommand cmd = new SqlCommand(procedureName, conn);
            cmd.CommandType = CommandType.StoredProcedure;
            SqlParameter dtparam = cmd.Parameters.AddWithValue(paramName, dtOwners);
            dtparam.SqlDbType = SqlDbType.Structured;
            try
            {
                conn.Open();
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                this.WriteToFile("Error While inserting Owners - App ID: " + appId + " - " + ex.Message.ToString() + " {0}", "Error");
                Notifyfailure(ex.Message.ToString().Length >= 250 ? ex.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : ex.Message.ToString().Replace('\'', '\"').TrimEnd('.'));
            }
            finally
            {
                conn.Close();
            }

        }

        private void UpdateSecFirms(string procedureName, string paramName, DataTable dtSecFirms, Int64 appId)
        {
            SqlConnection conn = new SqlConnection(conStr);
            SqlCommand cmd = new SqlCommand(procedureName, conn);
            cmd.CommandType = CommandType.StoredProcedure;
            SqlParameter dtparam = cmd.Parameters.AddWithValue(paramName, dtSecFirms);
            dtparam.SqlDbType = SqlDbType.Structured;
            try
            {
                conn.Open();
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                this.WriteToFile("Error While inserting Secondary Firms - App ID: " + appId + " - " + ex.Message.ToString() + " {0}", "Error");
                Notifyfailure(ex.Message.ToString().Length >= 250 ? ex.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : ex.Message.ToString().Replace('\'', '\"').TrimEnd('.'));
            }
            finally
            {
                conn.Close();
            }

        }

        private void UpdateOwnerId(long appId)
        {
            using (SqlConnection con = new SqlConnection(conStr))
            {
                try
                {
                    using (SqlCommand cmd = new SqlCommand("proc_UpdateOwnerID", con))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add("@appId", SqlDbType.BigInt).Value = appId;
                        con.Open();
                        cmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    this.WriteToFile("Error While inserting in Firm and Business Structure - App ID: " + appId + " - " + ex.Message.ToString() + " {0}", "Error");
                    Notifyfailure(ex.Message.ToString().Length >= 250 ? ex.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : ex.Message.ToString().Replace('\'', '\"').TrimEnd('.'));
                }
            }
        }

        private void UpdateFirmBusinessStructure(long appId, JsonSchema.Vendor vendor)
        {
            using (SqlConnection con = new SqlConnection(conStr))
            {
                try
                {
                    SqlDataAdapter adapter;
                    Int64 FirmId = 0;
                    using (SqlCommand cmd = new SqlCommand("proc_InsertFirmBusStructure", con))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add("@uuid", SqlDbType.VarChar).Value = vendor.UUID;
                        cmd.Parameters.Add("@appID", SqlDbType.BigInt).Value = appId;
                        if (string.IsNullOrEmpty(vendor.CurrentOwnershipDate))
                        {
                            cmd.Parameters.Add("@dateCurrentOwnership", SqlDbType.Date).Value = DBNull.Value;
                        }
                        else
                        {
                            cmd.Parameters.Add("@dateCurrentOwnership", SqlDbType.Date).Value = vendor.CurrentOwnershipDate;
                        }
                        if (string.IsNullOrEmpty(vendor.RegisteredDate))
                        {
                            cmd.Parameters.Add("@dateRegistered", SqlDbType.Date).Value = DBNull.Value;
                        }
                        else
                        {
                            cmd.Parameters.Add("@dateRegistered", SqlDbType.Date).Value = vendor.RegisteredDate;
                        }
                        cmd.Parameters.Add("@hrCertificate", SqlDbType.VarChar).Value = vendor.HRCertificate == null ? string.Empty : vendor.HRCertificate;
                        cmd.Parameters.Add("@methodAcquis", SqlDbType.VarChar).Value = vendor.MethodOfAquisition == null ? string.Empty : vendor.MethodOfAquisition;
                        cmd.Parameters.Add("@fullTimeEmp", SqlDbType.VarChar).Value = vendor.FullTimeEmployees;
                        cmd.Parameters.Add("@mnTaxId", SqlDbType.VarChar).Value = vendor.MNTaxID == null ? string.Empty : vendor.MNTaxID;
                        cmd.Parameters.Add("@federalTaxId", SqlDbType.VarChar).Value = vendor.FederalTaxID == null ? string.Empty : vendor.FederalTaxID;
                        cmd.Parameters.Add("@nonProfit", SqlDbType.Bit).Value = vendor.NonProfit;
                        cmd.Parameters.Add("@rehabFac", SqlDbType.Bit).Value = vendor.Rehabilitation;
                        cmd.Parameters.Add("@llc", SqlDbType.Bit).Value = vendor.LLCLLP;
                        cmd.Parameters.Add("@proprietorship", SqlDbType.Bit).Value = vendor.Proprietorship;
                        cmd.Parameters.Add("@partnership", SqlDbType.Bit).Value = vendor.Partnership;
                        cmd.Parameters.Add("@corporation", SqlDbType.Bit).Value = vendor.Corporation;
                        cmd.Parameters.Add("@DBE", SqlDbType.Bit).Value = vendor.DBE;
                        cmd.Parameters.Add("@USDVA", SqlDbType.Bit).Value = vendor.USDVA;
                        cmd.Parameters.Add("@WBENC", SqlDbType.Bit).Value = vendor.WBENC;
                        cmd.Parameters.Add("@NCMSDC", SqlDbType.Bit).Value = vendor.NCMSDC;
                        cmd.Parameters.Add("@NGLCC", SqlDbType.Bit).Value = vendor.NGLCC;
                        DataSet ds = new DataSet();
                        con.Open();
                        adapter = new SqlDataAdapter(cmd);
                        adapter.Fill(ds);
                        FirmId = Int64.Parse(ds.Tables[0].Rows[0][0].ToString());

                        DataTable dtGrossRevenue = new DataTable();
                        dtGrossRevenue.Columns.Add("firmId");
                        dtGrossRevenue.Columns.Add("grossYear");
                        dtGrossRevenue.Columns.Add("grossRevenue");
                        dtGrossRevenue.Rows.Add(FirmId, vendor.GrossYear1, vendor.GrossRevenue1);
                        dtGrossRevenue.Rows.Add(FirmId, vendor.GrossYear2, vendor.GrossRevenue2);
                        dtGrossRevenue.Rows.Add(FirmId, vendor.GrossYear3, vendor.GrossRevenue3);

                        if (dtGrossRevenue.Rows.Count != 0)
                        {
                            UpdateGrossRevenue("[proc_VendorGrossRevenue]", "@grossRevArray", dtGrossRevenue, appId);
                        }

                        this.WriteToFile("Saved Firm and Business Structure Sucessfully", "Log");
                    }
                }
                catch (Exception ex)
                {
                    this.WriteToFile("Error While inserting in Firm and Business Structure - App ID: " + appId + " - " + ex.Message.ToString() + " {0}", "Error");
                    Notifyfailure(ex.Message.ToString().Length >= 250 ? ex.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : ex.Message.ToString().Replace('\'', '\"').TrimEnd('.'));
                }
                finally
                {
                    con.Close();
                }
            }
        }

        private void UpdateGrossRevenue(string procedureName, string paramName, DataTable dtGrossRevenue, Int64 appId)
        {
            SqlConnection conn = new SqlConnection(conStr);
            SqlCommand cmd = new SqlCommand(procedureName, conn);
            cmd.CommandType = CommandType.StoredProcedure;
            SqlParameter dtparam = cmd.Parameters.AddWithValue(paramName, dtGrossRevenue);
            dtparam.SqlDbType = SqlDbType.Structured;
            try
            {
                conn.Open();
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                this.WriteToFile("Error While inserting Gross Revenue - App ID: " + appId + " - " + ex.Message.ToString() + " {0}", "Error");
                Notifyfailure(ex.Message.ToString().Length >= 250 ? ex.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : ex.Message.ToString().Replace('\'', '\"').TrimEnd('.'));
            }
            finally
            {
                conn.Close();
            }
        }


        protected override void OnStop()
        {
            NotifyProcessStop();
            this.WriteToFile("VMP Batch Process Stopped {0}","Log");
        }
        
        public JsonSchema.Applications LoadJson(string url)
        {
            JsonSchema.Applications objApplications = new JsonSchema.Applications();
            using (StreamReader r = new StreamReader(url))
            {
                string json = r.ReadToEnd();
                dynamic array = JsonConvert.DeserializeObject(json);
                foreach (dynamic application in array)
                {
                    try
                    {
                        JsonSchema.Vendor objVendor = new JsonSchema.Vendor();
                        foreach (dynamic node in application)
                        {
                            foreach (dynamic childnode in node)
                            {
                                try
                                {
                                    string tagname = childnode.Name;
                                    switch (tagname)
                                    {
                                        case "uuid":
                                            try { objVendor.UUID = childnode.Value.value.Value; } catch { objVendor.UUID = string.Empty; }
                                            break;
                                        case "attachment_count":
                                            try { objVendor.AttachmentCount = childnode.Value.Value; } catch { objVendor.AttachmentCount = 0; }
                                            break;
                                        case "attachments":
                                            foreach (dynamic attachment in childnode.Value)
                                            {
                                                JsonSchema.Attachments objAttachment = new JsonSchema.Attachments();
                                                try { objAttachment.AttachmentType = attachment.Value.attachment_category.value.label.Value; } catch { objAttachment.AttachmentType = string.Empty; }
                                                try { objAttachment.AttachmentDescription = attachment.Value.description.value.value.Value; } catch { objAttachment.AttachmentDescription = string.Empty; }
                                                try { objAttachment.AttachmentPath = attachment.Value.file.value.url.Value; } catch { objAttachment.AttachmentPath = string.Empty; }
                                                objVendor.AttachmentList.Add(objAttachment);
                                            }
                                            break;
                                        case "dba_name":
                                            try { objVendor.VendorName = childnode.Value.value.Value; } catch { objVendor.VendorName = string.Empty; }
                                            break;
                                        case "legal_firm_name":
                                            try { objVendor.BusinessName = childnode.Value.value.Value; } catch { objVendor.BusinessName = string.Empty; }
                                            break;
                                        case "describe_activities":
                                            try { objVendor.ProductDescription = childnode.Value.value.Value; } catch { objVendor.ProductDescription = string.Empty; }
                                            break;
                                        case "address":
                                            try { objVendor.State = childnode.Last.value.administrative_area.Value; } catch { objVendor.State = string.Empty; }
                                            try { objVendor.City = childnode.Last.value.locality.Value; } catch { objVendor.City = string.Empty; }
                                            try { objVendor.Address = childnode.Last.value.address_line1.Value + " " + childnode.Last.value.address_line2.Value + " " + childnode.Last.value.address_line3.Value; } catch { objVendor.Address = string.Empty; }
                                            try { objVendor.Zipcode = childnode.Last.value.postal_code.Value; } catch { objVendor.Zipcode = string.Empty; }
                                            break;
                                        case "county":
                                            try { objVendor.County = childnode.Value.value.label.Value; } catch { objVendor.County = string.Empty; }
                                            break;
                                        case "business_telephone":
                                            try { objVendor.Telephone = childnode.Value.value.Value; } catch { objVendor.Telephone = string.Empty; }
                                            break;
                                        case "business_email_address":
                                            try { objVendor.EMail = childnode.Value.value.Value; } catch { objVendor.EMail = string.Empty; }
                                            break;
                                        case "business_website":
                                            try { objVendor.WebAddress = childnode.Value.value.Value; } catch { objVendor.WebAddress = string.Empty; }
                                            break;
                                        case "business_fax":
                                            try { objVendor.Fax = childnode.Value.value.Value; } catch { objVendor.Fax = string.Empty; }
                                            break;
                                        case "registration_date":
                                            try { objVendor.RegisteredDate = childnode.Value.value.Value; } catch { objVendor.RegisteredDate = string.Empty; }
                                            break;
                                        case "date_ownership_was_acquire":
                                            try { objVendor.CurrentOwnershipDate = childnode.Value.value.Value; } catch { objVendor.CurrentOwnershipDate = string.Empty; }
                                            break;
                                        case "submitted":
                                            try { objVendor.ApplicationDate = childnode.Value.value.Value; } catch { objVendor.ApplicationDate = string.Empty; }
                                            break;
                                        case "current_certifications":
                                            try
                                            {
                                                for (int i = 0; i < childnode.Value.value.Count; i++)
                                                {
                                                    if (childnode.Value.value[i].value.Value == "State of Minnesota Human Rights – Workforce Certificate of Compliance")
                                                    {
                                                        objVendor.HRCertificate = "Yes";
                                                    }
                                                    if (childnode.Value.value[i].value.Value == "MnUCP (any certifying member agency)")
                                                    {
                                                        objVendor.DBE = true;
                                                    }
                                                    else if (childnode.Value.value[i].value.Value == "NMSDC (National Minority Supplier Development Council, and regional affiliate councils)")
                                                    {
                                                        objVendor.NCMSDC = true;
                                                    }
                                                    else if (childnode.Value.value[i].value.Value == "NGLCC (National Gay and Lesbian Chamber of Commerce)")
                                                    {
                                                        objVendor.NGLCC = true;
                                                    }
                                                    else if (childnode.Value.value[i].value.Value == "WBENC (Women Business Enterprise National Council)")
                                                    {
                                                        objVendor.WBENC = true;
                                                    }
                                                    else if (childnode.Value.value[i].value.Value == "USDVA (U.S. Dept of Veterans Affairs)")
                                                    {
                                                        objVendor.USDVA = true;
                                                    }
                                                }
                                            }
                                            catch
                                            {

                                            }
                                            break;
                                        case "method_of_acquisition":
                                            try
                                            {
                                                if (childnode.Value.value.Value == "Started new business")
                                                {
                                                    objVendor.MethodOfAquisition = "Founded";
                                                }
                                                else if (childnode.Value.value.Value == "Bought existing business" || childnode.Value.value.Value == "Inherited business" || childnode.Value.value.Value == "Merger or consolidation")
                                                {
                                                    objVendor.MethodOfAquisition = "Purchased Existing";
                                                }
                                                else if (childnode.Value.value.Value == "Secured concession")
                                                {
                                                    objVendor.MethodOfAquisition = "Franchise";
                                                }
                                            }
                                            catch
                                            {

                                            }
                                            break;
                                        case "full_time":
                                            try { objVendor.FullTimeEmployees = childnode.Value.value.Value; } catch { objVendor.FullTimeEmployees = string.Empty; }
                                            break;
                                        case "state_tax_id":
                                            try { objVendor.MNTaxID = childnode.Value.value.Value; } catch { objVendor.MNTaxID = string.Empty; }
                                            break;
                                        case "federal_tax_id_ein":
                                            try { objVendor.FederalTaxID = childnode.Value.value.Value; } catch { objVendor.FederalTaxID = string.Empty; }
                                            break;
                                        case "specify_the_gross_receipts":
                                            try
                                            {
                                                for (int i = 0; i < childnode.Value.value.Count; i++)
                                                {
                                                    if (i == 0)
                                                    {
                                                        objVendor.GrossRevenue3 = childnode.Value.value[i].value_1_value.value.Value;
                                                        objVendor.GrossYear3 = childnode.Value.value[i].value_0_value.value.Value;
                                                    }
                                                    else if (i == 1)
                                                    {
                                                        objVendor.GrossRevenue2 = childnode.Value.value[i].value_1_value.value.Value;
                                                        objVendor.GrossYear2 = childnode.Value.value[i].value_0_value.value.Value;
                                                    }
                                                    else if (i == 2)
                                                    {
                                                        objVendor.GrossRevenue1 = childnode.Value.value[i].value_1_value.value.Value;
                                                        objVendor.GrossYear1 = childnode.Value.value[i].value_0_value.value.Value;
                                                    }
                                                }
                                            }
                                            catch
                                            {

                                            }
                                            break;
                                        case "business_structure":
                                            string business;
                                            try { business = childnode.Value.value.Value; } catch { business = string.Empty; }
                                            if (business == "sole")
                                            {
                                                objVendor.Proprietorship = true;
                                            }
                                            else if (business == "partnership" || business == "joint")
                                            {
                                                objVendor.Partnership = true;
                                            }
                                            else if (business == "corporation")
                                            {
                                                objVendor.Corporation = true;
                                            }
                                            else if (business == "llp" || business == "llc")
                                            {
                                                objVendor.LLCLLP = true;
                                            }
                                            break;
                                        case "business_type":

                                            try
                                            {
                                                if (childnode.Value.value.Value == "non-profit-rehab")
                                                {
                                                    objVendor.Rehabilitation = true;
                                                    objVendor.NonProfit = true;
                                                    objVendor.Proprietorship = false;
                                                    objVendor.Partnership = false;
                                                    objVendor.LLCLLP = false;
                                                    objVendor.Corporation = false;
                                                }
                                            }
                                            catch { }
                                            break;
                                        case "owners":
                                            foreach (dynamic ownerNode in childnode.Value)
                                            {
                                                JsonSchema.Owners objOwner = new JsonSchema.Owners();
                                                try { objOwner.OwnerUUID = ownerNode.Value.uuid.value.Value; } catch { objOwner.OwnerUUID = string.Empty; }
                                                try { objOwner.FirstName = ownerNode.Value.first_name.value.Value; } catch { objOwner.FirstName = string.Empty; }
                                                try { objOwner.LastName = ownerNode.Value.last_name.value.Value; } catch { objOwner.LastName = string.Empty; }
                                                try { objOwner.Address = ownerNode.Value.address.value.address_line1.Value + " " + ownerNode.Value.address.value.address_line2.Value; } catch { objOwner.Address = string.Empty; }
                                                try { objOwner.City = ownerNode.Value.address.value.locality.Value; } catch { objOwner.City = string.Empty; }
                                                try { objOwner.State = ownerNode.Value.address.value.administrative_area.Value; } catch { objOwner.State = string.Empty; }
                                                try { objOwner.ZipCode = ownerNode.Value.address.value.postal_code.Value; } catch { objOwner.ZipCode = string.Empty; }
                                                try { objOwner.Phone = ownerNode.Value.phone_primary.value.Value; } catch { objOwner.Phone = string.Empty; }
                                                try { objOwner.OtherPhone = ownerNode.Value.phone_secondary.value.Value; } catch { objOwner.OtherPhone = string.Empty; }
                                                try { objOwner.PercentageOwnership = float.Parse(ownerNode.Value.percentage_owned.value.Value); } catch (Exception ex) { objOwner.PercentageOwnership = 0; }
                                                try
                                                {
                                                    if (ownerNode.Value.citizenship_residence.value.Value != null)
                                                    {
                                                        if (ownerNode.Value.citizenship_residence.value.Value == "citizen")
                                                        {
                                                            objOwner.Citizen = true;
                                                        }
                                                        else if (ownerNode.Value.citizenship_residence.value.Value == "resident")
                                                        {
                                                            objOwner.Citizen = true;
                                                        }
                                                    }
                                                }
                                                catch { }
                                                if (objOwner.Citizen)
                                                {
                                                    if (ownerNode.Value.ethnicity.value.Value == "Asian- Pacific American" || ownerNode.Value.ethnicity.value.Value == "Subcontinent Asian American")
                                                    {
                                                        objOwner.Asian = true;
                                                    }
                                                    if (ownerNode.Value.ethnicity.value.Value == "Black American")
                                                    {
                                                        objOwner.Black = true;
                                                    }
                                                    if (ownerNode.Value.ethnicity.value.Value == "Hispanic American")
                                                    {
                                                        objOwner.Hispanic = true;
                                                    }
                                                    if (ownerNode.Value.ethnicity.value.Value == "Native American")
                                                    {
                                                        objOwner.AmericanIndian = true;
                                                        objOwner.IndiginousAmerican = true;
                                                        objOwner.TribalID = ownerNode.Value.tribal_id_number.value.Value;
                                                    }
                                                    try
                                                    {
                                                        if (ownerNode.Value.gender.value.Value == "female")
                                                        {
                                                            objOwner.Woman = true;
                                                        }
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        objOwner.Woman = false;
                                                    }
                                                    try
                                                    {
                                                        if (ownerNode.Value.physically_disabled.value.Value == "1")
                                                        {
                                                            objOwner.PhysicallyDisabled = true;
                                                        }
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        objOwner.PhysicallyDisabled = false;
                                                    }
                                                    try
                                                    {
                                                        if (ownerNode.Value.service_disabled_veteran.value.Value == "1")
                                                        {
                                                            objOwner.ServiceDisabledVeteran = true;
                                                        }
                                                        else if (ownerNode.Value.veteran.value.Value == "1")
                                                        {
                                                            objOwner.Veteran = true;
                                                        }
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        objOwner.Veteran = false;
                                                    }
                                                }
                                                try
                                                {
                                                    if (ownerNode.Value.shares_of_stock != null)
                                                    {
                                                        if (ownerNode.Value.shares_of_stock.value.value_0_value.label.Value == "Number")
                                                            objOwner.NoOfSharesOwned = ownerNode.Value.shares_of_stock.value.value_0_value.value.Value;
                                                    }
                                                }
                                                catch { objOwner.NoOfSharesOwned = string.Empty; }
                                                try { objOwner.CurrentPosition = ownerNode.Value.title.value.Value; } catch { objOwner.CurrentPosition = string.Empty; }
                                                try
                                                {
                                                    if (ownerNode.Value.own_other_explain != null)
                                                    {
                                                        for (int i = 0; i < ownerNode.Value.own_other_explain.value.Count; i++)
                                                        {
                                                            JsonSchema.SecondaryFirms objSecondaryFirms = new JsonSchema.SecondaryFirms();
                                                            try { objSecondaryFirms.FirmName = ownerNode.Value.own_other_explain.value[i].value_0_value.value.Value; } catch { objSecondaryFirms.FirmName = string.Empty; }
                                                            try { objSecondaryFirms.FirmAddress = ownerNode.Value.own_other_explain.value[i].value_1_value.value.Value; } catch { objSecondaryFirms.FirmAddress = string.Empty; }
                                                            try { objSecondaryFirms.FirmPercentageOwnership = float.Parse(ownerNode.Value.own_other_explain.value[i].value_2_value.value.Value); } catch { objSecondaryFirms.FirmPercentageOwnership = 0; }
                                                            try { objSecondaryFirms.FirmGrossRevenue = ownerNode.Value.own_other_explain.value[i].value_3_value.value.Value; } catch { objSecondaryFirms.FirmGrossRevenue = "0"; }
                                                            objOwner.SecondaryFirm.Add(objSecondaryFirms);
                                                        }
                                                    }
                                                }
                                                catch { }
                                                objVendor.owner.Add(objOwner);
                                                // objVendor.owner.Add(new JsonSchema.Owners() {Address=objOwner.Address,AmericanIndian=objOwner.AmericanIndian,Asian=objOwner.Asian,Black=objOwner.Black,Citizen=objOwner.Citizen,City=objOwner.City,CurrentAsset=objOwner.CurrentAsset,CurrentPosition=objOwner.CurrentPosition,CurrentResponsibilities=objOwner.CurrentResponsibilities,CurrentSalary=objOwner.CurrentSalary,Eskimos=objOwner.Eskimos,FirstName=objOwner.FirstName,Hispanic=objOwner.Hispanic,IndiginousAmerican=objOwner.IndiginousAmerican,LastName=objOwner.LastName,MiddleName=objOwner.MiddleName,NativeHawaiians=objOwner.NativeHawaiians,NoOfSharesOwned=objOwner.NoOfSharesOwned,OtherPhone=objOwner.OtherPhone,OwnerUUID=objOwner.OwnerUUID,PercentageOwnership=objOwner.PercentageOwnership,PersonalNetworth=objOwner.PersonalNetworth,PersonalNetworthYear=objOwner.PersonalNetworthYear,Phone=objOwner.Phone,PhysicallyDisabled=objOwner.PhysicallyDisabled,RelevantExperience=objOwner.RelevantExperience,SecondaryFirm=objOwner.SecondaryFirm,ServiceDisabledVeteran=objOwner.ServiceDisabledVeteran,SignedApp=objOwner.SignedApp,SignedShares=objOwner.SignedShares,State=objOwner.State,Title=objOwner.Title,TotalLiabilities=objOwner.TotalLiabilities,TribalID=objOwner.TribalID,Veteran=objOwner.Veteran,Woman=objOwner.Woman,YearsofEducation=objOwner.YearsofEducation,YearsofExperience=objOwner.YearsofExperience,ZipCode=objOwner.ZipCode});
                                            }
                                            //JsonSchema.Owners objOwner = new JsonSchema.Owners();
                                            break;
                                    }
                                }
                                catch (Exception exc)
                                {
                                    this.WriteToFile(exc.Message.ToString().Length >= 250 ? exc.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : exc.Message.ToString().Replace('\'', '\"').TrimEnd('.'), "Error");
                                }
                            }
                        }
                        // Current Responsibities of Owners

                        foreach (dynamic node in application)
                        {

                            foreach (dynamic childnode in node)
                            {
                                try
                                {
                                    string tagname = childnode.Name;
                                    switch (tagname)
                                    {
                                        case "authorized_checks":
                                        case "financial_decisions":
                                        case "transactions":
                                            foreach (dynamic item in childnode.Value)
                                            {
                                                try
                                                {
                                                    bool matchfound = false;
                                                    int count = 0;
                                                    foreach (JsonSchema.Owners addOwner in objVendor.owner)
                                                    {
                                                        string firstName = item.Value.first_name.value.Value;
                                                        string lastName = item.Value.last_name.value.Value;
                                                        if (addOwner.FirstName.Trim() == firstName.Trim() && addOwner.LastName.Trim() == lastName.Trim())
                                                        {
                                                            matchfound = true;
                                                            //Update the existing owner with the Financial Responsibilities = True
                                                            objVendor.owner[count].Financial = true;
                                                        }
                                                        count++;
                                                    }
                                                    if (matchfound == false)
                                                    {
                                                        //Do the insert to the owners object as new owner (Owner Name, Financial Responsibilities, Target Group, Job Title) 
                                                        JsonSchema.Owners objAdditionalOwner = new JsonSchema.Owners();
                                                        try { objAdditionalOwner.FirstName = item.Value.first_name.value.Value; } catch (Exception ex) { }
                                                        try { objAdditionalOwner.LastName = item.Value.last_name.value.Value; } catch (Exception ex) { }
                                                        objAdditionalOwner.Financial = true;
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Asian- Pacific American" || item.Value.ethnicity.value.Value == "Subcontinent Asian American")
                                                            {
                                                                objAdditionalOwner.Asian = true;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Black American")
                                                            {
                                                                objAdditionalOwner.Black = true;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Hispanic American")
                                                            {
                                                                objAdditionalOwner.Hispanic = true;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Native American")
                                                            {
                                                                objAdditionalOwner.AmericanIndian = true;
                                                                objAdditionalOwner.IndiginousAmerican = true;
                                                                //objAdditionalOwner.TribalID = item.Value.tribal_id_number.value.Value;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.gender.value.Value == "female")
                                                            {
                                                                objAdditionalOwner.Woman = true;
                                                            }
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            objAdditionalOwner.Woman = false;
                                                        }
                                                        objVendor.owner.Add(objAdditionalOwner);
                                                    }
                                                }
                                                catch (Exception ex) { }

                                            }
                                            break;
                                        case "estimating":
                                        case "negotiating":
                                        case "supervisor":
                                            foreach (dynamic item in childnode.Value)
                                            {
                                                try
                                                {
                                                    bool matchfound = false;
                                                    int count = 0;
                                                    foreach (JsonSchema.Owners addOwner in objVendor.owner)
                                                    {
                                                        string firstName = item.Value.first_name.value.Value;
                                                        string lastName = item.Value.last_name.value.Value;
                                                        if (addOwner.FirstName.Trim() == firstName.Trim() && addOwner.LastName.Trim() == lastName.Trim())
                                                        {
                                                            matchfound = true;
                                                            //Update the existing owner with the Financial Responsibilities = True
                                                            objVendor.owner[count].ProjectManagement = true;
                                                        }
                                                        count++;
                                                    }
                                                    if (matchfound == false)
                                                    {
                                                        //Do the insert to the owners object as new owner (Owner Name, Financial Responsibilities, Target Group, Job Title) 
                                                        JsonSchema.Owners objAdditionalOwner = new JsonSchema.Owners();
                                                        try { objAdditionalOwner.FirstName = item.Value.first_name.value.Value; } catch (Exception ex) { }
                                                        try { objAdditionalOwner.LastName = item.Value.last_name.value.Value; } catch (Exception ex) { }
                                                        objAdditionalOwner.ProjectManagement = true;
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Asian- Pacific American" || item.Value.ethnicity.value.Value == "Subcontinent Asian American")
                                                            {
                                                                objAdditionalOwner.Asian = true;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Black American")
                                                            {
                                                                objAdditionalOwner.Black = true;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Hispanic American")
                                                            {
                                                                objAdditionalOwner.Hispanic = true;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Native American")
                                                            {
                                                                objAdditionalOwner.AmericanIndian = true;
                                                                objAdditionalOwner.IndiginousAmerican = true;
                                                                //objAdditionalOwner.TribalID = item.Value.tribal_id_number.value.Value;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.gender.value.Value == "female")
                                                            {
                                                                objAdditionalOwner.Woman = true;
                                                            }
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            objAdditionalOwner.Woman = false;
                                                        }
                                                        objVendor.owner.Add(objAdditionalOwner);
                                                    }
                                                }
                                                catch (Exception ex) { }

                                            }
                                            break;
                                        case "hiring_management":
                                        case "hiring_other":
                                            foreach (dynamic item in childnode.Value)
                                            {
                                                try
                                                {
                                                    bool matchfound = false;
                                                    int count = 0;
                                                    foreach (JsonSchema.Owners addOwner in objVendor.owner)
                                                    {
                                                        string firstName = item.Value.first_name.value.Value;
                                                        string lastName = item.Value.last_name.value.Value;
                                                        if (addOwner.FirstName.Trim() == firstName.Trim() && addOwner.LastName.Trim() == lastName.Trim())
                                                        {
                                                            matchfound = true;
                                                            //Update the existing owner with the Financial Responsibilities = True
                                                            objVendor.owner[count].Personnel = true;
                                                        }
                                                        count++;
                                                    }
                                                    if (matchfound == false)
                                                    {
                                                        //Do the insert to the owners object as new owner (Owner Name, Financial Responsibilities, Target Group, Job Title) 
                                                        JsonSchema.Owners objAdditionalOwner = new JsonSchema.Owners();
                                                        try { objAdditionalOwner.FirstName = item.Value.first_name.value.Value; } catch (Exception ex) { }
                                                        try { objAdditionalOwner.LastName = item.Value.last_name.value.Value; } catch (Exception ex) { }
                                                        objAdditionalOwner.Personnel = true;
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Asian- Pacific American" || item.Value.ethnicity.value.Value == "Subcontinent Asian American")
                                                            {
                                                                objAdditionalOwner.Asian = true;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Black American")
                                                            {
                                                                objAdditionalOwner.Black = true;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Hispanic American")
                                                            {
                                                                objAdditionalOwner.Hispanic = true;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Native American")
                                                            {
                                                                objAdditionalOwner.AmericanIndian = true;
                                                                objAdditionalOwner.IndiginousAmerican = true;
                                                                //objAdditionalOwner.TribalID = item.Value.tribal_id_number.value.Value;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.gender.value.Value == "female")
                                                            {
                                                                objAdditionalOwner.Woman = true;
                                                            }
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            objAdditionalOwner.Woman = false;
                                                        }
                                                        objVendor.owner.Add(objAdditionalOwner);
                                                    }
                                                }
                                                catch (Exception ex) { }

                                            }
                                            break;
                                        case "marketing":
                                            foreach (dynamic item in childnode.Value)
                                            {
                                                try
                                                {
                                                    bool matchfound = false;
                                                    int count = 0;
                                                    foreach (JsonSchema.Owners addOwner in objVendor.owner)
                                                    {
                                                        string firstName = item.Value.first_name.value.Value;
                                                        string lastName = item.Value.last_name.value.Value;
                                                        if (addOwner.FirstName.Trim() == firstName.Trim() && addOwner.LastName.Trim() == lastName.Trim())
                                                        {
                                                            matchfound = true;
                                                            //Update the existing owner with the Financial Responsibilities = True
                                                            objVendor.owner[count].SalesMarketing = true;
                                                        }
                                                        count++;
                                                    }
                                                    if (matchfound == false)
                                                    {
                                                        //Do the insert to the owners object as new owner (Owner Name, Financial Responsibilities, Target Group, Job Title) 
                                                        JsonSchema.Owners objAdditionalOwner = new JsonSchema.Owners();
                                                        try { objAdditionalOwner.FirstName = item.Value.first_name.value.Value; } catch (Exception ex) { }
                                                        try { objAdditionalOwner.LastName = item.Value.last_name.value.Value; } catch (Exception ex) { }
                                                        objAdditionalOwner.SalesMarketing = true;
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Asian- Pacific American" || item.Value.ethnicity.value.Value == "Subcontinent Asian American")
                                                            {
                                                                objAdditionalOwner.Asian = true;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Black American")
                                                            {
                                                                objAdditionalOwner.Black = true;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Hispanic American")
                                                            {
                                                                objAdditionalOwner.Hispanic = true;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Native American")
                                                            {
                                                                objAdditionalOwner.AmericanIndian = true;
                                                                objAdditionalOwner.IndiginousAmerican = true;
                                                                //objAdditionalOwner.TribalID = item.Value.tribal_id_number.value.Value;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.gender.value.Value == "female")
                                                            {
                                                                objAdditionalOwner.Woman = true;
                                                            }
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            objAdditionalOwner.Woman = false;
                                                        }
                                                        objVendor.owner.Add(objAdditionalOwner);
                                                    }
                                                }
                                                catch (Exception ex) { }

                                            }
                                            break;
                                        case "planning":
                                            foreach (dynamic item in childnode.Value)
                                            {
                                                try
                                                {
                                                    bool matchfound = false;
                                                    int count = 0;
                                                    foreach (JsonSchema.Owners addOwner in objVendor.owner)
                                                    {
                                                        string firstName = item.Value.first_name.value.Value;
                                                        string lastName = item.Value.last_name.value.Value;
                                                        if (addOwner.FirstName.Trim() == firstName.Trim() && addOwner.LastName.Trim() == lastName.Trim())
                                                        {
                                                            matchfound = true;
                                                            //Update the existing owner with the Financial Responsibilities = True
                                                            objVendor.owner[count].BusinessPlanning = true;
                                                        }
                                                        count++;
                                                    }
                                                    if (matchfound == false)
                                                    {
                                                        //Do the insert to the owners object as new owner (Owner Name, Financial Responsibilities, Target Group, Job Title) 
                                                        JsonSchema.Owners objAdditionalOwner = new JsonSchema.Owners();
                                                        try { objAdditionalOwner.FirstName = item.Value.first_name.value.Value; } catch (Exception ex) { }
                                                        try { objAdditionalOwner.LastName = item.Value.last_name.value.Value; } catch (Exception ex) { }
                                                        objAdditionalOwner.BusinessPlanning = true;
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Asian- Pacific American" || item.Value.ethnicity.value.Value == "Subcontinent Asian American")
                                                            {
                                                                objAdditionalOwner.Asian = true;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Black American")
                                                            {
                                                                objAdditionalOwner.Black = true;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Hispanic American")
                                                            {
                                                                objAdditionalOwner.Hispanic = true;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.ethnicity.value.Value == "Native American")
                                                            {
                                                                objAdditionalOwner.AmericanIndian = true;
                                                                objAdditionalOwner.IndiginousAmerican = true;
                                                                //objAdditionalOwner.TribalID = item.Value.tribal_id_number.value.Value;
                                                            }
                                                        }
                                                        catch (Exception ex) { }
                                                        try
                                                        {
                                                            if (item.Value.gender.value.Value == "female")
                                                            {
                                                                objAdditionalOwner.Woman = true;
                                                            }
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            objAdditionalOwner.Woman = false;
                                                        }
                                                        objVendor.owner.Add(objAdditionalOwner);
                                                    }
                                                }
                                                catch (Exception ex) { }

                                            }
                                            break;
                                        case "pnws":
                                            foreach (dynamic item in childnode.Value)
                                            {
                                                try
                                                {
                                                    int count = 0;
                                                    foreach (JsonSchema.Owners addOwner in objVendor.owner)
                                                    {
                                                        string firstName = item.Value.first_name.value.Value;
                                                        string lastName = item.Value.last_name.value.Value;
                                                        if (addOwner.FirstName.Trim() == firstName.Trim() && addOwner.LastName.Trim() == lastName.Trim())
                                                        {
                                                            //Update the existing owner with the CurrentAsset,TotalLiabilities and PersonalNetworth
                                                            objVendor.owner[count].PersonalNetworth = item.Value.total.value.Value;
                                                            objVendor.owner[count].CurrentAsset = item.Value.assets_total.value.Value;
                                                            objVendor.owner[count].TotalLiabilities = item.Value.liabilities_total.value.Value;
                                                        }
                                                        count++;
                                                    }
                                                }
                                                catch (Exception ex) { }
                                            }
                                            break;
                                    }
                                }
                                catch (Exception ex)
                                {

                                }
                            }
                        }
                        if (objVendor.VendorName == string.Empty)
                        {
                            objVendor.VendorName = objVendor.BusinessName;
                        }
                        //Add each application to the list
                        objApplications.ApplicationList.Add(objVendor);
                        this.WriteToFile("Application list successfully serialized {0}", "Log");
                        
                    }
                    catch (Exception exc)
                    {
                        this.WriteToFile(exc.Message.ToString().Length >= 250 ? exc.Message.ToString().Substring(0, 249).Replace('\'', '\"').TrimEnd('.') : exc.Message.ToString().Replace('\'', '\"').TrimEnd('.') + " {0}", "Error");
                        Notifyfailure("Error While reading the response from Portal API.Not is correct format: " + (exc.Message.ToString().Length >= 200 ? exc.Message.ToString().Substring(0, 199).Replace('\'', '\"').TrimEnd('.') : exc.Message.ToString().Replace('\'', '\"').TrimEnd('.')));
                    }
                }
            }
            return objApplications;
        }

        private void WriteToFile(string text,string logType)
        {
            try
            {
                string path;
                if (logType == "Error")
                {
                    path = ConfigurationManager.AppSettings["ErrorPath"] + ConfigurationManager.AppSettings["ErrorFileName"] + DateTime.Now.ToString("yyyyMdd") + ".txt";
                }
                else
                {
                    path = ConfigurationManager.AppSettings["LogPath"] + ConfigurationManager.AppSettings["LogFileName"] + DateTime.Now.ToString("yyyyMdd") + ".txt";
                }
                using (StreamWriter writer = new StreamWriter(path, true))
                {
                    writer.WriteLine(string.Format(text, DateTime.Now.ToString("MM/dd/yyyy hh:mm:ss tt")));
                    writer.Close();
                }
            }
            catch(Exception exc)
            {
                Notifyfailure("Error While writing Log file: " + (exc.Message.ToString().Length >= 200 ? exc.Message.ToString().Substring(0, 199).Replace('\'', '\"').TrimEnd('.') : exc.Message.ToString().Replace('\'', '\"').TrimEnd('.')));
            }

        }

    }

   
}
