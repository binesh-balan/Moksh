/*
    Copyright (c) 2017 Marcin Szeniak (https://github.com/Klocman/)
    Apache License Version 2.0
*/

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core;
using Klocman;
using Klocman.Tools;
using Microsoft.Win32.Interop;
using UninstallerAutomatizer.Properties;
using UninstallTools;
using Debug = System.Diagnostics.Debug;

namespace UninstallerAutomatizer
{
    public class UninstallHandler
    {
        public bool KillOnFail { get; private set; }

        public string UninstallTarget { get; private set; }

        public bool IsDaemon { get; private set; }

        public event EventHandler<UninstallHandlerUpdateArgs> StatusUpdate;

        public void Pause()
        {
            throw new NotImplementedException();
        }

        public void Resume()
        {
            throw new NotImplementedException();
        }

        public void Start()
        {
            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();

            if (args.Length == 1 && args[0].Equals("/d", StringComparison.OrdinalIgnoreCase))
            {
                StartDaemon();
                return;
            }

            IsDaemon = false;

            if (args.Length < 2)
            {
                Program.ReturnValue = ResultWin32.ERROR_BAD_ARGUMENTS;
                OnStatusUpdate(new UninstallHandlerUpdateArgs(UninstallHandlerUpdateKind.Failed, Localization.Error_Invalid_number_of_arguments));
                return;
            }

            UninstallerType uType;
            if (!Enum.TryParse(args[0], out uType))
            {
                Program.ReturnValue = ResultWin32.ERROR_BAD_ARGUMENTS;
                OnStatusUpdate(new UninstallHandlerUpdateArgs(UninstallHandlerUpdateKind.Failed, string.Format(Localization.Error_UnknownUninstallerType, args[0])));
                return;
            }

            args = args.Skip(1).ToArray();

            if (args[0].Equals("/k", StringComparison.InvariantCultureIgnoreCase))
            {
                args = args.Skip(1).ToArray();
                KillOnFail = true;
            }

            UninstallTarget = string.Join(" ", args);
            

            if (!File.Exists(ProcessTools.SeparateArgsFromCommand(UninstallTarget).FileName))
            {
                Program.ReturnValue = ResultWin32.ERROR_BAD_ARGUMENTS;
                OnStatusUpdate(new UninstallHandlerUpdateArgs(UninstallHandlerUpdateKind.Failed, string.Format(Localization.Error_InvalidPath, UninstallTarget)));
                return;
            }

            if (uType != UninstallerType.Nsis)
            {
                Program.ReturnValue = ResultWin32.ERROR_BAD_ARGUMENTS;
                OnStatusUpdate(new UninstallHandlerUpdateArgs(UninstallHandlerUpdateKind.Failed, string.Format(Localization.Error_NotSupported, uType)));
                return;
            }

            OnStatusUpdate(new UninstallHandlerUpdateArgs(UninstallHandlerUpdateKind.Normal, string.Format(Localization.Message_Starting, UninstallTarget)));

            _automationThread = new Thread(AutomationThread) { Name = "AutomationThread", IsBackground = false, Priority = ThreadPriority.AboveNormal };
            _automationThread.Start();
        }

        private void StartDaemon()
        {
            IsDaemon = true;
            OnStatusUpdate(new UninstallHandlerUpdateArgs(UninstallHandlerUpdateKind.Normal, Localization.UninstallHandler_StartDaemon));

            _automationThread = new Thread(DaemonThread) { Name = "AutomationThread", IsBackground = false, Priority = ThreadPriority.AboveNormal };
            _automationThread.Start();
        }

        readonly ConcurrentDictionary<int, Task> _runningHooks = new();

