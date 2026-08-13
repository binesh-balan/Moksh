using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using UniversalUninstaller.Properties;

namespace UniversalUninstaller
{
    internal static class Program
    {
        private static bool _quietMode;

        /// <summary>
        /// The main entry point for the application.
        /// args:
        /// Exe.exe [/q] DirPath
        /// /q - quiet
        /// return codes:
        /// 0 - ok
        /// 1 - Installation aborted by user (cancel button)
        /// 11 - invalid arguments
        /// 161 - failed to delete
        /// </summary>
        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var args = Environment.GetCommandLineArgs().Skip(1).ToList();

            if (args.Any(x => x.Equals("/q", StringComparison.OrdinalIgnoreCase)))
                _quietMode = true;

            if (args.Count > 2 || args.Count < 1)
            {
                ShowInvalidArgsBox();
                return 11;
            }

            var strings = args.Where(x => !x.StartsWith("/", StringComparison.Ordinal)).ToList();
            if (strings.Count != 1)
            {
                ShowInvalidArgsBox();
                return 11;
            }

            DirectoryInfo dir;
            try
            {
                dir = new DirectoryInfo(strings.Single().Trim(' ', '"'));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                ShowInvalidArgsBox();
                return 11;
            }

            // Refuse protected locations before showing any UI or deleting anything. The target path
            // originates from a registry InstallLocation value, which any standard user can write, so
            // it is untrusted input reaching a recursive delete in an elevated process.
            if (Klocman.Tools.PathTools.IsProtectedSystemPath(dir.FullName))
            {
                ShowProtectedPathBox(dir.FullName);
                return 11;
            }

            if (!_quietMode)
            {
                var uninstallWindow = new UninstallSelection(dir);
                Application.Run(uninstallWindow);
                if (uninstallWindow.WasCancelled)
                    return 1;
                if (uninstallWindow.DeleteFailed)
                    return 161;
            }
            else
            {
                try
                {
                    DeleteItems(new[] {dir});
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception);
                    Klocman.LogWriter.WriteMessageToLog(exception.ToString());
                    return 161;
                }
            }

            return 0;
        }

        private static void ShowProtectedPathBox(string path)
        {
            var message = "Refusing to delete a protected system location:" + Environment.NewLine + path;
            Console.WriteLine(message);
            Klocman.LogWriter.WriteMessageToLog(message);

            if (_quietMode) return;

            MessageBox.Show(message, Localisation.Program_ShowInvalidArgsBox_Title,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public static void DeleteItems(IEnumerable<FileSystemInfo> it)
        {
            foreach (var fileSystemInfo in it)
            {
                if (Klocman.Tools.PathTools.IsProtectedSystemPath(fileSystemInfo.FullName))
                    throw new UnauthorizedAccessException(
                        "Refusing to delete a protected system location: " + fileSystemInfo.FullName);

                if (fileSystemInfo is DirectoryInfo di)
                {
                    RecursiveDelete(di);
                }
                else
                {
                    ClearReadOnlyFlag(fileSystemInfo);
                    fileSystemInfo.Delete();
                }
            }
        }

        public static void RecursiveDelete(DirectoryInfo baseDir)
        {
            if (!baseDir.Exists)
                return;

            // A junction or symlink enumerates its *target's* contents, so descending into one would
            // delete files outside the directory the user actually selected. Unlink it and stop.
            if (Klocman.Tools.PathTools.IsReparsePoint(baseDir))
            {
                baseDir.Delete();
                return;
            }

            foreach (var info in baseDir.GetFileSystemInfos())
            {
                if (info is DirectoryInfo dir)
                {
                    // Don't clear the read-only flag before the reparse check - doing so would apply
                    // the attribute change to the link target.
                    if (Klocman.Tools.PathTools.IsReparsePoint(dir))
                    {
                        dir.Delete();
                        continue;
                    }

                    ClearReadOnlyFlag(dir);
                    RecursiveDelete(dir);
                }
                else
                {
                    ClearReadOnlyFlag(info);
                    info.Delete();
                }
            }

            ClearReadOnlyFlag(baseDir);
            WaitForDirEmpty(baseDir);
            baseDir.Delete();
        }

        /// <summary>
        /// FileSystemInfo.Delete is non-blocking, so we have to wait until it finished
        /// before deleting the owning directory to prevent dir not empty exceptions.
        /// Bounded - a locked file, or one an active process keeps recreating, must not hang
        /// an elevated process forever.
        /// </summary>
        private static void WaitForDirEmpty(DirectoryInfo baseDir)
        {
            const int maxAttempts = 100; // ~10 seconds
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                Thread.Sleep(100);
                baseDir.Refresh();
                if (!baseDir.Exists || !baseDir.GetFileSystemInfos().Any()) return;
            }

            throw new IOException("Timed out waiting for directory to empty: " + baseDir.FullName);
        }

        /// <summary>
        /// FileSystemInfo.Delete throws access denied if file or dir is read only.
        /// </summary>
        private static void ClearReadOnlyFlag(FileSystemInfo info)
        {
            info.Attributes &= ~FileAttributes.ReadOnly;
        }

        private static void ShowInvalidArgsBox()
        {
            if (_quietMode) return;

            MessageBox.Show(Localisation.Program_ShowInvalidArgsBox_Message,
                Localisation.Program_ShowInvalidArgsBox_Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}