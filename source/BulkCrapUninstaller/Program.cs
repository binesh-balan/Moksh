/*
    Copyright (c) 2017 Marcin Szeniak (https://github.com/Klocman/)
    Apache License Version 2.0
*/

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Xml.Linq;
using BulkCrapUninstaller.Forms;
using BulkCrapUninstaller.Functions.Ratings;
using BulkCrapUninstaller.Properties;
using Klocman.Extensions;
using Klocman.Forms.Tools;
using Klocman.Tools;
using Microsoft.Win32;
using UninstallTools;
using UninstallTools.Factory;

namespace BulkCrapUninstaller
{
    //TODO This is a leftover class, extract the self installed detection logic and get rid of it
    public static class Program
    {
        private static DirectoryInfo _assemblyLocation;
        private static string _installedRegistryKeyName;
        private static bool? _isInstalled;

        public static DirectoryInfo AssemblyLocation
        {
            get
            {
                if (_assemblyLocation == null)
                {
                    var location = Assembly.GetAssembly(typeof(Program))?.Location;
                    if (location == null) throw new InvalidOperationException("Failed to get entry assembly location");
                    if (Path.HasExtension(location))
                        location = PathTools.GetDirectory(location);
                    _assemblyLocation = new DirectoryInfo(location);
                }
                return _assemblyLocation;
            }
        }

        public static Version AssemblyVersion => Assembly.GetExecutingAssembly().GetName().Version;

        /// <summary>
        ///     Do not call before CheckForOldSettings() completes
        /// </summary>
        public static bool EnableDebug => Debugger.IsAttached || Settings.Default.Debug;

        /// <summary>
        ///     Base address of the telemetry/ratings backend, or null when there is none.
        /// </summary>
        /// <remarks>
        ///     MOKSH does not operate a backend. Upstream BCUninstaller posted crash reports, usage
        ///     statistics and application ratings to http://bugsklocman.ddns.net:7721 - cleartext HTTP,
        ///     to a dynamic-DNS host, with the payload in the URL query string. Crash reports carry
        ///     stack traces, which in an uninstaller routinely embed file paths containing the Windows
        ///     account name. That endpoint is removed rather than repointed; set this to an HTTPS
        ///     address you control if you ever add a backend of your own.
        /// </remarks>
        public static Uri ConnectionString { get; } = null;

        /// <summary>
        ///     True when a backend is configured. All network reporting is skipped when false.
        /// </summary>
        public static bool HomeServerAvailable => ConnectionString != null;

        public static string InstalledRegistryKeyName
        {
            get
            {
                if (_installedRegistryKeyName == null)
                    GetInstalledRegKey();
                return _installedRegistryKeyName;
            }
        }

        public static bool IsAfterUpgrade { get; private set; }

        /// <summary>
        ///     Use setter to override the value
        /// </summary>
        public static bool IsInstalled
        {
            get
            {
                if (!_isInstalled.HasValue)
                    _isInstalled = InstalledRegistryKeyName.IsNotEmpty();
                return _isInstalled.Value;
            }
            internal set { _isInstalled = value; }
        }

        internal static string ConfigFileFullname { get; private set; }

