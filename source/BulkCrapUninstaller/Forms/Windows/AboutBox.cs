/*
    Copyright (c) 2017 Marcin Szeniak (https://github.com/Klocman/)
    Apache License Version 2.0
*/

using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using BulkCrapUninstaller.Functions.Tools;
using BulkCrapUninstaller.Properties;
using Klocman.Extensions;
using Klocman.Forms.Tools;
using Klocman.Tools;

namespace BulkCrapUninstaller.Forms
{
    sealed partial class AboutBox : Form
    {
        public AboutBox()
        {
            InitializeComponent();
            labelVersion.Text += AssemblyVersion;
            labelCopyright.Text = AssemblyCopyright;

            // Attribution. MOKSH is a derivative work of Bulk Crap Uninstaller; the Apache-2.0 licence
            // requires that the origin stays visible, and crediting the original author is simply right.
            labelCompanyName.Text =
                "MØKSH — maintained by Binesh Balan" + Environment.NewLine
                + Environment.NewLine
                + "Based on Bulk Crap Uninstaller," + Environment.NewLine
                + "created by Marcin Szeniak (Klocman)." + Environment.NewLine
                + "Original work © 2017–2026 Marcin Szeniak, Apache-2.0." + Environment.NewLine
                + Environment.NewLine
                + "All credit for the original application belongs to" + Environment.NewLine
                + "Marcin Szeniak and the Bulk Crap Uninstaller contributors.";
            labelis64.Text += ProcessTools.Is64BitProcess.ToYesNo();
            Assembly.GetExecutingAssembly().Modules.First().GetPEKind(out var pekind, out var ifmachine);
            string machine;
            if (Enum.IsDefined<ImageFileMachine>(ifmachine))
            {
                machine = Enum.GetName<ImageFileMachine>(ifmachine);
            } else // Work around .NET 6 not having WoA64 definitions in GetPEKind

            {
                machine = "ARM64";
            }
                labelArchitecture.Text += machine;
            labelPortable.Text += Program.IsInstalled.ToYesNo();

            var translationCredits = new[]
            {
                // en - English
                new {culture = CultureInfo.GetCultureInfo("en"), translator = "Marcin Szeniak"},

                // ar - Arabic
                new {culture =  CultureInfo.GetCultureInfo("ar"), translator = "MFM Dawdeh"},

                // Czech
                new {culture =  CultureInfo.GetCultureInfo("cs-CZ"), translator = "Richard Kahl"},

                // de - German
                new {culture =  CultureInfo.GetCultureInfo("de"), translator = "Dieter Hummel, Thomas Werk"},

                // es - Spanish
                new {culture =  CultureInfo.GetCultureInfo("es"), translator = "MS-PC2, Freddynic159, Emilio J. Grao"},

                // fr - French
                new {culture =  CultureInfo.GetCultureInfo("fr"), translator = "Thierry Delaunay, Orphée V."},

                // Hungarian
                new {culture =  CultureInfo.GetCultureInfo("hu"), translator = "Phoenix (Döbröntei Sándor)"},

                // it - Italian
                new {culture =  CultureInfo.GetCultureInfo("it"), translator = "Luca Carrabba (luca.carrabba@yahoo.com)"},
                
                // ja - Japanese
                new {culture =  CultureInfo.GetCultureInfo("ja"), translator = "KKbion"},

                // nl - Dutch
                new {culture =  CultureInfo.GetCultureInfo("nl"), translator = "Jaap Kramer"},

                // Polish
                new {culture =  CultureInfo.GetCultureInfo("pl"), translator = "Marcin Szeniak"},

                // pt - Portuguese
                new {culture =  CultureInfo.GetCultureInfo("pt-PT"), translator = "Artur Álvaro Pereira"},
                new {culture =  CultureInfo.GetCultureInfo("pt-BR"), translator = "Silvio Corral"},

                // Russian
                new {culture = CultureInfo.GetCultureInfo("ru"), translator = "wvxwxvw, Kommprog"},

                // Slovenian
                new {culture = CultureInfo.GetCultureInfo("sl"), translator = "Jadran Rudec"},

                // Swedish
                new {culture = CultureInfo.GetCultureInfo("sv"), translator = "@glecas"},

                // Turkish
                new {culture = CultureInfo.GetCultureInfo("tr"), translator = "Harun Güngör, @DogancanYr"},

                // Ukrainian
                new {culture = CultureInfo.GetCultureInfo("uk"), translator = "Serhii Horoshko"},

                // Vietnamese
                new {culture = CultureInfo.GetCultureInfo("vi"), translator = "wanwanvxt / Vũ Xuân Trường"},
                
                // Simplified Chinese
                new {culture = CultureInfo.GetCultureInfo("zh-Hans"), translator = "cc713"},
                
                // Traditional Chinese
                new {culture = CultureInfo.GetCultureInfo("zh-Hant"), translator = "Henryliu880922"}
            };

            foreach (var translationCredit in translationCredits
                .Select(x => new { x.culture.DisplayName, x.translator })
                .OrderBy(x => x.DisplayName))
            {
                var l = new Label
                {
                    Text = translationCredit.DisplayName + " - " + translationCredit.translator,
                    Padding = new Padding(0, 0, 0, 3),
                    AutoSize = true,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                flowLayoutPanel2.Controls.Add(l);
            }
        }

        public static string AssemblyCompany
        {
            get
            {
                var attributes = Assembly.GetExecutingAssembly()
                    .GetCustomAttributes(typeof(AssemblyCompanyAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyCompanyAttribute)attributes[0]).Company;
            }
        }

        public static string AssemblyCopyright
        {
            get
            {
                var attributes = Assembly.GetExecutingAssembly()
                    .GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false);
                if (attributes.Length == 0)
                {
                    return "";
                }
                return ((AssemblyCopyrightAttribute)attributes[0]).Copyright;
            }
        }

        public static Version AssemblyVersion => Assembly.GetExecutingAssembly().GetName().Version;

        private void button1_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            PremadeDialogs.StartProcessSafely(@"http://objectlistview.sourceforge.net");
        }

        private void linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            PremadeDialogs.StartProcessSafely(
                @"http://www.codeproject.com/Articles/20917/Creating-a-Custom-Settings-Provider");
        }

        private void linkLabel3_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            PremadeDialogs.StartProcessSafely(@"https://github.com/Templarian/WindowsIcons/");
        }

        /// <summary>
        /// Was "Official webpage". MOKSH has no webpage, so this now points at the project
        /// MOKSH is derived from - which is what belongs in an About box anyway.
        /// </summary>
        private void linkLabel4_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            BrandLinks.Open(BrandLinks.IsSet(Resources.HomepageUrl)
                ? Resources.HomepageUrl
                : Resources.UpstreamProjectUrl);
        }

        private void linkLabel5_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            PremadeDialogs.StartProcessSafely(@"https://nbug.codeplex.com/");
        }

        private void linkLabel6_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            PremadeDialogs.StartProcessSafely(@"http://dotnetzip.codeplex.com/");
        }

        private void linkLabel7_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            PremadeDialogs.StartProcessSafely(@"http://taskscheduler.codeplex.com/");
        }

        private void linkLabel8_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            PremadeDialogs.StartProcessSafely(@"https://github.com/TestStack/White");
        }

        private void linkLabel9_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            PremadeDialogs.StartProcessSafely(@"https://www.voidtools.com/");
        }
    }
}
