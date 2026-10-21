using System.Drawing;
using QRCoder;
using Forms = System.Windows.Forms;

namespace Pulse.Remote;

public sealed class CompanionWindow : Forms.Form
{
    readonly Forms.NotifyIcon tray;
    readonly Forms.Timer timer;
    readonly Forms.Label status;
    readonly Forms.PictureBox qr;
    readonly Forms.TextBox url;
    readonly Forms.ComboBox addresses;
    bool exiting;
    readonly CancellationTokenSource instanceCancellation = new();

    public CompanionWindow(SensorService sensors, LocalNameService localName)
    {
        Text = "Pulse Remote • Connect your iPhone";
        BackColor = Color.FromArgb(14, 19, 25); ForeColor = Color.FromArgb(232, 239, 242);
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(570, 810); FormBorderStyle = Forms.FormBorderStyle.FixedDialog;
        MaximizeBox = false; StartPosition = Forms.FormStartPosition.CenterScreen;
        var layout = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Fill, FlowDirection = Forms.FlowDirection.TopDown, WrapContents = false, Padding = new Forms.Padding(28), AutoScroll = true };
        Controls.Add(layout);
        void Label(string text, int height, int size = 10) => layout.Controls.Add(new Forms.Label { Text = text, Width = 495, Height = height, Font = new Font("Segoe UI", size), ForeColor = ForeColor });
        Label("PULSE  /  REMOTE", 32, 12);
        Label("Your PC. In your pocket.", 48, 24);
        Label("Pair once on your home Wi-Fi. Save the permanent link in Safari\nwith Share → Add to Home Screen. Reuse it every time.", 55);
        addresses = new Forms.ComboBox { Width = 495, DropDownStyle = Forms.ComboBoxStyle.DropDownList };
        addresses.Items.Add(Program.Settings.HostName);
        addresses.Items.AddRange(Program.Addresses());
        if (addresses.Items.Count == 0) addresses.Items.Add("127.0.0.1");
        layout.Controls.Add(addresses);
        qr = new Forms.PictureBox { Width = 245, Height = 245, SizeMode = Forms.PictureBoxSizeMode.Zoom, BackColor = Color.White, Margin = new Forms.Padding(120, 14, 0, 14) };
        layout.Controls.Add(qr);
        url = new Forms.TextBox { Width = 495, ReadOnly = true };
        layout.Controls.Add(url);
        var buttons = new Forms.FlowLayoutPanel { Width = 495, Height = 43 };
        void Button(string title, Action action)
        {
            var b = new Forms.Button { Text = title, AutoSize = true, Height = 34, BackColor = Color.FromArgb(38, 49, 60), FlatStyle = Forms.FlatStyle.Flat };
            b.Click += (_, _) => action(); buttons.Controls.Add(b);
        }
        Button("Open dashboard", () => Program.Open(Program.Url("127.0.0.1")));
        Button("Copy phone link", () => Forms.Clipboard.SetText(url.Text));
        Button("Hide to tray", Hide);
        layout.Controls.Add(buttons);
        status = new Forms.Label { Width = 495, Height = 65, ForeColor = Color.FromArgb(133, 223, 191) };
        layout.Controls.Add(status);
        var autoStart = new Forms.CheckBox { Text = "Open HWiNFO automatically when Pulse starts", Width = 495, Height = 30, Checked = Program.Settings.StartHwInfo };
        autoStart.CheckedChanged += (_, _) => { Program.Settings.StartHwInfo = autoStart.Checked; Program.Settings.Save(); };
        layout.Controls.Add(autoStart);
        var startupStatus = new Forms.Label { Width = 495, Height = 44, ForeColor = Color.FromArgb(151, 167, 181) };
        var hwButtons = new Forms.FlowLayoutPanel { Width = 495, Height = 38 };
        var startHw = new Forms.Button { Text = "Start HWiNFO", AutoSize = true, Height = 32, FlatStyle = Forms.FlatStyle.Flat };
        startHw.Click += (_, _) => startupStatus.Text = HwInfoLauncher.Start(Program.Settings);
        var chooseHw = new Forms.Button { Text = "Choose HWiNFO…", AutoSize = true, Height = 32, FlatStyle = Forms.FlatStyle.Flat };
        chooseHw.Click += (_, _) =>
        {
            using var dialog = new Forms.OpenFileDialog { Title = "Locate HWiNFO64.exe", Filter = "HWiNFO64|HWiNFO64.exe", CheckFileExists = true };
            if (dialog.ShowDialog(this) != Forms.DialogResult.OK) return;
            if (!Path.GetFileName(dialog.FileName).Equals("HWiNFO64.exe", StringComparison.OrdinalIgnoreCase)) return;
            Program.Settings.HwInfoPath = dialog.FileName; Program.Settings.Save();
            startupStatus.Text = "Saved. Click Start HWiNFO, or reopen Pulse.";
        };
        hwButtons.Controls.AddRange([startHw, chooseHw]);
        layout.Controls.Add(hwButtons); layout.Controls.Add(startupStatus);
        Label("The first address above is permanent, even if your PC’s IP changes. IP addresses below it are fallbacks. Allow phone access once after this update to enable local name discovery.", 55);
        Label("Closing Pulse hides it to the tray. Right-click its tray icon to quit. HWiNFO Free’s 12-hour sharing limit still needs manual re-enabling.", 45);
        Shown += (_, _) => BeginInvoke(() =>
        {
            _ = InstanceSignal.Listen(() =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(() =>
                {
                    Show(); WindowState = Forms.FormWindowState.Normal; Activate();
                    if (Program.Settings.StartHwInfo && !HwInfoLauncher.IsRunning) startupStatus.Text = HwInfoLauncher.Start(Program.Settings);
                });
            }, instanceCancellation.Token);
            startupStatus.Text = Program.Settings.StartHwInfo ? HwInfoLauncher.Start(Program.Settings) : "HWiNFO automatic startup is off.";
            if (localName.Error is not null) startupStatus.Text += " " + localName.Error;
            File.WriteAllText(Path.Combine(Program.DataDirectory, "startup-status.txt"), startupStatus.Text);
        });
        addresses.SelectedIndexChanged += (_, _) =>
        {
            url.Text = Program.Url(addresses.SelectedItem!.ToString()!);
            using var data = QRCodeGenerator.GenerateQrCode(url.Text, QRCodeGenerator.ECCLevel.Q);
            using var code = new PngByteQRCode(data);
            using var stream = new MemoryStream(code.GetGraphic(8));
            var old = qr.Image;
            using var original = Image.FromStream(stream);
            qr.Image = new Bitmap(original); old?.Dispose();
        };
        addresses.SelectedIndex = 0;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Connect your phone", null, (_, _) => { Show(); WindowState = Forms.FormWindowState.Normal; Activate(); });
        menu.Items.Add("Open dashboard", null, (_, _) => Program.Open(Program.Url("127.0.0.1")));
        menu.Items.Add("Quit Pulse Remote", null, (_, _) => { exiting = true; Close(); });
        tray = new Forms.NotifyIcon { Icon = SystemIcons.Application, Visible = true, Text = "Pulse Remote", ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => { Show(); Activate(); };
        timer = new Forms.Timer { Interval = 2000 };
        timer.Tick += (_, _) => status.Text = sensors.Current.Status == "live"
            ? $"● Sharing {sensors.Current.Readings.Length} readings · updates every {Program.Settings.RefreshSeconds} seconds"
            : sensors.Current.Message;
        timer.Start();
        FormClosing += (_, e) => { if (!exiting && e.CloseReason == Forms.CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        FormClosed += (_, _) => { instanceCancellation.Cancel(); timer.Dispose(); tray.Dispose(); qr.Image?.Dispose(); };
    }
}
