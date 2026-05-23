using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace BrowserPicker;

/// <summary>
/// IPC via named pipe "BrowserPickerIPC".
/// O integrity label do pipe é definido explicitamente como MEDIUM via P/Invoke
/// (SetSecurityInfo com SACL "S:(ML;;NW;;;ME)"), para que processos de média
/// integridade (ligações de links do utilizador) possam ligar-se mesmo que o
/// tray esteja a correr elevado.
/// </summary>
internal static class PipeServer
{
    private const string PipeName = "BrowserPickerIPC";
    private static CancellationTokenSource? _cts;

    // -----------------------------------------------------------------------
    // P/Invoke — impor integrity label MEDIUM no handle do pipe
    // -----------------------------------------------------------------------

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string StringSecurityDescriptor,
        uint StringSDRevision,
        out IntPtr SecurityDescriptor,
        out uint SecurityDescriptorSize);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSecurityDescriptorSacl(
        IntPtr pSecurityDescriptor,
        [MarshalAs(UnmanagedType.Bool)] out bool lpbSaclPresent,
        out IntPtr pSacl,
        [MarshalAs(UnmanagedType.Bool)] out bool lpbSaclDefaulted);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint SetSecurityInfo(
        SafeHandle handle,
        uint objectType,
        uint securityInfo,
        IntPtr psidOwner,
        IntPtr psidGroup,
        IntPtr pDacl,
        IntPtr pSacl);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private const uint SE_KERNEL_OBJECT      = 6;
    private const uint SACL_SECURITY_INFORMATION = 0x00000008u;

    /// <summary>
    /// Define o integrity label MEDIUM no pipe para que processos de média
    /// integridade possam ligar-se independentemente do nível do servidor.
    /// SDDL: SACL com Mandatory Label, No-Write-Up, Medium Integrity (S-1-16-8192).
    /// </summary>
    private static void SetMediumIntegrityLabel(SafeHandle handle)
    {
        const string sddl = "S:(ML;;NW;;;ME)";

        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out IntPtr pSD, out _))
            return;

        try
        {
            if (GetSecurityDescriptorSacl(pSD, out bool present, out IntPtr pSacl, out _) && present)
                SetSecurityInfo(handle, SE_KERNEL_OBJECT, SACL_SECURITY_INFORMATION,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, pSacl);
        }
        finally
        {
            LocalFree(pSD);
        }
    }

    // -----------------------------------------------------------------------
    // Server — runs in the tray instance
    // -----------------------------------------------------------------------

    internal static void StartServer(DispatcherQueue dispatcher, Action<string> onUrl)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Message,
                        PipeOptions.Asynchronous);

                    SetMediumIntegrityLabel(pipe.SafePipeHandle);

                    await pipe.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(pipe);
                    var url = await reader.ReadToEndAsync(token);

                    if (!string.IsNullOrWhiteSpace(url))
                        dispatcher.TryEnqueue(() => onUrl(url.Trim()));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch { /* ignorar erros transitórios, continuar a ouvir */ }
            }
        }, token);
    }

    // -----------------------------------------------------------------------
    // Client — called by new process launches to forward URL to tray instance
    // -----------------------------------------------------------------------

    internal static bool TrySendUrl(string url)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(1000); // timeout de 1 segundo
            using var writer = new StreamWriter(pipe);
            writer.Write(url);
            return true;
        }
        catch { return false; }
    }
}
