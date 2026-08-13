/*
    Copyright (c) 2017 Marcin Szeniak (https://github.com/Klocman/)
    Apache License Version 2.0

    Added in the MOKSH fork by Binesh Balan.
*/

using System.Windows.Forms;
using Klocman.Forms.Tools;

namespace BulkCrapUninstaller.Functions.Tools
{
    /// <summary>
    /// Guards every outbound link in the UI against an unconfigured URL.
    /// </summary>
    /// <remarks>
    /// MOKSH ships with no homepage, contact form, donation page, social account or
    /// issue tracker, so the corresponding entries in Resources.resx are empty. Rather
    /// than deleting the controls that point at them - which would mean unpicking
    /// designer-generated layout in a dozen forms - the controls are hidden whenever
    /// their URL is blank. Fill a value back into Resources.resx and the control
    /// reappears with no code change.
    /// </remarks>
    internal static class BrandLinks
    {
        /// <summary>True if the URL is configured for this build.</summary>
        public static bool IsSet(string url) => !string.IsNullOrWhiteSpace(url);

        /// <summary>Open the URL, or do nothing if it isn't configured.</summary>
        public static void Open(string url)
        {
            if (IsSet(url))
                PremadeDialogs.StartProcessSafely(url);
        }

        /// <summary>
        /// Hide the supplied controls unless the URL is configured. Call from a form's
        /// constructor after InitializeComponent.
        /// </summary>
        public static void HideUnless(string url, params Control[] controls)
        {
            if (IsSet(url)) return;

            foreach (var control in controls)
            {
                if (control == null) continue;
                control.Visible = false;
                control.Enabled = false;
            }
        }
    }
}
