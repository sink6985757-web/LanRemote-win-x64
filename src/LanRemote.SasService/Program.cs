using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using LanRemote.Windows;

namespace LanRemote.SasService;

internal static class Program
{
    internal const string ServiceName = "LanRemoteSas";
    private const int ErrorFailedServiceControllerConnect = 1063;
    private const uint ServiceControlStop = 1;
    private const uint ServiceControlShutdown = 5;
    private const uint ServiceAcceptStop = 1;
    private const uint ServiceAcceptShutdown = 4;
    private const uint ServiceWin32OwnProcess = 0x10;
    private static readonly ServiceMainDelegate ServiceMainCallback = ServiceMain;
    private static readonly HandlerExDelegate HandlerCallback = Handler;
    private static readonly CancellationTokenSource Lifetime = new();
    private static nint _statusHandle;
    private static uint _checkpoint;

    private static int Main(string[] args)
    {
        if (args.Contains("--console", StringComparer.OrdinalIgnoreCase))
        {
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                Lifetime.Cancel();
            };
            RunPipeLoopAsync(Lifetime.Token).GetAwaiter().GetResult();
            return 0;
        }

        ServiceTableEntry[] table =
        [
            new ServiceTableEntry(ServiceName, ServiceMainCallback),
            new ServiceTableEntry(null, null),
        ];
        if (StartServiceCtrlDispatcher(table))
        {
            return 0;
        }

        int error = Marshal.GetLastWin32Error();
        if (error == ErrorFailedServiceControllerConnect)
        {
            Console.Error.WriteLine("This executable must be started by Windows Service Control Manager.");
            return error;
        }

        throw new Win32Exception(error, "StartServiceCtrlDispatcher failed.");
    }

    private static void ServiceMain(int argumentCount, nint arguments)
    {
        _statusHandle = RegisterServiceCtrlHandlerEx(ServiceName, HandlerCallback, nint.Zero);
        if (_statusHandle == nint.Zero)
        {
            return;
        }

        SetStatus(ServiceState.StartPending, acceptedControls: 0, waitHint: 3000);
        SetStatus(ServiceState.Running, ServiceAcceptStop | ServiceAcceptShutdown);
        try
        {
            RunPipeLoopAsync(Lifetime.Token).GetAwaiter().GetResult();
            SetStatus(ServiceState.Stopped, acceptedControls: 0);
        }
        catch
        {
            SetStatus(ServiceState.Stopped, acceptedControls: 0, win32ExitCode: 1);
        }
    }

    private static uint Handler(uint control, uint eventType, nint eventData, nint context)
    {
        if (control is ServiceControlStop or ServiceControlShutdown)
        {
            SetStatus(ServiceState.StopPending, acceptedControls: 0, waitHint: 3000);
            Lifetime.Cancel();
        }

        return 0;
    }

    private static async Task RunPipeLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using NamedPipeServerStream pipe = CreatePipeServer();
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await SasPipeProtocol.ReadAndValidateRequestAsync(pipe, cancellationToken)
                    .ConfigureAwait(false);
                NativeMethods.SendSAS(asUser: false);
                await SasPipeProtocol.WriteResultAsync(
                    pipe,
                    true,
                    "SAS 服務已送出 Ctrl+Alt+Delete；Windows 原則仍可能拒絕切換安全桌面。",
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (pipe.IsConnected)
                {
                    try
                    {
                        await SasPipeProtocol.WriteResultAsync(
                            pipe,
                            false,
                            $"SAS 服務要求失敗：{exception.Message}",
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }
    }

    private static NamedPipeServerStream CreatePipeServer()
    {
        PipeSecurity security = new();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            SasPipeProtocol.PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    private static void SetStatus(
        ServiceState state,
        uint acceptedControls,
        uint waitHint = 0,
        uint win32ExitCode = 0)
    {
        ServiceStatus status = new()
        {
            ServiceType = ServiceWin32OwnProcess,
            CurrentState = state,
            ControlsAccepted = acceptedControls,
            Win32ExitCode = win32ExitCode,
            WaitHint = waitHint,
            CheckPoint = state is ServiceState.StartPending or ServiceState.StopPending ? ++_checkpoint : 0,
        };
        _ = SetServiceStatus(_statusHandle, ref status);
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceCtrlDispatcher([In] ServiceTableEntry[] serviceTable);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint RegisterServiceCtrlHandlerEx(
        string serviceName,
        HandlerExDelegate handler,
        nint context);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetServiceStatus(nint serviceStatusHandle, ref ServiceStatus serviceStatus);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ServiceMainDelegate(int argumentCount, nint arguments);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint HandlerExDelegate(uint control, uint eventType, nint eventData, nint context);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceTableEntry
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Name;
        public ServiceMainDelegate? Callback;

        public ServiceTableEntry(string? name, ServiceMainDelegate? callback)
        {
            Name = name;
            Callback = callback;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public ServiceState CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    private enum ServiceState : uint
    {
        Stopped = 1,
        StartPending = 2,
        StopPending = 3,
        Running = 4,
    }

    private static class NativeMethods
    {
        [DllImport("sas.dll", SetLastError = true)]
        internal static extern void SendSAS([MarshalAs(UnmanagedType.Bool)] bool asUser);
    }
}
