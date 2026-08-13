/*
    Copyright (c) 2017 Marcin Szeniak (https://github.com/Klocman/)
    Apache License Version 2.0
*/

using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Klocman.IO;

namespace UninstallTools.Factory.InfoAdders
{
    public static class CertificateGetter
    {
        /// <param name="entry">Entry to look up.</param>
        /// <param name="sourceFile">
        ///     File the certificate was extracted from, or null when it did not come from a file on
        ///     disk (MSI store). Needed so the signature can actually be verified against the bytes it
        ///     is supposed to cover - the certificate alone cannot tell you that.
        /// </param>
        internal static X509Certificate2 TryGetCertificate(ApplicationUninstallerEntry entry, out string sourceFile)
        {
            sourceFile = null;

            // Don't even try if the entry is invalid, it will be marked as bad anyways
            if (!entry.IsValid)
                return null;

            try
            {
                X509Certificate2 result = null;
                if (entry.SortedExecutables != null)
                    result = TryExtractCertificateHelper(out sourceFile, entry.SortedExecutables);

                // Check executables before this because signatures in MSI store are modified and won't verify
                if (result == null && entry.UninstallerKind == UninstallerType.Msiexec)
                {
                    result = MsiTools.GetCertificate(entry.BundleProviderKey);
                    sourceFile = null;
                }

                // If no certs were found finally check the uninstaller
                if (result == null && !string.IsNullOrEmpty(entry.UninstallerFullFilename))
                    result = TryExtractCertificateHelper(out sourceFile, entry.UninstallerFullFilename);

                return result;
            }
            catch
            {
                // Default to no certificate
                sourceFile = null;
                return null;
            }
        }

        /// <summary>
        ///     Check first few files from the install directory for certificates
        /// </summary>
        private static X509Certificate2 TryExtractCertificateHelper(out string sourceFile, params string[] fileNames)
        {
            foreach (var candidate in fileNames.Take(2))
            {
                try
                {
                    var cert = new X509Certificate2(candidate);
                    sourceFile = candidate;
                    return cert;
                }
                catch
                {
                    // No cert was found, try next
                }
            }

            sourceFile = null;
            return null;
        }
    }
}