using System.Windows;
using LanRemote.Core;

namespace LanRemote.App;

public partial class PairingApprovalWindow : Window
{
    public PairingApprovalWindow(PairingRequest request)
    {
        InitializeComponent();
        DeviceText.Text = $"裝置：{request.DeviceName}";
        AddressText.Text = $"來源：{request.RemoteEndpoint.Address}";
        OperatingSystemText.Text = $"系統：{request.OperatingSystem}";
        PairingCodeText.Text = request.PairingCode.Length == 6
            ? $"{request.PairingCode[..3]} {request.PairingCode[3..]}"
            : request.PairingCode;
        if (!request.FileTransferRequested)
        {
            AllowFileTransferCheckBox.IsChecked = false;
            AllowFileTransferCheckBox.IsEnabled = false;
            AllowFileTransferCheckBox.Content = "發起端未要求檔案傳輸";
        }
    }

    public PairingApproval Approval { get; private set; } = new(false, false, false);

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        Approval = new PairingApproval(
            true,
            AllowFileTransferCheckBox.IsChecked == true,
            AllowClipboardTextCheckBox.IsChecked == true);
        DialogResult = true;
    }

    private void RejectButton_Click(object sender, RoutedEventArgs e)
    {
        Approval = new PairingApproval(false, false, false);
        DialogResult = false;
    }
}
