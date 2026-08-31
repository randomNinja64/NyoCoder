using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace NyoCoder
{
    internal struct ProcessRunResult
    {
        public int ExitCode;
        public string StdOut;
        public string StdErr;
    }

    internal sealed class ProcessRunOptions
    {
        public string StdinData;
        public string WorkingDirectory;
        public int TimeoutMilliseconds = -1;
    }

    /// <summary>
    /// Shared process spawning and stdout/stderr capture for tools and CurlClient.
    /// </summary>
    internal static class ProcessRunner
    {
        /// <summary>
        /// Runs a process and returns captured output. Throws <see cref="InvalidOperationException"/>
        /// on spawn/timeout failure; sets <paramref name="exitCode"/> to -1 in that case.
        /// </summary>
        public static string RunCommand(
            string fileName,
            string arguments,
            out int exitCode,
            bool combineErrorOutput = true,
            int timeoutMilliseconds = -1,
            string stdinData = null,
            string workingDirectory = null)
        {
            try
            {
                ProcessRunResult result = Run(
                    fileName,
                    arguments,
                    new ProcessRunOptions
                    {
                        StdinData = stdinData,
                        WorkingDirectory = workingDirectory,
                        TimeoutMilliseconds = timeoutMilliseconds
                    });

                exitCode = result.ExitCode;
                if (combineErrorOutput && !string.IsNullOrEmpty(result.StdErr))
                    return result.StdOut + result.StdErr;
                return result.StdOut;
            }
            catch (Exception ex)
            {
                exitCode = -1;
                throw new InvalidOperationException("Failed to execute " + fileName + ": " + ex.Message, ex);
            }
        }

        public static ProcessRunResult Run(string fileName, string arguments, ProcessRunOptions options = null)
        {
            if (options == null)
                options = new ProcessRunOptions();

            ProcessStartInfo psi = CreateStartInfo(fileName, arguments, options.StdinData != null);
            if (!string.IsNullOrEmpty(options.WorkingDirectory))
                psi.WorkingDirectory = options.WorkingDirectory;

            using (Process process = Process.Start(psi))
            {
                if (options.StdinData != null)
                {
                    process.StandardInput.Write(options.StdinData);
                    process.StandardInput.Close();
                }

                string output = "";
                string error = "";
                Thread outThread = new Thread(() => { output = process.StandardOutput.ReadToEnd(); });
                Thread errThread = new Thread(() => { error = process.StandardError.ReadToEnd(); });
                outThread.IsBackground = true;
                errThread.IsBackground = true;
                outThread.Start();
                errThread.Start();

                if (options.TimeoutMilliseconds > 0)
                {
                    if (!process.WaitForExit(options.TimeoutMilliseconds))
                    {
                        try { process.Kill(); } catch { }
                        process.WaitForExit();
                        throw new TimeoutException(string.Format(
                            "Process '{0}' timed out after {1} seconds.",
                            fileName,
                            options.TimeoutMilliseconds / 1000));
                    }
                }
                else
                {
                    process.WaitForExit();
                }

                outThread.Join();
                errThread.Join();

                return new ProcessRunResult
                {
                    ExitCode = process.ExitCode,
                    StdOut = output,
                    StdErr = error
                };
            }
        }

        /// <summary>
        /// Runs a process whose stdin is fed from a Windows named pipe (--data-binary @\\.\pipe\...).
        /// When <paramref name="stdoutConsumer"/> is null, stdout and stderr are read concurrently.
        /// When set, the consumer reads stdout (e.g. SSE streaming) and stderr is read after exit.
        /// </summary>
        public static ProcessRunResult RunWithNamedPipeStdin(
            string fileName,
            string arguments,
            string pipeName,
            byte[] stdinBytes,
            Action<TextReader> stdoutConsumer = null,
            int auxThreadJoinMilliseconds = 5000)
        {
            using (NamedPipeServerStream pipeServer = new NamedPipeServerStream(
                pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte))
            {
                ProcessStartInfo psi = CreateStartInfo(fileName, arguments, redirectStdin: false);

                using (Process process = Process.Start(psi))
                {
                    Thread pipeThread = StartNamedPipeFeed(pipeServer, stdinBytes);

                    if (stdoutConsumer != null)
                    {
                        stdoutConsumer(process.StandardOutput);
                        pipeThread.Join(auxThreadJoinMilliseconds);
                        process.WaitForExit();

                        return new ProcessRunResult
                        {
                            ExitCode = process.ExitCode,
                            StdOut = "",
                            StdErr = process.StandardError.ReadToEnd()
                        };
                    }

                    string output = "";
                    string error = "";
                    Thread outThread = new Thread(() => { try { output = process.StandardOutput.ReadToEnd(); } catch { } });
                    Thread errThread = new Thread(() => { try { error = process.StandardError.ReadToEnd(); } catch { } });
                    outThread.IsBackground = true;
                    errThread.IsBackground = true;
                    outThread.Start();
                    errThread.Start();

                    pipeThread.Join(auxThreadJoinMilliseconds);
                    outThread.Join();
                    errThread.Join(auxThreadJoinMilliseconds);
                    process.WaitForExit();

                    return new ProcessRunResult
                    {
                        ExitCode = process.ExitCode,
                        StdOut = output,
                        StdErr = error
                    };
                }
            }
        }

        private static ProcessStartInfo CreateStartInfo(string fileName, string arguments, bool redirectStdin)
        {
            return new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = redirectStdin,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
        }

        private static Thread StartNamedPipeFeed(NamedPipeServerStream pipeServer, byte[] stdinBytes)
        {
            Thread pipeThread = new Thread(() =>
            {
                try
                {
                    pipeServer.WaitForConnection();
                    pipeServer.Write(stdinBytes, 0, stdinBytes.Length);
                    pipeServer.Flush();
                    pipeServer.Close();
                }
                catch { }
            });
            pipeThread.IsBackground = true;
            pipeThread.Start();
            return pipeThread;
        }
    }
}