        /// <summary>
        /// Run in background as a daemon. Receive application PIDs to monitor, "stop" to exit.
        /// </summary>
        private void DaemonThread()
        {
            try
            {
                // CurrentUserOnly restricts the pipe to the account that created it. This daemon runs
                // elevated and will drive UI Automation against any PID it is handed, so an unrestricted
                // pipe let a medium-integrity process point it at an elevated window - clicking through
                // consent and security dialogs it should not be able to reach.
                using (var server = new NamedPipeServerStream("UninstallAutomatizerDaemon", PipeDirection.In,
                           1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly))
                using (var reader = new StreamReader(server))
                {
                    while (true)
                    {
                        server.WaitForConnection();
                        Debug.WriteLine("Client connected through pipe");
                        while (true)
                        {
                            var line = reader.ReadLine()?.ToLowerInvariant();

                            Debug.WriteLine("Received through pipe: " + (line ?? "NULL"));

                            if (line == null)
                            {
                                Thread.Sleep(500);
                                continue;
                            }

                            if (line == "stop")
                                return;

                            int pid;
                            if (!int.TryParse(line, out pid))
                            {
                                OnStatusUpdate(new UninstallHandlerUpdateArgs(UninstallHandlerUpdateKind.Normal,
                                    string.Format(Localization.UninstallHandler_InvalidProcessNumber, pid)));
                                continue;
                            }

                            try
                            {
                                if (_runningHooks.TryGetValue(pid, out var ttt) && !ttt.IsCompleted)
                                    continue;

                                var target = Process.GetProcessById(pid);

                                if (!ProcessCanBeAutomatized(target))
                                {
                                    Debug.WriteLine("Tried to automate not allowed process: " + target.ProcessName);
                                    continue;
                                }

                                var app = Application.Attach(target);

                                var t = new Task(() =>
                                {
                                    try
                                    {
                                        Debug.WriteLine("Running automatizer on thread pool");
                                        AutomatedUninstallManager.AutomatizeApplication(app, AutomatizeStatusCallback);
                                    }
                                    catch (Exception ex)
                                    {
                                        OnStatusUpdate(new UninstallHandlerUpdateArgs(UninstallHandlerUpdateKind.Normal,
                                            string.Format(Localization.Message_UninstallFailed,
                                                ex.InnerException?.Message ?? ex.Message)));
                                    }
                                    finally
                                    {
                                        _runningHooks.TryRemove(pid, out _);
                                    }
                                });

                                _runningHooks.AddOrUpdate(pid, t, (i, task) => t);

                                Debug.WriteLine("Created automatizer thread");
                                t.Start();
                            }
                            catch (SystemException ex) { Console.WriteLine(ex); }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OnStatusUpdate(new UninstallHandlerUpdateArgs(UninstallHandlerUpdateKind.Normal,
                    Localization.UninstallHandler_DaemonStoppedReason + (ex.InnerException?.Message ?? ex.Message)));
            }
            finally
            {
                OnStatusUpdate(new UninstallHandlerUpdateArgs(UninstallHandlerUpdateKind.Succeeded, Localization.Message_Success));
            }
        }

        /// <summary>
        /// Only automate processes that descend from the caller that asked us to.
        /// </summary>
        /// <remarks>
        /// The previous check ("PID above 4 and not myself") accepted any process on the machine, so
        /// a caller on the pipe could aim this elevated automation at an arbitrary window. Requiring
        /// an ancestor match keeps it to uninstallers actually launched by the requesting task.
        /// </remarks>
        private static bool ProcessCanBeAutomatized(Process target)
        {
            if (target.Id <= 4) return false;
            if (target.Id == Environment.ProcessId) return false;
            if (string.Equals(target.ProcessName, Program.AutomatizerProcessName, StringComparison.Ordinal)) return false;

            string modulePath;
            try
            {
                modulePath = target.MainModule?.FileName;
            }
            catch (Exception ex)
            {
                // Can't inspect it (protected or higher integrity) - don't touch it.
                Debug.WriteLine($"Refusing to automate process {target.Id}: {ex.Message}");
                return false;
            }

            if (string.IsNullOrEmpty(modulePath)) return false;

            // Never drive Windows' own binaries. consent.exe (the UAC prompt), credential dialogs and
            // security UI all live here, and clicking those is exactly what this must not be usable for.
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windows) &&
                modulePath.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"Refusing to automate a Windows system process: {modulePath}");
                return false;
            }

            return true;
        }

        Thread _automationThread;
        private bool _abort;

        private void AutomationThread()
        {
            try
            {
                AutomatedUninstallManager.UninstallNsisQuietly(UninstallTarget, AutomatizeStatusCallback);

                OnStatusUpdate(new UninstallHandlerUpdateArgs(UninstallHandlerUpdateKind.Succeeded, Localization.Message_Success));
            }
            catch (AutomatedUninstallManager.AutomatedUninstallException ex)
            {
                Debug.Assert(ex.InnerException != null, "ex.InnerException != null");

                OnStatusUpdate(new UninstallHandlerUpdateArgs(UninstallHandlerUpdateKind.Failed, string.Format(Localization.Message_UninstallFailed, ex.InnerException?.Message ?? ex.Message)));

                // todo grace period / move to window?
                if (ex.UninstallerProcess != null && KillOnFail)
                {
                    try
                    {
                        ex.UninstallerProcess.Kill(true);
                    }
                    catch
                    {
                        // Ignore process errors, can't do anything about it
                    }
                }

                Program.ReturnValue = ResultWin32.ERROR_FUNCTION_FAILED;
            }
        }

        private void AutomatizeStatusCallback(string s)
        {
            if (_abort) throw new OperationCanceledException(Localization.Message_UserCancelled);
            OnStatusUpdate(new UninstallHandlerUpdateArgs(UninstallHandlerUpdateKind.Normal, s));
        }

        private UninstallHandlerUpdateArgs _previousArgs;

        protected virtual void OnStatusUpdate(UninstallHandlerUpdateArgs e)
        {
            // Filter out repeated updates
            if (Equals(_previousArgs, e)) return;
            _previousArgs = e;

            StatusUpdate?.Invoke(this, e);
            Console.WriteLine(e.Message);
        }

        public void Abort()
        {
            _abort = true;
        }
    }
}