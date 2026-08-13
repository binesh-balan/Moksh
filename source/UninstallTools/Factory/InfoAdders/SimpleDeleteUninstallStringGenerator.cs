/*
    Copyright (c) 2017 Marcin Szeniak (https://github.com/Klocman/)
    Apache License Version 2.0
*/

using System;
using System.Diagnostics;
using System.IO;
using Klocman.Tools;

namespace UninstallTools.Factory.InfoAdders
{
    public class SimpleDeleteUninstallStringGenerator : IMissingInfoAdder
    {
        static SimpleDeleteUninstallStringGenerator()
        {
            try
            {
                UniversalUninstallerFilename = new FileInfo(
                    Path.Combine(UninstallToolsGlobalConfig.AssemblyLocation, "UniversalUninstaller.exe"));

                UniversalUninstallerIsAvailable = UniversalUninstallerFilename.Exists;
            }
            catch (Exception e)
            {
                Trace.WriteLine(e);

                UniversalUninstallerFilename = null;
                UniversalUninstallerIsAvailable = false;
            }
        }

        public static FileInfo UniversalUninstallerFilename { get; }
        public static bool UniversalUninstallerIsAvailable { get; }

        public void AddMissingInformation(ApplicationUninstallerEntry target)
        {
            if (target.UninstallerKind != UninstallerType.SimpleDelete)
                return;

            // No deleter bundled means no uninstall method. Previously this fell back to building a
            // "cmd.exe /C del ..." string by interpolating InstallLocation, which let a registry value
            // containing a quote inject arbitrary commands into an elevated shell.
            if (!UniversalUninstallerIsAvailable)
                return;

            // Defence in depth: UninstallerTypeAdder already screens the location before assigning
            // SimpleDelete, but this adder is public and reachable on its own.
            if (PathTools.IsProtectedSystemPath(target.InstallLocation))
                return;

            if (target.UninstallString == null)
                target.UninstallString = GetNewUninstallString(target.InstallLocation, false);

            if (target.QuietUninstallString == null)
                target.QuietUninstallString = GetNewUninstallString(target.InstallLocation, true);
        }

        public string[] RequiredValueNames { get; } = {
            nameof(ApplicationUninstallerEntry.UninstallerKind),
            nameof(ApplicationUninstallerEntry.InstallLocation)
        };

        public bool RequiresAllValues { get; } = true;
        public bool AlwaysRun { get; } = false;

        public string[] CanProduceValueNames { get; } = {
            nameof(ApplicationUninstallerEntry.UninstallString),
            nameof(ApplicationUninstallerEntry.QuietUninstallString)
        };

        public InfoAdderPriority Priority { get; } = InfoAdderPriority.RunDeadLast;

        private static string GetNewUninstallString(string installLocation, bool quiet)
        {
            // Trailing backslash before the closing quote is deliberate (it marks the value as a
            // directory), but it must not be preceded by a quote from the input itself.
            var sanitized = installLocation.Replace("\"", string.Empty).TrimEnd('\\');

            return quiet
                ? $"\"{UniversalUninstallerFilename.FullName}\" /Q \"{sanitized}\\\""
                : $"\"{UniversalUninstallerFilename.FullName}\" \"{sanitized}\\\"";
        }
    }
}