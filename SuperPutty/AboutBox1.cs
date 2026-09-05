/*
 *  https://github.com/jimradford/superputty/blob/master/License.txt
 */

using System;
using System.Reflection;
using System.Windows.Forms;
using System.Diagnostics;

namespace SuperPutty
{
    partial class AboutBox1 : Form
    {
        internal const string OriginalReleaseAttribution = "Version 1.5.0.0 Copyright (c) 2009 - 2023 Jim Radford";
        internal const string OriginalAuthorUrl = "https://www.jimradford.com";
        internal const string UpdateAttribution = "Updates by C. Thornton";
        internal const string CommunityRepositoryUrl = "https://github.com/greyhair-atx/superputty";

        public AboutBox1()
        {
            InitializeComponent();
            this.Text = String.Format("About {0}", AssemblyTitle);
            this.labelProductName.Text = AssemblyProduct;
            this.labelVersion.Text = String.Format("Version {0}", AssemblyVersion);
            this.labelCopyright.Text = OriginalReleaseAttribution;
            this.linkLabelCompany.Text = OriginalAuthorUrl;
            this.linkLabelCompany.LinkArea = new LinkArea(0, OriginalAuthorUrl.Length);
            this.linkLabelCompany.Links[0].LinkData = OriginalAuthorUrl;
            this.labelMaintainer.Text = UpdateAttribution;
            this.linkLabelCompany2.Text = CommunityRepositoryUrl;
            this.linkLabelCompany2.LinkArea = new LinkArea(0, CommunityRepositoryUrl.Length);
            this.linkLabelCompany2.Links[0].LinkData = CommunityRepositoryUrl;

            textBoxSupportText.AppendText("SuperPuTTY Version: " + SuperPuTTY.Version + System.Environment.NewLine);
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            foreach(var a in asms)
            {                
                textBoxSupportText.AppendText(a.FullName + System.Environment.NewLine);                
            }
        }

        #region Assembly Attribute Accessors

        public string AssemblyTitle
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyTitleAttribute), false);
                if (attributes.Length > 0)
                {
                    AssemblyTitleAttribute titleAttribute = (AssemblyTitleAttribute)attributes[0];
                    if (titleAttribute.Title != "")
                    {
                        return titleAttribute.Title;
                    }
                }
                return System.IO.Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().CodeBase);
            }
        }

        public string AssemblyVersion { get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(); } }

        public string AssemblyDescription
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyDescriptionAttribute)attributes[0]).Description;
            }
        }

        public string AssemblyProduct
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyProductAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyProductAttribute)attributes[0]).Product;
            }
        }

        public string AssemblyCopyright
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyCopyrightAttribute)attributes[0]).Copyright;
            }
        }

        public string AssemblyCompany
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCompanyAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyCompanyAttribute)attributes[0]).Company;
            }
        }
        #endregion

        private void richTextBox1_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            Process.Start(e.LinkText);
        }

        private void linkLabelCompany2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            LinkLabel link = (LinkLabel)sender;
            string target = e.Link.LinkData as string;
            Process.Start(String.IsNullOrEmpty(target) ? link.Text : target);
        }
    }
}
