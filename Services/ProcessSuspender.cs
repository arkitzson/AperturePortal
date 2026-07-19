using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ApertureOS.Services;

/// <summary>
/// Freezes/unfreezes a process by suspending all of its threads. Used to stop a running game
/// from processing further input while the pause overlay is open: XInput has no concept of
/// exclusive access, so a controller press reaches every process polling it, not just whichever
/// window has OS focus. Actually halting the game's threads is the only way to guarantee it
/// stops reacting.
/// </summary>
public sealed class ProcessSuspender
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll")]
    private static extern uint SuspendThread(IntPtr hThread);

    [DllImport("kernel32.dll")]
    private static extern int ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [Flags]
    private enum ThreadAccess : uint
    {
        SuspendResume = 0x0002
    }

    private readonly List<IntPtr> _suspendedThreadHandles = [];

    public Process? SuspendedProcess { get; private set; }

    /// <summary>
    /// Suspends every thread in the given process. Threads we can't open - e.g. the process is
    /// running elevated while this app isn't - are silently skipped; this is best-effort.
    /// </summary>
    public void Suspend(Process process)
    {
        Resume();

        foreach (ProcessThread thread in process.Threads)
        {
            IntPtr handle = OpenThread(ThreadAccess.SuspendResume, false, (uint)thread.Id);
            if (handle == IntPtr.Zero)
                continue;

            SuspendThread(handle);
            _suspendedThreadHandles.Add(handle);
        }

        SuspendedProcess = process;
    }

    /// <summary>Resumes all threads suspended by the last <see cref="Suspend"/> call, if any.</summary>
    public void Resume()
    {
        foreach (var handle in _suspendedThreadHandles)
        {
            ResumeThread(handle);
            CloseHandle(handle);
        }

        _suspendedThreadHandles.Clear();
        SuspendedProcess = null;
    }

    /// <summary>
    /// Releases the handles from the last <see cref="Suspend"/> call without resuming the
    /// threads. Use this instead of <see cref="Resume"/> when the process is about to be killed
    /// anyway - termination works fine on a suspended process, so there's no need to un-suspend
    /// it first.
    /// </summary>
    public void Discard()
    {
        foreach (var handle in _suspendedThreadHandles)
        {
            CloseHandle(handle);
        }

        _suspendedThreadHandles.Clear();
        SuspendedProcess = null;
    }
}
