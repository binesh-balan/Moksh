/*
    Copyright (c) 2017 Marcin Szeniak (https://github.com/Klocman/)
    Apache License Version 2.0
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using Klocman.Extensions;
using Microsoft.Win32;

namespace Klocman.Tools
{
    public static class PathTools
    {
        private static Dictionary<string, string> _volumeIdLookup;

        private static readonly char[] PathTrimChars = {
            '\\',
            '/',
            '"',
            // SPACE 
            '\u0020',
            // NO-BREAK SPACE 
            '\u00A0',
            // OGHAM SPACE MARK 
            '\u1680',
            // EN QUAD 
            '\u2000',
            // EM QUAD 
            '\u2001',
            // EN SPACE 
            '\u2002',
            // EM SPACE 
            '\u2003',
            // THREE-PER-EM SPACE 
            '\u2004',
            // FOUR-PER-EM SPACE 
            '\u2005',
            // SIX-PER-EM SPACE 
            '\u2006',
            // FIGURE SPACE 
            '\u2007',
            // PUNCTUATION SPACE 
            '\u2008',
            // THIN SPACE 
            '\u2009',
            // HAIR SPACE 
            '\u200A',
            // NARROW NO-BREAK SPACE 
            '\u202F',
            // MEDIUM MATHEMATICAL SPACE 
            '\u205F',
            // and IDEOGRAPHIC SPACE 
            '\u3000',

            // LINE SEPARATOR 
            '\u2028',

            // PARAGRAPH SEPARATOR  
            '\u2029',

            // CHARACTER TABULATION 
            '\u0009',
            // LINE FEED 
            '\u000A',
            // LINE TABULATION 
            '\u000B',
            // FORM FEED 
            '\u000C',
            // CARRIAGE RETURN 
            '\u000D',
            // NEXT LINE 
            '\u0085'
        };

        private static void PopulateVolumeIdLookup()
        {
            try
            {
                _volumeIdLookup = new Dictionary<string, string>();

                var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_Volume");

                foreach (var queryObj in searcher.Get().OfType<ManagementObject>())
                {
                    var id = (queryObj["DeviceID"] as string)?.TrimEnd('\\', '/');
                    var dl = queryObj["DriveLetter"] as string;

                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(dl))
                        continue;

                    _volumeIdLookup.Add(id, dl);
                }
            }
            catch (ManagementException e)
            {
                Console.WriteLine($@"An error occurred while querying for WMI data: {e.Message}");
            }
        }

        /// <summary>
        /// Convert path from the \\?\Volume{} form to the drive letter form.
        /// Only works for volumes with assigned drive letters.
        /// </summary>
        /// <param name="volumePath">Path to any element with volume in \\?\Volume{} form.</param>
        public static string ResolveVolumeIdToPath(string volumePath)
        {
            if (_volumeIdLookup == null)
                PopulateVolumeIdLookup();

            _volumeIdLookup.ForEach(x => volumePath = volumePath.Replace(x.Key, x.Value, StringComparison.OrdinalIgnoreCase));

            return volumePath;
        }

        /// <summary>
        /// Get full path of an application available in current environment. Same as writing it's name in CMD.
        /// </summary>
        /// <param name="filename">Name of the exectuable, including the extension</param>
        /// <returns></returns>
        /// <summary>
        /// Resolve an executable name to a full path using only machine-scoped locations.
        /// </summary>
        /// <remarks>
        /// This process runs elevated, so the search must not consult anything a standard user can
        /// write. Deliberately excluded:
        /// <list type="bullet">
        /// <item>the current directory - it is the application directory, which is user-writable for
        /// portable installs, so a planted binary there would run as administrator;</item>
        /// <item>HKCU App Paths - writable without elevation, giving any medium-integrity process a
        /// one-value route to elevated code execution.</item>
        /// </list>
        /// The machine PATH is still searched (it needs administrator rights to modify) but the user
        /// PATH from HKCU\Environment is not.
        /// </remarks>
        public static string GetFullPathOfExecutable(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) return null;

            // Reject anything that isn't a bare filename - a caller passing a path would sidestep the
            // whole point of this method.
            if (filename.IndexOfAny(new[] { '\\', '/', ':' }) >= 0) return null;

            IEnumerable<string> paths = Enumerable.Empty<string>();

            var machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
            if (machinePath != null)
                paths = paths.Concat(machinePath.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));

            var combinations = paths
                .Select(x =>
                {
                    try { return Path.Combine(NormalizePath(x), filename); }
                    catch (Exception) { return null; }
                })
                .Where(x => x != null);

            return combinations.FirstOrDefault(File.Exists) ?? GetExecutablePathFromAppPaths(filename);
        }

        /// <param name="exename">name of the exectuable, including .exe</param>
        private static string GetExecutablePathFromAppPaths(string exename)
        {
            const string appPaths = @"Software\Microsoft\Windows\CurrentVersion\App Paths";
            var executableEntry = Path.Combine(appPaths, exename);
            // HKLM only. HKCU is writable without elevation and must never steer an elevated process.
            using (var key = Registry.LocalMachine.OpenSubKey(executableEntry))
            {
                var result = key?.GetStringSafe(null);
                return string.IsNullOrWhiteSpace(result) ? null : result.Trim('"');
            }
        }

        /// <summary>
        ///     Get full directory path of directory that contains the item pointed at by the path string.
        /// </summary>
        public static string GetDirectory(string fullPath)
        {
            var trimmed = fullPath.TrimEnd('"', ' ', '\\').TrimStart('"', ' ');
            if (trimmed.Contains('\\'))
            {
                var index = trimmed.LastIndexOf('\\');
                if (index < trimmed.Length)
                {
                    return trimmed.Substring(0, index);
                }
            }
            return string.Empty;
        }

        /// <summary>
        ///     Get the topmost part of the path. If this is not a valid path return string.Empty.
        /// </summary>
        public static string GetName(string fullPath)
        {
            var trimmed = fullPath.TrimEnd('"', ' ', '\\');
            if (trimmed.Contains('\\'))
            {
                var index = trimmed.LastIndexOf('\\') + 1;
                if (index < trimmed.Length)
                {
                    return trimmed.Substring(index);
                }
            }
            return string.Empty;
        }

        /// <summary>
        ///     Trim supplied path to the required depth.
        /// </summary>
        /// <param name="path">Path to be trimmed</param>
        /// <param name="maxLevel">Maximal depth of the path, 0 will show only the root node</param>
        /// <returns>Trimmed path</returns>
        public static string GetPathUpToLevel(string path, int maxLevel)
        {
            return GetPathUpToLevel(path, maxLevel, false);
        }

        /// <summary>
        ///     Trim supplied path or full filename to the required depth.
        /// </summary>
        /// <param name="path">Path to be trimmed</param>
        /// <param name="maxLevel">Maximal depth of the path, 0 will show only the root node</param>
        /// <param name="containsFilename">If true, the last part of the path will be ignored, since it is a filename</param>
        /// <returns>Trimmed path</returns>
        public static string GetPathUpToLevel(string path, int maxLevel, bool containsFilename)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string directory;
            try
            {
                directory = containsFilename ? Path.GetDirectoryName(path) : Path.GetFullPath(path);

                if (string.IsNullOrEmpty(directory))
                    return string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }

            var directoryParts = directory.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (directoryParts.Length >= 1)
            {
                var result = string.Empty;

                for (var i = 0; i < maxLevel + 1 && i < directoryParts.Length; i++)
                {
                    result = string.Concat(result, directoryParts[i], "\\");
                }

                return result;
            }
            return string.Empty;
        }

        // Try to get the windows directory, returns null if failed
        public static DirectoryInfo GetWindowsDirectory()
        {
            try
            {
                var windowsDirectory = Environment.GetEnvironmentVariable("SystemRoot");
                if (windowsDirectory != null) return new DirectoryInfo(windowsDirectory);
            }
            catch
            {
                //Check other
            }
            try
            {
                var windowsDirectory = Environment.GetEnvironmentVariable("windir");
                if (windowsDirectory != null) return new DirectoryInfo(windowsDirectory);
            }
            catch
            {
                //Messed up environment variables or security too high
            }
            return null;
        }

        /// <summary>
        ///     Change path to normal case. Example: C:\PROGRAM FILES => C:\Program files
        /// </summary>
        public static string PathToNormalCase(string path)
        {
            var directoryParts = NormalizePath(path).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (directoryParts.Length < 1)
                return string.Empty;

            var result = string.Empty;

            for (var i = 0; i < directoryParts.Length; i++)
            {
                var part = directoryParts[i].ToLower();
                result = string.Concat(result, part.Substring(0, 1).ToUpperInvariant() + part.Substring(1), "\\");
            }

            return result;
        }

        public static bool PathsEqual(string path1, string path2)
        {
            if (string.IsNullOrEmpty(path1) || string.IsNullOrEmpty(path2))
                return false;

            try
            {
                path1 = path1.SafeNormalize().Trim(PathTrimChars);
                path2 = path2.SafeNormalize().Trim(PathTrimChars);
                return path1.Equals(path2, StringComparison.InvariantCultureIgnoreCase);
            }
            catch
            {
                // Fall back to ordinal in case SafeNormalize isn't safe enough
                return path1.Trim(PathTrimChars).Equals(path2.Trim(PathTrimChars), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Remove unnecessary spaces, quotes and path separators from start and end of the path.
        /// Might produce different path than intended in case it contains invalid unicode characters.
        /// </summary>
        public static string NormalizePath(string path1)
        {
            if (path1 == null) throw new ArgumentNullException(nameof(path1));
            return path1.SafeNormalize().Trim(PathTrimChars);
        }

        public static bool PathsEqual(FileSystemInfo path1, FileSystemInfo path2)
        {
            if (path1 == null || path2 == null)
                return false;

            return PathsEqual(path1.FullName, path2.FullName);
        }

        /// <summary>
        /// Replace all invalid file name characters from a string with _ so that it can be used as a file name.
        /// </summary>
        public static string SanitizeFileName(string name)
        {
            var invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            var invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);

            return Regex.Replace(name, invalidRegStr, "_");
        }

        /// <summary>
        /// Version of Path.Combine with much less restrictive input checks, and additional path cleanup.
        /// </summary>
        public static string GenerousCombine(string path1, string path2)
        {
            if (path1 == null || path2 == null)
                throw new ArgumentNullException(path1 == null ? nameof(path1) : nameof(path2));

            path1 = NormalizePath(path1);
            path2 = NormalizePath(path2);

            if (path2.Length == 0) return path1;
            if (path1.Length == 0 || Path.IsPathRooted(path2)) return path2;

            return path1 + Path.DirectorySeparatorChar + path2;
        }

        /// <summary>
        /// Get a cleaned up list of all paths in the PATH variables of both current user and the machine. Duplicates are removed.
        /// </summary>
        public static IEnumerable<string> GetAllEnvironmentPaths()
        {
            var parts = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)?.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries) ?? Enumerable.Empty<string>();
            parts = parts.Concat(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)?.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries) ?? Enumerable.Empty<string>());

            return parts.Where(x => !string.IsNullOrEmpty(x)).Select(NormalizePath).Select(Path.GetFullPath).DistinctBy(s => s.ToLower());
        }

        /// <summary>
        /// Check if subPath is a sub path inside basePath.
        /// If isFilesystemPath is true then attempt to normalize the path to its absolute form on the filesystem. Set to false for registry and other paths.
        /// </summary>
        public static bool SubPathIsInsideBasePath(string basePath, string subPath, bool normalizeFilesystemPath, bool includeExactMatch)
        {
            if (basePath == null) return false;
            basePath = NormalizePath(basePath).Replace('\\', '/');
            if (string.IsNullOrEmpty(basePath)) return false;
            if (normalizeFilesystemPath)
            {
                try { basePath = Path.GetFullPath(basePath).Replace('\\', '/'); }
                catch (SystemException) { }
            }

            if (subPath == null) return false;
            subPath = NormalizePath(subPath).Replace('\\', '/');
            if (string.IsNullOrEmpty(subPath)) return false;
            if (normalizeFilesystemPath)
            {
                try { subPath = Path.GetFullPath(subPath).Replace('\\', '/'); }
                catch (SystemException) { }
            }

            return subPath.StartsWith(basePath + '/', StringComparison.InvariantCultureIgnoreCase) || includeExactMatch && subPath.Equals(basePath, StringComparison.InvariantCultureIgnoreCase);
        }

        /// <summary>
        /// Directories that must never be deleted or handed to a recursive delete, even if some
        /// registry entry claims an application lives there. Resolved once, absolute and normalized.
        /// </summary>
        private static readonly Lazy<string[]> ProtectedRoots = new(() =>
        {
            var folders = new[]
            {
                Environment.SpecialFolder.Windows,
                Environment.SpecialFolder.System,
                Environment.SpecialFolder.SystemX86,
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.CommonProgramFiles,
                Environment.SpecialFolder.CommonProgramFilesX86,
                Environment.SpecialFolder.CommonApplicationData,
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.DesktopDirectory,
                Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.CommonDesktopDirectory,
                Environment.SpecialFolder.CommonDocuments,
                Environment.SpecialFolder.StartMenu,
                Environment.SpecialFolder.CommonStartMenu,
                Environment.SpecialFolder.Programs,
                Environment.SpecialFolder.CommonPrograms,
            };

            var results = new List<string>(folders.Length + 2);
            foreach (var folder in folders)
            {
                try
                {
                    var path = Environment.GetFolderPath(folder);
                    if (!string.IsNullOrWhiteSpace(path)) results.Add(Path.GetFullPath(path).TrimEnd('\\'));
                }
                catch (SystemException) { /* Folder not present on this system */ }
            }

            try
            {
                var users = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                if (!string.IsNullOrWhiteSpace(users)) results.Add(Path.GetFullPath(users).TrimEnd('\\'));
            }
            catch (SystemException) { }

            return results.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        });

        /// <summary>
        /// True if the path must never be deleted: a drive root, a well-known system or profile
        /// directory, or anything at or above them. Anything that cannot be resolved is treated as
        /// protected, so a malformed path fails closed rather than open.
        /// </summary>
        /// <remarks>
        /// This is the single gate for destructive operations. Callers must not roll their own
        /// checks - inconsistent guards between call sites is what allowed a registry-supplied
        /// InstallLocation to reach a recursive delete.
        /// </remarks>
        public static bool IsProtectedSystemPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;

            string full;
            try
            {
                full = Path.GetFullPath(NormalizePath(path)).TrimEnd('\\');
            }
            catch (Exception)
            {
                // Unparseable path - fail closed.
                return true;
            }

            if (full.Length == 0) return true;

            // Drive root ("C:", "C:\") or a bare UNC share root ("\\server\share").
            try
            {
                var root = Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(root)) return true;
                if (string.Equals(full, root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception)
            {
                return true;
            }

            foreach (var root in ProtectedRoots.Value)
            {
                // Exact match, or the candidate is an ancestor of a protected root.
                if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return true;
                if (SubPathIsInsideBasePath(full, root, false, false)) return true;
            }

            // Anything inside the Windows directory, at any depth.
            try
            {
                var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrWhiteSpace(windows) &&
                    SubPathIsInsideBasePath(windows, full, false, true))
                    return true;
            }
            catch (SystemException) { }

            return false;
        }

        /// <summary>
        /// True if the directory is a reparse point (junction, symlink or mount point).
        /// Recursive operations must never traverse into one - enumerating a junction returns the
        /// contents of its target, so descending through it acts on a directory the caller never chose.
        /// </summary>
        public static bool IsReparsePoint(FileSystemInfo info)
        {
            try
            {
                return info != null && info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception)
            {
                // Can't read the attributes - assume it is, so we skip it.
                return true;
            }
        }
    }
}