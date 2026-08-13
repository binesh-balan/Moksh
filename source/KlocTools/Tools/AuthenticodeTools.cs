/*
    Copyright (c) 2017 Marcin Szeniak (https://github.com/Klocman/)
    Apache License Version 2.0

    Authenticode verification added in the MOKSH fork by Binesh Balan.
*/

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Klocman.Tools
{
    /// <summary>
    /// Real Authenticode signature verification via WinVerifyTrust.
    /// </summary>
    /// <remarks>
    /// Extracting the embedded certificate with <see cref="System.Security.Cryptography.X509Certificates.X509Certificate2"/>
    /// and calling Verify() only validates the certificate chain - it never checks that the certificate
    /// actually signs the file's bytes. A tampered binary, or an unsigned one carrying a copied
    /// certificate blob, passes that check. Since the result is shown to the user as a trust signal
    /// used to decide what is safe to keep, it has to be a genuine signature check.
    /// </remarks>
    public static class AuthenticodeTools
    {
        private static readonly Guid WinTrustActionGenericVerifyV2 =
            new("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

        private const uint WtdUiNone = 2;
        private const uint WtdRevokeWholeChain = 1;
        private const uint WtdChoiceFile = 1;
        private const uint WtdStateActionVerify = 1;
        private const uint WtdStateActionClose = 2;
        private const uint WtdCacheOnlyUrlRetrieval = 0x00001000;

        private const uint TrustEOk = 0;

        /// <summary>
        /// Verify that the file carries a valid Authenticode signature whose chain terminates in a
        /// trusted root. Returns false for unsigned, tampered, expired-without-timestamp, and revoked
        /// files alike, and for anything that cannot be read.
        /// </summary>
        public static bool IsTrusted(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;

            try
            {
                if (!File.Exists(filePath)) return false;
            }
            catch (Exception)
            {
                return false;
            }

            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = filePath,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };

            var pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());

            try
            {
                Marshal.StructureToPtr(fileInfo, pFileInfo, false);

                var data = new WinTrustData
                {
                    cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    dwUIChoice = WtdUiNone,
                    fdwRevocationChecks = WtdRevokeWholeChain,
                    dwUnionChoice = WtdChoiceFile,
                    pFile = pFileInfo,
                    dwStateAction = WtdStateActionVerify,
                    hWVTStateData = IntPtr.Zero,
                    pwszURLReference = null,
                    // Don't stall an interactive scan on CRL/OCSP fetches for every executable found.
                    dwProvFlags = WtdCacheOnlyUrlRetrieval,
                    dwUIContext = 0,
                    pSignatureSettings = IntPtr.Zero
                };

                var result = WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, ref data);

                // Always close the state, whatever the verdict, or the provider leaks its context.
                data.dwStateAction = WtdStateActionClose;
                WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, ref data);

                return result == TrustEOk;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Authenticode check failed for [{filePath}]: {ex.Message}");
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(pFileInfo);
            }
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
            ref WinTrustData pWVTData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPTStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            [MarshalAs(UnmanagedType.LPTStr)] public string pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }
    }
}