        /// <summary>
        ///     Remove old or invalid setting files and make sure settings are ready to be used.
        ///     Run before the settings are used, best at the very start of the application.
        /// </summary>
        internal static void PrepareSettings()
        {
            const string exeName = "Moksh";

            IsAfterUpgrade = false;
            try
            {
                var dir = AssemblyLocation;
                // Check if we are bundled with a launcher and place settings in the same folder as the launcher, so they are shared between different builds
                if (dir.Name.StartsWith("win-") && dir.Parent != null &&
                    File.Exists(Path.Combine(dir.Parent.FullName, exeName + ".exe"))) dir = dir.Parent;

                var settingsDir = dir.FullName;
                ConfigFileFullname = Path.Combine(settingsDir, exeName + ".settings");

                PortableSettingsProvider.PortableSettingsProvider.AppSettingsPathOverride = settingsDir;
                PortableSettingsProvider.PortableSettingsProvider.ApplicationNameOverride = exeName;

                var settingsXmlDocument = XDocument.Parse(File.ReadAllText(ConfigFileFullname));
                if (settingsXmlDocument.Root == null) throw new FormatException("Missing root element");
                
                var result = settingsXmlDocument.Root.Element("MiscVersion");
                if (result == null) throw new FormatException("Invalid version number");
                if (result.Value.Equals("Reset")) throw new OperationCanceledException("Settings reset was requested");

                if (!string.IsNullOrWhiteSpace(result.Value) && new Version(result.Value) < AssemblyVersion)
                    IsAfterUpgrade = true;

                // One extra check to make sure loading and using the settings doesn't throw
                // Initializes the Default settings object (unless it has been accessed before, which it shouldn't have)
                Settings.Default.Reload();
                Settings.Default.AdvancedSimulate = Settings.Default.AdvancedSimulate;
            }
            catch (Exception ex)
            {
                if (ex is FileNotFoundException)
                    Console.WriteLine(@"Settings file not found, creating new one.");
                else if (ex is not OperationCanceledException)
                    Console.WriteLine(@"Failed to load settings from the config file: " + ex);

                File.Delete(ConfigFileFullname);
                Settings.Default.Reload();
            }

            // Ensure the user ID is valid
            if (Settings.Default.MiscUserId == 0)
                Settings.Default.MiscUserId = GetUniqueUserId();

            if (IsAfterUpgrade)
                ClearCaches(false);
        }

        /// <summary>
        /// Get a random installation ID, generated once and then persisted in the settings file.
        /// </summary>
        /// <remarks>
        /// Upstream derived this deterministically from the Windows SID, every identity claim, and the
        /// MAC address of every network interface, folded through MD5. That is a hardware and account
        /// fingerprint: stable across reinstalls, correlatable across machines, and not something a
        /// user can reset. A random value carries the same "distinguish one install from another"
        /// meaning with none of that, and the user can clear it by resetting settings.
        /// </remarks>
        private static ulong GetUniqueUserId()
        {
            var buffer = new byte[sizeof(ulong)];
            RandomNumberGenerator.Fill(buffer);
            var id = BitConverter.ToUInt64(buffer, 0);

            // 0 is the "not yet set" sentinel used by PrepareSettings.
            return id == 0 ? 1 : id;
        }

        /// <summary>
        /// Check if this application is installed by looking for the registry key created by the installer.
        /// If the key is not found it means this is most likely a portable version.
        /// </summary>
        private static void GetInstalledRegKey()
        {
            // This GUID is the AppID from the installer. It can end with an optional identifier if the installer had to create a new key because of a conflict.
            // Must stay in sync with AppId in installer\BcuSetup.iss.
            const string appId = "edf1b036-2b58-45ab-a933-88b908e026f8";
            const string regUninstallersKeyDirect = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

            // Preferred: a stable per-vendor key written by the installer.
            // The Inno Setup AppId below is fixed, but an MSI names its uninstall key after the
            // ProductCode, which has to change on every major upgrade - so a hardcoded GUID stops
            // matching after the first upgrade. This key does not move between versions.
            if (TryGetInstalledFromVendorKey())
                return;

            try
            {
                using var regKey = Registry.LocalMachine.OpenSubKey(regUninstallersKeyDirect);

                if (regKey == null)
                    throw new ArgumentException("Could not open Software registry key");

                var keyNames = regKey.GetSubKeyNames().Where(x => x.Contains(appId, StringComparison.InvariantCultureIgnoreCase));

                foreach (var keyName in keyNames)
                {
                    using var subKey = regKey.OpenSubKey(keyName, true);

                    var installLocation = subKey?.GetStringSafe(RegistryFactory.RegistryNameInstallLocation);
                    if (string.IsNullOrEmpty(installLocation)) continue;

                    if (PathTools.SubPathIsInsideBasePath(installLocation, AssemblyLocation.FullName, true, true))
                    {
                        // We are installed!
                        _installedRegistryKeyName = keyName;

                        // Update the version number in case it changed, so the user can see it in the list of installed programs
                        subKey.SetValue("DisplayVersion", AssemblyVersion.ToString(), RegistryValueKind.String);
                    }
                }
            }
            catch
            {
                _installedRegistryKeyName = String.Empty;
            }
        }

