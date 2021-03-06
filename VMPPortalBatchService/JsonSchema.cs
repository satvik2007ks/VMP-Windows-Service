﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VMPPortalBatchService
{
    public class JsonSchema
    {

        public class Applications
        {
            public List<Vendor> ApplicationList = new List<Vendor>();
        }
        public class Vendor
        {

            //Vendor Basic
            public string UUID { get; set; }                     //uuid
            public Int64 AttachmentCount { get; set; }           //attachment_count

            public List<Attachments> AttachmentList = new List<Attachments>();
            public string VendorName { get; set; }               //dba_name

            //public string LegalFirmName { get; set; }            //legal_firm_name
            public string BusinessName { get; set; }             // Not Used
            public string SwiftNumber { get; set; }              // Not Used
            public string PrimaryNaicCode { get; set; }          // Not Used
            public string PrimaryNaicId { get; set; }            // Not Used
            public string[] SecondaryNaicCode { get; set; }      // Not Used
            public string ProductDescription { get; set; }       //describe_activities
            public bool Neoserra { get; set; }                   //Not Used
            public string Address { get; set; }                  //address
            public string City { get; set; }                     //locality
            public string State { get; set; }                    //administrative_area
            public string Zipcode { get; set; }                  //postal_code
            public string County { get; set; }                   //county
            public string RegionId { get; set; }
            public string Telephone { get; set; }                //business_telephone
            public string Cellphone { get; set; }                 //Not Used
            public string Otherphone { get; set; }                //Not Used
            public string Fax { get; set; }                      //business_fax
            public string EMail { get; set; }                    //business_email_address
            public string WebAddress { get; set; }               //business_website

            //Owners
           // public List<Owners> owner { get; set; }
           public List<Owners> owner = new List<Owners>();

            //Business
            //Firm Information
            public string ApplicationDate { get; set; }               // Confirm with Nancy and Dorothy
            public string CurrentOwnershipDate { get; set; }               //date_ownership_was_acquire
            public string RegisteredDate { get; set; }               //registration_date

            public string HRCertificate = "No";              //current_certifications
            public string MethodOfAquisition { get; set; }               //method_of_acquisition
            public string FullTimeEmployees { get; set; }               //full_time
            public string MNTaxID { get; set; }               //state_tax_id
            public string FederalTaxID { get; set; }               //federal_tax_id_ein
            public string GrossRevenue1 { get; set; }               //specify_the_gross_receipts  // Current Year
            public string GrossYear1 { get; set; }               //specify_the_gross_receipts   // Current Year
            public string GrossRevenue2 { get; set; }               //specify_the_gross_receipts  // Previous Year
            public string GrossYear2 { get; set; }               //specify_the_gross_receipts  // Previous Year
            public string GrossRevenue3 { get; set; }               //specify_the_gross_receipts  // Older Year
            public string GrossYear3 { get; set; }               //specify_the_gross_receipts  // Older Year

            //Legal Structure
            public bool Corporation = false;             //
            public bool Partnership = false;                //
            public bool Proprietorship = false;               //
            public bool NonProfit = false;              //
            public bool Rehabilitation = false;              //
            public bool LLCLLP = false;              //

            //Type of Business Not Returned from Portal

            //Other Certification

            public bool DBE = false;               //
            public bool USDVA = false;               //Not Used
            public bool WBENC = false;              //
            public bool NGLCC = false;               //
            public bool NCMSDC = false;             //

        }

        public class Attachments
        {
            public string AttachmentType { get; set; }
            public string AttachmentDescription { get; set; }
            public string AttachmentPath { get; set; }
        }

        public class Owners
        {
            public string OwnerUUID { get; set; }
            public string Title { get; set; }
            public string FirstName { get; set; }
            public string MiddleName { get; set; }
            public string LastName { get; set; }
            public string Address { get; set; }
            public string City { get; set; }
            public string State { get; set; }
            public string ZipCode { get; set; }
            public string Phone { get; set; }
            public string OtherPhone { get; set; }
            public bool SignedShares = false;
            public bool SignedApp = false;

            public float PercentageOwnership = 0;
            public string NoOfSharesOwned = "0";

            public bool Citizen = false;
            public bool Asian = false;
            public bool Black = false;
            public bool Hispanic = false;
            public bool IndiginousAmerican = false;
            public bool Eskimos = false;
            public bool NativeHawaiians = false;
            public bool AmericanIndian = false;
            public string TribalID { get; set; }
            public bool PhysicallyDisabled = false;
            public bool Woman = false;
            public bool Veteran = false;
            public bool ServiceDisabledVeteran = false;

            public string PersonalNetworthYear { get; set; }
            public string CurrentAsset = "0";
            public string TotalLiabilities = "0";
            public string PersonalNetworth = "0";
            public string CurrentSalary { get; set; }
            public string YearsofEducation { get; set; }
            public string CurrentPosition { get; set; }

            public bool BusinessPlanning = false;

            public bool SalesMarketing = false;
            public bool Financial = false;

            public bool Personnel = false;

            public bool ProjectManagement = false;

            public List<SecondaryFirms> SecondaryFirm = new List<SecondaryFirms>();
        }
        public class SecondaryFirms
        {
            public string FirmName { get; set; }
            public string FirmAddress { get; set; }
            public string FirmGrossRevenue { get; set; }
            public float FirmPercentageOwnership { get; set; }

            public bool SimilarBusiness = false;
        }

    }
}
