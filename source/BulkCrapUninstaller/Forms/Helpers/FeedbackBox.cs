/*
    Copyright (c) 2017 Marcin Szeniak (https://github.com/Klocman/)
    Apache License Version 2.0
*/

using System;
using System.Windows.Forms;
using BulkCrapUninstaller.Functions.Tools;
using BulkCrapUninstaller.Properties;

namespace BulkCrapUninstaller.Forms
{
    public partial class FeedbackBox : Form
    {
        /// <summary>
        /// True when at least one of this dialog's destinations is configured.
        /// </summary>
        /// <remarks>
        /// Every button here opens an external link. MOKSH configures none of them, so the
        /// dialog would be an empty box - and the nag timer that opens it unprompted after
        /// three minutes would be pure annoyance. Guarding the single entry point disables
        /// it for the nag, the menu item and the debug window alike.
        /// </remarks>
        public static bool IsAvailable =>
            BrandLinks.IsSet(Resources.SubmitFeedbackLink)
            || BrandLinks.IsSet(Resources.ReviewLink)
            || BrandLinks.IsSet(Resources.TwitterLink)
            || BrandLinks.IsSet(Resources.GithubNewIssueLink)
            || BrandLinks.IsSet(Resources.GithubIssuesLink)
            || BrandLinks.IsSet(Resources.TranslateLink)
            || BrandLinks.IsSet(Resources.DonateLink);

        public static void ShowFeedbackBox(Form parent, bool showDisableCheckbox)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            if (!IsAvailable) return;

            using (var f = new FeedbackBox())
            {
                f.checkBoxNeverShow.Visible = showDisableCheckbox;
                f.checkBoxNeverShow.Enabled = showDisableCheckbox;

                f.Icon = parent.Icon;
                f.Owner = parent;
                f.StartPosition = FormStartPosition.CenterParent;

                f.ShowDialog(parent);
            }
        }

        private FeedbackBox()
        {
            InitializeComponent();

            if (DesignMode) return;

            Settings.Default.SettingBinder.BindControl(checkBoxNeverShow, x => x.MiscFeedbackNagNeverShow, this);
            Settings.Default.SettingBinder.SendUpdates(this);

            // Hide any individual destination that isn't configured for this build.
            BrandLinks.HideUnless(Resources.SubmitFeedbackLink, buttonSendFeedback);
            BrandLinks.HideUnless(Resources.ReviewLink, buttonRate);
            BrandLinks.HideUnless(Resources.TwitterLink, buttonTwitter);
            BrandLinks.HideUnless(Resources.GithubNewIssueLink, buttonSubmitGithub);
            BrandLinks.HideUnless(Resources.GithubIssuesLink, buttonIssues);
            BrandLinks.HideUnless(Resources.TranslateLink, buttonTranslate);
            BrandLinks.HideUnless(Resources.DonateLink, buttonDonate);
        }

        private void buttonSendFeedback2_Click(object sender, EventArgs e)
        {
            BrandLinks.Open(Resources.SubmitFeedbackLink);
        }

        private void buttonRate_Click(object sender, EventArgs e)
        {
            BrandLinks.Open(Resources.ReviewLink);
        }

        private void buttonTwitter_Click(object sender, EventArgs e)
        {
            BrandLinks.Open(Resources.TwitterLink);
        }

        private void buttonSubmitGithub_Click(object sender, EventArgs e)
        {
            BrandLinks.Open(Resources.GithubNewIssueLink);
        }

        private void buttonIssues_Click(object sender, EventArgs e)
        {
            BrandLinks.Open(Resources.GithubIssuesLink);
        }

        private void buttonTranslate_Click(object sender, EventArgs e)
        {
            BrandLinks.Open(Resources.TranslateLink);
        }

        private void buttonDonate_Click(object sender, EventArgs e)
        {
            BrandLinks.Open(Resources.DonateLink);
        }
    }
}