        /// <summary>
        ///     Registry key the MSI writes so the application can recognise an installed deployment
        ///     without depending on a ProductCode that changes with every major upgrade.
        /// </summary>
        internal const string VendorRegistryKey = @"SOFTWARE\Binesh Balan\Moksh";

        /// <summary>
        ///     Returns true if the vendor key exists and points at this copy of the application.
        /// </summary>
        private static bool TryGetInstalledFromVendorKey()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(VendorRegistryKey);

                var installLocation = key?.GetStringSafe(RegistryFactory.RegistryNameInstallLocation);
                if (string.IsNullOrEmpty(installLocation))
                    return false;

                // Only claim to be installed if this executable is the installed one - a portable
                // copy running on a machine that also has MOKSH installed must still report portable.
                if (!PathTools.SubPathIsInsideBasePath(installLocation, AssemblyLocation.FullName, true, true))
                    return false;

                // Prefer the real ARP key name when the installer recorded it, so callers that use
                // this value to locate the uninstall entry keep working.
                var productCode = key.GetStringSafe("ProductCode");
                _installedRegistryKeyName = string.IsNullOrEmpty(productCode) ? VendorRegistryKey : productCode;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void StartLogCleaner()
        {
            try
            {
                const string cleanerName = "CleanLogs.bat";
                var cleanerPath = Path.Combine(AssemblyLocation.FullName, cleanerName);

                if (!File.Exists(cleanerPath))
                {
                    Console.WriteLine(@"WARNING: CleanLogs.bat doesn't exist, can't clean logs.");
                    return;
                }

                var cleanerUri = PathToUri(cleanerPath);
                if (cleanerUri.IsUnc)
                {
                    // 'cmd.exe /c start' doesn't work with UNC paths, script has to run in foreground.
                    Process.Start(new ProcessStartInfo(cleanerPath) { UseShellExecute = true });
                }
                else
                {
                    // Run cleanup script in minimized cmd window.
                    // Both cmd.exe and the script are fully qualified: the working directory is the
                    // application directory, which is user-writable for portable installs, and this
                    // process is elevated - an unqualified name would resolve there first.
                    var ps = new ProcessStartInfo
                    {
                        WorkingDirectory = AssemblyLocation.FullName,
                        FileName = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                        Arguments = "/c start /min \"\" \"" + cleanerPath + "\"",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Minimized
                    };
                    Process.Start(ps);
                }
            }
            catch (Exception ex)
            {
                // Ignore errors, not critical
                Console.WriteLine(ex);
            }
        }

        private static Uri PathToUri(string filePath)
        {
            try
            {
                return new Uri(filePath);
            }
            catch (UriFormatException)
            {
                filePath = Path.GetFullPath(filePath);
                return new Uri(filePath);
            }
        }

        public static void ClearCaches(bool showErrors)
        {
            try
            {
                MainWindow.CertificateCache.ClearChache();
                UninstallToolsGlobalConfig.ClearChache();
            }
            catch (SystemException systemException)
            {
                if (showErrors)
                    PremadeDialogs.GenericError(systemException);
                else
                    Console.WriteLine(systemException);
            }
        }

        /// <exception cref="InvalidOperationException">No backend is configured.</exception>
        public static HttpClient HomeServerClient
        {
            get
            {
                if (!HomeServerAvailable)
                    throw new InvalidOperationException("No reporting backend is configured for this build.");

                var cl = new HttpClient();
                cl.BaseAddress = ConnectionString;
                return cl;
            }
        }
    }
}