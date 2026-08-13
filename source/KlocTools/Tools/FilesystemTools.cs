/*
    Copyright (c) 2017 Marcin Szeniak (https://github.com/Klocman/)
    Apache License Version 2.0
*/

using System;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Klocman.Extensions;

namespace Klocman.Tools
{
    public static class FilesystemTools
    {
        /// <summary>
        /// Check the architecture of the executable. E.g. 64bit.
        /// Returns Unknown if the architecture is unsupported or not specified.
        /// </summary>
        /// <param name="filename">Full path to the executable file.</param>
        public static MachineType CheckExecutableMachineType(string filename)
        {
            if (!filename.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase))
            {
                throw new IOException("Not a Windows .exe file.");
            }

            using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.Position = 0x3c;
                var fileData = new byte[1024];

                var bytesRead = stream.Read(fileData, 0, 1024);

                for (var i = 0; i < bytesRead; i++)
                {
                    // Look for the PE signature (PE\0\0)
                    if (i + 5 >= bytesRead) break;
                    if (fileData[i] != 0x50) continue;
                    if (fileData[i + 1] != 0x45 || fileData[i + 2] != 0 || fileData[i + 3] != 0) continue;

                    // Join two bytes representing the architecture
                    var machineId = fileData[i + 5] << 8 | fileData[i + 4];
                    switch (machineId)
                    {
                        case 0x8664:
                            return MachineType.X64;
                        case 0x14c:
                            return MachineType.X86;
                        case 0x200:
                            return MachineType.Ia64;
                        case 0xaa64:
                        case 0xA641:
                        case 0xA64E:
                            return MachineType.ARM64;
                        default:
                            return MachineType.Unknown;
                    }
                }
            }

            return MachineType.Unknown;
        }

        public static void CopyRecursive(string sourcePath, string targetPath)
        {
            CopyRecursive(new DirectoryInfo(sourcePath), new DirectoryInfo(targetPath));
        }

        public static void CopyRecursive(DirectoryInfo source, DirectoryInfo target)
        {
            // Check if the target directory exists, if not, create it.
            if (Directory.Exists(target.FullName) == false)
            {
                Directory.CreateDirectory(target.FullName);
            }

            // Copy each file into it’s new directory.
            foreach (var fi in source.GetFiles())
            {
                //Console.WriteLine(@"Copying {0}\{1}", target.FullName, fi.Name);
                fi.CopyTo(Path.Combine(target.ToString(), fi.Name), true);
            }

            // Copy each subdirectory using recursion.
            foreach (var diSourceSubDir in source.GetDirectories())
            {
                var nextTargetSubDir =
                    target.CreateSubdirectory(diSourceSubDir.Name);
                CopyRecursive(diSourceSubDir, nextTargetSubDir);
            }
        }

        public static bool CreateSymlink(string symlinkFileName, string targetFileName, SymbolicLinkType type)
        {
            return CreateSymbolicLink(symlinkFileName, targetFileName, type) != 0;
        }

        public static void MoveDirectory(string sourcePath, string targetPath)
        {
            MoveDirectory(new DirectoryInfo(sourcePath), new DirectoryInfo(targetPath));
        }

        public static void MoveDirectory(DirectoryInfo source, DirectoryInfo target)
        {
            if (source.RootEquals(target))
                Directory.Move(source.FullName, target.FullName);
            else
            {
                CopyRecursive(source, target);
                source.Delete(true);
            }
        }

        public static void CompressDirectory(string dirFullName) => CompressDirectory(dirFullName, ManagementOptions.InfiniteTimeout);
        public static void CompressDirectory(string dirFullName, TimeSpan timeout)
        {
            var objPath = "Win32_Directory.Name=" + "\"" + dirFullName.Replace(@"\", @"\\") + "\"";
            using (var dir = new ManagementObject(objPath))
            {
                var outParams = dir.InvokeMethod("Compress", null, new InvokeMethodOptions { Timeout = timeout });
                if (outParams == null) throw new ArgumentNullException(nameof(outParams));
                var ret = (uint)outParams.Properties["ReturnValue"].Value;
                if (ret != 0)
                    throw new IOException("Win32_Directory.Compress returned " + ret);
            }
        }

        /// <summary>
        /// True if any non-administrator principal can modify the file or the directory holding it.
        /// </summary>
        /// <remarks>
        /// Use this before executing any binary or script this application discovered rather than
        /// shipped. Running elevated means a file a standard user can replace is a direct route from
        /// medium integrity to administrator. Returns true (unsafe) whenever the answer cannot be
        /// determined, so an unreadable ACL fails closed.
        /// </remarks>
        public static bool IsWritableByNonAdministrators(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;

            try
            {
                if (!IsPathEntryProtected(path)) return true;

                var parent = Path.GetDirectoryName(Path.GetFullPath(path));
                // A writable containing directory lets an attacker replace the file wholesale.
                return !string.IsNullOrEmpty(parent) && !IsPathEntryProtected(parent);
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>Returns false if a non-administrator principal holds a write-ish right on the item.</summary>
        private static bool IsPathEntryProtected(string path)
        {
            AuthorizationRuleCollection rules;
            try
            {
                var info = new FileInfo(path);
                rules = info.Exists
                    ? info.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
                    : new DirectoryInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier));
            }
            catch (Exception)
            {
                return false;
            }

            const FileSystemRights dangerous = FileSystemRights.WriteData
                                               | FileSystemRights.CreateFiles
                                               | FileSystemRights.AppendData
                                               | FileSystemRights.CreateDirectories
                                               | FileSystemRights.Delete
                                               | FileSystemRights.DeleteSubdirectoriesAndFiles
                                               | FileSystemRights.ChangePermissions
                                               | FileSystemRights.TakeOwnership
                                               | FileSystemRights.WriteAttributes
                                               | FileSystemRights.WriteExtendedAttributes;

            var trusted = new[]
            {
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                new SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, null),
            };

            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.AccessControlType != AccessControlType.Allow) continue;
                if ((rule.FileSystemRights & dangerous) == 0) continue;

                if (rule.IdentityReference is not SecurityIdentifier sid) return false;
                if (Array.Exists(trusted, t => sid.Equals(t))) continue;

                // TrustedInstaller and other service SIDs are administrator-equivalent for this purpose.
                if (sid.Value.StartsWith("S-1-5-80-", StringComparison.OrdinalIgnoreCase)) continue;

                return false;
            }

            return true;
        }

        [DllImport("shlwapi.dll")]
        public static extern bool PathIsNetworkPath(string pszPath);

        [DllImport("kernel32.dll", EntryPoint = "CreateSymbolicLinkW", CharSet = CharSet.Unicode)]
        private static extern int CreateSymbolicLink([In] string lpSymlinkFileName, [In] string lpTargetFileName,
            SymbolicLinkType dwFlags);
    }
}